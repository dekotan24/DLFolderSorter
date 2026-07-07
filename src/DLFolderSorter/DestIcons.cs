using System.Drawing.Drawing2D;
using DLFolderSorter.Core;

namespace DLFolderSorter;

/// <summary>
/// 仕分け先グリッドの区分アイコンを内蔵描画で生成する。
/// カテゴリ色のチップ+1文字グリフ、AI作品には紫の「AI」バッジを右に並べる。
/// </summary>
internal static class DestIcons
{
    private static readonly Dictionary<string, Bitmap> Cache = new();

    private static readonly Dictionary<string, (Color Color, string Glyph)> Chips = new()
    {
        [Category.Game] = (Color.FromArgb(76, 175, 80), "ゲ"),
        [Category.Voice] = (Color.FromArgb(33, 150, 243), "音"),
        [Category.Movie] = (Color.FromArgb(255, 152, 0), "動"),
        [Category.Book] = (Color.FromArgb(233, 30, 99), "画"),
        [Category.Av] = (Color.FromArgb(244, 67, 54), "AV"),
        [Category.Unknown] = (Color.FromArgb(130, 130, 135), "？"),
    };

    private static readonly Color AiBadgeColor = Color.FromArgb(156, 39, 176);

    private static readonly Dictionary<string, (Color Color, string Glyph)> SiteChips = new()
    {
        ["DLsite"] = (Color.FromArgb(21, 101, 192), "DL"),
        ["FANZA"] = (Color.FromArgb(198, 40, 40), "FZ"),
    };

    /// <summary>
    /// 区分アイコン: [カテゴリ][AIバッジ][サイト] の固定スロット配置（列が揃うように、無い要素は空欄）。
    /// </summary>
    public static Bitmap Get(string category, bool isAi, string site = "")
    {
        var key = $"{category}|{(isAi ? "AI" : "")}|{site}";
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var (color, glyph) = Chips.GetValueOrDefault(category, Chips[Category.Unknown]);
        var bmp = new Bitmap(70, 20);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            DrawChip(g, new Rectangle(0, 0, 20, 20), color, glyph, glyph.Length > 1 ? 7f : 9f);
            if (isAi)
            {
                DrawChip(g, new Rectangle(23, 0, 20, 20), AiBadgeColor, "AI", 8f);
            }
            if (SiteChips.TryGetValue(site, out var siteChip))
            {
                DrawChip(g, new Rectangle(46, 0, 22, 20), siteChip.Color, siteChip.Glyph, 7f);
            }
        }
        Cache[key] = bmp;
        return bmp;
    }

    private static void DrawChip(Graphics g, Rectangle rect, Color color, string text, float fontSize)
    {
        using var path = RoundedRect(rect, 5);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
        using var font = new Font("Yu Gothic UI", fontSize, FontStyle.Bold, GraphicsUnit.Point);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, Brushes.White, new RectangleF(rect.X, rect.Y + 0.5f, rect.Width, rect.Height), format);
    }

    private static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d - 1, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d - 1, rect.Bottom - d - 1, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d - 1, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
