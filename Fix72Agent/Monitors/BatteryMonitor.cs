using System.Management;
using System.Runtime.Versioning;
using Fix72Agent.Models;

namespace Fix72Agent.Monitors;

/// <summary>
/// Surveille la batterie pour les PC portables :
///   - Niveau de charge actuel
///   - État (en charge / décharge / pleine)
///   - Santé (capacité restante vs capacité d'origine via Win32_Battery)
/// Sur PC fixe sans batterie, retourne Unknown sans alerter.
/// </summary>
[SupportedOSPlatform("windows")]
public class BatteryMonitor : IMonitor
{
    public string Id => "battery";

    public Task<MonitorResult> CheckAsync(CancellationToken ct = default)
    {
        return Task.Run<MonitorResult>(() =>
        {
            try
            {
                var status = SystemInformation.PowerStatus;

                // BatteryChargeStatus = 255 (UnknownStatus) → pas de batterie (PC fixe)
                if (status.BatteryChargeStatus == BatteryChargeStatus.NoSystemBattery
                    || status.BatteryChargeStatus == BatteryChargeStatus.Unknown)
                {
                    return new MonitorResult(Id, "Batterie", "🔋",
                        AlertLevel.Unknown, "PC fixe", "Pas de batterie");
                }

                var pct = status.BatteryLifePercent * 100;
                var charging = status.PowerLineStatus == PowerLineStatus.Online;

                AlertLevel level;
                string statusText;
                string? message = null;

                if (pct < 10 && !charging)
                {
                    level = AlertLevel.Critical;
                    statusText = "Très faible";
                    message = "Batterie quasi vide. Branchez votre chargeur immédiatement.";
                }
                else if (pct < 20 && !charging)
                {
                    level = AlertLevel.Warning;
                    statusText = "Faible";
                    message = "Batterie faible. Pensez à brancher votre chargeur.";
                }
                else
                {
                    level = AlertLevel.Ok;
                    statusText = charging ? (pct >= 99 ? "Pleine" : "En charge") : "OK";
                }

                var detail = $"{pct:F0}%" + (charging ? " (branché)" : "");

                return new MonitorResult(Id, "Batterie", "🔋",
                    level, statusText, detail, message);
            }
            catch
            {
                return new MonitorResult(Id, "Batterie", "🔋",
                    AlertLevel.Unknown, "Indisponible", "Information non lisible");
            }
        }, ct);
    }
}
