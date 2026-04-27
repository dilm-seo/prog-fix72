using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.Versioning;
using Fix72Agent.Controls;
using Fix72Agent.Models;
using Fix72Agent.Services;

namespace Fix72Agent;

[SupportedOSPlatform("windows")]
public class MainDashboard : Form
{
    private readonly TrayApplication _tray;
    private readonly SettingsService _settings;

    // Tabs state
    private int _activeTab;
    private RoundedPanel[] _tabButtons = null!;
    private Panel[] _tabPanels = null!;

    // Dashboard tab widgets
    private Panel _tilesContainer = null!;
    private RoundedPanel _statusBanner = null!;
    private Label _lblOverallStatus = null!;
    private Button _btnRefresh = null!;
    private Button _btnToggleAll = null!;
    private bool _showAllTiles = false;

    /// <summary>Capteurs prioritaires affichés par défaut. Les autres sont masqués
    /// (sauf s'ils sont en alerte — on ne cache jamais une alerte).</summary>
    private static readonly HashSet<string> EssentialMonitorIds = new()
    {
        "disk", "ram", "windowsupdate", "antivirus", "smart", "network"
    };

    // Footer
    private Label _lblLastCheck = null!;

    private static readonly Color BrandBlue = Color.FromArgb(0, 102, 204);
    private static readonly Color BrandBlueDark = Color.FromArgb(0, 70, 140);
    private static readonly Color SoftBg = Color.FromArgb(244, 247, 252);
    private static readonly Color TextDark = Color.FromArgb(45, 55, 72);
    private static readonly Color TextMuted = Color.FromArgb(110, 120, 140);

    public MainDashboard(TrayApplication tray, SettingsService settings)
    {
        _tray = tray;
        _settings = settings;

        Text = "Fix72 Agent";
        Size = new Size(820, 920);
        MinimumSize = new Size(820, 700);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        BackColor = SoftBg;
        Font = new Font("Segoe UI", 9);
        DoubleBuffered = true;
        Icon = IconFactory.CreateShieldIcon(BrandBlue);

        // Ordre d'ajout important : Footer (Bottom) → ContactStrip (Bottom) → Header (Top) → TabBar (Top) → TabContent (Fill)
        BuildFooter();
        BuildContactStrip();
        BuildHeader();
        BuildTabBar();
        BuildTabContent();

        SetActiveTab(0);
        RefreshDashboard();

        _tray.ResultsUpdated += OnResultsUpdated;
        FormClosed += (s, e) => _tray.ResultsUpdated -= OnResultsUpdated;
    }

    private void OnResultsUpdated(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(new Action(RefreshDashboard));
        else RefreshDashboard();
    }

