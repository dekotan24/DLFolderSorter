using System.Text.Json;
using DLsiteInfoGetter;

namespace DLFolderSorter.Core;

/// <summary>
/// 作品IDから種別を照会してカテゴリを決める。結果はJSONLキャッシュに永続化する。
/// - 正の結果（found）と恒久的な負の結果（not_found）の両方をキャッシュする
/// - タイムアウト等の一時失敗はキャッシュせず、次回再試行させる
/// - サイト負荷軽減のためAPI呼び出しはRateSeconds間隔+ジッター
/// </summary>
public sealed class ClassifyService
{
    private readonly string _cachePath;
    private readonly Dictionary<string, CacheRecord> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _http = new();
    private readonly Random _random = new();
    private DateTime _lastRequest = DateTime.MinValue;

    /// <summary>work_type/breadcrumbカテゴリ名 → 仕分けカテゴリのマッピング。</summary>
    public CategoryMapping Mapping { get; }

    public int ConsecutiveFailures { get; private set; }

    public ClassifyService(string cacheDirectory, CategoryMapping? mapping = null)
    {
        Directory.CreateDirectory(cacheDirectory);
        _cachePath = Path.Combine(cacheDirectory, "classify_cache.jsonl");
        Mapping = mapping ?? CategoryMapping.LoadOrDefault(Path.Combine(cacheDirectory, "mapping.json"));
        LoadCache();
    }

    private void LoadCache()
    {
        if (!File.Exists(_cachePath)) return;
        foreach (var line in File.ReadLines(_cachePath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var rec = JsonSerializer.Deserialize<CacheRecord>(line);
                if (rec != null) _cache[rec.WorkId] = rec;
            }
            catch (JsonException)
            {
                // 中断時の書きかけ行は無視（definitiveな行だけ生かす）
            }
        }
    }

    private void AppendCache(CacheRecord rec)
    {
        _cache[rec.WorkId] = rec;
        File.AppendAllText(_cachePath, JsonSerializer.Serialize(rec) + Environment.NewLine);
    }

    public bool TryGetCached(string workId, out CacheRecord record)
        => _cache.TryGetValue(workId, out record!);

    /// <summary>キャッシュ中のnot_foundレコードを削除する（「not_foundを再試行」用）。</summary>
    public void ClearNotFound()
    {
        var keep = _cache.Values.Where(r => r.Found).ToList();
        _cache.Clear();
        foreach (var r in keep) _cache[r.WorkId] = r;
        File.WriteAllLines(_cachePath, keep.Select(r => JsonSerializer.Serialize(r)));
    }

    /// <summary>
    /// 1件照会する。キャッシュヒット時は即時。一時失敗はnullを返す（キャッシュされない）。
    /// </summary>
    public async Task<CacheRecord?> ClassifyAsync(SortItem item, double rateSeconds, CancellationToken ct)
    {
        if (item.IdKind == IdKind.None) return null;
        if (TryGetCached(item.WorkId, out var cached)) return cached;

        await ThrottleAsync(rateSeconds, ct);

        try
        {
            CacheRecord rec = item.IdKind switch
            {
                IdKind.Dlsite => FetchDlsite(item.WorkId),
                IdKind.FanzaDoujin => FetchFanzaDoujin(item.WorkId),
                IdKind.FanzaCid => await FetchFanzaCidAsync(item.WorkId, ct),
                _ => throw new InvalidOperationException(),
            };
            AppendCache(rec);
            ConsecutiveFailures = 0;
            return rec;
        }
        catch (ArgumentException)
        {
            // InfoGetterは「作品が見つからない」をArgumentExceptionで表す → 恒久not_foundとしてキャッシュ
            var rec = new CacheRecord { WorkId = item.WorkId, Found = false, Site = item.Site };
            AppendCache(rec);
            ConsecutiveFailures = 0;
            return rec;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // ネットワーク一時失敗等。キャッシュせず呼び出し側に再試行させる
            ConsecutiveFailures++;
            return null;
        }
    }

    private async Task ThrottleAsync(double rateSeconds, CancellationToken ct)
    {
        var wait = _lastRequest + TimeSpan.FromSeconds(rateSeconds + _random.NextDouble()) - DateTime.Now;
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, ct);
        }
        _lastRequest = DateTime.Now;
    }

    private CacheRecord FetchDlsite(string workId)
    {
        var info = DLsiteInfo.GetInfo(workId);
        return new CacheRecord
        {
            WorkId = workId,
            Found = true,
            Site = "DLsite",
            WorkType = info.WorkType,
            WorkTypeString = info.WorkTypeString,
            Title = info.Title,
            Maker = info.Circle,
            IsAi = info.IsAiGenerated,
        };
    }

    private CacheRecord FetchFanzaDoujin(string workId)
    {
        var info = FanzaInfo.GetInfo(workId);
        return new CacheRecord
        {
            WorkId = workId,
            Found = true,
            Site = "FANZA",
            WorkType = info.WorkType,
            WorkTypeString = info.WorkType,
            Title = info.Title,
            Maker = info.Circle,
            IsAi = info.IsAiGenerated,
        };
    }

    private async Task<CacheRecord> FetchFanzaCidAsync(string workId, CancellationToken ct)
    {
        var type = await FanzaFloorResolver.ResolveAsync(_http, workId, ct);
        if (string.IsNullOrEmpty(type))
        {
            return new CacheRecord { WorkId = workId, Found = false, Site = "FANZA" };
        }
        return new CacheRecord
        {
            WorkId = workId,
            Found = true,
            Site = "FANZA",
            WorkType = $"floor:{type}",
            WorkTypeString = type,
        };
    }
}
