using System.Management;
using System.Runtime.Versioning;
using Fix72Agent.Models;

namespace Fix72Agent.Monitors;

/// <summary>
/// Lit la température CPU via les zones thermiques ACPI exposées par WMI.
/// Note : sur beaucoup de PC grand public, le BIOS ne remonte qu'une valeur figée
/// (ex: 27.8°C constant). Dans ce cas, le capteur reste en "Indisponible" plutôt
/// que de générer de fausses alertes.
/// </summary>
[SupportedOSPlatform("windows")]
public class TemperatureMonitor : IMonitor
{
    public string Id => "temperature";

    public Task<MonitorResult> CheckAsync(CancellationToken ct = default)
    {
        return Task.Run<MonitorResult>(() =>
        {
            try
            {
                var scope = new ManagementScope(@"\\.\root\WMI");
                var query = new ObjectQuery("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                using var searcher = new ManagementObjectSearcher(scope, query);
                using var collection = searcher.Get();

                var temps = new List<double>();
                foreach (ManagementObject obj in collection)
                {
                    var raw = Convert.ToDouble(obj["CurrentTemperature"]);
                    // ACPI renvoie en dixièmes de Kelvin
                    var celsius = (raw - 2732) / 10.0;
                    if (celsius is > 10 and < 120)
                        temps.Add(celsius);
                    obj.Dispose();
                }

                if (temps.Count == 0)
                {
                    return new MonitorResult(Id, "Température CPU", "🌡️",
                        AlertLevel.Unknown, "Non disponible",
                        "Pas de capteur lisible sur ce PC");
                }

                // Détection des "valeurs figées" connues (BIOS qui ne remonte rien)
                if (temps.All(t => Math.Abs(t - temps[0]) < 0.1))
                {
                    if (Math.Abs(temps[0] - 27.8) < 0.5 || Math.Abs(temps[0] - 50.0) < 0.5)
                    {
                        return new MonitorResult(Id, "Température CPU", "🌡️",
                            AlertLevel.Unknown, "Non disponible",
                            "BIOS ne remonte pas la valeur réelle");
                    }
                }

                var maxTemp = temps.Max();
                AlertLevel level = maxTemp switch
                {
                    > 90 => AlertLevel.Critical,
                    > 80 => AlertLevel.Warning,
                    _ => AlertLevel.Ok
                };

                string status = level switch
                {
                    AlertLevel.Critical => "Brûlant",
                    AlertLevel.Warning => "Chaud",
                    _ => "Normal"
                };

                string? message = level switch
                {
                    AlertLevel.Critical =>
                        $"Votre processeur monte à {maxTemp:F0}°C. C'est dangereux. " +
                        "Vérifiez que les aérations ne sont pas obstruées et arrêtez les programmes lourds.",
                    AlertLevel.Warning =>
                        $"Votre ordinateur chauffe ({maxTemp:F0}°C). Vérifiez les aérations.",
                    _ => null
                };

                return new MonitorResult(Id, "Température CPU", "🌡️",
                    level, status, $"{maxTemp:F0}°C", message);
            }
            catch (Exception)
            {
                return new MonitorResult(Id, "Température CPU", "🌡️",
                    AlertLevel.Unknown, "Non disponible", "Capteur non accessible");
            }
        }, ct);
    }
}
