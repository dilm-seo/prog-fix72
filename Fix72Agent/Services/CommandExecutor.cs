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
    private readonly Func<CancellationToken, Task<bool>>? _onCheckUpdate;

    /// <param name="onForceCheck">Callback : déclenche un re-scan immédiat des capteurs.</param>
    /// <param name="showNotification">Callback : affiche une bulle dans la zone de notif (titre, message).</param>
    /// <param name="onCheckUpdate">Callback optionnel : déclenche une vérification + install de mise à jour.</param>
    public CommandExecutor(
        Action onForceCheck,
        Action<string, string> showNotification,
        Func<CancellationToken, Task<bool>>? onCheckUpdate = null)
    {
        _onForceCheck = onForceCheck;
        _showNotification = showNotification;
        _onCheckUpdate = onCheckUpdate;
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
                "reset_network"           => await HandleResetNetworkAsync(ct),
                "winget_upgrade"          => await HandleWingetUpgradeAsync(ct),
                "list_top_processes"      => HandleListTopProcesses(),
                "kill_process"            => HandleKillProcess(parameters),
                "check_update"            => await HandleCheckUpdateAsync(ct),
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

    // ── reset_network ───────────────────────────────────────────────
    /// <summary>
    /// Reset réseau "soft" en mode utilisateur : flush DNS, release/renew IP,
    /// restart de l'adaptateur. Les commandes admin (winsock reset, ip reset,
    /// hosts file) sont skipées avec un message explicite — elles nécessitent
    /// l'agent en service Windows (Phase 2).
    /// </summary>
    private async Task<ExecutionResult> HandleResetNetworkAsync(CancellationToken ct)
    {
        var steps = new List<object>();
        bool anySuccess = false;

        async Task RunStep(string label, string exe, string args)
        {
            try
            {
                var (code, stdout, stderr) = await RunCaptureAsync(exe, args, TimeSpan.FromSeconds(30), ct);
                var ok = code == 0;
                if (ok) anySuccess = true;
                steps.Add(new {
                    step = label, exit_code = code, ok,
                    output = (stdout + (string.IsNullOrWhiteSpace(stderr) ? "" : "\n" + stderr)).Trim()
                });
            }
            catch (Exception ex)
            {
                steps.Add(new { step = label, ok = false, error = ex.Message });
            }
        }

        await RunStep("flush_dns",   "ipconfig", "/flushdns");
        await RunStep("release_ip",  "ipconfig", "/release");
        await RunStep("renew_ip",    "ipconfig", "/renew");
        await RunStep("flush_arp",   "arp",      "-d *");
        await RunStep("nbtstat_release", "nbtstat", "-R");
        await RunStep("nbtstat_renew",   "nbtstat", "-RR");

        // Restart du Wi-Fi via netsh wlan (user-level OK)
        await RunStep("wifi_disconnect", "netsh",  "wlan disconnect");
        await Task.Delay(1500, ct);
        await RunStep("wifi_connect",    "netsh",  "wlan connect name=*");

        Logger.Info($"reset_network : {steps.Count} étapes, anySuccess={anySuccess}");

        return new ExecutionResult(
            anySuccess ? "done" : "failed",
            new
            {
                steps,
                note = "Reset complet (winsock, ip stack, hosts) requiert l'agent en mode admin (Phase 2)."
            },
            anySuccess ? null : "Toutes les étapes ont échoué."
        );
    }

    // ── winget_upgrade ──────────────────────────────────────────────
    /// <summary>
    /// Lance "winget upgrade --all" en mode silencieux. En user-mode,
    /// les apps system-scope échoueront — c'est documenté.
    /// </summary>
    private async Task<ExecutionResult> HandleWingetUpgradeAsync(CancellationToken ct)
    {
        // Vérifie que winget existe
        var wingetPath = Environment.ExpandEnvironmentVariables(
            "%LOCALAPPDATA%\\Microsoft\\WindowsApps\\winget.exe");
        bool hasWinget = File.Exists(wingetPath) || File.Exists("winget.exe");

        if (!hasWinget)
        {
            // Cherche dans PATH
            try
            {
                var (c, _, _) = await RunCaptureAsync("winget", "--version", TimeSpan.FromSeconds(5), ct);
                if (c != 0) hasWinget = false; else hasWinget = true;
            }
            catch { hasWinget = false; }
        }

        if (!hasWinget)
        {
            return new ExecutionResult("failed", null,
                "winget non installé sur ce PC (Windows 10 1809+ / Windows 11 requis).");
        }

        // 1. List avant upgrade
        var (codeList, listOut, _) = await RunCaptureAsync(
            "winget", "upgrade --include-unknown --accept-source-agreements",
            TimeSpan.FromMinutes(1), ct);

        var availableCount = listOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(2) // skip header
            .Count(l => l.Trim().Length > 0 && !l.StartsWith("---") && !l.Contains("upgrade") && !l.Contains("disponible"));

        // 2. Upgrade
        var (code, stdout, stderr) = await RunCaptureAsync(
            "winget",
            "upgrade --all --include-unknown --silent --accept-source-agreements --accept-package-agreements --disable-interactivity",
            TimeSpan.FromMinutes(20),
            ct);

        var combined = (stdout + "\n" + stderr).Trim();
        var tail = combined.Length > 5000 ? combined[^5000..] : combined;

        // Compte succès/échecs en parsant la sortie
        int successCount = Regex.Matches(stdout, @"Successfully (?:installed|upgraded)", RegexOptions.IgnoreCase).Count;
        int failCount    = Regex.Matches(stdout, @"(?:failed|échec|erreur)", RegexOptions.IgnoreCase).Count;

        Logger.Info($"winget_upgrade : exit={code} success={successCount} fail={failCount}");

        return new ExecutionResult(
            code == 0 ? "done" : "failed",
            new
            {
                exit_code = code,
                upgrades_available = availableCount,
                success_count = successCount,
                fail_count = failCount,
                log_tail = tail,
                note = "Les apps installées en system-scope nécessitent admin (Phase 2)."
            },
            code == 0 ? null : $"winget exit code {code}"
        );
    }

    // ── list_top_processes ──────────────────────────────────────────
    /// <summary>
    /// Top 10 process par RAM utilisée. Permet à l'admin de voir ce qui
    /// consomme le plus avant de décider d'un kill_process.
    /// </summary>
    private static ExecutionResult HandleListTopProcesses()
    {
        var processes = Process.GetProcesses()
            .Where(p => { try { return p.WorkingSet64 > 1024 * 1024; } catch { return false; } })
            .Select(p =>
            {
                try
                {
                    return new
                    {
                        pid = p.Id,
                        name = p.ProcessName,
                        ram_mb = (long)(p.WorkingSet64 / 1024 / 1024),
                        threads = p.Threads.Count,
                        start_time = (string?)null,
                    };
                }
                catch { return null; }
            })
            .Where(x => x != null)
            .OrderByDescending(x => x!.ram_mb)
            .Take(10)
            .ToArray();

        return new ExecutionResult("done", new
        {
            count = processes.Length,
            processes,
            note = "Pour fermer un process : commande 'kill_process' avec param { process_name: 'nom' }",
        }, null);
    }

    // ── kill_process ────────────────────────────────────────────────
    private static readonly string[] ProtectedProcesses = new[]
    {
        "system", "idle", "csrss", "winlogon", "services", "lsass", "smss",
        "wininit", "lsm", "svchost", "dwm", "Fix72Agent",
    };

    /// <summary>
    /// Ferme tous les process portant ce nom (sans .exe). Refuse les process
    /// système critiques. Le client peut être notifié via Show-Balloon.
    /// </summary>
    private ExecutionResult HandleKillProcess(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty("process_name", out var nameEl) ||
            nameEl.ValueKind != JsonValueKind.String)
        {
            return new ExecutionResult("failed", null,
                "Paramètre process_name (string) requis. Ex: { \"process_name\": \"chrome\" }");
        }

        var rawName = nameEl.GetString() ?? "";
        var name = rawName.Trim().ToLowerInvariant().Replace(".exe", "");
        if (string.IsNullOrEmpty(name))
            return new ExecutionResult("failed", null, "process_name vide.");

        if (ProtectedProcesses.Any(p => p.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return new ExecutionResult("failed", null,
                $"Process '{name}' protégé (critique système ou agent lui-même). Refusé.");
        }

        var killed = new List<object>();
        var failed = new List<object>();

        foreach (var p in Process.GetProcessesByName(name))
        {
            try
            {
                var pid = p.Id;
                var ram = p.WorkingSet64 / 1024 / 1024;
                p.Kill(entireProcessTree: true);
                p.WaitForExit(3000);
                killed.Add(new { pid, name, ram_mb = ram });
            }
            catch (Exception ex)
            {
                failed.Add(new { pid = p.Id, name, error = ex.Message });
            }
        }

        Logger.Info($"kill_process[{name}] : {killed.Count} fermés, {failed.Count} échoués");

        if (killed.Count == 0 && failed.Count == 0)
        {
            return new ExecutionResult("failed", null,
                $"Aucun process trouvé avec le nom '{name}'.");
        }

        return new ExecutionResult(
            killed.Count > 0 ? "done" : "failed",
            new { killed_count = killed.Count, failed_count = failed.Count, killed, failed },
            failed.Count > 0 && killed.Count == 0 ? "Tous les kills ont échoué (peut-être process admin)." : null
        );
    }

    // ── check_update ────────────────────────────────────────────────
    private async Task<ExecutionResult> HandleCheckUpdateAsync(CancellationToken ct)
    {
        if (_onCheckUpdate == null)
            return new ExecutionResult("failed", null, "Auto-update non configuré sur cet agent");

        var launched = await _onCheckUpdate(ct);
        return new ExecutionResult("done", new { update_launched = launched }, null);
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

    /// <summary>
    /// Lance un process en capturant stdout + stderr, avec timeout. Retourne
    /// (exit_code, stdout, stderr).
    /// </summary>
    private static async Task<(int exitCode, string stdout, string stderr)> RunCaptureAsync(
        string fileName, string args, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(fileName, args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi);
        if (p == null) throw new Exception($"Impossible de lancer {fileName}");

        var sbOut = new StringBuilder();
        var sbErr = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
        p.ErrorDataReceived  += (_, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await p.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            throw new TimeoutException($"{fileName} a dépassé {timeout.TotalSeconds} s.");
        }

        return (p.ExitCode, sbOut.ToString(), sbErr.ToString());
    }
}