    // ─────────────────────────────────────────────────────────────────
    //  HEADER
    // ─────────────────────────────────────────────────────────────────
    private void BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 110
        };
        header.Paint += PaintHeaderBackground;

        var logo = LogoLoader.Load();
        if (logo != null)
        {
            var logoPic = new PictureBox
            {
                Image = logo,
                Location = new Point(24, 18),
                Size = new Size(190, 75),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            header.Controls.Add(logoPic);
        }
        else
        {
            var fallback = new Label
            {
                Text = "Fix72 Agent",
                Font = new Font("Segoe UI", 20, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(28, 28),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            header.Controls.Add(fallback);
        }

        var greeting = string.IsNullOrWhiteSpace(_settings.Settings.ClientName)
            ? "Votre PC est entre de bonnes mains"
            : $"Bonjour {_settings.Settings.ClientName} — votre PC est entre de bonnes mains";

        var sub = new Label
        {
            Text = greeting,
            Font = new Font("Segoe UI", 11),
            ForeColor = Color.FromArgb(220, 235, 255),
            Location = new Point(230, 44),
            Size = new Size(560, 30),
            BackColor = Color.Transparent
        };
        header.Controls.Add(sub);

        Controls.Add(header);
    }

    private static void PaintHeaderBackground(object? sender, PaintEventArgs e)
    {
        var panel = (Panel)sender!;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var brush = new LinearGradientBrush(
            panel.ClientRectangle, BrandBlue, BrandBlueDark, LinearGradientMode.Vertical))
        {
            g.FillRectangle(brush, panel.ClientRectangle);
        }

        using var deco = new SolidBrush(Color.FromArgb(20, 255, 255, 255));
        g.FillEllipse(deco, panel.Width - 200, -100, 300, 300);
        using var deco2 = new SolidBrush(Color.FromArgb(15, 255, 255, 255));
        g.FillEllipse(deco2, panel.Width - 350, 30, 200, 200);
        using var deco3 = new SolidBrush(Color.FromArgb(12, 255, 255, 255));
        g.FillEllipse(deco3, panel.Width - 450, -50, 150, 150);
    }

    // ─────────────────────────────────────────────────────────────────
    //  TAB BAR
    // ─────────────────────────────────────────────────────────────────
    private void BuildTabBar()
    {
        var tabBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 60,
            BackColor = SoftBg,
            Padding = new Padding(20, 12, 20, 0)
        };
        Controls.Add(tabBar);

        var labels = new[]
        {
            ("🏠", "Tableau de bord"),
            ("⚡", "Actions rapides"),
            ("❓", "À propos / Aide"),
        };

        _tabButtons = new RoundedPanel[3];
        int totalW = (820 - 40) / 3 - 8;
        int x = 0;
        for (int i = 0; i < 3; i++)
        {
            var btn = new RoundedPanel
            {
                Location = new Point(x, 0),
                Size = new Size(totalW, 44),
                CornerRadius = 10,
                BorderColor = Color.FromArgb(220, 226, 236),
                BorderWidth = 1,
                FillColor = Color.White,
                HoverFillColor = Color.FromArgb(240, 245, 252),
                BackColor = SoftBg,
                Cursor = Cursors.Hand,
                Tag = i
            };

            var lbl = new Label
            {
                Text = $"{labels[i].Item1}   {labels[i].Item2}",
                Font = new Font("Segoe UI Emoji", 10, FontStyle.Bold),
                ForeColor = TextMuted,
                Location = new Point(0, 0),
                Size = new Size(totalW, 44),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                UseCompatibleTextRendering = true
            };
            btn.Controls.Add(lbl);

            int idx = i;
            void OnClick(object? s, EventArgs e) => SetActiveTab(idx);
            btn.Click += OnClick;
            lbl.Click += OnClick;

            HookHover(btn);
            tabBar.Controls.Add(btn);
            _tabButtons[i] = btn;
            x += totalW + 12;
        }
    }

    private void SetActiveTab(int idx)
    {
        _activeTab = idx;

        for (int i = 0; i < 3; i++)
        {
            var active = i == idx;
            _tabButtons[i].FillColor = active ? BrandBlue : Color.White;
            _tabButtons[i].HoverFillColor = active ? BrandBlueDark : Color.FromArgb(240, 245, 252);
            _tabButtons[i].BorderColor = active ? BrandBlue : Color.FromArgb(220, 226, 236);
            var lbl = (Label)_tabButtons[i].Controls[0];
            lbl.ForeColor = active ? Color.White : TextMuted;
            _tabButtons[i].Invalidate();
        }

        if (_tabPanels != null)
        {
            for (int i = 0; i < _tabPanels.Length; i++)
                _tabPanels[i].Visible = (i == idx);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  TAB CONTENT (3 panels stacked, only one visible)
    // ─────────────────────────────────────────────────────────────────
    private void BuildTabContent()
    {
        var container = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SoftBg
        };
        Controls.Add(container);
        container.BringToFront();

        var tab1 = BuildTabDashboard();
        var tab2 = BuildTabQuickActions();
        var tab3 = BuildTabAbout();

        tab1.Dock = tab2.Dock = tab3.Dock = DockStyle.Fill;
        container.Controls.Add(tab1);
        container.Controls.Add(tab2);
        container.Controls.Add(tab3);

        _tabPanels = new[] { tab1, tab2, tab3 };
    }

    // ─── TAB 1 : DASHBOARD ──────────────────────────────────────────
    private Panel _tabDashboardPanel = null!;
    private Label _lblSection = null!;

    private Panel BuildTabDashboard()
    {
        _tabDashboardPanel = new Panel
        {
            BackColor = SoftBg,
            AutoScroll = true,
            Padding = new Padding(0)
        };

        _statusBanner = new RoundedPanel
        {
            Location = new Point(20, 10),
            Size = new Size(700, 80),
            FillColor = Color.FromArgb(232, 245, 233),
            BorderColor = Color.FromArgb(46, 160, 67),
            BorderWidth = 2,
            CornerRadius = 12,
            DropShadow = false,
            BackColor = SoftBg
        };
        _lblOverallStatus = new Label
        {
            Text = "Vérification en cours…",
            Font = new Font("Segoe UI Emoji", 14, FontStyle.Bold),
            ForeColor = TextDark,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(20, 0, 20, 0),
            BackColor = Color.Transparent,
            UseCompatibleTextRendering = true
        };
        _statusBanner.Controls.Add(_lblOverallStatus);
        _tabDashboardPanel.Controls.Add(_statusBanner);

        _lblSection = new Label
        {
            Text = "Vos indicateurs",
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            ForeColor = TextDark,
            Location = new Point(20, 105),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        _tabDashboardPanel.Controls.Add(_lblSection);

        _btnRefresh = new Button
        {
            Text = "🔄  Vérifier maintenant",
            Location = new Point(550, 100),
            Size = new Size(170, 28),
            BackColor = Color.White,
            ForeColor = BrandBlue,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Cursor = Cursors.Hand,
            UseCompatibleTextRendering = true
        };
        _btnRefresh.FlatAppearance.BorderColor = BrandBlue;
        _btnRefresh.FlatAppearance.BorderSize = 1;
        _btnRefresh.Click += async (s, e) =>
        {
            _btnRefresh.Enabled = false;
            _btnRefresh.Text = "  Vérification en cours…";
            try { await _tray.ForceCheckAsync(); }
            finally
            {
                _btnRefresh.Text = "🔄  Vérifier maintenant";
                _btnRefresh.Enabled = true;
            }
        };
        _tabDashboardPanel.Controls.Add(_btnRefresh);

        _tilesContainer = new Panel
        {
            Location = new Point(20, 140),
            Size = new Size(700, 360),
            BackColor = SoftBg
        };
        _tabDashboardPanel.Controls.Add(_tilesContainer);

        _btnToggleAll = new Button
        {
            Text = "▼  Afficher tous les indicateurs",
            Location = new Point(20, 510),
            Size = new Size(700, 36),
            BackColor = Color.White,
            ForeColor = BrandBlue,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Visible = false
        };
        _btnToggleAll.FlatAppearance.BorderColor = Color.FromArgb(220, 226, 236);
        _btnToggleAll.FlatAppearance.BorderSize = 1;
        _btnToggleAll.Click += (s, e) =>
        {
            _showAllTiles = !_showAllTiles;
            RefreshDashboard();
        };
        _tabDashboardPanel.Controls.Add(_btnToggleAll);

        // PILIER : on contrôle 100% le redimensionnement nous-mêmes.
        _tabDashboardPanel.Resize += (s, e) => RelayoutDashboardTab();

        return _tabDashboardPanel;
    }

    /// <summary>
    /// Repositionne et redimensionne TOUS les éléments du tab Dashboard à partir
    /// de la largeur réelle du conteneur. Plus fiable que les Anchor.
    /// </summary>
    private void RelayoutDashboardTab()
    {
        if (_tabDashboardPanel == null) return;

        int w = _tabDashboardPanel.ClientSize.Width;
        if (w <= 0) return;

        const int margin = 20;
        int contentW = w - 2 * margin;
        if (contentW < 100) contentW = 100;

        // Status banner
        _statusBanner.Bounds = new Rectangle(margin, 10, contentW, 80);

        // Section label : reste à gauche
        _lblSection.Location = new Point(margin, 105);

        // Bouton refresh : aligné à droite
        _btnRefresh.Location = new Point(w - margin - _btnRefresh.Width, 100);

        // Conteneur de tuiles : prend toute la largeur
        _tilesContainer.Location = new Point(margin, 140);
        _tilesContainer.Width = contentW;
        // Hauteur recalculée par LayoutTiles

        LayoutTiles();

        // Toggle : sous les tuiles, pleine largeur
        _btnToggleAll.Location = new Point(margin, _tilesContainer.Bottom + 10);
        _btnToggleAll.Width = contentW;
    }

    // ─── TAB 2 : QUICK ACTIONS ──────────────────────────────────────
    private Panel BuildTabQuickActions()
    {
        var panel = new Panel
        {
            BackColor = SoftBg,
            AutoScroll = true,
            Padding = new Padding(0, 20, 0, 20)
        };

        var intro = new Label
        {
            Text = "Petites actions utiles que vous pouvez lancer en un clic.\n" +
                   "Pas de risque : chaque action demande confirmation avant de s'exécuter.",
            Font = new Font("Segoe UI", 10),
            ForeColor = TextMuted,
            Location = new Point(20, 20),
            Size = new Size(760, 50),
            BackColor = Color.Transparent
        };
        panel.Controls.Add(intro);

        var actions = QuickActions.GetAll();
        int x = 20;
        int y = 90;
        const int tileW = 365;
        const int tileH = 110;
        const int gap = 15;

        for (int i = 0; i < actions.Count; i++)
        {
            var action = actions[i];

            var p = new RoundedPanel
            {
                Location = new Point(x, y),
                Size = new Size(tileW, tileH),
                FillColor = Color.White,
                HoverFillColor = Color.FromArgb(245, 250, 255),
                BorderColor = Color.FromArgb(228, 232, 240),
                CornerRadius = 12,
                Cursor = Cursors.Hand,
                BackColor = SoftBg
            };

            var lblIcon = new Label
            {
                Text = action.Icon,
                Font = new Font("Segoe UI Emoji", 28),
                Location = new Point(15, 22),
                Size = new Size(70, 70),
                BackColor = Color.Transparent,
                UseCompatibleTextRendering = true,
                TextAlign = ContentAlignment.MiddleCenter
            };
            p.Controls.Add(lblIcon);

            var lblTitle = new Label
            {
                Text = action.Title,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = TextDark,
                Location = new Point(95, 18),
                Size = new Size(tileW - 105, 22),
                BackColor = Color.Transparent
            };
            p.Controls.Add(lblTitle);

            var lblDesc = new Label
            {
                Text = action.Description,
                Font = new Font("Segoe UI", 8),
                ForeColor = TextMuted,
                Location = new Point(95, 42),
                Size = new Size(tileW - 105, 60),
                BackColor = Color.Transparent
            };
            p.Controls.Add(lblDesc);

            HookHover(p);
            void OnClick(object? s, EventArgs e) => RunQuickAction(action);
            p.Click += OnClick;
            foreach (Control c in p.Controls) c.Click += OnClick;

            panel.Controls.Add(p);

            // Layout 2 colonnes
            if (i % 2 == 0)
                x = 20 + tileW + gap;
            else
            {
                x = 20;
                y += tileH + gap;
            }
        }

        return panel;
    }

    private void RunQuickAction(QuickActions.QuickAction action)
    {
        var confirm = MessageBox.Show(
            action.Description + "\n\nLancer ?",
            action.Title,
            MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (confirm != DialogResult.OK) return;

        try
        {
            action.Execute();
            _tray.ShowBalloon(action.Title, "Action effectuée avec succès.", false);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Erreur : " + ex.Message,
                "Fix72 Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ─── TAB 3 : À PROPOS ───────────────────────────────────────────
    private Panel BuildTabAbout()
    {
        var panel = new Panel
        {
            BackColor = SoftBg,
            AutoScroll = true,
            Padding = new Padding(0, 20, 0, 20)
        };

        // Bloc logo + version
        var logoCard = new RoundedPanel
        {
            Location = new Point(20, 20),
            Size = new Size(760, 130),
            FillColor = Color.White,
            CornerRadius = 14,
            BorderColor = Color.FromArgb(228, 232, 240),
            BackColor = SoftBg,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var logo = LogoLoader.Load();
        if (logo != null)
        {
            var logoPic = new PictureBox
            {
                Image = logo,
                Location = new Point(20, 20),
                Size = new Size(180, 90),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            logoCard.Controls.Add(logoPic);
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var lblName = new Label
        {
            Text = "Fix72 Agent",
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            ForeColor = TextDark,
            Location = new Point(220, 25),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        logoCard.Controls.Add(lblName);

        var lblVer = new Label
        {
            Text = $"Version {version}  •  Développé par Etienne Aubry — Fix72",
            Font = new Font("Segoe UI", 9),
            ForeColor = TextMuted,
            Location = new Point(222, 60),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        logoCard.Controls.Add(lblVer);

        var lblDesc = new Label
        {
            Text = "Surveillance bienveillante de votre PC — Le Mans (72)",
            Font = new Font("Segoe UI", 9, FontStyle.Italic),
            ForeColor = TextMuted,
            Location = new Point(222, 80),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        logoCard.Controls.Add(lblDesc);

        panel.Controls.Add(logoCard);

        // Bloc "Ce qui est surveillé"
        var monitorsCard = new RoundedPanel
        {
            Location = new Point(20, 165),
            Size = new Size(760, 200),
            FillColor = Color.White,
            CornerRadius = 14,
            BorderColor = Color.FromArgb(228, 232, 240),
            BackColor = SoftBg,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        var lblMonTitle = new Label
        {
            Text = "🔍   Ce que je surveille pour vous",
            Font = new Font("Segoe UI Emoji", 11, FontStyle.Bold),
            ForeColor = TextDark,
            Location = new Point(20, 15),
            AutoSize = true,
            BackColor = Color.Transparent,
            UseCompatibleTextRendering = true
        };
        monitorsCard.Controls.Add(lblMonTitle);

        var lblMonList = new Label
        {
            Text =
                "•  Espace disque dur disponible          •  Mises à jour Windows en attente\n" +
                "•  Mémoire RAM saturée                    •  Antivirus actif et à jour\n" +
                "•  Température du processeur            •  Santé des disques (SMART)\n" +
                "•  Lenteur au démarrage / uptime       •  Connexion Internet et latence\n" +
                "•  Niveau de batterie (PC portables)",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = TextDark,
            Location = new Point(30, 55),
            Size = new Size(720, 130),
            BackColor = Color.Transparent
        };
        monitorsCard.Controls.Add(lblMonList);

        panel.Controls.Add(monitorsCard);

        // Bloc Confidentialité
        var privacyCard = new RoundedPanel
        {
            Location = new Point(20, 380),
            Size = new Size(760, 130),
            FillColor = Color.FromArgb(232, 245, 233),
            CornerRadius = 14,
            BorderColor = Color.FromArgb(46, 160, 67),
            BorderWidth = 1,
            BackColor = SoftBg,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        var lblPrivTitle = new Label
        {
            Text = "🔒   Confidentialité — vos engagements Fix72",
            Font = new Font("Segoe UI Emoji", 11, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 100, 50),
            Location = new Point(20, 15),
            AutoSize = true,
            BackColor = Color.Transparent,
            UseCompatibleTextRendering = true
        };
        privacyCard.Controls.Add(lblPrivTitle);
        var lblPrivBody = new Label
        {
            Text =
                "✓  Aucune donnée personnelle transmise           ✓  Aucun accès à vos fichiers\n" +
                "✓  Pas de caméra, pas de micro                          ✓  Aucun keylogger\n" +
                "✓  Vous gardez le contrôle total à tout moment   ✓  Conforme RGPD",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(50, 70, 55),
            Location = new Point(30, 50),
            Size = new Size(720, 70),
            BackColor = Color.Transparent
        };
        privacyCard.Controls.Add(lblPrivBody);

        panel.Controls.Add(privacyCard);

        // Lien site web
        var lnk = new LinkLabel
        {
            Text = $"🌐  {Defaults.WebsiteUrl}",
            Font = new Font("Segoe UI", 10),
            Location = new Point(20, 525),
            AutoSize = true,
            BackColor = Color.Transparent,
            LinkColor = BrandBlue,
            ActiveLinkColor = BrandBlueDark,
            UseCompatibleTextRendering = true
        };
        lnk.Click += (s, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Defaults.WebsiteUrl,
                    UseShellExecute = true
                });
            }
            catch { }
        };
        panel.Controls.Add(lnk);

        return panel;
    }

    // ─────────────────────────────────────────────────────────────────
    //  CONTACT STRIP (toujours visible en bas)
    // ─────────────────────────────────────────────────────────────────
    private void BuildContactStrip()
    {
        var strip = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 130,
            BackColor = Color.FromArgb(238, 242, 248),
            Padding = new Padding(15, 10, 15, 10),
            ColumnCount = 2,
            RowCount = 2,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        strip.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        strip.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        // Bordure haute pour bien séparer du contenu
        strip.Paint += (s, e) =>
        {
            using var pen = new Pen(Color.FromArgb(220, 226, 236), 1);
            e.Graphics.DrawLine(pen, 0, 0, strip.Width, 0);
        };

        // Bouton principal "Appeler" — occupe les 2 colonnes du haut
        var btnCall = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8),
            FillColor = BrandBlue,
            HoverFillColor = BrandBlueDark,
            BorderWidth = 0,
            CornerRadius = 14,
            Cursor = Cursors.Hand,
            BackColor = Color.FromArgb(238, 242, 248)
        };
        var lblCallText = new Label
        {
            Text = $"📞   Appeler Fix72\n{_settings.Settings.TechnicianPhone}",
            Font = new Font("Segoe UI Emoji", 12, FontStyle.Bold),
            ForeColor = Color.White,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            UseCompatibleTextRendering = true
        };
        btnCall.Controls.Add(lblCallText);
        HookHover(btnCall);
        EventHandler callHandler = (s, e) => _tray.CallTechnician();
        btnCall.Click += callHandler;
        lblCallText.Click += callHandler;
        strip.Controls.Add(btnCall, 0, 0);
        strip.SetColumnSpan(btnCall, 2);

        // Boutons secondaires
        var btnTele = MakeSecondaryFlowButton("🖥️   Téléassistance");
        EventHandler teleHandler = (s, e) => _tray.StartTeleassist();
        btnTele.Click += teleHandler;
        foreach (Control c in btnTele.Controls) c.Click += teleHandler;
        strip.Controls.Add(btnTele, 0, 1);

        var btnMsg = MakeSecondaryFlowButton("💬   Envoyer un message");
        EventHandler msgHandler = (s, e) => _tray.OpenMessageDialog();
        btnMsg.Click += msgHandler;
        foreach (Control c in btnMsg.Controls) c.Click += msgHandler;
        strip.Controls.Add(btnMsg, 1, 1);

        Controls.Add(strip);
    }

    private RoundedPanel MakeSecondaryFlowButton(string text)
    {
        var p = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 4, 0),
            FillColor = Color.White,
            HoverFillColor = Color.FromArgb(240, 246, 255),
            BorderColor = BrandBlue,
            BorderWidth = 1,
            CornerRadius = 8,
            Cursor = Cursors.Hand,
            BackColor = Color.FromArgb(238, 242, 248)
        };
        var lbl = new Label
        {
            Text = text,
            Font = new Font("Segoe UI Emoji", 10, FontStyle.Bold),
            ForeColor = BrandBlue,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            UseCompatibleTextRendering = true
        };
        p.Controls.Add(lbl);
        HookHover(p);
        return p;
    }

    // ─────────────────────────────────────────────────────────────────
    //  FOOTER
    // ─────────────────────────────────────────────────────────────────
    private void BuildFooter()
    {
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 26,
            BackColor = Color.FromArgb(228, 234, 244)
        };
        Controls.Add(footer);

        _lblLastCheck = new Label
        {
            Text = "Vérification en cours…",
            Font = new Font("Segoe UI", 8),
            ForeColor = TextMuted,
            Location = new Point(15, 6),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        footer.Controls.Add(_lblLastCheck);

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var versionLabel = new Label
        {
            Text = $"v{version}  •  fix72.com",
            Font = new Font("Segoe UI", 8),
            ForeColor = TextMuted,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(footer.Width - 150, 6),
            Size = new Size(140, 14),
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Color.Transparent
        };
        footer.Controls.Add(versionLabel);
    }

    // ─────────────────────────────────────────────────────────────────
    //  STATUS BANNER UPDATE
    // ─────────────────────────────────────────────────────────────────
    private void UpdateStatusBanner(AlertLevel worst)
    {
        var criticals = _tray.LatestResults
            .Where(r => r.Level == AlertLevel.Critical)
            .Select(r => r.DisplayName)
            .ToList();
        var warnings = _tray.LatestResults
            .Where(r => r.Level == AlertLevel.Warning)
            .Select(r => r.DisplayName)
            .ToList();

        Color fill, border;
        string text;

        switch (worst)
        {
            case AlertLevel.Critical:
                fill = Color.FromArgb(255, 235, 235);
                border = Color.FromArgb(220, 53, 69);
                text = $"🔴   Urgent — appelez Fix72 : {string.Join(", ", criticals)}";
                break;
            case AlertLevel.Warning:
                fill = Color.FromArgb(255, 248, 225);
                border = Color.FromArgb(255, 159, 28);
                text = $"⚠️   Attention à : {string.Join(", ", warnings)}";
                break;
            case AlertLevel.Ok:
                fill = Color.FromArgb(232, 245, 233);
                border = Color.FromArgb(46, 160, 67);
                text = "✅   Tout va bien sur votre PC";
                break;
            default:
                fill = Color.FromArgb(245, 245, 245);
                border = Color.LightGray;
                text = "❓   État partiellement inconnu";
                break;
        }

        _statusBanner.FillColor = fill;
        _statusBanner.BorderColor = border;
        _statusBanner.Invalidate();
        _lblOverallStatus.Text = text;
    }

    // ─────────────────────────────────────────────────────────────────
    //  TILES
    // ─────────────────────────────────────────────────────────────────
    private void RefreshDashboard()
    {
        if (_tilesContainer == null) return;

        _tilesContainer.SuspendLayout();
        _tilesContainer.Controls.Clear();

        var results = _tray.LatestResults;

        // Filtre : par défaut, on affiche les capteurs essentiels + tout ce qui est en alerte.
        var displayed = _showAllTiles
            ? results.ToList()
            : results.Where(r =>
                EssentialMonitorIds.Contains(r.Id) || r.Level >= AlertLevel.Warning
              ).ToList();

        if (displayed.Count == 0 && results.Count == 0)
        {
            var loading = new Label
            {
                Text = "Vérification en cours, patientez…",
                Font = new Font("Segoe UI", 11),
                ForeColor = TextMuted,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            _tilesContainer.Controls.Add(loading);
        }
        else
        {
            foreach (var r in displayed)
                _tilesContainer.Controls.Add(CreateTile(r));
        }

        _tilesContainer.ResumeLayout();

        // Toggle "afficher tous"
        int hiddenCount = results.Count - displayed.Count;
        if (_btnToggleAll != null)
        {
            _btnToggleAll.Visible = (hiddenCount > 0) || _showAllTiles;
            _btnToggleAll.Text = _showAllTiles
                ? "▲  Afficher moins"
                : $"▼  Afficher tous les indicateurs ({hiddenCount} de plus)";
        }

        // Force un relayout complet pour propager les nouvelles tailles
        RelayoutDashboardTab();

        var worst = results.Count > 0 ? results.Max(r => r.Level) : AlertLevel.Unknown;
        UpdateStatusBanner(worst);

        _lblLastCheck.Text = _tray.LastCheck == default
            ? "Vérification en cours…"
            : $"Dernière vérification : à {_tray.LastCheck:HH:mm}  •  {results.Count} indicateurs surveillés";
    }

    /// <summary>
    /// Layout manuel des tuiles en 2 colonnes : on calcule depuis la largeur RÉELLE
    /// du conteneur, donc rien ne déborde quelle que soit la taille de fenêtre.
    /// </summary>
    private void LayoutTiles()
    {
        const int gap = 10;
        const int tileHeight = 115;

        int width = _tilesContainer.ClientSize.Width;
        if (width <= 0) return;

        int tileWidth = (width - gap) / 2;
        int col = 0;
        int row = 0;

        foreach (Control ctrl in _tilesContainer.Controls)
        {
            // Cas spécial : label "loading" centré
            if (ctrl is Label lbl && lbl.Dock == DockStyle.Fill)
            {
                ctrl.Bounds = new Rectangle(0, 0, width, tileHeight);
                continue;
            }

            int x = col * (tileWidth + gap);
            int y = row * (tileHeight + gap);
            ctrl.Bounds = new Rectangle(x, y, tileWidth, tileHeight);

            col++;
            if (col >= 2) { col = 0; row++; }
        }

        int totalRows = (int)Math.Ceiling(_tilesContainer.Controls.Count / 2.0);
        _tilesContainer.Height = Math.Max(tileHeight, totalRows * (tileHeight + gap) - gap);

        // Repositionne le bouton "Afficher tous"
        if (_btnToggleAll != null && _btnToggleAll.Visible)
            _btnToggleAll.Top = _tilesContainer.Top + _tilesContainer.Height + 10;
    }

    private RoundedPanel CreateTile(MonitorResult r)
    {
        var fill = Color.White;
        var hoverFill = Color.FromArgb(248, 251, 255);
        var border = Color.FromArgb(228, 232, 240);
        var accent = r.Level switch
        {
            AlertLevel.Critical => Color.FromArgb(220, 53, 69),
            AlertLevel.Warning => Color.FromArgb(255, 159, 28),
            AlertLevel.Ok => Color.FromArgb(46, 160, 67),
            _ => Color.FromArgb(180, 188, 200)
        };

        if (r.Level == AlertLevel.Critical)
        {
            fill = Color.FromArgb(255, 245, 245);
            hoverFill = Color.FromArgb(255, 235, 235);
            border = Color.FromArgb(245, 198, 203);
        }
        else if (r.Level == AlertLevel.Warning)
        {
            fill = Color.FromArgb(255, 252, 240);
            hoverFill = Color.FromArgb(255, 248, 225);
            border = Color.FromArgb(245, 220, 165);
        }

        var p = new RoundedPanel
        {
            FillColor = fill,
            HoverFillColor = hoverFill,
            BorderColor = border,
            AccentBarColor = accent,
            CornerRadius = 12,
            BackColor = SoftBg,
            Cursor = (r.Level >= AlertLevel.Warning && !string.IsNullOrEmpty(r.Message))
                ? Cursors.Hand : Cursors.Default
            // Pas de Dock/Margin : LayoutTiles() positionne et dimensionne explicitement.
        };

        var lblIcon = new Label
        {
            Text = r.Icon,
            Font = new Font("Segoe UI Emoji", 16),
            Location = new Point(14, 8),
            Size = new Size(32, 32),
            BackColor = Color.Transparent,
            UseCompatibleTextRendering = true,
            TextAlign = ContentAlignment.MiddleCenter
        };
        p.Controls.Add(lblIcon);

        var lblName = new Label
        {
            Text = r.DisplayName,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = TextDark,
            Location = new Point(50, 14),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        p.Controls.Add(lblName);

        var lblStatus = new Label
        {
            Text = r.Status,
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = accent,
            Location = new Point(14, 48),
            Size = new Size(220, 32),
            BackColor = Color.Transparent
        };
        p.Controls.Add(lblStatus);

        var lblDetail = new Label
        {
            Text = r.Detail,
            Font = new Font("Segoe UI", 8),
            ForeColor = TextMuted,
            Location = new Point(14, 84),
            Size = new Size(220, 30),
            BackColor = Color.Transparent
        };
        p.Controls.Add(lblDetail);

        if (r.Level >= AlertLevel.Warning && !string.IsNullOrEmpty(r.Message))
        {
            var hint = new Label
            {
                Text = "› Voir et résoudre",
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                ForeColor = accent,
                Location = new Point(14, 108),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            p.Controls.Add(hint);

            void OnTileClick(object? s, EventArgs e)
            {
                using var dlg = new AlertDialog(r, () => _tray.CallTechnician());
                dlg.ShowDialog(this);
            }
            p.Click += OnTileClick;
            foreach (Control c in p.Controls) c.Click += OnTileClick;
        }

        HookHover(p);
        return p;
    }

    // ─────────────────────────────────────────────────────────────────
    //  HOVER HANDLING
    // ─────────────────────────────────────────────────────────────────
    private static void HookHover(RoundedPanel p)
    {
        EventHandler enter = (s, e) => p.IsHovered = true;
        EventHandler leave = (s, e) =>
        {
            var pt = p.PointToClient(MousePosition);
            if (!p.ClientRectangle.Contains(pt))
                p.IsHovered = false;
        };
        p.MouseEnter += enter;
        p.MouseLeave += leave;
        foreach (Control c in p.Controls)
        {
            c.MouseEnter += enter;
            c.MouseLeave += leave;
        }
    }
}
