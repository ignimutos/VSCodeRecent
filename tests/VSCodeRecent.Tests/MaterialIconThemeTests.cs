using VSCodeRecent.Models;
using Xunit;

namespace VSCodeRecent.Tests;

/// <summary>
/// 图标关联表的查找规则。表本身是随包发布的常量（嵌入资源），这些用例同时充当
/// 「关联表没坏、也没被裁错」的回归检查 —— 裁表脚本删错东西会让这里先红。
/// </summary>
public class MaterialIconThemeTests
{
    private static MaterialIconTheme Theme => MaterialIconTheme.Shared;

    /// <summary>精确文件名优先于扩展名：package.json 是 nodejs 图标而不是 json。</summary>
    [Theory]
    [InlineData("package.json", "nodejs")]
    [InlineData(".gitignore", "git")]
    [InlineData("docker-compose.yml", "docker")]
    public void ForFileName_FileNameRuleWins(string fileName, string expected)
    {
        Assert.Equal(expected, Theme.ForFileName(fileName));
    }

    /// <summary>最常见的路径：按扩展名匹配。</summary>
    [Theory]
    [InlineData("config.yaml", "yaml")]
    [InlineData("Program.cs", "csharp")]
    [InlineData("dev.ps1", "powershell")]
    [InlineData("index.ts", "typescript")]
    public void ForFileName_MatchesByExtension(string fileName, string expected)
    {
        Assert.Equal(expected, Theme.ForFileName(fileName));
    }

    /// <summary>
    /// 组合扩展名要取最长能匹配上的那段：config.yaml.tpl 在 VS Code 里是 smarty
    /// 图标（tpl），不是 yaml。取最短后缀会得到完全不同的颜色。
    /// </summary>
    [Fact]
    public void ForFileName_PrefersLongestMatchingExtension()
    {
        Assert.Equal("smarty", Theme.ForFileName("config.yaml.tpl"));
    }

    /// <summary>表里没见过的名字回落到默认文件图标，而不是抛异常。</summary>
    [Theory]
    [InlineData("no-such-thing.zzz")]
    [InlineData("Makefile.unknown")]
    public void ForFileName_UnknownFallsBackToDefault(string fileName)
    {
        Assert.Equal(Theme.File, Theme.ForFileName(fileName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ForFileName_MissingNameFallsBackToDefault(string? fileName)
    {
        Assert.Equal(Theme.File, Theme.ForFileName(fileName));
    }

    /// <summary>大小写不敏感：Windows 上 Settings.JSON 也要拿到 json 图标。</summary>
    [Fact]
    public void ForFileName_IsCaseInsensitive()
    {
        Assert.Equal(Theme.ForFileName("settings.json"), Theme.ForFileName("SETTINGS.JSON"));
    }

    /// <summary>
    /// 工作区不能走文件名查表 —— .code-workspace 在主题里指向 VS Code 官方 logo，
    /// 已按商标考虑排除，改用中性的 folder-open。
    /// </summary>
    [Fact]
    public void Workspace_UsesNeutralIconNotVscode()
    {
        Assert.Equal("folder-open", Theme.Workspace);
        Assert.Equal("folder-open", Theme.ForFileName("My.code-workspace"));
    }

    /// <summary>排除的图标不该还留在包里。</summary>
    [Fact]
    public void VscodeLogoIsExcluded()
    {
        Assert.NotEqual("vscode", Theme.ForFileName("My.code-workspace"));
        Assert.NotEqual("vscode", Theme.Workspace);
    }

    /// <summary>三个 IconKind 各自的兜底图标都要有值，否则列表会拿到空路径。</summary>
    [Fact]
    public void DefaultsArePopulated()
    {
        Assert.False(string.IsNullOrWhiteSpace(Theme.Folder));
        Assert.False(string.IsNullOrWhiteSpace(Theme.File));
        Assert.False(string.IsNullOrWhiteSpace(Theme.Workspace));
    }
}
