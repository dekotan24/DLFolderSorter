namespace DLFolderSorter.Core;

/// <summary>
/// 照会済みのSortItem群から移動・リネーム計画を立てる。
/// 衝突は「計画内の重複」と「宛先の実在物」の両方に対して検出し、該当全件をSkipにする。
/// </summary>
public static class Planner
{
    /// <summary>仕分け元フォルダをスキャンしてSortItemを列挙する（直下のみ、再帰しない）。</summary>
    public static List<SortItem> Scan(SortConfig config)
    {
        var items = new List<SortItem>();
        var source = new DirectoryInfo(config.SourceFolder);
        var detectAv = HasAvDestination(config);
        foreach (var entry in source.EnumerateFileSystemInfos())
        {
            var isDir = (entry.Attributes & FileAttributes.Directory) != 0;
            if (!isDir && !config.IncludeFiles) continue;
            var item = new SortItem
            {
                Name = entry.Name,
                FullPath = entry.FullName,
                IsDirectory = isDir,
            };
            IdExtractor.Extract(item, detectAv);
            items.Add(item);
        }
        return items;
    }

    private static bool HasAvDestination(SortConfig config)
        => !string.IsNullOrEmpty(config.DestinationMap.GetValueOrDefault(Category.Av));

    /// <summary>照会結果を反映し、各アイテムのアクション・新名前・宛先を決める。</summary>
    public static void BuildPlan(List<SortItem> items, SortConfig config)
    {
        // 1パス目: 各アイテムの希望ターゲットを決める
        foreach (var item in items)
        {
            item.Action = PlanAction.Stay;
            item.TargetPath = "";
            item.NewName = "";

            if (item.IdKind == IdKind.None)
            {
                item.Flag = "IDなし";
                item.Category = Category.Unknown;
                continue;
            }
            if (item.WorkType == "" && item.Category == Category.Unknown && item.Title == "" && item.Flag == "")
            {
                item.Flag = "未照会または照会失敗";
            }

            var destFolder = ResolveDestination(item, config);
            var newName = item.Name;
            if (config.RenameIdOnly
                && item.Title != ""
                && !NameBuilder.HasTitlePart(item.Prefix, item.Suffix))
            {
                newName = NameBuilder.Build(item.WorkId, item.Maker, item.Title, item.Suffix, item.ExtChain);
            }

            var moveNeeded = destFolder != "";
            var renameNeeded = !string.Equals(newName, item.Name, StringComparison.Ordinal);
            if (!moveNeeded && !renameNeeded) continue;

            var targetDir = moveNeeded ? destFolder : Path.GetDirectoryName(item.FullPath)!;
            item.TargetPath = Path.Combine(targetDir, newName);
            item.NewName = renameNeeded ? newName : "";
            item.Action = (moveNeeded, renameNeeded) switch
            {
                (true, true) => PlanAction.MoveRename,
                (true, false) => PlanAction.Move,
                _ => PlanAction.Rename,
            };
        }

        // 2パス目: 衝突検出（Windowsは大文字小文字を無視するのでOrdinalIgnoreCase）
        var targetCounts = items
            .Where(i => i.TargetPath != "" && i.Action != PlanAction.Stay)
            .GroupBy(i => i.TargetPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (item.TargetPath == "" || item.Action == PlanAction.Stay || item.Action == PlanAction.Skip) continue;
            if (targetCounts.GetValueOrDefault(item.TargetPath) > 1)
            {
                item.Action = PlanAction.Skip;
                item.Flag = $"衝突: 同名になる対象が{targetCounts[item.TargetPath]}件";
            }
            else if (File.Exists(item.TargetPath) || Directory.Exists(item.TargetPath))
            {
                item.Action = PlanAction.Skip;
                item.Flag = "衝突: 宛先に同名が既に存在";
            }
        }
    }

    /// <summary>カテゴリ・サイト・AI属性と設定から宛先フォルダを決める。空文字=移動しない。</summary>
    private static string ResolveDestination(SortItem item, SortConfig config)
    {
        if (item.Category == Category.Unknown && !config.MoveUnknown) return "";

        var key = DestKey.Build(item.Category, item.Site, item.IsAi, config.SeparateSites, config.SeparateAi);
        var dest = config.DestinationMap.GetValueOrDefault(key, "");
        if (dest == "") return "";

        // 既に宛先フォルダ直下にあるなら移動不要
        var currentDir = Path.GetDirectoryName(item.FullPath) ?? "";
        if (string.Equals(Path.TrimEndingDirectorySeparator(currentDir), Path.TrimEndingDirectorySeparator(dest), StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }
        return dest;
    }
}
