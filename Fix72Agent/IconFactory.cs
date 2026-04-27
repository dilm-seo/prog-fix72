using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Fix72Agent;

[SupportedOSPlatform("windows")]
public static class IconFactory
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>Bouclier coloré pour la zone de notification (16-32px).</summary>
    public static Icon CreateShieldIcon(Color baseColor, int size = 32)
    {
        var bmp = CreateShieldBitmap(baseColor, size);
        var hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        bmp.Dispose();
        return icon;
    }

    /// <summary>Variante plus grande pour les en-têtes de fenêtres (64-128px).</summary>
    public static Bitmap CreateShieldBitmap(Color baseColor, int size = 32)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        float w = size, h = size;

        // Bouclier — forme arrondie (style "écusson")
        using var path = new GraphicsPath();
        path.AddBezier(
            new PointF(w * 0.50f, h * 0.04f),
            new PointF(w * 0.92f, h * 0.16f),
            new PointF(w * 0.92f, h * 0.16f),
            new PointF(w * 0.92f, h * 0.50f));
        path.AddBezier(
            new PointF(w * 0.92f, h * 0.50f),
            new PointF(w * 0.92f, h * 0.78f),
            new PointF(w * 0.50f, h * 0.96f),
            new PointF(w * 0.50f, h * 0.96f));
        path.AddBezier(
            new PointF(w * 0.50f, h * 0.96f),
            new PointF(w * 0.50f, h * 0.96f),
            new PointF(w * 0.08f, h * 0.78f),
            new PointF(w * 0.08f, h * 0.50f));
        path.AddBezier(
            new PointF(w * 0.08f, h * 0.50f),
            new PointF(w * 0.08f, h * 0.16f),
            new PointF(w * 0.08f, h * 0.16f),
            new PointF(w * 0.50f, h * 0.04f));
        path.CloseFigure();

        // Remplissage avec un dégradé pour donner du volume
        var lighter = Lighten(baseColor, 0.20f);
        var darker = Darken(baseColor, 0.18f);
        using (var brush = new LinearGradientBrush(
            new RectangleF(0, 0, w, h), lighter, darker, LinearGradientMode.Vertical))
        {
            g.FillPath(brush, path);
        }

        // Légère bordure plus foncée
        using (var pen = new Pen(Darken(baseColor, 0.35f), Math.Max(1f, size * 0.04f)))
        {
            g.DrawPath(pen, path);
        }

        // Reflet brillant en haut (effet "glossy")
        using (var glossPath = new GraphicsPath())
        {
            glossPath.AddBezier(
                new PointF(w * 0.18f, h * 0.18f),
                new PointF(w * 0.30f, h * 0.10f),
                new PointF(w * 0.70f, h * 0.10f),
                new PointF(w * 0.82f, h * 0.18f));
            glossPath.AddBezier(
                new PointF(w * 0.82f, h * 0.18f),
                new PointF(w * 0.82f, h * 0.32f),
                new PointF(w * 0.50f, h * 0.42f),
                new PointF(w * 0.18f, h * 0.32f));
            glossPath.CloseFigure();
            using var glossBrush = new SolidBrush(Color.FromArgb(60, 255, 255, 255));
            g.FillPath(glossBrush, glossPath);
        }

        // Texte "F" stylisé (gras, ombre légère)
        using var font = new Font("Segoe UI", size * 0.42f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        var textRect = new RectangleF(0, h * 0.10f, w, h * 0.78f);

        // Ombre
        using (var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
        {
            g.DrawString("F", font, shadow,
                new RectangleF(textRect.X + 0.6f, textRect.Y + 0.6f, textRect.Width, textRect.Height), sf);
        }
        // Texte principal
        g.DrawString("F", font, Brushes.White, textRect, sf);

        return bmp;
    }

    private static Color Lighten(Color c, float pct) =>
        Color.FromArgb(c.A,
            (int)Math.Min(255, c.R + (255 - c.R) * pct),
            (int)Math.Min(255, c.G + (255 - c.G) * pct),
            (int)Math.Min(255, c.B + (255 - c.B) * pct));

    private static Color Darken(Color c, float pct) =>
        Color.FromArgb(c.A,
            (int)Math.Max(0, c.R - c.R * pct),
            (int)Math.Max(0, c.G - c.G * pct),
            (int)Math.Max(0, c.B - c.B * pct));
}
