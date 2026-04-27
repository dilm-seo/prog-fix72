using System.Management;
using System.Runtime.Versioning;
using Fix72Agent.Models;

namespace Fix72Agent.Monitors;

/// <summary>
/// Surveille la santé des disques.
///   1. Méthode moderne (Windows 10+) : MSFT_PhysicalDisk dans le namespace Storage —
///      compatible HDD/SSD/NVMe, expose HealthStatus et OperationalStatus.
///   2. Fallback ancienne API : MSStorageDriver_FailurePredictStatus (souvent vide sur SSD).
/// </summary>
[SupportedOSPlatform("windows")]
public class SmartMonitor : IMonitor
{
    public string Id => "smart";

    public Task<MonitorResult> CheckAsync(CancellationToken ct = default)
    {
        return Task.Run<MonitorResult>(() =>
        {
            // 1. Méthode moderne
            var modernResult = TryModernApi();
            if (modernResult != null) return modernResult;

            // 2. Fallback ancienne API
            var legacyResult = TryLegacyApi();
            if (legacyResult != null) return legacyResult;

            return new MonitorResult(Id, "Santé disque", "💽",
                AlertLevel.Unknown, "Indisponible", "Information non remontée");
        }, ct);
    }

    private MonitorResult? TryModernApi()
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            var query = new ObjectQuery("SELECT FriendlyName, HealthStatus, OperationalStatus FROM MSFT_PhysicalDisk");
            using var searcher = new ManagementObjectSearcher(scope, query);
            using var collection = searcher.Get();

            // HealthStatus codes : 0=Healthy, 1=Warning, 2=Unhealthy, 5=Unknown
            var disks = new List<(string Name, ushort Health)>();
            foreach (ManagementObject obj in collection)
            {
                var name = obj["FriendlyName"]?.ToString()?.Trim() ?? "Disque inconnu";
                ushort health = 5;
                try { health = Convert.ToUInt16(obj["HealthStatus"]); } catch { }
                disks.Add((name, health));
                obj.Dispose();
            }

            if (disks.Count == 0) return null;

            var unhealthy = disks.Where(d => d.Health == 2).ToList();
            var warning = disks.Where(d => d.Health == 1).ToList();
            var healthy = disks.Count(d => d.Health == 0);

            if (unhealthy.Count > 0)
            {
                var names = string.Join(", ", unhealthy.Select(d => Truncate(d.Name, 25)));
                return new MonitorResult(Id, "Santé disque", "💽",
                    AlertLevel.Critical, "Défaillant",
                    names,
                    "ATTENTION : un de vos disques montre des signes de défaillance. " +
                    "Sauvegardez vos données IMMÉDIATEMENT et appelez Fix72.");
            }
            if (warning.Count > 0)
            {
                var names = string.Join(", ", warning.Select(d => Truncate(d.Name, 25)));
                return new MonitorResult(Id, "Santé disque", "💽",
                    AlertLevel.Warning, "À surveiller",
                    names,
                    "Un de vos disques signale un avertissement de santé. " +
                    "Pas urgent, mais à faire vérifier prochainement.");
            }

            // Tous OK
            var firstName = Truncate(disks[0].Name, 28);
            return new MonitorResult(Id, "Santé disque", "💽",
                AlertLevel.Ok, "OK",
                disks.Count == 1 ? firstName : $"{disks.Count} disques en bonne santé");
        }
        catch
        {
            return null;
        }
    }

    private MonitorResult? TryLegacyApi()
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\WMI");
            var query = new ObjectQuery(
                "SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus");
            using var searcher = new ManagementObjectSearcher(scope, query);
            using var collection = searcher.Get();

            int diskCount = 0;
            int failing = 0;
            foreach (ManagementObject obj in collection)
            {
                diskCount++;
                if (Convert.ToBoolean(obj["PredictFailure"])) failing++;
                obj.Dispose();
            }

            if (diskCount == 0) return null;

            if (failing > 0)
            {
                return new MonitorResult(Id, "Santé disque", "💽",
                    AlertLevel.Critical, "Défaillant",
                    $"{failing} disque(s) à risque",
                    "ATTENTION : votre disque dur montre des signes de défaillance prochaine. " +
                    "Sauvegardez vos données IMMÉDIATEMENT et appelez Fix72.");
            }
            return new MonitorResult(Id, "Santé disque", "💽",
                AlertLevel.Ok, "OK", $"{diskCount} disque(s) en bonne santé");
        }
        catch
        {
            return null;
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
