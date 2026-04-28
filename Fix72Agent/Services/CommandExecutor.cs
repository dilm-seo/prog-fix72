using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using Fix72Agent.Models;

namespace Fix72Agent.Services;

/// <summary>
/// Exécute les commandes envoyées par le dashboard admin.
/// Toutes les méthodes sont best-effort, retournent un (status, result, error).
/// </summary>
[SupportedOSPlatform("windows")]
public class CommandExecutor
{
    public record ExecutionResult(string Status, object? Result, string? ErrorMessage);

    private readonly Action _onForceCheck;
    private readonly Action<string, string> _showNotification;

    /// <summary>
    /// </summary>
    /// <param name="onForceCheck">Callback : déclenche un re-scan immédiat des capteurs.</param>
    /// <param name="showNotification">Callback : affiche une bulle dans la zone de notif (titre, message).</param>
    public CommandExecutor(Action onForceCheck, Action<string, string> showNotification)
    {
        _onForceCheck = onForceCheck;
        _showNotification = showNotification;
    }

    public async Task<ExecutionResult> ExecuteAsync(string command, JsonElement parameters, CancellationToken ct = default)
    {
        try
        {
            return command switch
            {
                "force_check"             => HandleForceCheck(),
                "clean_temp"              => await HandleCleanTempAsync(ct),
                "empty_recycle_bin"       => HandleEmptyRecycleBin(),
                "notify_user"             => HandleNotifyUser(parameters),
                "install_windows_updates" => await HandleInstallWindowsUpdatesAsync(ct),
                "request_anydesk"         => HandleRequestAnyDesk(),
                "get_event_logs"          => HandleGetEventLogs(),
                _                         => new ExecutionResult("failed", null, $"Commande inconnue : {command}")
            };
        }
        catch (Exception ex)
        {
            Logger.Warn($"CommandExecutor[{command}] exception : {ex.Message}");
            return new ExecutionResult("failed", null, ex.Message);
        }
    }

    // ── force_check ─────────────────────────────────────────────────
    private ExecutionResult HandleForceCheck()
    {
        _onForceCheck();
        return new ExecutionResult("done", new { triggered = true }, null);
    }

    // ── clean_temp ──────────────────────────────────────────────────
    private async Task<ExecutionResult> HandleCleanTempAsync(CancellationToken ct)
    {
        long bytesFreed = 0;
        int filesDeleted = 0;
        int errors = 0;

        var paths = new[]
        {
            Environment.ExpandEnvironmentVariables("%TEMP%"),
            Environment.ExpandEnvironmentVariables("%LOCALAPPDATA%\\Temp"),
            Environment.ExpandEnvironmentVariables("%LOCALAPPDATA%\\Microsoft\\Windows\\INetCache"),
            Environment.ExpandEnvironmentVariables("%WINDIR%\\Temp"),
        };

        foreach (var p in paths)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(p)) continue;

