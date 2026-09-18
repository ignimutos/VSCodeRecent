namespace VSCodeRecent.Models;

/// <summary>
/// 一条最近记录的类型。用值而不是字符串 —— 排序权重、显示名、是否算「项目」
/// 都挂在类型自己身上，加新类型时编译器会把漏掉的 switch 指出来。
/// </summary>
internal enum ItemKind
{
    /// <summary>.code-workspace 工作区文件</summary>
    Workspace,

    /// <summary>项目文件夹</summary>
    Folder,

    /// <summary>单独打开的文件</summary>
    File,
}

internal static class ItemKindExtensions
{
    /// <summary>
    /// Order 并列时的兜底顺序：工作区 → 文件夹 → 文件。
    /// 列表顺序本身按 Order（最后打开时间倒序），类型只在分不出先后时才用 ——
    /// 比如 storage.json 的 workspaces 与 folders 是两个各自从 0 计数的数组。
    /// 展示分组见 <see cref="ItemGroups"/>。
    /// </summary>
    public static int Rank(this ItemKind kind) => kind switch
    {
        ItemKind.Workspace => 0,
        ItemKind.Folder => 1,
        ItemKind.File => 2,
        _ => 3,
    };

    /// <summary>列表页与根列表 Fallback 共用，保证两处文案一致。</summary>
    public static string Label(this ItemKind kind) => kind switch
    {
        ItemKind.Workspace => "工作区",
        ItemKind.Folder => "文件夹",
        ItemKind.File => "文件",
        _ => kind.ToString(),
    };

    /// <summary>是否算「项目」。设置里关掉「显示文件」时按此过滤。</summary>
    public static bool IsProject(this ItemKind kind) => kind != ItemKind.File;
}
