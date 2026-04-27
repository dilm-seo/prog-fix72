using System.Management;
using System.Runtime.Versioning;
using Fix72Agent.Models;

namespace Fix72Agent.Monitors;

/// <summary>
/// Lit l'état des antivirus installés via WMI SecurityCenter2.
/// Le champ ProductState est un bitmask encodé : on en extrait l'état "actif" et "à jour".
/// Ref : https://mspscripts.com/decoding-the-product-state-of-an-antivirus-product-via-wmi/
/// </summary>
[SupportedOSPlatform("windows")]
public class AntivirusMonitor : IMonitor
{
    public string Id => "antivirus";

    public Task<MonitorResult> CheckAsync(CancellationToken ct = default)
    {
        return Task.Run<MonitorResult>(() =>
        {
            try
            {
                var scope = new ManagementScope(@"\\.\root\SecurityCenter2");
                var query = new ObjectQuery("SELECT displayName, productState FROM AntiVirusProduct");
                using var searcher = new ManagementObjectSearcher(scope, query);
                using var collection = searcher.Get();

                var products = new List<(string Name, bool Enabled, bool UpToDate)>();

                foreach (ManagementObject obj in collection)
                {
                    var name = obj["displayName"]?.ToString() ?? "Antivirus inconnu";
                    var state = Convert.ToInt32(obj["productState"]);
                    // Décodage du bitmask :
                    //  - octet 2 (bits 8-15)  : état (0x10 = enabled)
                    //  - octet 3 (bits 16-23) : signature à jour (0x00 = à jour, 0x10 = obsolète)
                    bool enabled = ((state >> 12) & 0xF) == 1;
                    bool upToDate = ((state >> 4) & 0xF) == 0;
                    products.Add((name, enabled, upToDate));
                    obj.Dispose();
                }

                var openSecurityCenter = new MonitorAction(
                    "Ouvrir Sécurité Windows", "windowsdefender:");

                if (products.Count == 0)
                {
                    return new MonitorResult(Id, "Antivirus", "🛡️",
                        AlertLevel.Critical, "AUCUN",
                        "Aucun antivirus détecté",
                        "Aucun antivirus actif sur ce PC. Votre ordinateur est exposé. " +
                        "Activez Windows Defender ou installez un antivirus.",
                        openSecurityCenter);
                }

                var active = products.Where(p => p.Enabled).ToList();
                if (active.Count == 0)
                {
                    var names = string.Join(", ", products.Select(p => p.Name));
                    return new MonitorResult(Id, "Antivirus", "🛡️",
                        AlertLevel.Critical, "Désactivé", names,
                        "Votre antivirus est INACTIF. Activez-le immédiatement depuis Sécurité Windows.",
                        openSecurityCenter);
                }

                var outdated = active.Where(p => !p.UpToDate).ToList();
                if (outdated.Count > 0)
                {
                    var names = string.Join(", ", outdated.Select(p => p.Name));
                    return new MonitorResult(Id, "Antivirus", "🛡️",
                        AlertLevel.Warning, "Obsolète", names,
                        "La base de données antivirus n'est plus à jour. Lancez une mise à jour.",
                        openSecurityCenter);
                }

                var activeName = active[0].Name;
                if (activeName.Length > 25) activeName = activeName[..25] + "…";

                return new MonitorResult(Id, "Antivirus", "🛡️",
                    AlertLevel.Ok, "Actif", activeName);
            }
            catch (Exception)
            {
                return new MonitorResult(Id, "Antivirus", "🛡️",
                    AlertLevel.Unknown, "Indisponible", "WMI SecurityCenter2 inaccessible");
            }
        }, ct);
    }
}
