using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Fix72Agent.Services;

/// <summary>
/// Vérifie les releases GitHub et installe silencieusement une version plus récente.
/// Endpoint : GET https://api.github.com/repos/dilm-seo/prog-fix72/releases/latest
/// Asset attendu dans chaque release : Fix72Agent-Setup.exe
/// </summary>
public class AutoUpdater
{
    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/dilm-seo/prog-fix72/releases/latest";

    private const string InstallerAssetName = "Fix72Agent-Setup.exe";

    private static readonly HttpClient Http;

    static AutoUpdater()
    {
        Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        Http.DefaultRequestHeaders.Add("User-Agent", "Fix72Agent-AutoUpdater/1.0");
    }

    private readonly Action<string, string, bool> _showBalloon;

    /// <param name="showBalloon">Callback (title, message, isWarning) pour afficher une bulle tray.</param>
    public AutoUpdater(Action<string, string, bool> showBalloon)
    {
        _showBalloon = showBalloon;
    }

    public record UpdateCheckResult(
        bool UpdateAvailable,
        Version? LatestVersion,
        string? DownloadUrl,
        string? Error);

    /// <summary>Interroge GitHub et détermine si une version plus récente est disponible.</summary>
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return new UpdateCheckResult(false, null, null, $"HTTP {(int)resp.StatusCode}");

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var tagName = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var versionStr = tagName.TrimStart('v');
            if (!Version.TryParse(versionStr, out var latestVersion))
                return new UpdateCheckResult(false, null, null, $"Tag de version invalide : {tagName}");

            var current = Assembly.GetExecutingAssembly().GetName().Version
                          ?? new Version(0, 0, 0);

            if (latestVersion <= current)
            {
                Logger.Info($"AutoUpdater : version courante {current.ToString(3)} est à jour");
                return new UpdateCheckResult(false, latestVersion, null, null);
            }

            // Cherche l'asset Fix72Agent-Setup.exe
            string? downloadUrl = null;
            if (doc.RootElement.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.Equals(InstallerAssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }
            }

            if (downloadUrl == null)
                return new UpdateCheckResult(false, latestVersion, null,
                    $"Asset '{InstallerAssetName}' introuvable dans la release {tagName}");

            Logger.Info($"AutoUpdater : nouvelle version {latestVersion.ToString(3)} disponible → {downloadUrl}");
            return new UpdateCheckResult(true, latestVersion, downloadUrl, null);
        }
        catch (Exception ex)
        {
            Logger.Warn($"AutoUpdater.CheckAsync : {ex.Message}");
            return new UpdateCheckResult(false, null, null, ex.Message);
        }
    }

    /// <summary>
    /// Télécharge l'installateur et le lance en mode silencieux.
    /// L'installateur Inno Setup avec /CLOSEAPPLICATIONS force la fermeture de l'exe en cours.
    /// </summary>
    public async Task<bool> DownloadAndInstallAsync(
        string downloadUrl,
        Version latestVersion,
        CancellationToken ct = default)
    {
        var installerPath = Path.Combine(
            Path.GetTempPath(),
            $"Fix72Agent-Setup-{latestVersion.ToString(3)}.exe");

        try
        {
            _showBalloon(
                "Fix72 Agent — Mise à jour",
                $"Téléchargement de la version {latestVersion.ToString(3)}...",
                false);

            using var resp = await Http.GetAsync(
                downloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                ct);
            resp.EnsureSuccessStatusCode();

            using (var fs = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var stream = await resp.Content.ReadAsStreamAsync(ct))
            {
                await stream.CopyToAsync(fs, ct);
            }

            _showBalloon(
                "Fix72 Agent — Mise à jour",
                $"Installation de la version {latestVersion.ToString(3)} en cours...",
                false);

            Logger.Info($"AutoUpdater : lancement installateur {installerPath}");

            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                // /VERYSILENT : pas de fenêtre
                // /NORESTART : pas de redémarrage auto
                // /CLOSEAPPLICATIONS : Inno Setup ferme Fix72Agent.exe s'il tourne encore
                Arguments = "/VERYSILENT /NORESTART /CLOSEAPPLICATIONS",
                UseShellExecute = true, // requis pour l'élévation UAC
            });

            // Petit délai pour laisser apparaître la bulle avant la fermeture
            await Task.Delay(2000, ct);
            Application.Exit();
            return true;
        }
        catch (OperationCanceledException)
        {
            Logger.Warn("AutoUpdater : téléchargement annulé");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Warn($"AutoUpdater.DownloadAndInstallAsync : {ex.Message}");
            _showBalloon("Fix72 Agent — Mise à jour échouée", ex.Message, true);
            try { if (File.Exists(installerPath)) File.Delete(installerPath); } catch { }
            return false;
        }
    }

    /// <summary>
    /// Vérifie et installe en une seule étape.
    /// Si <paramref name="manualCheck"/> = true, notifie aussi quand déjà à jour.
    /// </summary>
    public async Task<bool> CheckAndUpdateAsync(
        bool manualCheck = false,
        CancellationToken ct = default)
    {
        var result = await CheckAsync(ct);

        if (result.Error != null)
        {
            Logger.Warn($"AutoUpdater : {result.Error}");
            if (manualCheck)
                _showBalloon("Fix72 Agent — Mise à jour",
                    "Vérification impossible (réseau indisponible).", true);
            return false;
        }

        if (!result.UpdateAvailable || result.DownloadUrl == null || result.LatestVersion == null)
        {
            if (manualCheck)
                _showBalloon("Fix72 Agent",
                    "Vous utilisez déjà la dernière version.", false);
            return false;
        }

        return await DownloadAndInstallAsync(result.DownloadUrl, result.LatestVersion, ct);
    }
}
