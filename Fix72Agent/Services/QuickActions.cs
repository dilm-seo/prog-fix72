using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Fix72Agent.Services;

/// <summary>
/// Actions rapides exposées dans le dashboard pour des opérations courantes
/// que les clients seniors n'osent pas faire eux-mêmes.
/// </summary>
[SupportedOSPlatform("windows")]
public static class QuickActions
{
    [DllImport("Shell32.dll")]
    private static extern int SHEmptyRecycleBin(IntPtr hWnd, string? pszRootPath, uint dwFlags);

    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    public record QuickAction(
        string Id,
        string Icon,
        string Title,
        string Description,
        Action Execute);

    public static IReadOnlyList<QuickAction> GetAll() => new QuickAction[]
    {
        new("emptybin", "🗑️", "Vider la corbeille",
            "Libère de l'espace en supprimant définitivement les fichiers de la corbeille.",
            EmptyRecycleBin),

        new("flushdns", "🌐", "Réparer Internet",
            "Vide le cache DNS — résout beaucoup de problèmes de pages qui ne chargent pas.",
            FlushDns),

        new("restartexplorer", "🪟", "Redémarrer le bureau",
            "Relance l'Explorateur Windows. Utile si la barre des tâches a buggé.",
            RestartExplorer),

        new("opensecurity", "🛡️", "Sécurité Windows",
            "Ouvre la fenêtre de l'antivirus pour lancer une analyse.",
            OpenSecurity),
    };

    public static void EmptyRecycleBin()
    {
        try
        {
            SHEmptyRecycleBin(IntPtr.Zero, null,
                SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
            Logger.Info("Corbeille vidée.");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Vidage corbeille échoué : {ex.Message}");
        }
    }

    public static void FlushDns()
    {
        try
        {
            var psi = new ProcessStartInfo("ipconfig", "/flushdns")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
            Logger.Info("Cache DNS vidé.");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Flush DNS échoué : {ex.Message}");
        }
    }

    public static void RestartExplorer()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                try { p.Kill(); } catch { }
            }
            // Windows relance automatiquement explorer.exe
            Logger.Info("Explorer redémarré.");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Redémarrage Explorer échoué : {ex.Message}");
        }
    }

    public static void OpenSecurity()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "windowsdefender:",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Warn($"Ouverture Sécurité Windows échouée : {ex.Message}");
        }
    }
}
