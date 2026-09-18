using VSCodeRecent.Models;
using Xunit;

namespace VSCodeRecent.Tests;

/// <summary>
/// 过滤、排序、去重。都是纯函数，不碰文件系统。
/// </summary>
public class VSCodeRecentHistoryTests
{
    private static VSCodeItem Item(string path, ItemKind kind, int order) => new()
    {
        Title = Path.GetFileName(path),
        Path = path,
        Kind = kind,
        Order = order,
        Target = new VSCodeOpenTarget.LocalFolder(path),
    };

    /// <summary>「显示文件」关闭时只留工作区与文件夹。</summary>
    [Fact]
    public void Filter_ExcludesFilesWhenDisabled()
    {
        IReadOnlyList<VSCodeItem> items =
        [
            Item(@"C:\a\one.cs", ItemKind.File, 0),
            Item(@"C:\a\proj", ItemKind.Folder, 1),
            Item(@"C:\a\x.code-workspace", ItemKind.Workspace, 2),
        ];

        var filtered = VSCodeRecentHistory.Filter(items, includeFiles: false);

        Assert.Equal([ItemKind.Folder, ItemKind.Workspace], filtered.Select(i => i.Kind));
    }

    [Fact]
    public void Filter_KeepsEverythingWhenEnabled()
    {
        IReadOnlyList<VSCodeItem> items =
        [
            Item(@"C:\a\one.cs", ItemKind.File, 0),
            Item(@"C:\a\proj", ItemKind.Folder, 1),
        ];

        Assert.Equal(2, VSCodeRecentHistory.Filter(items, includeFiles: true).Count);
    }

    /// <summary>Rank 只是 Order 并列时的兜底：工作区 → 文件夹 → 文件。</summary>
    [Fact]
    public void Rank_OrdersWorkspaceThenFolderThenFile()
    {
        Assert.True(ItemKind.Workspace.Rank() < ItemKind.Folder.Rank());
        Assert.True(ItemKind.Folder.Rank() < ItemKind.File.Rank());
    }

    /// <summary>
    /// 核心排序语义：Order 升序 = 最后打开时间倒序，**不按类型分组**。
    /// 工作区与文件夹在 VSCode 的记录里属于同一段，工作区不该因为类型被提前。
    /// </summary>
    [Fact]
    public void SortAndDedupe_OrdersByRecencyAcrossKinds()
    {
        var reads = new[]
        {
            SourceRead.Ok(
            [
                Item(@"C:\a\ws.code-workspace", ItemKind.Workspace, 5),
                Item(@"C:\a\recent", ItemKind.Folder, 0),
                Item(@"C:\a\older", ItemKind.Folder, 3),
            ]),
        };

        var sorted = VSCodeRecentHistory.SortAndDedupe(reads);

        Assert.Equal(
            [@"C:\a\recent", @"C:\a\older", @"C:\a\ws.code-workspace"],
            sorted.Select(i => i.Path));
    }

    /// <summary>
    /// 跨来源不按 Order 混排：Order 只在单个来源内有意义，两个来源都从 0 开始。
    /// 来源优先级由枚举顺序决定。
    /// </summary>
    [Fact]
    public void SortAndDedupe_KeepsSourceOrderAcrossSources()
    {
        var reads = new[]
        {
            SourceRead.Ok([Item(@"C:\a\first", ItemKind.Folder, 9)]),
            SourceRead.Ok([Item(@"C:\a\second", ItemKind.Folder, 0)]),
        };

        var sorted = VSCodeRecentHistory.SortAndDedupe(reads);

        Assert.Equal([@"C:\a\first", @"C:\a\second"], sorted.Select(i => i.Path));
    }

    /// <summary>同一路径出现在多个来源时，保留先出现的那个来源的记录。</summary>
    [Fact]
    public void SortAndDedupe_KeepsFirstSourceForDuplicatePath()
    {
        var reads = new[]
        {
            SourceRead.Ok([Item(@"C:\a\dup", ItemKind.Folder, 4)]),
            SourceRead.Ok([Item(@"C:\a\dup", ItemKind.Folder, 0)]),
        };

        var sorted = VSCodeRecentHistory.SortAndDedupe(reads);

        var item = Assert.Single(sorted);
        Assert.Equal(4, item.Order);
    }

    /// <summary>路径去重大小写不敏感（Windows 语义）。</summary>
    [Fact]
    public void SortAndDedupe_DedupesPathsCaseInsensitively()
    {
        var reads = new[]
        {
            SourceRead.Ok([Item(@"C:\A\dup", ItemKind.Folder, 0)]),
            SourceRead.Ok([Item(@"c:\a\DUP", ItemKind.Folder, 0)]),
        };

        Assert.Single(VSCodeRecentHistory.SortAndDedupe(reads));
    }

    /// <summary>读失败的来源不贡献条目，但不影响其它来源排序。</summary>
    [Fact]
    public void SortAndDedupe_SkipsFailedSources()
    {
        var reads = new[]
        {
            SourceRead.Failed("库被锁"),
            SourceRead.Ok(
            [
                Item(@"C:\a\b", ItemKind.Folder, 1),
                Item(@"C:\a\a", ItemKind.Folder, 0),
            ]),
        };

        var sorted = VSCodeRecentHistory.SortAndDedupe(reads);

        Assert.Equal([@"C:\a\a", @"C:\a\b"], sorted.Select(i => i.Path));
    }

    [Fact]
    public void IsProject_ExcludesOnlyFiles()
    {
        Assert.True(ItemKind.Workspace.IsProject());
        Assert.True(ItemKind.Folder.IsProject());
        Assert.False(ItemKind.File.IsProject());
    }

    /// <summary>
    /// 类型名由字符串参数转 —— ItemKind 是 internal，不能出现在测试类的 public 签名里。
    /// </summary>
    [Theory]
    [InlineData("Workspace", "工作区")]
    [InlineData("Folder", "文件夹")]
    [InlineData("File", "文件")]
    public void Label_IsStable(string kindName, string expected)
    {
        var kind = Enum.Parse<ItemKind>(kindName);

        Assert.Equal(expected, kind.Label());
        Assert.Equal(expected, Item("x", kind, 0).TypeLabel);
    }
}
