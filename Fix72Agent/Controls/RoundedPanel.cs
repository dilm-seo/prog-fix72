using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;

namespace Fix72Agent.Controls;

/// <summary>
/// Panel avec coins arrondis, bordure légère, barre d'accent gauche optionnelle,
/// ombre projetée subtile et état "hover" qui change le fond.
/// </summary>
[SupportedOSPlatform("windows")]
public class RoundedPanel : Panel
{
    public int CornerRadius { get; set; } = 10;
    public Color FillColor { get; set; } = Color.White;
    public Color HoverFillColor { get; set; } = Color.Empty;
    public Color BorderColor { get; set; } = Color.FromArgb(228, 232, 240);
    public int BorderWidth { get; set; } = 1;
    public Color AccentBarColor { get; set; } = Color.Empty;
    public int AccentBarWidth { get; set; } = 4;
    public bool DropShadow { get; set; } = true;

    private bool _isHovered;
    public bool IsHovered
    {
        get => _isHovered;
        set
        {
            if (_isHovered == value) return;
            _isHovered = value;
            Invalidate();
        }
    }

    public RoundedPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint
                 | ControlStyles.SupportsTransparentBackColor, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Le panneau dessine son fond AU-DESSUS de la couleur du parent.
        // BackColor = couleur du parent visible dans les coins.
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);

        // Ombre portée subtile (3 passes pour effet de douceur)
        if (DropShadow)
        {
            for (int i = 3; i >= 1; i--)
            {
                var alpha = (byte)(IsHovered ? (10 - i * 2) : (6 - i));
                if (alpha == 0) continue;
                using var shadowPath = MakeRoundedPath(
                    new Rectangle(rect.X + i / 2, rect.Y + i, rect.Width, rect.Height),
                    CornerRadius);
                using var shadowBrush = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
                g.FillPath(shadowBrush, shadowPath);
            }
        }

        using var path = MakeRoundedPath(rect, CornerRadius);

        // Fill
        var fill = (IsHovered && HoverFillColor != Color.Empty) ? HoverFillColor : FillColor;
        using (var brush = new SolidBrush(fill))
            g.FillPath(brush, path);

        // Accent bar à gauche (clip dans la forme arrondie)
        if (AccentBarColor != Color.Empty)
        {
            var prevClip = g.Clip;
            g.SetClip(path, CombineMode.Replace);
            using var accentBrush = new SolidBrush(AccentBarColor);
            g.FillRectangle(accentBrush, 0, 0, AccentBarWidth, Height);
            g.Clip = prevClip;
        }

        // Border
        if (BorderWidth > 0)
        {
            using var pen = new Pen(BorderColor, BorderWidth);
            g.DrawPath(pen, path);
        }

        base.OnPaint(e);
    }

    public static GraphicsPath MakeRoundedPath(Rectangle r, int radius)
    {
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        if (d < 2)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
