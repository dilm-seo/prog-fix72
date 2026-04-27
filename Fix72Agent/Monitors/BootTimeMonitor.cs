using System.Diagnostics.Eventing.Reader;
using System.Management;
using System.Runtime.Versioning;
using Fix72Agent.Models;

namespace Fix72Agent.Monitors;

/// <summary>
/// Surveille la santé du démarrage Windows.
///   1. Tentative principale : durée du dernier boot via Event Log Diagnostics-Performance ID 100
///      (ne marche qu'en mode admin sur la plupart des PC).
///   2. Fallback : temps écoulé depuis le dernier démarrage via Win32_OperatingSystem.LastBootUpTime
///      (toujours accessible, plus utile à long terme : "votre PC tourne depuis 45 jours sans redémarrer").
/// </summary>
[SupportedOSPlatform("windows")]
public class BootTimeMonitor : IMonitor
{
    public string Id => "boottime";

    private const string LogName = "Microsoft-Windows-Diagnostics-Performance/Operational";
    private const int BootEventId = 100;

    public Task<MonitorResult> CheckAsync(CancellationToken ct = default)
    {
        return Task.Run<MonitorResult>(() =>
        {
            // 1. Essai durée de boot via Event Log
            var bootDuration = TryReadBootDuration();
            if (bootDuration.HasValue)
                return BuildBootDurationResult(bootDuration.Value);

            // 2. Fallback : uptime via Win32_OperatingSystem
            var uptime = TryReadUptime();
            if (uptime.HasValue)
                return BuildUptimeResult(uptime.Value);

            return new MonitorResult(Id, "Démarrage", "⚡",
                AlertLevel.Unknown, "Indisponible", "Information non accessible");
        }, ct);
    }

    private static double? TryReadBootDuration()
    {
        try
        {
            var query = new EventLogQuery(LogName, PathType.LogName,
                $"*[System[(EventID={BootEventId})]]")
            {
                ReverseDirection = true
            };
            using var reader = new EventLogReader(query);

            using var lastEvent = reader.ReadEvent();
            if (lastEvent == null) return null;

            foreach (var prop in lastEvent.Properties)
            {
                if (prop.Value is uint u && u > 1000) return u / 1000.0;
                if (prop.Value is int i && i > 1000) return i / 1000.0;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static TimeSpan? TryReadUptime()
    {
        try
        {
            var query = new ObjectQuery("SELECT LastBootUpTime FROM Win32_OperatingSystem");
            using var searcher = new ManagementObjectSearcher(query);
            using var collection = searcher.Get();

            foreach (ManagementObject obj in collection)
            {
                var raw = obj["LastBootUpTime"]?.ToString();
                obj.Dispose();
                if (string.IsNullOrEmpty(raw)) continue;

                var bootTime = ManagementDateTimeConverter.ToDateTime(raw);
                return DateTime.Now - bootTime;
            }
        }
        catch { /* fall through */ }
        return null;
    }

    private MonitorResult BuildBootDurationResult(double seconds)
    {
        AlertLevel level = seconds switch
        {
            > 180 => AlertLevel.Critical,
            > 90 => AlertLevel.Warning,
            _ => AlertLevel.Ok
        };
        string status = level switch
        {
            AlertLevel.Critical => "Très lent",
            AlertLevel.Warning => "Lent",
            _ => "Normal"
        };
        string? message = level switch
        {
            AlertLevel.Critical =>
                $"Votre ordinateur met {seconds:F0} secondes à démarrer — c'est anormalement long. " +
                "Trop de logiciels au démarrage ou un disque dur fatigué. Appelez Fix72.",
            AlertLevel.Warning =>
                $"Votre ordinateur démarre en {seconds:F0} secondes. C'est plus lent que la normale.",
            _ => null
        };
        MonitorAction? action = level >= AlertLevel.Warning
            ? new MonitorAction("Voir les programmes au démarrage", "ms-settings:startupapps")
            : null;
        return new MonitorResult(Id, "Démarrage", "⚡",
            level, status, $"{seconds:F0} secondes au dernier boot", message, action);
    }

    private MonitorResult BuildUptimeResult(TimeSpan uptime)
    {
        var days = (int)uptime.TotalDays;
        var hours = (int)uptime.TotalHours;

        AlertLevel level = days switch
        {
            > 30 => AlertLevel.Warning,    // un redémarrage régulier améliore les performances
            _ => AlertLevel.Ok
        };

        string status = level == AlertLevel.Warning ? "À redémarrer" : "OK";

        string detail = days >= 1
            ? $"Allumé depuis {days} jour(s)"
            : $"Allumé depuis {hours} heure(s)";

        string? message = level == AlertLevel.Warning
            ? $"Votre ordinateur n'a pas redémarré depuis {days} jours. " +
              "Un redémarrage de temps en temps libère la mémoire et accélère le PC."
            : null;

        MonitorAction? action = level == AlertLevel.Warning
            ? new MonitorAction("Redémarrer maintenant", "shutdown.exe", "/r /t 30 /c \"Redémarrage demandé par Fix72 Agent\"")
            : null;

        return new MonitorResult(Id, "Démarrage", "⚡",
            level, status, detail, message, action);
    }
}
