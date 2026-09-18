using VSCodeRecent.Models;
using Xunit;

namespace VSCodeRecent.Tests;

/// <summary>
/// JSON 解析。这些用例以前一个都写不出来 —— 解析逻辑内嵌在直接读文件系统的
/// 静态方法里，只能靠手动跑扩展验证。
/// </summary>
public class ParseHistoryKeyTests
{
    /// <summary>本机一定存在的文件，用来过存在性检查。</summary>
    private static readonly string ExistingFile = typeof(ParseHistoryKeyTests).Assembly.Location;

    private static string FileUri(string path) => "file:///" + path.Replace('\\', '/');

    [Fact]
    public void SingleFileEntry_ParsesAsFileKind()
    {
        var json = $$"""
            {"entries":[
              {"fileUri":"{{FileUri(ExistingFile)}}"}
            ]}
            """;

        var items = ParseHistoryKey.RecordTargets(json, out var error);

        Assert.Null(error);
        var item = Assert.Single(items!);
        Assert.Equal(ItemKind.File, item.Kind);
        Assert.False(item.Kind.IsProject());
        Assert.Equal(Path.GetFileName(ExistingFile), item.Title);
    }

    [Fact]
    public void EmptyEntries_YieldsEmptyListNotFailure()
    {
        var items = ParseHistoryKey.RecordTargets("""{"entries":[]}""", out var error);

        Assert.Null(error);
        Assert.Empty(items!);
    }

    /// <summary>文件不存在的记录必须丢掉（与旧行为一致）。</summary>
    [Fact]
    public void MissingFileEntry_IsDropped()
    {
        var json = """
            {"entries":[
              {"fileUri":"file:///nonexistent/path/nope.cs"}
            ]}
            """;

        var items = ParseHistoryKey.RecordTargets(json, out var error);

        Assert.Null(error);
        Assert.Empty(items!);
    }

    /// <summary>坏 JSON 要报错，而不是静默返回空 —— 这正是第二处深化的目的。</summary>
    [Fact]
    public void MalformedRecordJson_ReturnsNullWithReason()
    {
        // 故意留一个未闭合的括号
        Assert.Null(ParseHistoryKey.RecordTargets("""{"entries":[{"fileUri":"x"}""", out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void MissingEntriesKey_IsNotAFailure()
    {
        var items = ParseHistoryKey.RecordTargets("""{"somethingElse":1}""", out var error);

        Assert.Null(error);
        Assert.Empty(items!);
    }

    /// <summary>
    /// storage.json 的 backupWorkspaces 不做存在性检查（项目可能暂时不在线），
    /// 所以任意路径都会被收下 —— 与最近记录的规则不同。
    /// </summary>
    [Fact]
    public void StorageJsonWorkspace_DoesNotCheckExistence()
    {
        var json = """
            {"backupWorkspaces":{"workspaces":[
              {"configURIPath":"file:///gone/away/My.code-workspace"}
            ]}}
            """;

        var items = ParseHistoryKey.StorageTargets(json, out var error);

        Assert.Null(error);
        var item = Assert.Single(items!);
        Assert.Equal(ItemKind.Workspace, item.Kind);
        Assert.Equal("My", item.Title);
        Assert.True(item.Kind.IsProject());
    }

    [Fact]
    public void StorageJsonFolder_DoesNotCheckExistence()
    {
        var json = """
            {"backupWorkspaces":{"folders":[
              {"folderUri":"file:///gone/away/proj"}
            ]}}
            """;

        var items = ParseHistoryKey.StorageTargets(json, out var error);

        Assert.Null(error);
        var item = Assert.Single(items!);
        Assert.Equal(ItemKind.Folder, item.Kind);
        Assert.Equal("proj", item.Title);
        Assert.Equal("/gone/away/proj", item.Path);
    }

    [Fact]
    public void StorageJsonWslEntry_IsKept()
    {
        var json = """
            {"backupWorkspaces":{"folders":[
              {"folderUri":"vscode-remote://wsl+Ubuntu-22.04/home/user/proj"}
            ]}}
            """;

        var items = ParseHistoryKey.StorageTargets(json, out var error);

        Assert.Null(error);
        var item = Assert.Single(items!);
        var wsl = Assert.IsType<VSCodeOpenTarget.WslFolder>(item.Target);
        Assert.Equal("Ubuntu-22.04", wsl.Distro);
        Assert.Equal("proj", item.Title);
    }

    [Fact]
    public void MissingBackupWorkspaces_YieldsEmptyList()
    {
        var items = ParseHistoryKey.StorageTargets("""{"other":true}""", out var error);

        Assert.Null(error);
        Assert.Empty(items!);
    }

    [Fact]
    public void MalformedStorageJson_ReturnsNullWithReason()
    {
        Assert.Null(ParseHistoryKey.StorageTargets("not json at all", out var error));
        Assert.NotNull(error);
    }
}
