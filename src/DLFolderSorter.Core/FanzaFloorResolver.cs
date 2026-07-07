using System.Net;
using System.Text.RegularExpressions;

namespace DLFolderSorter.Core;

/// <summary>
/// DMM横断検索でcid/品番からフロア（av/doujin/anime等）を逆引きする。
/// 元ネタはJavSP (Yuukiy/JavSP web/fanza.py) だが、DMMの検索・URL形式が2026年時点で変わっているため現行仕様に合わせてある:
/// - 検索URL: https://www.dmm.co.jp/search/=/searchstr={id}/ （旧 /search/?redirect=1&amp;searchstr= は404になった）
/// - AV系の結果は新形式 video.dmm.co.jp/{channel}/content/?id={cid}
/// - 同人・パッケージ系は旧形式 www.dmm.co.jp/{product}/{type}/-/detail/=/cid={cid} のまま
/// cid⇔品番(SGKI-015等)の相互変換は非決定的なため、変換ではなく検索逆引きが正攻法。
/// </summary>
public static partial class FanzaFloorResolver
{
    // 新形式: video.dmm.co.jp/av/content/?id=1sgki00015c
    [GeneratedRegex(@"video\.dmm\.co\.jp/([a-z]+)/content/\?id=([a-z0-9_]+)", RegexOptions.IgnoreCase)]
    private static partial Regex NewStyleUrl();

    // 旧形式: www.dmm.co.jp/dc/doujin/-/detail/=/cid=d_750863 等
    [GeneratedRegex(@"/([a-z]+)/([a-z]+)/-/detail/=/cid=([a-z0-9_]+)", RegexOptions.IgnoreCase)]
    private static partial Regex OldStyleUrl();

    /// <summary>フロア種別の優先度（同一IDが複数フロアでヒットした場合に高い方を採用）。</summary>
    private static readonly Dictionary<string, int> TypePriority = new()
    {
        // 新形式チャンネル
        ["av"] = 12, ["amateur"] = 11, ["cinema"] = 7,
        // 旧形式タイプ
        ["videoa"] = 10, ["anime"] = 8, ["nikkatsu"] = 6, ["doujin"] = 4, ["dvd"] = 3, ["ppr"] = 2,
    };

    /// <summary>
    /// 検索結果からフロア(type)を判定する。見つからない場合は空文字。
    /// 検索したIDと同じcidの結果を優先し、無ければ（品番形式検索など）全結果から最良を採る。
    /// </summary>
    public static async Task<string> ResolveAsync(HttpClient client, string cidOrCode, CancellationToken ct = default)
    {
        var url = $"https://www.dmm.co.jp/search/=/searchstr={WebUtility.UrlEncode(cidOrCode)}/";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        req.Headers.TryAddWithoutValidation("Cookie", "age_check_done=1");
        req.Headers.TryAddWithoutValidation("Accept-Language", "ja,en-US;q=0.9");
        using var resp = await client.SendAsync(req, ct);
        // 非2xx（503・レート制限等）は一時障害としてthrowし、恒久not_found（空文字=検索0件）と区別する。
        // 混同すると一時障害が毒キャッシュとして永続化してしまう
        resp.EnsureSuccessStatusCode();
        var html = await resp.Content.ReadAsStringAsync(ct);

        var wanted = cidOrCode.ToLowerInvariant();
        var hits = new List<(string Type, string Cid)>();
        foreach (Match m in NewStyleUrl().Matches(html))
        {
            hits.Add((m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value.ToLowerInvariant()));
        }
        foreach (Match m in OldStyleUrl().Matches(html))
        {
            hits.Add((m.Groups[2].Value.ToLowerInvariant(), m.Groups[3].Value.ToLowerInvariant()));
        }
        if (hits.Count == 0) return "";

        var exact = hits.Where(h => h.Cid == wanted).ToList();
        var pool = exact.Count > 0 ? exact : hits;
        return pool.OrderByDescending(h => TypePriority.GetValueOrDefault(h.Type)).First().Type;
    }
}
