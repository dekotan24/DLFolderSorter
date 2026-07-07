namespace DLFolderSorter.Core;

/// <summary>作品IDの種類。</summary>
public enum IdKind
{
    None,
    /// <summary>RJ/VJ/RE/VE/BJ/AJ + 数字</summary>
    Dlsite,
    /// <summary>d_数字 / d_英字数字（FANZA同人cid）</summary>
    FanzaDoujin,
    /// <summary>DMM cid（1sgki00015c等）または品番（SGKI-015等）。フロアは逆引きで判定する</summary>
    FanzaCid,
}

/// <summary>仕分けカテゴリ。mapping JSONのキーに対応する。</summary>
public static class Category
{
    public const string Game = "Game";
    public const string Voice = "Voice";
    public const string Movie = "Movie";
    public const string Book = "Book";
    public const string Av = "AV";
    public const string Unknown = "Unknown";

    public static readonly string[] All = { Game, Voice, Movie, Book, Av, Unknown };
}

/// <summary>仕分け対象1件。スキャン→照会→計画→実行を通して使う。</summary>
public sealed class SortItem
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }

    public IdKind IdKind { get; set; } = IdKind.None;
    public string WorkId { get; set; } = "";
    /// <summary>IDより前の部分（A-/G-等の旧分類コード。リネーム時に捨てる）</summary>
    public string Prefix { get; set; } = "";
    /// <summary>IDより後ろ、拡張子より前（-v1.8等。リネーム時に温存する）</summary>
    public string Suffix { get; set; } = "";
    /// <summary>拡張子チェーン（.part1.rar / .zip。フォルダは空）</summary>
    public string ExtChain { get; set; } = "";

    // 照会結果
    public string WorkType { get; set; } = "";
    public string WorkTypeString { get; set; } = "";
    public string Title { get; set; } = "";
    public string Maker { get; set; } = "";
    public string Site { get; set; } = "";   // "DLsite" / "FANZA"
    public bool IsAi { get; set; }

    // 計画
    public string Category { get; set; } = Core.Category.Unknown;
    public PlanAction Action { get; set; } = PlanAction.Stay;
    public string NewName { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public string Flag { get; set; } = "";
    /// <summary>プレビューでユーザーが除外した場合false。</summary>
    public bool Enabled { get; set; } = true;

    // 実行結果
    public string Result { get; set; } = "";
}

public enum PlanAction
{
    Stay,
    Rename,
    Move,
    MoveRename,
    Skip,
}

/// <summary>照会キャッシュの1レコード（JSONL）。</summary>
public sealed class CacheRecord
{
    public required string WorkId { get; set; }
    public bool Found { get; set; }
    public string Site { get; set; } = "";
    public string WorkType { get; set; } = "";
    public string WorkTypeString { get; set; } = "";
    public string Title { get; set; } = "";
    public string Maker { get; set; } = "";
    public bool IsAi { get; set; }
}

/// <summary>アプリ設定（JSON保存）。</summary>
public sealed class SortConfig
{
    public string SourceFolder { get; set; } = "";
    /// <summary>true=DLsite/FANZAで仕分け先を分ける（ソート軸）。</summary>
    public bool SeparateSites { get; set; }
    /// <summary>true=AI生成作品を別の仕分け先に分ける（ソート軸）。</summary>
    public bool SeparateAi { get; set; }
    /// <summary>区分キー（DestKey.Build）→ 仕分け先フォルダ。空文字=その区分は動かさない。</summary>
    public Dictionary<string, string> DestinationMap { get; set; } = new();
    /// <summary>false=フォルダのみ仕分け対象。</summary>
    public bool IncludeFiles { get; set; } = true;
    /// <summary>IDしか含まない名前を [ID] [サークル] タイトル 形式にリネームする。</summary>
    public bool RenameIdOnly { get; set; } = true;
    /// <summary>種別不明をUnknownカテゴリへ移動する（false=元の場所に残す）。</summary>
    public bool MoveUnknown { get; set; }
    /// <summary>API呼び出し間隔（秒）。</summary>
    public double RateSeconds { get; set; } = 2.5;
}
