using DLFolderSorter.Core;
using Xunit;

namespace DLFolderSorter.Core.Tests;

public class DestKeyTests
{
    [Fact]
    public void NoAxes_PlainCategories()
    {
        var rows = DestKey.EnumerateRows(separateSites: false, separateAi: false);
        Assert.Equal(new[] { "Game", "Voice", "Movie", "Book", "AV", "Unknown" }, rows.Select(r => r.Key).ToArray());
        Assert.Equal("ゲーム", rows[0].Label);
    }

    [Fact]
    public void SitesOnly_SplitsBySite()
    {
        var rows = DestKey.EnumerateRows(separateSites: true, separateAi: false);
        Assert.Equal("Game|DLsite", rows[0].Key);
        Assert.Equal("ゲーム（DLsite）", rows[0].Label);
        Assert.Equal("Game|FANZA", rows[1].Key);
        // AV/Unknownは分割されない
        Assert.Contains(rows, r => r.Key == "AV" && r.Label == "AV");
        Assert.Contains(rows, r => r.Key == "Unknown" && r.Label == "不明");
    }

    [Fact]
    public void AiOnly_AiRowFirst()
    {
        var rows = DestKey.EnumerateRows(separateSites: false, separateAi: true);
        Assert.Equal("Game|AI", rows[0].Key);
        Assert.Equal("AIゲーム", rows[0].Label);
        Assert.Equal("Game", rows[1].Key);
        Assert.Equal("ゲーム", rows[1].Label);
    }

    [Fact]
    public void BothAxes_OrderMatchesSpec()
    {
        // デコ指定の並び: AIゲーム(DLsite), ゲーム(DLsite), AIゲーム(FANZA), ゲーム(FANZA), ...
        var rows = DestKey.EnumerateRows(separateSites: true, separateAi: true);
        Assert.Equal("Game|DLsite|AI", rows[0].Key);
        Assert.Equal("AIゲーム（DLsite）", rows[0].Label);
        Assert.Equal("Game|DLsite", rows[1].Key);
        Assert.Equal("Game|FANZA|AI", rows[2].Key);
        Assert.Equal("Game|FANZA", rows[3].Key);
        Assert.Equal("Voice|DLsite|AI", rows[4].Key);
        // 4カテゴリ×4 + AV + Unknown = 18行
        Assert.Equal(18, rows.Count);
    }

    [Theory]
    [InlineData("Game", "DLsite", false, false, false, "Game")]
    [InlineData("Game", "DLsite", true, false, false, "Game")]          // AI軸OFFならAIでも同じ区分
    [InlineData("Game", "DLsite", true, false, true, "Game|AI")]
    [InlineData("Game", "FANZA", false, true, false, "Game|FANZA")]
    [InlineData("Game", "FANZA", true, true, true, "Game|FANZA|AI")]
    [InlineData("AV", "FANZA", false, true, true, "AV")]                // AVは常に単一区分
    [InlineData("Unknown", "DLsite", true, true, true, "Unknown")]      // 不明も常に単一区分
    public void Build_MatchesAxes(string category, string site, bool isAi, bool sites, bool ai, string expected)
    {
        Assert.Equal(expected, DestKey.Build(category, site, isAi, sites, ai));
    }

    [Fact]
    public void Build_And_EnumerateRows_AreConsistent()
    {
        // Buildが返すキーは必ずEnumerateRowsの行に存在する（区分の取りこぼし防止）
        foreach (var sites in new[] { false, true })
        foreach (var ai in new[] { false, true })
        {
            var keys = DestKey.EnumerateRows(sites, ai).Select(r => r.Key).ToHashSet();
            foreach (var cat in new[] { Category.Game, Category.Voice, Category.Movie, Category.Book, Category.Av, Category.Unknown })
            foreach (var site in DestKey.Sites)
            foreach (var isAi in new[] { false, true })
            {
                var key = DestKey.Build(cat, site, isAi, sites, ai);
                Assert.Contains(key, keys);
            }
        }
    }
}
