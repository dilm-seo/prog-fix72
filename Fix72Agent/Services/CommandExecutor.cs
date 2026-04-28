using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    /// <summary>
    /// Nettoyage de disque exhaustif via le script PowerShell embarqué Clean-Disk.ps1 :
    /// caches dev (npm/pnpm/yarn/pip/gradle/maven/nuget/cargo/go/composer),
    /// caches navigateurs (Chrome/Edge/Brave/Firefox/Opera/Vivaldi/Thorium tous profils),
    /// caches IDE & apps Electron (VS Code, Cursor, JetBrains, Slack, Discord, Teams,
    /// Spotify, Notion, Obsidian, Claude…), Temp Windows utilisateur, corbeille.
    /// Phase admin sautée (l'agent tourne en user mode) — gain typique 1-15 Go.
    /// </summary>
    private async Task<ExecutionResult> HandleCleanTempAsync(CancellationToken ct)
    {
        // 1. Extraire le script depuis les ressources embarquées vers %TEMP%
        var scriptPath = Path.Combine(Path.GetTempPath(), $"Fix72-Clean-Disk-{Guid.NewGuid():N}.ps1");
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("Fix72Agent.Resources.Clean-Disk.ps1");
            if (stream == null)
            {
                return new ExecutionResult("failed", null, "Resource Clean-Disk.ps1 introuvable dans le binaire");
            }
            using var fs = File.Create(scriptPath);
            await stream.CopyToAsync(fs, ct);
        }
        catch (Exception ex)
        {
            return new ExecutionResult("failed", null, $"Extraction script : {ex.Message}");
        }

        // 2. Lancer PowerShell avec le script (mode user uniquement, pas d'UAC)
        var sb = new StringBuilder();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -NonInteractive -File \"{scriptPath}\" -Yes -NoElevate -SkipAdmin -SkipDism -SkipCleanmgr",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var proc = Process.Start(psi);
            if (proc == null) throw new Exception("Lancement powershell.exe échoué");

            proc.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
            proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) sb.AppendLine("[ERR] " + e.Data); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // Timeout 10 minutes (les vrais cas tournent en 30 s à 3 min)
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(10));
            await proc.WaitForExitAsync(timeoutCts.Token);

            var exit = proc.ExitCode;
            var stdout = sb.ToString();

            // 3. Parser le rapport final pour extraire :
            //    - "Espace libre apres : X.X GB"
            //    - "Total libere par le script  : X.X GB"
            //    - "Gain disque        : X.X GB"
            long? totalFreedBytes = ParseSizeLine(stdout, @"Total libere par le script\s*:\s*([\d,\.]+)\s*(B|KB|MB|GB)");
            long? gainBytes        = ParseSizeLine(stdout, @"Gain disque\s*:\s*([\d,\.]+)\s*(B|KB|MB|GB)");
            long? freeAfterBytes   = ParseSizeLine(stdout, @"Espace libre apres\s*:\s*([\d,\.]+)\s*(B|KB|MB|GB)");

            // Top 10 (lignes apres "Top 10 plus gros postes")
            var topMatches = Regex.Matches(stdout, @"^\s*([\d,\.]+\s*(?:B|KB|MB|GB))\s+(.+?)\s*$",
                RegexOptions.Multiline);
            var topFiltered = topMatches
                .Cast<Match>()
                .Where(m => stdout.IndexOf("Top 10", StringComparison.OrdinalIgnoreCase) >= 0
                            && m.Index > stdout.IndexOf("Top 10", StringComparison.OrdinalIgnoreCase))
                .Take(10)
                .Select(m => new { size = m.Groups[1].Value.Trim(), label = m.Groups[2].Value.Trim() })
                .ToArray();

            // Tronque le log si trop gros
            var logTrunc = stdout.Length > 4000 ? stdout[^4000..] : stdout;

            Logger.Info($"clean_temp (deep) : exit={exit} freed={totalFreedBytes ?? 0} bytes");

            return new ExecutionResult(
                exit == 0 ? "done" : "failed",
                new
                {
                    exit_code = exit,
                    bytes_freed = totalFreedBytes,
                    mb_freed = totalFreedBytes.HasValue ? totalFreedBytes.Value / 1024 / 1024 : (long?)null,
                    gb_freed = totalFreedBytes.HasValue ? Math.Round(totalFreedBytes.Value / 1024.0 / 1024.0 / 1024.0, 2) : (double?)null,
                    disk_gain_bytes = gainBytes,
                    free_after_bytes = freeAfterBytes,
                    top_targets = topFiltered,
                    log_tail = logTrunc,
                },
                exit == 0 ? null : $"PowerShell exit code {exit}");
        }
        catch (OperationCanceledException)
        {
            return new ExecutionResult("failed", null, "Timeout (>10 min) — script trop long");
        }
        catch (Exception ex)
        {
            return new ExecutionResult("failed", null, $"Exécution script : {ex.Message}");
        }
        finally
        {
            try { if (File.Exists(scriptPath)) File.Delete(scriptPath); } catch { }
        }
    }

    /// <summary>
    /// Extrait une valeur en bytes depuis une ligne du type "X.X GB" / "X.X MB".
    /// Le script PS utilise la virgule française comme séparateur décimal — on gère les deux.
    /// </summary>
    private static long? ParseSizeLine(string text, string regex)
    {
        var m = Regex.Match(text, regex, RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var raw = m.Groups[1].Value.Replace(',', '.').Replace(" ", "");
        if (!double.TryParse(raw, System.Globalization.NumberStyles.Any,
                             System.Globalization.CultureInfo.InvariantCulture, out double val))
            return null;
        var unit = m.Groups[2].Value.ToUpperInvariant();
        return unit switch
        {
            "GB" => (long)(val * 1024 * 1024 * 1024),
            "MB" => (long)(val * 1024 * 1024),
            "KB" => (long)(val * 1024),
            _    => (long)val,
        };
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
