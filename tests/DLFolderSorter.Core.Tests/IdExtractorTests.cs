using DLFolderSorter.Core;
using Xunit;

namespace DLFolderSorter.Core.Tests;

/// <summary>
/// ID抽出の回帰テスト。ケースは2026-07-07のO:\Game仕分け(8119件)で実在した名前と
/// JavSPのテストデータに基づく。
/// </summary>
public class IdExtractorTests
{
    private static SortItem Make(string name, bool isDir = false)
        => new() { Name = name, FullPath = @"C:\src\" + name, IsDirectory = isDir };

    [Theory]
    [InlineData("A-RJ01012286.rar", "RJ01012286", "A-", "", ".rar")]
    [InlineData("A-RJ01029918.part1.rar", "RJ01029918", "A-", "", ".part1.rar")]
    [InlineData("G-RJ19123.zip", "RJ19123", "G-", "", ".zip")] // 5桁RJが実在する
    [InlineData("G-RJ01002988-v1.1.3.rar", "RJ01002988", "G-", "-v1.1.3", ".rar")]
    [InlineData("G-G-RJ01076970-v2.08-DLC-v1.2.zip", "RJ01076970", "G-G-", "-v2.08-DLC-v1.2", ".zip")]
    [InlineData("A-VJ01000517.zip", "VJ01000517", "A-", "", ".zip")]
    [InlineData("DJGAME-250111-RJ01305765----4C745346_1.rar", "RJ01305765", "DJGAME-250111-", "----4C745346_1", ".rar")]
    public void Dlsite_PrefixSuffixExt(string name, string id, string prefix, string suffix, string ext)
    {
        var item = Make(name);
        IdExtractor.Extract(item, detectAv: true);
        Assert.Equal(IdKind.Dlsite, item.IdKind);
        Assert.Equal(id, item.WorkId);
        Assert.Equal(prefix, item.Prefix);
        Assert.Equal(suffix, item.Suffix);
        Assert.Equal(ext, item.ExtChain);
    }

    [Fact]
    public void Dlsite_TwoIds_TakesFirst()
    {
        var item = Make("G-RJ01162016-RJ01426940-v25.08.25.part1.rar");
        IdExtractor.Extract(item, detectAv: true);
        Assert.Equal("RJ01162016", item.WorkId);
        Assert.Equal("-RJ01426940-v25.08.25", item.Suffix);
    }

    [Theory]
    [InlineData("d_750863.zip", "d_750863")]
    [InlineData("[d_123456] 作品タイトル", "d_123456")]
    [InlineData("d_aisoft3356.zip", "d_aisoft3356")] // 英字レーベル入りcid(JavSPテストデータ実在)
    [InlineData("D_750863.zip", "d_750863")] // 大文字は小文字化
    public void FanzaDoujin_Detected(string name, string id)
    {
        var item = Make(name, isDir: name.StartsWith('['));
        IdExtractor.Extract(item, detectAv: true);
        Assert.Equal(IdKind.FanzaDoujin, item.IdKind);
        Assert.Equal(id, item.WorkId);
        Assert.Equal("FANZA", item.Site);
    }

    [Fact]
    public void FanzaDoujin_NoFalsePositive_AfterAlnum()
    {
        // sound_01 のような英数字直後のd_を誤検出しない(v1.4.1の実バグ)
        var item = Make("sound_015.zip");
        IdExtractor.Extract(item, detectAv: false);
        Assert.Equal(IdKind.None, item.IdKind);
    }

    [Theory]
    [InlineData("1sgki00015c.mp4", "1sgki00015c")] // デコ提供の実例URL由来
    [InlineData("ssis00905.mp4", "ssis00905")]
    [InlineData("hjmo00214", "hjmo00214")] // JavSPテストデータ(フォルダ名)
    public void FanzaCid_WholeName(string name, string cid)
    {
        var item = Make(name, isDir: !name.Contains('.'));
        IdExtractor.Extract(item, detectAv: true);
        Assert.Equal(IdKind.FanzaCid, item.IdKind);
        Assert.Equal(cid, item.WorkId);
    }

    [Theory]
    [InlineData("SGKI-015.mp4", "SGKI-015")]
    [InlineData("SSIS905 タイトル付き.mp4", "SSIS-905")]
    [InlineData("[SGKI-015] なんかのタイトル", "SGKI-015")]
    public void FanzaCid_HyphenCode(string name, string code)
    {
        var item = Make(name, isDir: !name.Contains('.'));
        IdExtractor.Extract(item, detectAv: true);
        Assert.Equal(IdKind.FanzaCid, item.IdKind);
        Assert.Equal(code, item.WorkId);
    }

    [Fact]
    public void AvDetection_OnlyWhenEnabled()
    {
        var item = Make("SGKI-015.mp4");
        IdExtractor.Extract(item, detectAv: false);
        Assert.Equal(IdKind.None, item.IdKind);
    }

    [Theory]
    [InlineData("GAME-250725-1338252-Muttsuri-DL_1.part1.rar")] // 昨日のID無し実例。誤検出しないこと
    [InlineData("読めないタイトルのフォルダ")]
    public void NoId_NotMisdetected(string name)
    {
        var item = Make(name, isDir: !name.Contains('.'));
        IdExtractor.Extract(item, detectAv: true);
        Assert.Equal(IdKind.None, item.IdKind);
    }

    [Fact]
    public void DlsiteTakesPriorityOverAv()
    {
        // "CG-RJ01047751" の "CG" をAV品番と誤認しない
        var item = Make("CG-RJ01047751.zip");
        IdExtractor.Extract(item, detectAv: true);
        Assert.Equal(IdKind.Dlsite, item.IdKind);
        Assert.Equal("RJ01047751", item.WorkId);
    }
}
