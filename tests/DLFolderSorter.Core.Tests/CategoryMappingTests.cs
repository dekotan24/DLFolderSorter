using DLFolderSorter.Core;
using Xunit;

namespace DLFolderSorter.Core.Tests;

/// <summary>
/// 分類カバレッジの回帰テスト。
/// コード一覧は2026-07-07にDLsite実サイト（works/type全5カテゴリページ）と
/// FANZA同人実サイト（media=一覧、breadcrumb実測）から採取した一次情報。
/// </summary>
public class CategoryMappingTests
{
    /// <summary>DLsite現行の全21 work_typeコード（works/typeページ実測）。</summary>
    private static readonly string[] LiveDlsiteCodes =
    {
        "SLN", "ACN", "RPG", "ADV", "ETC", "TBL", "QIZ", "DNV", "TYP", "STG", "PZL", // ゲーム系11
        "TOL", "IMT", "AMT", "ET3",                                                  // ツール・素材・その他4
        "VCM", "MNG", "ICG", "MOV", "SOU", "MUS",                                    // コミック/CG/動画/音声系6
    };

    /// <summary>dlsite-asyncに存在した旧・書籍系コード（現行UIには無いが旧作品が返しうる）。</summary>
    private static readonly string[] LegacyDlsiteCodes = { "SCM", "WBT", "NRE", "PBC" };

    [Fact]
    public void AllLiveDlsiteCodes_AreMapped()
    {
        var mapping = new CategoryMapping();
        foreach (var code in LiveDlsiteCodes)
        {
            Assert.True(mapping.DlsiteWorkTypes.ContainsKey(code), $"未マッピングのwork_type: {code}");
        }
    }

    [Fact]
    public void LegacyDlsiteCodes_AreMapped()
    {
        var mapping = new CategoryMapping();
        foreach (var code in LegacyDlsiteCodes)
        {
            Assert.True(mapping.DlsiteWorkTypes.ContainsKey(code), $"未マッピングのレガシーwork_type: {code}");
        }
    }

    [Theory]
    [InlineData("SOU", Category.Voice)]
    [InlineData("VCM", Category.Voice)]
    [InlineData("MUS", Category.Voice)]
    [InlineData("MOV", Category.Movie)]
    [InlineData("RPG", Category.Game)]
    [InlineData("MNG", Category.Book)]
    [InlineData("ICG", Category.Book)]
    [InlineData("TOL", Category.Unknown)]
    [InlineData("IMT", Category.Unknown)]
    public void DlsiteResolve_KnownCodes(string code, string expected)
    {
        var mapping = new CategoryMapping();
        var rec = new CacheRecord { WorkId = "RJ000001", Found = true, Site = "DLsite", WorkType = code };
        Assert.Equal(expected, mapping.Resolve(rec));
    }

    /// <summary>FANZA同人の現行メディア4種のbreadcrumb実名が全部マッピングされていること。</summary>
    [Theory]
    [InlineData("ボイス", Category.Voice)]
    [InlineData("ゲーム", Category.Game)]
    [InlineData("コミック", Category.Book)]  // breadcrumb実測名(d_786513)。「マンガ」ではない
    [InlineData("CG", Category.Book)]
    public void FanzaDoujinResolve_LiveCategoryNames(string name, string expected)
    {
        var mapping = new CategoryMapping();
        var rec = new CacheRecord { WorkId = "d_000001", Found = true, Site = "FANZA", WorkType = name };
        Assert.Equal(expected, mapping.Resolve(rec));
    }

    [Theory]
    [InlineData("floor:av", Category.Av)]
    [InlineData("floor:videoa", Category.Av)]
    [InlineData("floor:amateur", Category.Av)]
    [InlineData("floor:anime", Category.Movie)]
    [InlineData("floor:doujin", Category.Unknown)]
    public void FanzaFloorResolve(string workType, string expected)
    {
        var mapping = new CategoryMapping();
        var rec = new CacheRecord { WorkId = "abc00001", Found = true, Site = "FANZA", WorkType = workType };
        Assert.Equal(expected, mapping.Resolve(rec));
    }

    [Fact]
    public void LoadOrDefault_MissingFile_KeepsAllDefaults()
    {
        var mapping = CategoryMapping.LoadOrDefault(Path.Combine(Path.GetTempPath(), "dlfs_no_such_mapping.json"));
        foreach (var code in LiveDlsiteCodes.Concat(LegacyDlsiteCodes))
        {
            Assert.True(mapping.DlsiteWorkTypes.ContainsKey(code), $"既定値が失われている: {code}");
        }
        Assert.Equal(Category.Book, mapping.FanzaDoujinCategories["コミック"]);
    }

    [Fact]
    public void LoadOrDefault_PartialFile_KeepsOtherDictsDefault()
    {
        // mapping.jsonに一部のプロパティしか無い場合、無いプロパティはコード既定値が生きる
        var path = Path.Combine(Path.GetTempPath(), $"dlfs_partial_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"DlsiteWorkTypes":{"SOU":"Voice"}}""");
        try
        {
            var mapping = CategoryMapping.LoadOrDefault(path);
            Assert.Single(mapping.DlsiteWorkTypes);                              // 指定したdictは全置換
            Assert.Equal(Category.Book, mapping.FanzaDoujinCategories["コミック"]); // 未指定dictは既定値
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UnknownValues_FallBackToUnknown()
    {
        var mapping = new CategoryMapping();
        Assert.Equal(Category.Unknown, mapping.Resolve(new CacheRecord { WorkId = "RJ1", Found = true, Site = "DLsite", WorkType = "XXX" }));
        Assert.Equal(Category.Unknown, mapping.Resolve(new CacheRecord { WorkId = "d_1", Found = true, Site = "FANZA", WorkType = "未知カテゴリ" }));
        Assert.Equal(Category.Unknown, mapping.Resolve(new CacheRecord { WorkId = "RJ2", Found = false }));
    }
}
