namespace VSCodeRecent.Models;

/// <summary>一条 VSCode 最近打开记录。</summary>
internal sealed record VSCodeItem
{
    /// <summary>工作区用完整路径，文件夹/文件用各自的名字。</summary>
    public required string Title { get; init; }

    /// <summary>
    /// 本地权威路径。文件夹/文件用反斜杠形式；工作区保持 VSCode 记录里的路径。
    /// 远程位置见 <see cref="Target"/>。
    /// </summary>
    public required string Path { get; init; }

    public required ItemKind Kind { get; init; }

    /// <summary>同一数据源内的顺序，越小越近。</summary>
    public required int Order { get; init; }

    /// <summary>打开时要做什么。解析时就算好，不再由字符串前缀反推。</summary>
    public required VSCodeOpenTarget Target { get; init; }

    /// <summary>列表页与根列表 Fallback 共用。</summary>
    public string TypeLabel => Kind.Label();
}
