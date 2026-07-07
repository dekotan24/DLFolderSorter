using DLFolderSorter.Core;
using Xunit;

namespace DLFolderSorter.Core.Tests;

public sealed class PlannerExecutorTests : IDisposable
{
    private readonly string _root;
    private readonly string _src;
    private readonly string _voice;
    private readonly string _game;

    public PlannerExecutorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "dlfs_test_" + Guid.NewGuid().ToString("N"));
        _src = Path.Combine(_root, "src");
        _voice = Path.Combine(_root, "Voice");
        _game = Path.Combine(_root, "Game");
        Directory.CreateDirectory(_src);
        Directory.CreateDirectory(_voice);
        Directory.CreateDirectory(_game);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private SortConfig MakeConfig() => new()
    {
        SourceFolder = _src,
        IncludeFiles = true,
        RenameIdOnly = true,
        DestinationMap = new()
        {
            [Category.Voice] = _voice,
            [Category.Game] = "",  // ゲームは動かさない設定
        },
    };

    private static SortItem Classified(string path, bool isDir, string category, string title = "タイトル", string maker = "サークル")
    {
        var item = new SortItem
        {
            Name = Path.GetFileName(path),
            FullPath = path,
            IsDirectory = isDir,
        };
        IdExtractor.Extract(item, detectAv: true);
        item.Category = category;
        item.Title = title;
        item.Maker = maker;
        return item;
    }

    private string CreateSrcFile(string name)
    {
        var p = Path.Combine(_src, name);
        File.WriteAllText(p, "dummy");
        return p;
    }

    [Fact]
    public void Scan_ExcludesFiles_WhenIncludeFilesOff()
    {
        CreateSrcFile("A-RJ01000001.zip");
        Directory.CreateDirectory(Path.Combine(_src, "[RJ01000002] [c] フォルダ作品"));
        var config = MakeConfig();
        config.IncludeFiles = false;

        var items = Planner.Scan(config);

        Assert.Single(items);
        Assert.True(items[0].IsDirectory);
    }

    [Fact]
    public void BuildPlan_MoveAndRename()
    {
        var p = CreateSrcFile("V-RJ01000001.zip");
        var item = Classified(p, false, Category.Voice);
        var items = new List<SortItem> { item };

        Planner.BuildPlan(items, MakeConfig());

        Assert.Equal(PlanAction.MoveRename, item.Action);
        Assert.Equal(Path.Combine(_voice, "[RJ01000001] [サークル] タイトル.zip"), item.TargetPath);
    }

    [Fact]
    public void BuildPlan_TitledName_MovesWithoutRename()
    {
        var name = "[RJ01000001] [サークル] 既に整った名前.zip";
        var p = CreateSrcFile(name);
        var item = Classified(p, false, Category.Voice);

        Planner.BuildPlan(new List<SortItem> { item }, MakeConfig());

        Assert.Equal(PlanAction.Move, item.Action);
        Assert.Equal(Path.Combine(_voice, name), item.TargetPath);
    }

    [Fact]
    public void BuildPlan_EmptyDestination_RenamesInPlace()
    {
        var p = CreateSrcFile("G-RJ01000003.zip");
        var item = Classified(p, false, Category.Game);

        Planner.BuildPlan(new List<SortItem> { item }, MakeConfig());

        Assert.Equal(PlanAction.Rename, item.Action);
        Assert.Equal(Path.Combine(_src, "[RJ01000003] [サークル] タイトル.zip"), item.TargetPath);
    }

    [Fact]
    public void BuildPlan_NotFound_NoRename()
    {
        var p = CreateSrcFile("G-RJ01000004.zip");
        var item = Classified(p, false, Category.Unknown, title: "", maker: "");
        item.Flag = "作品が見つかりません(削除済み?)";

        Planner.BuildPlan(new List<SortItem> { item }, MakeConfig());

        Assert.Equal(PlanAction.Stay, item.Action);
    }

    [Fact]
    public void BuildPlan_PlanCollision_SkipsAll()
    {
        // 同一作品のA-版とCG-版（昨日のRJ01444167の実例）
        var a = Classified(CreateSrcFile("A-RJ01444167.zip"), false, Category.Voice);
        var b = Classified(CreateSrcFile("CG-RJ01444167.zip"), false, Category.Voice);
        var items = new List<SortItem> { a, b };

        Planner.BuildPlan(items, MakeConfig());

        Assert.Equal(PlanAction.Skip, a.Action);
        Assert.Equal(PlanAction.Skip, b.Action);
    }

    [Fact]
    public void BuildPlan_DestinationExists_Skips()
    {
        var p = CreateSrcFile("V-RJ01000005.zip");
        File.WriteAllText(Path.Combine(_voice, "[RJ01000005] [サークル] タイトル.zip"), "already");
        var item = Classified(p, false, Category.Voice);

        Planner.BuildPlan(new List<SortItem> { item }, MakeConfig());

        Assert.Equal(PlanAction.Skip, item.Action);
        Assert.Contains("既に存在", item.Flag);
    }

    [Fact]
    public void BuildPlan_PartSiblings_AllRenamedConsistently()
    {
        var items = new List<SortItem>
        {
            Classified(CreateSrcFile("A-RJ01029918.part1.rar"), false, Category.Voice),
            Classified(CreateSrcFile("A-RJ01029918.part2.rar"), false, Category.Voice),
        };

        Planner.BuildPlan(items, MakeConfig());

        Assert.All(items, i => Assert.Equal(PlanAction.MoveRename, i.Action));
        Assert.Equal(2, items.Select(i => i.TargetPath).Distinct().Count());
        Assert.All(items, i => Assert.StartsWith("[RJ01029918] [サークル] タイトル.part", Path.GetFileName(i.TargetPath)));
    }

    [Fact]
    public async Task Execute_SameVolume_MovesAndLogs()
    {
        var p = CreateSrcFile("V-RJ01000006.zip");
        var item = Classified(p, false, Category.Voice);
        Planner.BuildPlan(new List<SortItem> { item }, MakeConfig());
        var logPath = Path.Combine(_root, "log.csv");

        var executor = new Executor();
        var (ok, skipped, errors) = await executor.ExecuteAsync(new List<SortItem> { item }, logPath, CancellationToken.None);

        Assert.Equal((1, 0, 0), (ok, skipped, errors));
        Assert.False(File.Exists(p));
        Assert.True(File.Exists(item.TargetPath));
        var log = File.ReadAllText(logPath);
        Assert.Contains("ok", log);
    }

    [Fact]
    public async Task Execute_Rerun_IsIdempotent()
    {
        var p = CreateSrcFile("V-RJ01000007.zip");
        var item = Classified(p, false, Category.Voice);
        Planner.BuildPlan(new List<SortItem> { item }, MakeConfig());
        var logPath = Path.Combine(_root, "log.csv");
        var executor = new Executor();

        await executor.ExecuteAsync(new List<SortItem> { item }, logPath, CancellationToken.None);
        var second = await executor.ExecuteAsync(new List<SortItem> { item }, logPath, CancellationToken.None);

        // 2回目はsrc不在としてスキップされ、エラーにならない
        Assert.Equal((0, 1, 0), second);
    }

    [Fact]
    public async Task Execute_DirectoryMove()
    {
        var dir = Path.Combine(_src, "V-RJ01000008");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "01.mp3"), "audio");
        var item = Classified(dir, true, Category.Voice);
        Planner.BuildPlan(new List<SortItem> { item }, MakeConfig());

        var executor = new Executor();
        var result = await executor.ExecuteAsync(new List<SortItem> { item }, Path.Combine(_root, "log.csv"), CancellationToken.None);

        Assert.Equal((1, 0, 0), result);
        Assert.True(File.Exists(Path.Combine(item.TargetPath, "01.mp3")));
    }

    [Fact]
    public async Task Execute_DisabledItem_NotProcessed()
    {
        var p = CreateSrcFile("V-RJ01000009.zip");
        var item = Classified(p, false, Category.Voice);
        Planner.BuildPlan(new List<SortItem> { item }, MakeConfig());
        item.Enabled = false;

        var executor = new Executor();
        var result = await executor.ExecuteAsync(new List<SortItem> { item }, Path.Combine(_root, "log.csv"), CancellationToken.None);

        Assert.Equal((0, 0, 0), result);
        Assert.True(File.Exists(p));
    }

    [Fact]
    public void Preflight_SameVolume_NoWarnings()
    {
        var p = CreateSrcFile("V-RJ01000010.zip");
        var item = Classified(p, false, Category.Voice);
        Planner.BuildPlan(new List<SortItem> { item }, MakeConfig());

        var warnings = Executor.PreflightCheck(new List<SortItem> { item });

        Assert.Empty(warnings);
    }
}
