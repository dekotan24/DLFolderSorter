using System.Text.RegularExpressions;

namespace DLFolderSorter.Core;

/// <summary>
/// リネーム名の生成。DLsiteMP3TagEditorの命名規則 [ID] [サークル] タイトル 準拠。
/// プレフィックス（旧分類コード）は捨て、サフィックス（バージョン表記等）は温存する。
/// 温存しないと同一作品の複数バージョンで名前が衝突する（実測: 衝突587件→2件）。
/// </summary>
public static partial class NameBuilder
{
    // Windowsで使えない文字 + ゼロ幅文字（U+200B〜U+200D, U+FEFF。px-downloaderで実害があった）
    [GeneratedRegex("[:*?\"<>|/\\\\\u200B-\\u200D\\uFEFF]")]
    private static partial Regex InvalidChars();

    private const int MaxNameLength = 180;

    /// <summary>ID以外に「タイトルらしき情報」（CJK文字）を含むか。含む場合はリネームしない。</summary>
    public static bool HasTitlePart(string prefix, string suffix)
        => Regex.IsMatch(prefix + suffix, @"[぀-ヿ㐀-鿿ｦ-ﾟ]");

    /// <summary>[ID] [サークル] タイトル+サフィックス+拡張子 形式の新名前を作る。</summary>
    public static string Build(string workId, string maker, string title, string suffix, string extChain)
    {
        maker = Sanitize(maker);
        title = Sanitize(title);
        suffix = Sanitize(suffix);
        var name = $"[{workId}] [{maker}] {title}";
        var budget = MaxNameLength - suffix.Length - extChain.Length;
        if (budget < 10) budget = 10;
        if (name.Length > budget)
        {
            name = name[..budget].TrimEnd();
        }
        return (name + suffix).TrimEnd(' ', '.') + extChain;
    }

    public static string Sanitize(string s)
        => InvalidChars().Replace(s ?? "", "_").Trim();
}
