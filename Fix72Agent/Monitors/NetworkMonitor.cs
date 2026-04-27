using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using Fix72Agent.Models;

namespace Fix72Agent.Monitors;

/// <summary>
/// Vérifie la connectivité Internet via un ping vers Cloudflare DNS (1.1.1.1) puis Google DNS (8.8.8.8).
/// Mesure aussi la latence pour détecter une connexion lente.
/// </summary>
[SupportedOSPlatform("windows")]
public class NetworkMonitor : IMonitor
{
    public string Id => "network";

    private static readonly string[] Targets = { "1.1.1.1", "8.8.8.8" };

    public async Task<MonitorResult> CheckAsync(CancellationToken ct = default)
    {
        if (!NetworkInterface.GetIsNetworkAvailable())
        {
            return new MonitorResult(Id, "Internet", "🌐",
                AlertLevel.Critical, "Déconnecté",
                "Aucun réseau détecté",
                "Votre ordinateur n'a pas accès au réseau. Vérifiez le câble ou le Wi-Fi.");
        }

        long? bestLatency = null;
        foreach (var target in Targets)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(target, 2000);
                if (reply.Status == IPStatus.Success)
                {
                    var latency = reply.RoundtripTime;
                    if (bestLatency == null || latency < bestLatency)
                        bestLatency = latency;
                    break;
                }
            }
            catch { /* essaye le prochain */ }
        }

        if (bestLatency == null)
        {
            return new MonitorResult(Id, "Internet", "🌐",
                AlertLevel.Critical, "Pas de connexion",
                "Réseau local sans accès Internet",
                "Votre ordinateur est connecté au réseau mais Internet ne répond pas. " +
                "Redémarrez votre box ou contactez votre fournisseur.");
        }

        AlertLevel level = bestLatency switch
        {
            > 500 => AlertLevel.Warning,
            _ => AlertLevel.Ok
        };

        string status = level switch
        {
            AlertLevel.Warning => "Lente",
            _ => "Connectée"
        };

        string detail = $"Latence : {bestLatency} ms";

        string? message = level == AlertLevel.Warning
            ? $"Votre connexion Internet est lente ({bestLatency} ms). Redémarrez votre box pour améliorer."
            : null;

        return new MonitorResult(Id, "Internet", "🌐",
            level, status, detail, message);
    }
}
