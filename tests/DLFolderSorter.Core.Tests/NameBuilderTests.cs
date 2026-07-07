using DLFolderSorter.Core;
using Xunit;

namespace DLFolderSorter.Core.Tests;

public class NameBuilderTests
{
    [Fact]
    public void Build_BasicFormat()
    {
        var name = NameBuilder.Build("RJ01002988", "メタモルフォーゼ", "セイントギアフォース", "-v1.1.3", ".rar");
        Assert.Equal("[RJ01002988] [メタモルフォーゼ] セイントギアフォース-v1.1.3.rar", name);
    }

    [Fact]
    public void Build_SuffixPreserved_AvoidsVersionCollision()
    {
        // サフィックス温存により同一作品の別バージョンが別名になる(昨日の衝突587→2件の核心)
        var a = NameBuilder.Build("RJ01028095", "サークル", "タイトル", "-v1.8", ".zip");
        var b = NameBuilder.Build("RJ01028095", "サークル", "タイトル", "-v2.0", ".zip");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Build_InvalidCharsSanitized()
    {
        var name = NameBuilder.Build("RJ000001", "CHAOS-R / CHAOS-L", "タイトル: <試験>?", "", ".zip");
        Assert.DoesNotContain('/', name[1..]); // 先頭[以降にパス不正文字が無い
        Assert.DoesNotContain(':', name);
        Assert.DoesNotContain('<', name);
        Assert.DoesNotContain('?', name);
    }

    [Fact]
    public void Build_ZeroWidthCharsRemoved()
    {
        // px-downloaderで実害があったU+200B(ゼロ幅スペース)
        var name = NameBuilder.Build("RJ000001", "\u200Bサークル", "タイトル\uFEFF", "", ".zip");
        Assert.DoesNotContain('\u200B', name);
        Assert.DoesNotContain('\uFEFF', name);
    }

    [Fact]
    public void Build_LongTitleTruncated_KeepsSuffixAndExt()
    {
        var longTitle = new string('あ', 300);
        var name = NameBuilder.Build("RJ01003347", "サークル", longTitle, "-v1.0", ".part1.rar");
        Assert.True(name.Length <= 200, $"len={name.Length}");
        Assert.EndsWith("-v1.0.part1.rar", name);
    }

    [Fact]
    public void Build_NoTrailingDotOrSpace()
    {
        var name = NameBuilder.Build("RJ000001", "サークル", "タイトル…終わりが点.", "", "");
        Assert.False(name.EndsWith('.'));
        Assert.False(name.EndsWith(' '));
    }

    [Theory]
    [InlineData("A-", "", false)]              // プレフィックスのみ→リネーム対象
    [InlineData("G-", "-v1.8", false)]         // 版数サフィックス→リネーム対象
    [InlineData("[", "] [サークル] タイトル", true)] // CJKあり→リネームしない
    [InlineData("", "スペシャル版", true)]
    public void HasTitlePart(string prefix, string suffix, bool expected)
    {
        Assert.Equal(expected, NameBuilder.HasTitlePart(prefix, suffix));
    }
}
