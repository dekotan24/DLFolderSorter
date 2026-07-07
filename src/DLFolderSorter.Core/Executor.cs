using System.Text;

namespace DLFolderSorter.Core;

/// <summary>
/// 計画を実行する。安全設計:
/// - 同一ボリューム: File.Move/Directory.Move（原子的・瞬時。既存宛先は上書きしない）
/// - 別ボリューム: コピー→サイズ検証→検証成功後のみ元削除
/// - 実行直前に毎回 src存在・dst不在・両ルート生存を確認（ドライブ切断ガード）
/// - 1件ごとにCSVログをappend&flush（クラッシュしても移動済み分の記録が残る=手動undoのレシピ）
/// - 連続エラーで自動中断（再実行すれば処理済み分はsrc不在としてスキップされ、続きから再開できる）
/// </summary>
public sealed class Executor
{
    public int MaxConsecutiveErrors { get; init; } = 10;

    public event Action<int, int, SortItem>? Progress;

    /// <summary>実行前チェック: 別ボリューム移動分の合計サイズが宛先の空き容量に収まるか。</summary>
    public static List<string> PreflightCheck(List<SortItem> items)
    {
        var warnings = new List<string>();
        var crossVolume = items
            .Where(i => i.Enabled && (i.Action is PlanAction.Move or PlanAction.MoveRename))
            .Where(i => !IsSameVolume(i.FullPath, i.TargetPath))
            .GroupBy(i => Path.GetPathRoot(i.TargetPath)!, StringComparer.OrdinalIgnoreCase);

        foreach (var group in crossVolume)
        {
            long required = 0;
            foreach (var item in group)
            {
                try
                {
                    required += item.IsDirectory
                        ? new DirectoryInfo(item.FullPath).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
                        : new FileInfo(item.FullPath).Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"サイズ取得失敗: {item.Name} ({ex.Message})");
                }
            }
            var free = new DriveInfo(group.Key).AvailableFreeSpace;
            if (required > free)
            {
                warnings.Add($"空き容量不足: {group.Key} 必要{required / 1024 / 1024:N0}MB / 空き{free / 1024 / 1024:N0}MB");
            }
        }
        return warnings;
    }

    public static bool IsSameVolume(string a, string b)
        => string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    /// <summary>計画を実行し、(成功, スキップ, エラー)件数を返す。</summary>
    public async Task<(int Ok, int Skipped, int Errors)> ExecuteAsync(
        List<SortItem> items, string logPath, CancellationToken ct)
    {
        var targets = items
            .Where(i => i.Enabled && i.Action is PlanAction.Rename or PlanAction.Move or PlanAction.MoveRename)
            .ToList();

        int ok = 0, skipped = 0, errors = 0, consecutiveErrors = 0;
        var writeHeader = !File.Exists(logPath);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        await using var log = new StreamWriter(logPath, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        if (writeHeader)
        {
            await log.WriteLineAsync("timestamp,action,src,dst,result,error");
        }
        await log.WriteLineAsync($"# DLFolderSorter run {DateTime.Now:O}");
        await log.FlushAsync(ct);

        for (var i = 0; i < targets.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = targets[i];
            var result = await Task.Run(() => ExecuteOne(item), ct);
            item.Result = result.Result;

            await log.WriteLineAsync(string.Join(",",
                DateTime.Now.ToString("O"),
                item.Action,
                Csv(item.FullPath),
                Csv(item.TargetPath),
                result.Result,
                Csv(result.Error)));
            await log.FlushAsync(ct);

            switch (result.Result)
            {
                case "ok":
                    ok++;
                    consecutiveErrors = 0;
                    break;
                case "error":
                    errors++;
                    consecutiveErrors++;
                    break;
                default:
                    skipped++;
                    break;
            }

            Progress?.Invoke(i + 1, targets.Count, item);

            if (consecutiveErrors >= MaxConsecutiveErrors)
            {
                item.Result += " (連続エラーにより中断)";
                break;
            }
        }
        return (ok, skipped, errors);
    }

    private (string Result, string Error) ExecuteOne(SortItem item)
    {
        try
        {
            // 実行直前の再チェック（TOCTOU・ドライブ切断対策）
            var srcRoot = Path.GetPathRoot(item.FullPath)!;
            var dstRoot = Path.GetPathRoot(item.TargetPath)!;
            if (!Directory.Exists(srcRoot) || !Directory.Exists(dstRoot))
            {
                return ("error", "ドライブが見つかりません");
            }
            var srcExists = item.IsDirectory ? Directory.Exists(item.FullPath) : File.Exists(item.FullPath);
            if (!srcExists)
            {
                return ("src_missing", "");
            }
            if (File.Exists(item.TargetPath) || Directory.Exists(item.TargetPath))
            {
                return ("target_exists", "");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(item.TargetPath)!);

            if (IsSameVolume(item.FullPath, item.TargetPath))
            {
                if (item.IsDirectory)
                {
                    Directory.Move(item.FullPath, item.TargetPath);
                }
                else
                {
                    File.Move(item.FullPath, item.TargetPath, overwrite: false);
                }
            }
            else
            {
                CrossVolumeMove(item);
            }
            return ("ok", "");
        }
        catch (Exception ex)
        {
            return ("error", ex.Message);
        }
    }

    /// <summary>
    /// 別ボリューム移動: 一時名(.dlfs_tmp)へコピー→サイズ検証→宛先ボリューム内で正規名へリネーム(原子的)→元削除。
    /// 途中失敗しても正規名を部分コピーが占有しない（占有すると再実行時にtarget_existsで
    /// 「完了済み」を装ってしまうため）。失敗時は一時名を掃除する。
    /// </summary>
    private static void CrossVolumeMove(SortItem item)
    {
        var staging = item.TargetPath + ".dlfs_tmp";
        try
        {
            // 前回クラッシュの残骸があれば除去
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            if (File.Exists(staging)) File.Delete(staging);

            if (item.IsDirectory)
            {
                CopyDirectory(item.FullPath, staging);
                VerifyDirectoryCopy(item.FullPath, staging);
                Directory.Move(staging, item.TargetPath);
                Directory.Delete(item.FullPath, recursive: true);
            }
            else
            {
                File.Copy(item.FullPath, staging, overwrite: false);
                VerifyFileCopy(item.FullPath, staging);
                File.Move(staging, item.TargetPath, overwrite: false);
                File.Delete(item.FullPath);
            }
        }
        catch
        {
            try
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
                if (File.Exists(staging)) File.Delete(staging);
            }
            catch (IOException)
            {
                // 掃除失敗は無視（次回実行時にも除去を試みる）
            }
            throw;
        }
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(dst, Path.GetRelativePath(src, dir)));
        }
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(dst, Path.GetRelativePath(src, file)), overwrite: false);
        }
    }

    private static void VerifyFileCopy(string src, string dst)
    {
        var srcLen = new FileInfo(src).Length;
        var dstLen = new FileInfo(dst).Length;
        if (srcLen != dstLen)
        {
            throw new IOException($"コピー検証失敗: サイズ不一致 ({srcLen} != {dstLen})");
        }
    }

    private static void VerifyDirectoryCopy(string src, string dst)
    {
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var copied = Path.Combine(dst, Path.GetRelativePath(src, file));
            if (!File.Exists(copied))
            {
                throw new IOException($"コピー検証失敗: ファイル欠落 {copied}");
            }
            VerifyFileCopy(file, copied);
        }
    }

    private static string Csv(string s)
        => s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
}