            try
            {
                foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(f);
                        var size = info.Length;
                        info.Delete();
                        bytesFreed += size;
                        filesDeleted++;
                    }
                    catch
                    {
                        errors++; // fichiers verrouillés en cours d'utilisation = OK on saute
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"clean_temp dans {p} : {ex.Message}");
                errors++;
            }
        }

        await Task.CompletedTask;
        Logger.Info($"clean_temp : {filesDeleted} fichiers supprimés, {bytesFreed / 1024 / 1024} Mo libérés ({errors} ignorés)");
        return new ExecutionResult("done", new
        {
            files_deleted = filesDeleted,
            bytes_freed = bytesFreed,
            mb_freed = bytesFreed / 1024 / 1024,
            files_skipped = errors,
        }, null);
    }

    // ── empty_recycle_bin ───────────────────────────────────────────
    private static ExecutionResult HandleEmptyRecycleBin()
    {
        QuickActions.EmptyRecycleBin();
        return new ExecutionResult("done", new { ok = true }, null);
    }

    // ── notify_user ─────────────────────────────────────────────────
    private ExecutionResult HandleNotifyUser(JsonElement parameters)
    {
        var msg = "Etienne (Fix72) souhaite vous prévenir.";
        if (parameters.ValueKind == JsonValueKind.Object &&
            parameters.TryGetProperty("message", out var mEl) &&
            mEl.ValueKind == JsonValueKind.String)
        {
            var s = mEl.GetString();
            if (!string.IsNullOrWhiteSpace(s)) msg = s;
        }
        _showNotification("Message de Fix72", msg);
        return new ExecutionResult("done", new { delivered = true, message = msg }, null);
    }

    // ── install_windows_updates ─────────────────────────────────────
    /// <summary>
    /// Lance "wuauclt /detectnow" puis "UsoClient ScanInstallWait" pour forcer
    /// l'installation des MAJ détectées. Méthode légère ; pour un contrôle fin
    /// on basculera sur l'API COM Microsoft.Update.Session en Phase 2.
    /// </summary>
    private async Task<ExecutionResult> HandleInstallWindowsUpdatesAsync(CancellationToken ct)
    {
        try
        {
            // UsoClient est l'orchestrateur Windows Update sur Win10/11
            await RunProcessAsync("UsoClient", "StartScan", ct);
            await RunProcessAsync("UsoClient", "StartDownload", ct);
            await RunProcessAsync("UsoClient", "StartInstall", ct);
            return new ExecutionResult("done", new { method = "UsoClient", note = "Lancement asynchrone" }, null);
        }
        catch (Exception ex)
        {
            return new ExecutionResult("failed", null, ex.Message);
        }
    }

    // ── request_anydesk ─────────────────────────────────────────────
    private ExecutionResult HandleRequestAnyDesk()
    {
        // Tente d'ouvrir AnyDesk si installé. Si absent, ouvre la page de téléchargement.
        var anydeskPath = TryFindAnyDesk();
        if (anydeskPath != null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(anydeskPath) { UseShellExecute = true });
                _showNotification("Téléassistance", "AnyDesk est lancé. Communiquez votre code à Etienne.");
                return new ExecutionResult("done", new { launched = true, path = anydeskPath }, null);
            }
            catch (Exception ex)
            {
                return new ExecutionResult("failed", null, $"Lancement AnyDesk échoué : {ex.Message}");
            }
        }

        // Pas installé : ouvre la page web Fix72
        try
        {
            Process.Start(new ProcessStartInfo(Defaults.TeleassistanceUrl) { UseShellExecute = true });
            return new ExecutionResult("done", new { launched = false, opened_url = true }, null);
        }
        catch (Exception ex)
        {
            return new ExecutionResult("failed", null, $"AnyDesk introuvable et navigateur indisponible : {ex.Message}");
        }
    }

    private static string? TryFindAnyDesk()
    {
        var candidates = new[]
        {
            Environment.ExpandEnvironmentVariables("%PROGRAMFILES%\\AnyDesk\\AnyDesk.exe"),
            Environment.ExpandEnvironmentVariables("%PROGRAMFILES(X86)%\\AnyDesk\\AnyDesk.exe"),
            Environment.ExpandEnvironmentVariables("%LOCALAPPDATA%\\Programs\\AnyDesk\\AnyDesk.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    // ── get_event_logs ──────────────────────────────────────────────
    private ExecutionResult HandleGetEventLogs()
    {
        try
        {
            var entries = new List<object>();

            // EventLog n'est pas dispo proprement sans elevation — on utilise wevtutil pour les 20 derniers
            var psi = new ProcessStartInfo("wevtutil", "qe System /c:20 /rd:true /f:text")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi);
            if (p == null) throw new Exception("Lancement wevtutil échoué");
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);
            if (output.Length > 8000) output = output[..8000] + "\n[...tronqué...]";
            return new ExecutionResult("done", new { source = "System", text = output }, null);
        }
        catch (Exception ex)
        {
            return new ExecutionResult("failed", null, ex.Message);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────
    private static async Task RunProcessAsync(string fileName, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(fileName, args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi);
        if (p == null) throw new Exception($"Impossible de lancer {fileName}");
        await p.WaitForExitAsync(ct);
    }
}
