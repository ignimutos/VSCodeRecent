using VSCodeRecent.Models;
using Xunit;

namespace VSCodeRecent.Tests;

/// <summary>
/// 列表页的分组切割：项目一段、文件一段，空组不出现。
/// </summary>
public class ItemGroupsTests
{
    private static VSCodeItem Item(string path, ItemKind kind, int order) => new()
    {
        Title = Path.GetFileName(path),
        Path = path,
        Kind = kind,
        Order = order,
        Target = new VSCodeOpenTarget.LocalFolder(path),
    };

    /// <summary>标题要带条数：项目段与文件段各报各的数量。</summary>
    [Fact]
    public void Split_PutsProjectsBeforeFilesWithCounts()
    {
        IReadOnlyList<VSCodeItem> items =
        [
            Item(@"C:\a\one.cs", ItemKind.File, 0),
            Item(@"C:\a\proj", ItemKind.Folder, 1),
            Item(@"C:\a\x.code-workspace", ItemKind.Workspace, 2),
        ];

        var groups = ItemGroups.Split(items);

        Assert.Equal(
            [$"{ItemGroups.ProjectsTitle} (2)", $"{ItemGroups.FilesTitle} (1)"],
            groups.Select(g => g.Title));
        Assert.Equal(2, groups[0].Items.Count);
        Assert.Single(groups[1].Items);
    }

    /// <summary>组内保持传进来的顺序（排序是上一步的事）。</summary>
    [Fact]
    public void Split_PreservesOrderInsideGroups()
    {
        IReadOnlyList<VSCodeItem> items =
        [
            Item(@"C:\a\newer", ItemKind.Folder, 0),
            Item(@"C:\a\older", ItemKind.Folder, 1),
            Item(@"C:\a\f2.cs", ItemKind.File, 2),
            Item(@"C:\a\f1.cs", ItemKind.File, 3),
        ];

        var groups = ItemGroups.Split(items);

        Assert.Equal([@"C:\a\newer", @"C:\a\older"], groups[0].Items.Select(i => i.Path));
        Assert.Equal([@"C:\a\f2.cs", @"C:\a\f1.cs"], groups[1].Items.Select(i => i.Path));
    }

    /// <summary>默认视图（关掉「显示文件」）只有一组，标题该由页面自己省掉。</summary>
    [Fact]
    public void Split_OnlyProjects_YieldsSingleGroup()
    {
        IReadOnlyList<VSCodeItem> items = [Item(@"C:\a\proj", ItemKind.Folder, 0)];

        var groups = ItemGroups.Split(items);

        Assert.Equal($"{ItemGroups.ProjectsTitle} (1)", Assert.Single(groups).Title);
    }

    [Fact]
    public void Split_OnlyFiles_YieldsSingleGroup()
    {
        IReadOnlyList<VSCodeItem> items = [Item(@"C:\a\one.cs", ItemKind.File, 0)];

        var groups = ItemGroups.Split(items);

        Assert.Equal($"{ItemGroups.FilesTitle} (1)", Assert.Single(groups).Title);
    }

    [Fact]
    public void Split_Empty_YieldsNoGroups()
    {
        Assert.Empty(ItemGroups.Split([]));
    }
}
