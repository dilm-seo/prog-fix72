using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;

namespace Fix72Agent;

/// <summary>
/// Fenêtre de saisie d'un message à envoyer à Fix72.
/// Le message saisi est joint au prochain envoi webhook (event_type = "client_message").
/// </summary>
[SupportedOSPlatform("windows")]
public class MessageDialog : Form
{
    private static readonly Color BrandBlue = Color.FromArgb(0, 102, 204);
    private static readonly Color BrandBlueDark = Color.FromArgb(0, 70, 140);

    public string Message { get; private set; } = "";

    public MessageDialog()
    {
        Text = "Envoyer un message — Fix72";
        Size = new Size(560, 480);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9);
        Icon = IconFactory.CreateShieldIcon(BrandBlue);

        BuildHeader();
        var textBox = BuildTextArea();
        BuildButtons(textBox);
    }

    private void BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 70
        };
        header.Paint += (s, e) =>
        {
            using var brush = new LinearGradientBrush(
                header.ClientRectangle, BrandBlue, BrandBlueDark, LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(brush, header.ClientRectangle);
        };

        var title = new Label
        {
            Text = "💬   Envoyer un message à Etienne",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(20, 20),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        header.Controls.Add(title);

        Controls.Add(header);
    }

    private TextBox BuildTextArea()
    {
        var instr = new Label
        {
            Text = "Décrivez votre problème ou votre demande. Etienne vous répondra rapidement.",
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(60, 60, 60),
            Location = new Point(25, 90),
            Size = new Size(510, 40),
            BackColor = Color.Transparent
        };
        Controls.Add(instr);

        var textBox = new TextBox
        {
            Location = new Point(25, 135),
            Size = new Size(510, 220),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Segoe UI", 11),
            BorderStyle = BorderStyle.FixedSingle,
            AcceptsReturn = true
        };
        Controls.Add(textBox);

        var hint = new Label
        {
            Text = "Maximum 1000 caractères. Pas besoin de signature : votre nom est joint automatiquement.",
            Font = new Font("Segoe UI", 8, FontStyle.Italic),
            ForeColor = Color.Gray,
            Location = new Point(25, 360),
            Size = new Size(510, 20),
            BackColor = Color.Transparent
        };
        Controls.Add(hint);

        return textBox;
    }

    private void BuildButtons(TextBox textBox)
    {
        var btnSend = new Button
        {
            Text = "📤   Envoyer le message",
            Location = new Point(280, 395),
            Size = new Size(180, 40),
            BackColor = BrandBlue,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btnSend.FlatAppearance.BorderSize = 0;
        btnSend.Click += (s, e) =>
        {
            var msg = textBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(msg))
            {
                MessageBox.Show("Veuillez écrire un message avant d'envoyer.",
                    "Fix72 Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (msg.Length > 1000) msg = msg[..1000];
            Message = msg;
            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(btnSend);
        AcceptButton = btnSend;

        var btnCancel = new Button
        {
            Text = "Annuler",
            Location = new Point(465, 395),
            Size = new Size(80, 40),
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
}
