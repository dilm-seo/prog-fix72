using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using Fix72Agent.Models;
using Fix72Agent.Monitors;
using Fix72Agent.Services;

namespace Fix72Agent;

public class TrayApplication : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly List<IMonitor> _monitors;
    private readonly SettingsService _settings;
    private readonly WebhookReporter _webhook;
    private readonly Icon _iconGreen;
    private readonly Icon _iconOrange;
    private readonly Icon _iconRed;
    private readonly Dictionary<string, DateTime> _lastNotified = new();
    private readonly HashSet<string> _criticalAlertedIds = new();

    public List<MonitorResult> LatestResults { get; private set; } = new();
    public DateTime LastCheck { get; private set; }
    public event EventHandler? ResultsUpdated;

    private MainDashboard? _dashboard;

    public TrayApplication(SettingsService settings)
    {
        _settings = settings;
        _webhook = new WebhookReporter(settings);
        _monitors = new List<IMonitor>
        {
            new DiskMonitor(),
            new TemperatureMonitor(),
            new RamMonitor(),
            new WindowsUpdateMonitor(),
            new AntivirusMonitor(),
            new BootTimeMonitor(),
            new SmartMonitor(),
            new NetworkMonitor(),
            new BatteryMonitor()
        };

        _iconGreen = IconFactory.CreateShieldIcon(Color.FromArgb(46, 160, 67));
        _iconOrange = IconFactory.CreateShieldIcon(Color.FromArgb(255, 159, 28));
        _iconRed = IconFactory.CreateShieldIcon(Color.FromArgb(220, 53, 69));

        _trayIcon = new NotifyIcon
        {
            Icon = _iconGreen,
            Text = "Fix72 Agent",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _trayIcon.DoubleClick += (s, e) => ShowDashboard();
        _trayIcon.BalloonTipClicked += (s, e) => ShowDashboard();

        _timer = new System.Windows.Forms.Timer
        {
            Interval = Math.Max(1, _settings.Settings.CheckIntervalMinutes) * 60 * 1000
        };
        _timer.Tick += async (s, e) => await CheckAllAsync();
        _timer.Start();

        _ = CheckAllAsync();
    }

    private ContextMenuStrip BuildMenu()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var menu = new ContextMenuStrip();

        var header = new ToolStripMenuItem($"🛡️  Fix72 Agent  v{version?.ToString(3)}");
        header.Enabled = false;
        menu.Items.Add(header);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("📊  Voir l'état de mon PC", null, (s, e) => ShowDashboard());
        menu.Items.Add($"📞  Appeler Etienne — {_settings.Settings.TechnicianPhone}", null, (s, e) => CallTechnician());
        menu.Items.Add("💬  Envoyer un message", null, (s, e) => OpenMessageDialog());
        menu.Items.Add("🖥️  Démarrer une téléassistance", null, (s, e) => StartTeleassist());

        if (_webhook.IsConfigured)
            menu.Items.Add("📧  Envoyer un rapport à Fix72", null, async (s, e) => await SendManualReportAsync());

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("⚙️  Paramètres", null, (s, e) => OpenSettings());
        menu.Items.Add("❓  À propos", null, (s, e) => ShowAbout());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("✕  Quitter", null, (s, e) => ExitApp());

        return menu;
    }

    private async Task CheckAllAsync()
    {
        // Tous les monitors en parallèle — total = max(temps individuel) au lieu de la somme.
        var tasks = _monitors.Select(async m =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                return await m.CheckAsync(cts.Token);
            }
            catch (Exception ex)
            {
                return new MonitorResult(
                    m.Id, m.Id, "❓", AlertLevel.Unknown, "Erreur", ex.Message);
            }
        });

        var results = (await Task.WhenAll(tasks)).ToList();

        LatestResults = results;
        LastCheck = DateTime.Now;

        UpdateTrayIcon(results);
        if (_settings.Settings.NotificationsEnabled) NotifyIfNeeded(results);

        await TriggerWebhooksAsync(results);

        ResultsUpdated?.Invoke(this, EventArgs.Empty);
    }

    private async Task TriggerWebhooksAsync(List<MonitorResult> results)
    {
        if (!_webhook.IsConfigured) return;

        // Alertes critiques immédiates : on n'envoie qu'une fois par incident.
        if (_settings.Settings.ImmediateCriticalAlertsEnabled)
        {
            var currentCriticalIds = results
                .Where(r => r.Level == AlertLevel.Critical)
                .Select(r => r.Id)
                .ToHashSet();

            foreach (var r in results.Where(r => r.Level == AlertLevel.Critical))
            {
                if (_criticalAlertedIds.Add(r.Id))
                    await _webhook.SendImmediateCriticalAsync(r, results);
            }

            // Réinitialise les IDs résolus pour pouvoir re-déclencher si l'incident revient.
            _criticalAlertedIds.IntersectWith(currentCriticalIds);
        }

        // Rapport quotidien (uniquement si au moins une alerte, par défaut).
        await _webhook.MaybeSendDailyReportAsync(results);

        // Heartbeat hebdomadaire — preuve de vie de l'agent.
        await _webhook.MaybeSendHeartbeatAsync(results);
    }

    private void UpdateTrayIcon(List<MonitorResult> results)
    {
        var worst = results.Count > 0 ? results.Max(r => r.Level) : AlertLevel.Ok;

        _trayIcon.Icon = worst switch
        {
            AlertLevel.Critical => _iconRed,
            AlertLevel.Warning => _iconOrange,
            _ => _iconGreen
        };

        var alerts = results.Count(r => r.Level >= AlertLevel.Warning);
        _trayIcon.Text = alerts switch
        {
            0 => "Fix72 Agent — Tout va bien",
            1 => "Fix72 Agent — 1 alerte",
            _ => $"Fix72 Agent — {alerts} alertes"
        };
    }

    private void NotifyIfNeeded(List<MonitorResult> results)
    {
        var now = DateTime.Now;
        bool quietHours = _settings.Settings.QuietHoursEnabled && (now.Hour >= 22 || now.Hour < 8);

        foreach (var r in results)
        {
            if (r.Level < AlertLevel.Warning) continue;
            if (string.IsNullOrEmpty(r.Message)) continue;
            if (quietHours && r.Level != AlertLevel.Critical) continue;

            var lastTime = _lastNotified.GetValueOrDefault(r.Id);
            var minInterval = r.Level == AlertLevel.Critical
                ? TimeSpan.FromMinutes(30)
                : TimeSpan.FromHours(4);

            if (now - lastTime < minInterval) continue;

            _trayIcon.ShowBalloonTip(
                10000,
                $"Fix72 Agent — {r.DisplayName}",
                r.Message!,
                r.Level == AlertLevel.Critical ? ToolTipIcon.Error : ToolTipIcon.Warning);

            _lastNotified[r.Id] = now;
        }
    }

    /// <summary>Force une vérification immédiate (appelé par le bouton "Vérifier maintenant").</summary>
    public Task ForceCheckAsync() => CheckAllAsync();

    /// <summary>Affiche une notification toast simple — utilisée par les Quick Actions.</summary>
    public void ShowBalloon(string title, string message, bool warning)
    {
        _trayIcon.ShowBalloonTip(4000, title, message,
            warning ? ToolTipIcon.Warning : ToolTipIcon.Info);
    }

    public void ShowDashboard()
    {
        if (_dashboard == null || _dashboard.IsDisposed)
            _dashboard = new MainDashboard(this, _settings);

        _dashboard.Show();
        if (_dashboard.WindowState == FormWindowState.Minimized)
            _dashboard.WindowState = FormWindowState.Normal;
        _dashboard.BringToFront();
        _dashboard.Activate();
    }

    public void CallTechnician()
    {
        using var dlg = new CallDialog(_settings.Settings.TechnicianPhone);
        dlg.ShowDialog();
    }

    public async void OpenMessageDialog()
    {
        if (!_webhook.IsConfigured)
        {
            MessageBox.Show(
                "L'envoi de messages n'est pas configuré sur cet ordinateur.\n\n" +
                "Vous pouvez appeler Etienne au " + _settings.Settings.TechnicianPhone + ".",
                "Fix72 Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Rate-limit anti-spam : 1 message toutes les N minutes (5 par défaut).
        if (DateTime.TryParse(_settings.Settings.LastClientMessage, out var lastSent))
        {
            var cooldown = TimeSpan.FromMinutes(_settings.Settings.ClientMessageCooldownMinutes);
            var elapsed = DateTime.Now - lastSent;
            if (elapsed < cooldown)
            {
                var waitMin = (int)Math.Ceiling((cooldown - elapsed).TotalMinutes);
                MessageBox.Show(
                    $"Vous avez déjà envoyé un message il y a {(int)elapsed.TotalMinutes} minute(s).\n\n" +
                    $"Vous pourrez en envoyer un autre dans {waitMin} minute(s).\n\n" +
                    $"Pour une urgence, appelez Etienne au {_settings.Settings.TechnicianPhone}.",
                    "Fix72 Agent — Patientez un instant",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }

        using var dlg = new MessageDialog();
        if (dlg.ShowDialog() != DialogResult.OK) return;

        var ok = await _webhook.SendClientMessageAsync(dlg.Message, LatestResults);
        if (ok)
        {
            _settings.Settings.LastClientMessage = DateTime.Now.ToString("o");
            try { _settings.Save(); } catch { /* best-effort */ }

            _trayIcon.ShowBalloonTip(5000,
                "Fix72 Agent",
                "Message envoyé à Etienne. Il vous recontactera rapidement.",
                ToolTipIcon.Info);
        }
        else
        {
            MessageBox.Show(
                "L'envoi du message a échoué (quota atteint ou réseau indisponible).\n\n" +
                "Appelez Etienne au " + _settings.Settings.TechnicianPhone + ".",
                "Fix72 Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public void StartTeleassist()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Defaults.TeleassistanceUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Impossible d'ouvrir la page de téléassistance : " + ex.Message + "\n\n" +
                "Appelez Etienne au " + _settings.Settings.TechnicianPhone + ".",
                "Téléassistance — Fix72",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private async Task SendManualReportAsync()
    {
        if (!_webhook.IsConfigured)
        {
            MessageBox.Show(
                "Aucune URL de webhook configurée.\n\nÉditez settings.json pour ajouter le champ WebhookUrl.",
                "Fix72 Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Rate-limit anti-spam : 1 rapport manuel par tranche de N minutes (60 par défaut).
        if (DateTime.TryParse(_settings.Settings.LastManualReport, out var lastSent))
        {
            var cooldown = TimeSpan.FromMinutes(_settings.Settings.ManualReportCooldownMinutes);
            var elapsed = DateTime.Now - lastSent;
            if (elapsed < cooldown)
            {
                var waitMin = (int)Math.Ceiling((cooldown - elapsed).TotalMinutes);
                MessageBox.Show(
                    $"Vous avez déjà envoyé un rapport il y a {(int)elapsed.TotalMinutes} minute(s).\n\n" +
                    $"Vous pourrez en envoyer un nouveau dans {waitMin} minute(s).\n\n" +
                    $"Si c'est urgent, appelez Etienne au {_settings.Settings.TechnicianPhone}.",
                    "Fix72 Agent — Patientez un instant",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                Logger.Info("Rapport manuel bloqué par rate-limit.");
                return;
            }
        }

        var ok = await _webhook.SendManualReportAsync(LatestResults);
        if (ok)
        {
            _settings.Settings.LastManualReport = DateTime.Now.ToString("o");
            try { _settings.Save(); } catch { /* best-effort */ }

            _trayIcon.ShowBalloonTip(5000,
                "Fix72 Agent",
                "Rapport envoyé à Fix72. Etienne vous recontactera si nécessaire.",
                ToolTipIcon.Info);
        }
        else
        {
            MessageBox.Show(
                "L'envoi du rapport a échoué (quota atteint ou réseau indisponible).\n\n" +
                "Appelez Etienne au " + _settings.Settings.TechnicianPhone + " si c'est urgent.",
                "Fix72 Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenSettings()
    {
        using var dlg = new SettingsForm(_settings);
        dlg.ShowDialog();
        // Le menu contient le numéro de téléphone — on le rebuild pour refléter un éventuel changement.
        _trayIcon.ContextMenuStrip = BuildMenu();
    }

    public void ShowWelcomeIfFirstRun()
    {
        if (_settings.Settings.WelcomeShown) return;

        using var dlg = new WelcomeDialog(_settings);
        dlg.ShowDialog();

        _settings.Settings.WelcomeShown = true;
        try { _settings.Save(); } catch { /* best-effort */ }
    }

    private void ShowAbout()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        MessageBox.Show(
            $"Fix72 Agent v{version}\n\n" +
            "Surveillance bienveillante de votre PC\n" +
            "par Fix72 — Etienne Aubry\n\n" +
            $"Téléphone : {_settings.Settings.TechnicianPhone}\n" +
            "fix72.com",
            "À propos — Fix72 Agent",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ExitApp()
    {
        _timer.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Exit();
    }
}
