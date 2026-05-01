using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Fix72Agent.Models;

namespace Fix72Agent.Services;

/// <summary>
/// Client HTTP pour les Edge Functions Supabase de Fix72 :
///   POST /agent-ingest  — push d'événements (alerte, rapport quotidien, heartbeat, manuel)
///   GET  /agent-poll    — récupère les commandes pending pour cet agent
///   POST /agent-result  — reporte le résultat d'une commande exécutée
///
/// Auth : headers X-Agent-Id + X-Agent-Secret. Pas de JWT.
/// </summary>
public class Fix72ApiClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly SettingsService _settings;
    private readonly AgentAuthService _auth;

    public Fix72ApiClient(SettingsService settings, AgentAuthService auth)
    {
        _settings = settings;
        _auth = auth;
        Logger.Info($"Fix72ApiClient initialisé — URL : {settings.Settings.Fix72ApiUrl}");
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.Settings.Fix72ApiUrl);

    private string BaseUrl => _settings.Settings.Fix72ApiUrl.TrimEnd('/');

    private void AddAuthHeaders(HttpRequestMessage req)
    {
        req.Headers.Add("apikey", Defaults.Fix72ApiKey);
        req.Headers.Add("Authorization", $"Bearer {Defaults.Fix72ApiKey}");
        req.Headers.Add("X-Agent-Id", _auth.AgentId);
        req.Headers.Add("X-Agent-Secret", _auth.AgentSecret);
    }

    /// <summary>Pousse un payload identique à celui du webhook Make.com vers /agent-ingest.</summary>
    public async Task<bool> IngestAsync(object payload, CancellationToken ct = default)
    {
        if (!IsConfigured) return false;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/agent-ingest")
            {
                Content = JsonContent.Create(payload),
            };
            AddAuthHeaders(req);
            using var resp = await Http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                Logger.Info("Fix72 API ingest OK");
                return true;
            }
            var body = await resp.Content.ReadAsStringAsync(ct);
            Logger.Warn($"Fix72 API ingest HTTP {(int)resp.StatusCode} — URL: {BaseUrl}/agent-ingest — body: {body}");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Fix72 API ingest exception : {ex.Message}");
            return false;
        }
    }

    public record PendingCommand(string Id, string Command, JsonElement Params);

    /// <summary>Récupère les commandes en attente pour cet agent (max 10). Auto-marque comme "picked".</summary>
    public async Task<IReadOnlyList<PendingCommand>> PollCommandsAsync(CancellationToken ct = default)
    {
        if (!IsConfigured) return Array.Empty<PendingCommand>();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/agent-poll");
            AddAuthHeaders(req);
            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                if ((int)resp.StatusCode != 404)
                {
                    var errBody = await resp.Content.ReadAsStringAsync(ct);
                    Logger.Warn($"Fix72 API poll HTTP {(int)resp.StatusCode} — URL: {BaseUrl}/agent-poll — body: {errBody}");
                }
                return Array.Empty<PendingCommand>();
            }

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("commands", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return Array.Empty<PendingCommand>();

            var list = new List<PendingCommand>();
            foreach (var el in arr.EnumerateArray())
            {
                var id = el.GetProperty("id").GetString() ?? "";
                var cmd = el.GetProperty("command").GetString() ?? "";
                var prm = el.TryGetProperty("params", out var p) ? p.Clone() : default;
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(cmd))
                {
                    list.Add(new PendingCommand(id, cmd, prm));
                }
            }
            return list;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Fix72 API poll exception : {ex.Message}");
            return Array.Empty<PendingCommand>();
        }
    }

    /// <summary>Reporte le résultat d'exécution d'une commande.</summary>
    public async Task<bool> ReportResultAsync(
        string commandId,
        string status,
        object? result = null,
        string? errorMessage = null,
        CancellationToken ct = default)
    {
        if (!IsConfigured) return false;
        try
        {
            var body = new
            {
                command_id = commandId,
                status,
                result,
                error_message = errorMessage,
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/agent-result")
            {
                Content = JsonContent.Create(body),
            };
            AddAuthHeaders(req);
            using var resp = await Http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode) return true;
            var text = await resp.Content.ReadAsStringAsync(ct);
            Logger.Warn($"Fix72 API result HTTP {(int)resp.StatusCode} : {text}");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Fix72 API result exception : {ex.Message}");
            return false;
        }
    }
}
