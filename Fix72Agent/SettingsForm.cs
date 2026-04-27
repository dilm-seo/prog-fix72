using System.Drawing;
using Fix72Agent.Services;

namespace Fix72Agent;

/// <summary>
/// Fenêtre de paramètres — édition GUI au lieu de toucher au settings.json.
/// Restée volontairement minimale : les options "expert" (webhook, quotas) restent
/// en lecture seule visible mais éditables uniquement via le fichier.
/// </summary>
public class SettingsForm : Form
{
    private readonly SettingsService _settings;
    private static readonly Color BrandBlue = Color.FromArgb(0, 102, 204);

    private TextBox _txtClientName = null!;
    private TextBox _txtClientPhone = null!;
    private CheckBox _chkStartup = null!;
    private CheckBox _chkNotifications = null!;
    private CheckBox _chkQuietHours = null!;
    private NumericUpDown _numInterval = null!;

    public SettingsForm(SettingsService settings)
    {
        _settings = settings;

        Text = "Paramètres — Fix72 Agent";
        Size = new Size(520, 540);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9);
        Icon = IconFactory.CreateShieldIcon(BrandBlue);

        BuildHeader();
        BuildForm();
        BuildFooterButtons();
    }

    private void BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 60,
            BackColor = BrandBlue
        };
        var title = new Label
        {
            Text = "⚙️  Paramètres",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(20, 14),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        header.Controls.Add(title);
        Controls.Add(header);
    }

    private void BuildForm()
    {
        int y = 85;
        const int x = 25;
        const int labelW = 220;
        const int inputX = 250;

        AddSection("Identification du client", ref y);

        AddLabel("Nom / prénom du client :", x, y, labelW);
        _txtClientName = new TextBox
        {
            Text = _settings.Settings.ClientName,
            Location = new Point(inputX, y - 3),
            Size = new Size(225, 25),
            Font = new Font("Segoe UI", 10)
        };
        Controls.Add(_txtClientName);
        y += 35;

        AddLabel("Téléphone du client :", x, y, labelW);
        _txtClientPhone = new TextBox
        {
            Text = _settings.Settings.ClientPhone,
            Location = new Point(inputX, y - 3),
            Size = new Size(225, 25),
            Font = new Font("Segoe UI", 10)
        };
        Controls.Add(_txtClientPhone);
        y += 50;

        AddSection("Comportement", ref y);

        _chkStartup = new CheckBox
        {
            Text = "Démarrer automatiquement avec Windows",
            Checked = _settings.Settings.StartWithWindows,
            Location = new Point(x, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 10)
        };
        Controls.Add(_chkStartup);
        y += 30;

        _chkNotifications = new CheckBox
        {
            Text = "Afficher les notifications d'alerte",
            Checked = _settings.Settings.NotificationsEnabled,
            Location = new Point(x, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 10)
        };
        Controls.Add(_chkNotifications);
        y += 30;

        _chkQuietHours = new CheckBox
        {
            Text = "Mode silencieux entre 22h et 8h",
            Checked = _settings.Settings.QuietHoursEnabled,
            Location = new Point(x, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 10)
        };
        Controls.Add(_chkQuietHours);
        y += 40;

        AddLabel("Vérification toutes les :", x, y, labelW);
        _numInterval = new NumericUpDown
        {
            Minimum = 5,
            Maximum = 240,
            Value = Math.Clamp(_settings.Settings.CheckIntervalMinutes, 5, 240),
            Location = new Point(inputX, y - 3),
            Size = new Size(70, 25),
            Font = new Font("Segoe UI", 10)
        };
        Controls.Add(_numInterval);
        var lblMin = new Label
        {
            Text = "minutes",
            Location = new Point(inputX + 80, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 10)
        };
        Controls.Add(lblMin);
        y += 50;

        var info = new Label
        {
            Text = "ℹ️  Pour les paramètres avancés (webhook, quotas), éditez :\n" +
                   "%APPDATA%\\Fix72Agent\\settings.json",
            Font = new Font("Segoe UI", 8),
            ForeColor = Color.FromArgb(100, 100, 100),
            Location = new Point(x, y),
            Size = new Size(450, 35),
            BackColor = Color.Transparent
        };
        Controls.Add(info);
    }

    private void AddSection(string title, ref int y)
    {
        var lbl = new Label
        {
            Text = title.ToUpper(),
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = BrandBlue,
            Location = new Point(25, y),
            AutoSize = true
        };
        Controls.Add(lbl);
        y += 25;
    }

    private void AddLabel(string text, int x, int y, int width)
    {
        var lbl = new Label
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, 22),
            Font = new Font("Segoe UI", 10)
        };
        Controls.Add(lbl);
    }

    private void BuildFooterButtons()
    {
        var btnSave = new Button
        {
            Text = "Enregistrer",
            Location = new Point(280, 460),
            Size = new Size(110, 36),
            BackColor = BrandBlue,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Cursor = Cursors.Hand,
            DialogResult = DialogResult.OK
        };
        btnSave.FlatAppearance.BorderSize = 0;
        btnSave.Click += (s, e) => SaveSettings();
        Controls.Add(btnSave);
        AcceptButton = btnSave;

        var btnCancel = new Button
        {
            Text = "Annuler",
            Location = new Point(395, 460),
            Size = new Size(95, 36),
            BackColor = Color.White,
            ForeColor = Color.FromArgb(80, 80, 80),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10),
            Cursor = Cursors.Hand,
            DialogResult = DialogResult.Cancel
        };
        btnCancel.FlatAppearance.BorderColor = Color.LightGray;
        btnCancel.FlatAppearance.BorderSize = 1;
        Controls.Add(btnCancel);
        CancelButton = btnCancel;
    }

    private void SaveSettings()
    {
        _settings.Settings.ClientName = _txtClientName.Text.Trim();
        _settings.Settings.ClientPhone = _txtClientPhone.Text.Trim();
        _settings.Settings.StartWithWindows = _chkStartup.Checked;
        _settings.Settings.NotificationsEnabled = _chkNotifications.Checked;
        _settings.Settings.QuietHoursEnabled = _chkQuietHours.Checked;
        _settings.Settings.CheckIntervalMinutes = (int)_numInterval.Value;

        try
        {
            _settings.Save();
            AutoStartService.SetEnabled(_chkStartup.Checked);
            Logger.Info("Paramètres sauvegardés depuis l'UI.");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Sauvegarde paramètres échouée : {ex.Message}");
            MessageBox.Show("Impossible d'enregistrer les paramètres : " + ex.Message,
                "Fix72 Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
