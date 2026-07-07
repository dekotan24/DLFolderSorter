using System.Text.RegularExpressions;

namespace DLFolderSorter.Core;

/// <summary>
/// ファイル/フォルダ名から作品IDを抽出する純関数群。
/// 昨日(2026-07-07)のO:\Game仕分けスクリプトとJavSP(avid.py)の知見を移植。
/// </summary>
public static partial class IdExtractor
{
    // DLsite: 5桁のRJ番号(RJ19123)が実在するため桁数は{5,}
    [GeneratedRegex(@"(RJ|VJ|RE|VE|BJ|AJ)[0-9]{5,}", RegexOptions.IgnoreCase)]
    private static partial Regex DlsiteId();

    // FANZA同人: d_123456 / d_aisoft3356。英数字直後のd_は誤検出(sound_01等)なので負の後読み
    [GeneratedRegex(@"(?<![a-zA-Z0-9])d_([a-z]*\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex FanzaDoujinId();

    // DMM cid: 名前全体が英小文字+数字7〜19文字（JavSP get_cid準拠、約99%をカバー）
    [GeneratedRegex(@"^[a-z\d]{7,19}$")]
    private static partial Regex DmmCid();

    // AV品番: ABC-123 / ABC123 形式（例: SGKI-015, SSIS905）。
    // 誤検出を抑えるため英字2-6+数字2-5に限定し、DLsite/d_判定の後にのみ使う
    [GeneratedRegex(@"(?<![a-zA-Z0-9])([A-Z]{2,6})[-_ ]?(\d{2,5})(?![0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex AvCode();

    // ファイルの拡張子チェーン（.part1.rar / .zip / .rar / .mp4等の動画）
    [GeneratedRegex(@"((\.part\d+)?\.(zip|rar|7z)|\.(mp4|mkv|wmv|avi|iso|ts|m2ts))$", RegexOptions.IgnoreCase)]
    private static partial Regex ExtChain();

    /// <summary>名前から拡張子チェーンを分離する（フォルダはそのまま）。</summary>
    public static (string Stem, string ExtChain) SplitExtension(string name, bool isDirectory)
    {
        if (isDirectory) return (name, "");
        var m = ExtChain().Match(name);
        if (!m.Success) return (name, "");
        return (name[..m.Index], m.Value.ToLowerInvariant());
    }

    /// <summary>
    /// 名前からIDを抽出し、SortItemのID関連フィールドを埋める。
    /// 優先順: DLsite → FANZA同人 → AV(cid/品番)。AVはdetectAv=trueの場合のみ判定する。
    /// </summary>
    public static void Extract(SortItem item, bool detectAv)
    {
        var (stem, ext) = SplitExtension(item.Name, item.IsDirectory);
        item.ExtChain = ext;

        var dlsite = DlsiteId().Match(stem);
        if (dlsite.Success)
        {
            item.IdKind = IdKind.Dlsite;
            item.WorkId = dlsite.Value.ToUpperInvariant();
            item.Prefix = stem[..dlsite.Index];
            item.Suffix = stem[(dlsite.Index + dlsite.Length)..];
            item.Site = "DLsite";
            return;
        }

        var doujin = FanzaDoujinId().Match(stem);
        if (doujin.Success)
        {
            item.IdKind = IdKind.FanzaDoujin;
            item.WorkId = "d_" + doujin.Groups[1].Value.ToLowerInvariant();
            item.Prefix = stem[..doujin.Index];
            item.Suffix = stem[(doujin.Index + doujin.Length)..];
            item.Site = "FANZA";
            return;
        }

        if (!detectAv) return;

        // 名前全体がcidそのもの（1sgki00015c等）
        var cidCandidate = stem.Trim().ToLowerInvariant();
        if (DmmCid().IsMatch(cidCandidate) && ContainsBothLetterAndDigit(cidCandidate))
        {
            item.IdKind = IdKind.FanzaCid;
            item.WorkId = cidCandidate;
            item.Prefix = "";
            item.Suffix = "";
            item.Site = "FANZA";
            return;
        }

        // 品番形式（SGKI-015等）
        var av = AvCode().Match(stem);
        if (av.Success)
        {
            item.IdKind = IdKind.FanzaCid;
            item.WorkId = $"{av.Groups[1].Value.ToUpperInvariant()}-{av.Groups[2].Value}";
            item.Prefix = stem[..av.Index];
            item.Suffix = stem[(av.Index + av.Length)..];
            item.Site = "FANZA";
        }
    }

    private static bool ContainsBothLetterAndDigit(string s)
        => s.Any(char.IsAsciiLetter) && s.Any(char.IsAsciiDigit);
}
