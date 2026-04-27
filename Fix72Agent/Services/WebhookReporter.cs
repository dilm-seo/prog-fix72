using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using Fix72Agent.Models;

namespace Fix72Agent.Services;

public class WebhookReporter
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly SettingsService _settings;

    public WebhookReporter(SettingsService settings)
    {
        _settings = settings;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.Settings.WebhookUrl);

    public Task<bool> SendDailyReportAsync(IReadOnlyList<MonitorResult> results, CancellationToken ct = default)
        => SendAsync("daily_report", results, alert: null, ct);

    public Task<bool> SendImmediateCriticalAsync(MonitorResult alert, IReadOnlyList<MonitorResult> all, CancellationToken ct = default)
        => SendAsync("critical_alert", all, alert, ct);

    public Task<bool> SendManualReportAsync(IReadOnlyList<MonitorResult> results, CancellationToken ct = default)
        => SendAsync("manual_report", results, alert: null, ct);

    public Task<bool> SendHeartbeatAsync(IReadOnlyList<MonitorResult> results, CancellationToken ct = default)
        => SendAsync("heartbeat", results, alert: null, ct);

    public Task<bool> SendClientMessageAsync(string clientMessage, IReadOnlyList<MonitorResult> results, CancellationToken ct = default)
        => SendAsync("client_message", results, alert: null, ct, clientMessage);

    public async Task MaybeSendDailyReportAsync(IReadOnlyList<MonitorResult> results, CancellationToken ct = default)
    {
        if (!IsConfigured || !_settings.Settings.DailyReportEnabled) return;

        var now = DateTime.Now;
        DateTime lastSent = DateTime.MinValue;
        DateTime.TryParse(_settings.Settings.LastReportSent, out lastSent);

        bool pastDailyHour = now.Hour >= _settings.Settings.DailyReportHour;
        bool firstTime = lastSent == DateTime.MinValue;
        bool overADay = (now - lastSent).TotalHours >= 23;

        if (!pastDailyHour) return;
        if (!firstTime && !overADay) return;
        if (!firstTime && lastSent.Date == now.Date) return;

        // Économie d'ops Make : si tout va bien, on n'envoie pas le rapport quotidien.
        // Le heartbeat hebdomadaire s'occupe de prouver que l'agent est en vie.
        if (_settings.Settings.OnlySendDailyWhenAlerts)
        {
            var worst = results.Count > 0 ? results.Max(r => r.Level) : AlertLevel.Ok;
            if (worst < AlertLevel.Warning) return;
        }

        var ok = await SendDailyReportAsync(results, ct);
        if (ok)
        {
            _settings.Settings.LastReportSent = now.ToString("o");
            _settings.Save();
        }
    }

    public async Task MaybeSendHeartbeatAsync(IReadOnlyList<MonitorResult> results, CancellationToken ct = default)
    {
        if (!IsConfigured || !_settings.Settings.WeeklyHeartbeatEnabled) return;

        var now = DateTime.Now;
        DateTime lastSent = DateTime.MinValue;
        DateTime.TryParse(_settings.Settings.LastHeartbeatSent, out lastSent);

        bool firstTime = lastSent == DateTime.MinValue;
        bool overAWeek = (now - lastSent).TotalDays >= 7;

        if (!firstTime && !overAWeek) return;

        var ok = await SendHeartbeatAsync(results, ct);
        if (ok)
        {
            _settings.Settings.LastHeartbeatSent = now.ToString("o");
            _settings.Save();
        }
    }

    /// <summary>
    /// Vérifie le plafond quotidien d'envois webhook. Retourne true si on est sous le plafond,
    /// false sinon (et incrémente le compteur si true).
    /// </summary>
    private bool TryConsumeDailyQuota()
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        if (_settings.Settings.DailyWebhookCountDate != today)
        {
            _settings.Settings.DailyWebhookCountDate = today;
            _settings.Settings.DailyWebhookCount = 0;
        }

        if (_settings.Settings.DailyWebhookCount >= _settings.Settings.MaxDailyWebhookCalls)
        {
            Logger.Warn($"Quota webhook quotidien atteint ({_settings.Settings.MaxDailyWebhookCalls}). Envoi bloqué.");
            return false;
        }

        _settings.Settings.DailyWebhookCount++;
        try { _settings.Save(); } catch { /* best-effort */ }
        return true;
    }

    private async Task<bool> SendAsync(
        string eventType,
        IReadOnlyList<MonitorResult> all,
        MonitorResult? alert,
        CancellationToken ct,
        string? clientMessage = null)
    {
        var url = _settings.Settings.WebhookUrl;
        if (string.IsNullOrWhiteSpace(url)) return false;

        if (!TryConsumeDailyQuota()) return false;

        var worstLevel = all.Count > 0 ? all.Max(r => r.Level) : AlertLevel.Ok;
        var alertCount = all.Count(r => r.Level >= AlertLevel.Warning);
        var criticalCount = all.Count(r => r.Level == AlertLevel.Critical);

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        var payload = new
        {
            event_type = eventType,
            timestamp = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ssK"),
            client = new
            {
                name = string.IsNullOrWhiteSpace(_settings.Settings.ClientName) ? "(non renseigné)" : _settings.Settings.ClientName,
                id = _settings.Settings.ClientId,
                computer = Environment.MachineName,
                user = Environment.UserName,
                phone = string.IsNullOrWhiteSpace(_settings.Settings.ClientPhone) ? "(non renseigné)" : _settings.Settings.ClientPhone
            },
            technician = new
            {
                phone = _settings.Settings.TechnicianPhone
            },
            summary = new
            {
                overall_level = worstLevel.ToString().ToLowerInvariant(),
                alert_count = alertCount,
                critical_count = criticalCount,
                headline = BuildHeadline(eventType, worstLevel, alertCount, criticalCount, alert)
            },
            triggered_by = alert == null ? null : new
            {
                id = alert.Id,
                name = alert.DisplayName,
                level = alert.Level.ToString().ToLowerInvariant(),
                status = alert.Status,
                detail = alert.Detail,
                message = alert.Message ?? ""
            },
            client_message = clientMessage ?? "",
            monitors = all.Select(r => new
            {
                id = r.Id,
                name = r.DisplayName,
                level = r.Level.ToString().ToLowerInvariant(),
                status = r.Status,
                detail = r.Detail,
                message = r.Message ?? ""
            }),
            agent_version = version
        };

        try
        {
            using var resp = await Http.PostAsJsonAsync(url, payload, ct);
            if (resp.IsSuccessStatusCode)
            {
                Logger.Info($"Webhook envoyé : {eventType}");
                return true;
            }
            Logger.Warn($"Webhook {eventType} : HTTP {(int)resp.StatusCode}");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Webhook {eventType} : {ex.Message}");
            return false;
        }
    }

    private static string BuildHeadline(string eventType, AlertLevel worst, int alerts, int criticals, MonitorResult? alert)
    {
        if (eventType == "critical_alert" && alert != null)
            return $"🔴 ALERTE CRITIQUE — {alert.DisplayName} : {alert.Status}";

        if (eventType == "heartbeat")
            return worst >= AlertLevel.Warning
                ? $"💓 Heartbeat — {alerts} alerte(s) en cours"
                : "💓 Heartbeat — tout va bien";

        if (eventType == "client_message")
            return "💬 Message du client";

        return worst switch
        {
            AlertLevel.Critical => $"🔴 {criticals} alerte(s) critique(s)",
            AlertLevel.Warning => $"⚠️ {alerts} alerte(s)",
            AlertLevel.Ok => "✅ Tout va bien",
            _ => "❓ État inconnu"
        };
    }
}
