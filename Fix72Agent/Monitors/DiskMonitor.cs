using Fix72Agent.Models;

namespace Fix72Agent.Monitors;

public class DiskMonitor : IMonitor
{
    public string Id => "disk";

    public Task<MonitorResult> CheckAsync(CancellationToken ct = default)
    {
        var drives = DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
            .ToList();

        if (drives.Count == 0)
        {
            return Task.FromResult(new MonitorResult(
                Id, "Disque dur", "💾",
                AlertLevel.Unknown, "—", "Aucun disque détecté"));
        }

        var worst = drives
            .Select(d => new
            {
                Drive = d,
                FreePct = (double)d.AvailableFreeSpace / d.TotalSize * 100,
                FreeGB = d.AvailableFreeSpace / (1024d * 1024 * 1024)
            })
            .OrderBy(x => x.FreePct)
            .First();

        var letter = worst.Drive.Name.TrimEnd('\\');

        var level = worst.FreePct switch
        {
            < 5 => AlertLevel.Critical,
            < 15 => AlertLevel.Warning,
            _ => AlertLevel.Ok
        };

        var status = level switch
        {
            AlertLevel.Critical => "Plein",
            AlertLevel.Warning => "Faible",
            _ => "OK"
        };

        var detail = $"{worst.FreeGB:F0} Go libres sur {letter}";

        var message = level switch
        {
            AlertLevel.Critical =>
                $"Votre disque {letter} est presque plein ({worst.FreeGB:F0} Go restants). " +
                "Cela peut bloquer votre ordinateur. Appelez Fix72 pour faire le ménage.",
            AlertLevel.Warning =>
                $"Votre disque {letter} se remplit ({worst.FreeGB:F0} Go restants). " +
                "Pensez à supprimer des fichiers inutiles ou contactez Fix72.",
            _ => null
        };

        MonitorAction? action = level >= AlertLevel.Warning
            ? new MonitorAction("Lancer le nettoyage de disque", "cleanmgr.exe")
            : null;

        return Task.FromResult(new MonitorResult(
            Id, "Disque dur", "💾", level, status, detail, message, action));
    }
}
