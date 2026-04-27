using System.Text.Json;
using Fix72Agent.Models;

namespace Fix72Agent.Services;

public class SettingsService
{
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Fix72Agent");

    private static readonly string SettingsPath = Path.Combine(AppDataDir, "settings.json");

    public AppSettings Settings { get; private set; } = new();

    public string DataDirectory => AppDataDir;

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Impossible de lire settings.json ({ex.Message}). Utilisation des valeurs par défaut.");
            Settings = new AppSettings();
        }

        Sanitize();
    }

    /// <summary>
    /// Verrouille les paramètres dans des bornes raisonnables — empêche un settings.json
    /// modifié à la main (intentionnellement ou non) de spammer Make.com.
    /// </summary>
    private void Sanitize()
    {
        bool changed = false;

        if (Settings.CheckIntervalMinutes < 5)
        {
            Logger.Warn($"CheckIntervalMinutes={Settings.CheckIntervalMinutes} < 5, forcé à 5.");
            Settings.CheckIntervalMinutes = 5;
            changed = true;
        }
        if (Settings.CheckIntervalMinutes > 1440)
        {
            Settings.CheckIntervalMinutes = 1440;
            changed = true;
        }

        if (Settings.DailyReportHour < 0 || Settings.DailyReportHour > 23)
        {
            Settings.DailyReportHour = 9;
            changed = true;
        }

        if (Settings.MaxDailyWebhookCalls < 1) Settings.MaxDailyWebhookCalls = 1;
        if (Settings.MaxDailyWebhookCalls > 100) Settings.MaxDailyWebhookCalls = 100;

        if (Settings.ManualReportCooldownMinutes < 5)
        {
            Settings.ManualReportCooldownMinutes = 5;
            changed = true;
        }

        // Reset des timestamps absurdes (futur, ou très vieux).
        ResetIfBogus(Settings.LastReportSent, v => Settings.LastReportSent = v, ref changed);
        ResetIfBogus(Settings.LastHeartbeatSent, v => Settings.LastHeartbeatSent = v, ref changed);
        ResetIfBogus(Settings.LastManualReport, v => Settings.LastManualReport = v, ref changed);

        if (changed)
        {
            try { Save(); } catch { /* best-effort */ }
        }
    }

    private static void ResetIfBogus(string raw, Action<string> setter, ref bool changed)
    {
        if (string.IsNullOrEmpty(raw)) return;
        if (!DateTime.TryParse(raw, out var dt))
        {
            setter("");
            changed = true;
            return;
        }
        if (dt > DateTime.Now.AddHours(1) || dt < DateTime.Now.AddYears(-2))
        {
            setter("");
            changed = true;
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(AppDataDir);
        var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(SettingsPath, json);
    }
}
