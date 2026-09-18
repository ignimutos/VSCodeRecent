namespace VSCodeRecent.Models;

/// <summary>
/// 列表页要渲染的分组。纯函数，不碰渲染也不碰文件系统。
///
/// <para><b>为什么分组，而不是一个全局时间倒序的列表。</b>VSCode 的最近记录里，
/// 工作区与文件夹属于同一个 MRU 段，文件是紧接着的第二段 —— 两段之间没有可比较的
/// 时间戳（VSCode 只存顺序，不存时间），所以它自己也分段展示。这里照做，
/// 不假装能把两段揉成一条时间线。</para>
///
/// <para>组内顺序沿用 <see cref="VSCodeRecentHistory.SortAndDedupe"/> 排好的顺序，
/// 本类只做切割。</para>
/// </summary>
internal static class ItemGroups
{
    /// <summary>项目组（工作区 + 文件夹）的标题。</summary>
    public const string ProjectsTitle = "项目";

    /// <summary>文件组的标题。</summary>
    public const string FilesTitle = "文件";

    /// <summary>
    /// 切成「项目 → 文件」两组，空组不出现（只有文件时不会留一个空的项目组）。
    /// 标题带条数：文件动辄几百条，标题里先给个总数，省得以为要一路滚到底。
    /// </summary>
    public static IReadOnlyList<(string Title, IReadOnlyList<VSCodeItem> Items)> Split(
        IReadOnlyList<VSCodeItem> items)
    {
        var projects = items.Where(x => x.Kind.IsProject()).ToList();
        var files = items.Where(x => !x.Kind.IsProject()).ToList();

        var groups = new List<(string, IReadOnlyList<VSCodeItem>)>(2);

        if (projects.Count > 0)
        {
            groups.Add((Label(ProjectsTitle, projects.Count), projects));
        }

        if (files.Count > 0)
        {
            groups.Add((Label(FilesTitle, files.Count), files));
        }

        return groups;
    }

    /// <summary>分组标题：名字 + 条数，如「文件 (321)」。</summary>
    private static string Label(string title, int count) => $"{title} ({count})";
}
