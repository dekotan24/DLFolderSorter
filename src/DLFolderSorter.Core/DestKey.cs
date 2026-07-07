namespace DLFolderSorter.Core;

/// <summary>
/// 仕分け先の「区分」キーとその表示ラベル。
/// 有効なソート軸（販売元別・AI別）に応じてカテゴリ×販売元×AIの組み合わせを展開する。
/// - AVはFANZA固有かつAI判定情報が取れないため、常に単一区分
/// - 不明も常に単一区分
/// </summary>
public static class DestKey
{
    public static readonly string[] Sites = { "DLsite", "FANZA" };

    /// <summary>アイテムの属性から宛先キーを組み立てる。</summary>
    public static string Build(string category, string site, bool isAi, bool separateSites, bool separateAi)
    {
        if (category is Category.Av or Category.Unknown) return category;
        var key = category;
        if (separateSites) key += "|" + site;
        if (separateAi && isAi) key += "|AI";
        return key;
    }

    /// <summary>UI表示用ラベル（例：「AIゲーム（DLsite）」）。</summary>
    public static string Label(string category, string? site, bool isAi)
    {
        var name = CategoryLabel(category);
        if (isAi) name = "AI" + name;
        if (site != null) name += $"（{site}）";
        return name;
    }

    public static string CategoryLabel(string category) => category switch
    {
        Category.Game => "ゲーム",
        Category.Voice => "音声",
        Category.Movie => "動画",
        Category.Book => "CG・マンガ",
        Category.Av => "AV",
        Category.Unknown => "不明",
        _ => category,
    };

    /// <summary>
    /// 現在のソート軸で有効な全区分を表示順で列挙する。
    /// 並び順（デコ指定）: カテゴリごとに、DLsite→FANZAの順で各サイトのAI→通常。
    /// </summary>
    public static List<(string Key, string Label, string Category, bool IsAi, string Site)> EnumerateRows(bool separateSites, bool separateAi)
    {
        var rows = new List<(string, string, string, bool, string)>();
        foreach (var category in new[] { Category.Game, Category.Voice, Category.Movie, Category.Book })
        {
            if (separateSites)
            {
                foreach (var site in Sites)
                {
                    if (separateAi)
                    {
                        rows.Add((Build(category, site, true, true, true), Label(category, site, true), category, true, site));
                    }
                    rows.Add((Build(category, site, false, true, separateAi), Label(category, site, false), category, false, site));
                }
            }
            else
            {
                if (separateAi)
                {
                    rows.Add((Build(category, "", true, false, true), Label(category, null, true), category, true, ""));
                }
                rows.Add((Build(category, "", false, false, separateAi), Label(category, null, false), category, false, ""));
            }
        }
        rows.Add((Category.Av, CategoryLabel(Category.Av), Category.Av, false, "FANZA"));
        rows.Add((Category.Unknown, CategoryLabel(Category.Unknown), Category.Unknown, false, ""));
        return rows;
    }
}
