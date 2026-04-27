using System.Diagnostics;
using System.Drawing;
using Fix72Agent.Models;
using Fix72Agent.Services;

namespace Fix72Agent;

/// <summary>
/// Popup affichée quand l'utilisateur clique sur une tuile en alerte.
/// Affiche le message d'explication + 0 à 2 boutons d'action concrète,
/// + le bouton "Appeler Fix72" toujours présent.
/// </summary>
public class AlertDialog : Form
{
    private static readonly Color BrandBlue = Color.FromArgb(0, 102, 204);
    private static readonly Color CriticalRed = Color.FromArgb(220, 53, 69);
    private static readonly Color WarningOrange = Color.FromArgb(255, 159, 28);

    private readonly Action _onCallTechnician;

    public AlertDialog(MonitorResult result, Action onCallTechnician)
    {
        _onCallTechnician = onCallTechnician;

        var headerColor = result.Level switch
        {
            AlertLevel.Critical => CriticalRed,
            AlertLevel.Warning => WarningOrange,
            _ => BrandBlue
        };

        Text = $"{result.DisplayName} — Fix72";
        Size = new Size(520, 360);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9);
        Icon = IconFactory.CreateShieldIcon(headerColor);

        BuildHeader(result, headerColor);
        BuildMessage(result);
        BuildButtons(result);
    }

    private void BuildHeader(MonitorResult result, Color headerColor)
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 80,
            BackColor = headerColor
        };

        var icon = new Label
        {
            Text = result.Icon,
            Font = new Font("Segoe UI Emoji", 28),
            ForeColor = Color.White,
            Location = new Point(20, 18),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        header.Controls.Add(icon);

        var title = new Label
        {
            Text = result.DisplayName,
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(80, 16),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        header.Controls.Add(title);

        var status = new Label
        {
            Text = $"{result.Status} — {result.Detail}",
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.White,
            Location = new Point(80, 46),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        header.Controls.Add(status);

        Controls.Add(header);
    }

    private void BuildMessage(MonitorResult result)
    {
        var message = new Label
        {
            Text = result.Message ?? "",
            Font = new Font("Segoe UI", 11),
            ForeColor = Color.FromArgb(40, 40, 40),
            Location = new Point(20, 100),
            Size = new Size(480, 130),
            BackColor = Color.Transparent
        };
        Controls.Add(message);
    }

    private void BuildButtons(MonitorResult result)
    {
        // Bouton 1 : action concrète (si le monitor en propose une)
        int x = 20;
        const int btnW = 230;
        const int btnH = 50;
        int y = Height - 110;

        if (result.Action != null)
        {
            var btnAction = MakeButton(result.Action.Label, BrandBlue, Color.White, true);
            btnAction.Location = new Point(x, y);
            btnAction.Size = new Size(btnW, btnH);
            btnAction.Click += (s, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = result.Action.Command,
                        Arguments = result.Action.Args,
                        UseShellExecute = true
                    });
                    Logger.Info($"Action lancée : {result.Action.Command}");
                    Close();
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Action échouée ({result.Action.Command}) : {ex.Message}");
                    MessageBox.Show("Impossible de lancer cette action : " + ex.Message,
                        "Fix72 Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            Controls.Add(btnAction);
            x += btnW + 10;
        }

        // Bouton 2 : appeler Fix72 (toujours)
        var btnCall = MakeButton("📞  Appeler Fix72", Color.FromArgb(40, 167, 69), Color.White, true);
        btnCall.Location = new Point(x, y);
        btnCall.Size = new Size(result.Action != null ? btnW : 470, btnH);
        btnCall.Click += (s, e) =>
        {
            _onCallTechnician();
            Close();
        };
        Controls.Add(btnCall);

        // Bouton "Fermer" (discret, en dessous)
        var btnClose = MakeButton("Fermer", Color.FromArgb(240, 240, 240), Color.FromArgb(60, 60, 60), false);
        btnClose.Location = new Point(390, y + btnH + 8);
        btnClose.Size = new Size(110, 32);
        btnClose.DialogResult = DialogResult.Cancel;
        Controls.Add(btnClose);
        CancelButton = btnClose;
    }

    private static Button MakeButton(string text, Color bg, Color fg, bool bold)
    {
        var b = new Button
        {
            Text = text,
            BackColor = bg,
            ForeColor = fg,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10, bold ? FontStyle.Bold : FontStyle.Regular),
            Cursor = Cursors.Hand,
            TextAlign = ContentAlignment.MiddleCenter
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }
}
