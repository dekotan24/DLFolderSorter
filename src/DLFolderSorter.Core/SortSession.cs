namespace DLFolderSorter.Core;

/// <summary>
/// スキャン→照会→計画の一連の流れを取りまとめる。UIとテストの共通入口。
/// </summary>
public sealed class SortSession
{
    private readonly ClassifyService _classify;

    public int MaxConsecutiveFailures { get; init; } = 10;

    /// <summary>(処理済みユニークID数, 総ユニークID数, 今処理したID)</summary>
    public event Action<int, int, string>? ClassifyProgress;

    public SortSession(ClassifyService classify)
    {
        _classify = classify;
    }

    /// <summary>
    /// 全アイテムの作品種別を照会してCategory等を反映する。
    /// 戻り値: 一時失敗で照会できなかったユニークID数（0なら完了）。
    /// </summary>
    public async Task<int> ClassifyAllAsync(List<SortItem> items, SortConfig config, CancellationToken ct)
    {
        var byId = items
            .Where(i => i.IdKind != IdKind.None)
            .GroupBy(i => i.WorkId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var transientFailures = 0;
        var done = 0;
        foreach (var group in byId)
        {
            ct.ThrowIfCancellationRequested();
            var first = group.First();
            var rec = await _classify.ClassifyAsync(first, config.RateSeconds, ct);
            done++;
            ClassifyProgress?.Invoke(done, byId.Count, group.Key);

            if (rec is null)
            {
                transientFailures++;
                foreach (var item in group)
                {
                    item.Flag = "照会失敗(一時的)";
                }
                if (_classify.ConsecutiveFailures >= MaxConsecutiveFailures)
                {
                    throw new InvalidOperationException(
                        $"{MaxConsecutiveFailures}回連続で照会に失敗しました。ネットワークまたはサイト側の問題の可能性があるため中断します。再実行すると照会済み分はキャッシュから再開されます。");
                }
                continue;
            }

            foreach (var item in group)
            {
                Apply(item, rec);
            }
        }
        return transientFailures;
    }

    private void Apply(SortItem item, CacheRecord rec)
    {
        item.WorkType = rec.WorkType;
        item.WorkTypeString = rec.WorkTypeString;
        item.Title = rec.Title;
        item.Maker = rec.Maker;
        item.IsAi = rec.IsAi;
        if (!rec.Found)
        {
            item.Category = Category.Unknown;
            item.Flag = "作品が見つかりません(削除済み?)";
            return;
        }
        item.Category = _classify.Mapping.Resolve(rec);
        if (item.Category == Category.Unknown && item.Flag == "")
        {
            item.Flag = $"種別を分類できません: {rec.WorkType}";
        }
    }
}
