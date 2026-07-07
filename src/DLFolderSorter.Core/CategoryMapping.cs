using System.Text.Json;

namespace DLFolderSorter.Core;

/// <summary>
/// work_typeコード / FANZAカテゴリ名 / DMMフロア名 → 仕分けカテゴリのマッピング。
/// 既定値はコード内に持ち、mapping.jsonが存在すればそちらで上書きできる（編集UIは無し）。
/// </summary>
public sealed class CategoryMapping
{
    /// <summary>DLsiteのwork_typeコード → カテゴリ。</summary>
    public Dictionary<string, string> DlsiteWorkTypes { get; set; } = new()
    {
        // ゲーム系
        ["RPG"] = Category.Game, ["SLN"] = Category.Game, ["ADV"] = Category.Game,
        ["ACN"] = Category.Game, ["DNV"] = Category.Game, ["TBL"] = Category.Game,
        ["PZL"] = Category.Game, ["STG"] = Category.Game, ["QIZ"] = Category.Game,
        ["TYP"] = Category.Game, ["ETC"] = Category.Game,
        // 音声系
        ["SOU"] = Category.Voice, ["VCM"] = Category.Voice, ["MUS"] = Category.Voice,
        // 動画
        ["MOV"] = Category.Movie,
        // 画像・書籍系
        ["ICG"] = Category.Book, ["MNG"] = Category.Book, ["SCM"] = Category.Book,
        ["WBT"] = Category.Book, ["NRE"] = Category.Book, ["PBC"] = Category.Book,
        // 曖昧なもの（素材・ツール・その他）はUnknown
        ["ET3"] = Category.Unknown, ["TOL"] = Category.Unknown,
        ["IMT"] = Category.Unknown, ["AMT"] = Category.Unknown,
    };

    /// <summary>
    /// FANZA同人のbreadcrumbカテゴリ名 → カテゴリ。
    /// 現行のFANZA同人はメディア4種（コミック/CG/ゲーム/ボイス、2026-07実サイト確認）。
    /// 表記ゆれに備えて別名も残してある。
    /// </summary>
    public Dictionary<string, string> FanzaDoujinCategories { get; set; } = new()
    {
        // 現行の実名（breadcrumb実測: 「ボイス」「コミック」を確認済み）
        ["ボイス"] = Category.Voice,
        ["ゲーム"] = Category.Game,
        ["コミック"] = Category.Book,
        ["CG"] = Category.Book,
        // 表記ゆれ・旧名への保険
        ["ボイス・ASMR"] = Category.Voice,
        ["同人ゲーム"] = Category.Game,
        ["マンガ"] = Category.Book,
        ["同人誌"] = Category.Book,
        ["CG・イラスト"] = Category.Book,
        ["動画"] = Category.Movie,
        ["動画作品"] = Category.Movie,
    };

    /// <summary>DMMフロア名（cid逆引き結果） → カテゴリ。</summary>
    public Dictionary<string, string> FanzaFloors { get; set; } = new()
    {
        // 新形式チャンネル (video.dmm.co.jp/{channel}/content/)
        ["av"] = Category.Av,
        ["amateur"] = Category.Av,
        ["cinema"] = Category.Movie,
        // 旧形式タイプ
        ["videoa"] = Category.Av,
        ["videoc"] = Category.Av,
        ["nikkatsu"] = Category.Av,
        ["dvd"] = Category.Av,
        ["ppr"] = Category.Av,
        ["anime"] = Category.Movie,
        ["doujin"] = Category.Unknown, // cid経由で同人に流れた場合は要確認扱い
    };

    /// <summary>照会結果からカテゴリを決める。マップに無い値はUnknown。</summary>
    public string Resolve(CacheRecord rec)
    {
        if (!rec.Found) return Category.Unknown;
        if (rec.Site == "DLsite")
        {
            return DlsiteWorkTypes.GetValueOrDefault(rec.WorkType, Category.Unknown);
        }
        if (rec.WorkType.StartsWith("floor:", StringComparison.Ordinal))
        {
            return FanzaFloors.GetValueOrDefault(rec.WorkType["floor:".Length..], Category.Unknown);
        }
        return FanzaDoujinCategories.GetValueOrDefault(rec.WorkType, Category.Unknown);
    }

    public static CategoryMapping LoadOrDefault(string jsonPath)
    {
        if (File.Exists(jsonPath))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<CategoryMapping>(File.ReadAllText(jsonPath));
                if (loaded != null) return loaded;
            }
            catch (JsonException)
            {
                // 壊れたmapping.jsonは無視して既定値を使う
            }
        }
        return new CategoryMapping();
    }
}
