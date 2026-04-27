using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;
using Fix72Agent.Services;

namespace Fix72Agent;

[SupportedOSPlatform("windows")]
public class WelcomeDialog : Form
{
    private static readonly Color BrandBlue = Color.FromArgb(0, 102, 204);
    private static readonly Color BrandBlueDark = Color.FromArgb(0, 70, 140);

    public WelcomeDialog(SettingsService settings)
    {
        Text = "Bienvenue — Fix72 Agent";
        Size = new Size(640, 660);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9);
        Icon = IconFactory.CreateShieldIcon(BrandBlue);

        var name = string.IsNullOrWhiteSpace(settings.Settings.ClientName) ? "" : $" {settings.Settings.ClientName}";

        BuildHeader(name);
        BuildBody();
        BuildFooter();
    }

    private void BuildHeader(string name)
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 160
        };
        header.Paint += (s, e) =>
        {
            using var brush = new LinearGradientBrush(
                header.ClientRectangle, BrandBlue, BrandBlueDark, LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(brush, header.ClientRectangle);
        };

        // Logo Fix72 PNG embarqué (avec fallback bouclier généré si introuvable)
        var logoImg = LogoLoader.Load();
        var logoPic = new PictureBox
        {
            Image = logoImg ?? (Image)IconFactory.CreateShieldBitmap(Color.White, 96),
            Size = logoImg != null ? new Size(220, 96) : new Size(96, 96),
            Location = new Point(30, 32),
            BackColor = Color.Transparent,
            SizeMode = PictureBoxSizeMode.Zoom
        };
        header.Controls.Add(logoPic);

        int textX = logoImg != null ? 270 : 160;

        var title = new Label
        {
            Text = "Bienvenue" + name,
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(textX, 48),
            Size = new Size(640 - textX - 20, 36),
            BackColor = Color.Transparent
        };
        header.Controls.Add(title);

        var sub = new Label
        {
            Text = "Fix72 Agent veille sur votre ordinateur.",
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(220, 235, 255),
            Location = new Point(textX + 2, 86),
            Size = new Size(640 - textX - 20, 30),
            BackColor = Color.Transparent
        };
        header.Controls.Add(sub);

        Controls.Add(header);
    }

    private void BuildBody()
    {
        var body = new Label
        {
            Text =
                "Ce logiciel discret surveille en permanence l'état de votre ordinateur :\n" +
                "espace disque, mémoire, mises à jour, antivirus, température, santé du disque...\n\n" +
                "Vous ne le verrez quasiment jamais. Une petite icône en bouclier reste\n" +
                "dans la zone de notification (en bas à droite, près de l'horloge).",
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(50, 50, 50),
            Location = new Point(35, 180),
            Size = new Size(560, 100),
            BackColor = Color.Transparent
        };
        Controls.Add(body);

        // Bloc "engagement de confidentialité"
        var privacyBox = new Panel
        {
            Location = new Point(35, 290),
            Size = new Size(560, 130),
            BackColor = Color.FromArgb(232, 245, 233)
        };
        privacyBox.Paint += (s, e) =>
        {
            using var pen = new Pen(Color.FromArgb(46, 160, 67), 1);
            e.Graphics.DrawRectangle(pen, 0, 0, privacyBox.Width - 1, privacyBox.Height - 1);
        };

        var privacyTitle = new Label
        {
            Text = "🔒  Vos engagements de confidentialité",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 100, 50),
            Location = new Point(15, 12),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        privacyBox.Controls.Add(privacyTitle);

        var privacyContent = new Label
        {
            Text =
                "✓  Aucune donnée personnelle n'est transmise\n" +
                "✓  Aucun accès à vos fichiers, photos, mots de passe\n" +
                "✓  Pas de caméra, pas de micro\n" +
                "✓  Vous gardez le contrôle total",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(50, 70, 55),
            Location = new Point(15, 38),
            Size = new Size(540, 90),
            BackColor = Color.Transparent
        };
        privacyBox.Controls.Add(privacyContent);

        Controls.Add(privacyBox);

        // Message de fin
        var bottomMsg = new Label
        {
            Text = "Si quelque chose ne va pas, je serai prévenu et je vous rappellerai.\n" +
                   "Vous pouvez aussi me joindre à tout moment en cliquant sur l'icône.",
            Font = new Font("Segoe UI", 9, FontStyle.Italic),
            ForeColor = Color.FromArgb(80, 80, 80),
            Location = new Point(35, 435),
            Size = new Size(560, 50),
            BackColor = Color.Transparent
        };
        Controls.Add(bottomMsg);
    }

    private void BuildFooter()
    {
        // Footer avec téléphone + bouton, sur fond gris clair pour bien séparer
        var footer = new Panel
        {
            Location = new Point(0, 500),
            Size = new Size(640, 95),
            BackColor = Color.FromArgb(245, 247, 250)
        };
        Controls.Add(footer);

        var phone = new Label
        {
            Text = "📞   Etienne — Fix72",
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            ForeColor = BrandBlue,
            Location = new Point(35, 18),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        footer.Controls.Add(phone);

        var phoneNumber = new Label
        {
            Text = Defaults.TechnicianPhone,
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 40, 40),
            Location = new Point(35, 40),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        footer.Controls.Add(phoneNumber);

        var btnClose = new Button
        {
            Text = "J'ai compris",
            Location = new Point(450, 26),
            Size = new Size(160, 50),
            BackColor = BrandBlue,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            Cursor = Cursors.Hand,
            DialogResult = DialogResult.OK
        };
        btnClose.FlatAppearance.BorderSize = 0;
        footer.Controls.Add(btnClose);
        AcceptButton = btnClose;
    }
}
