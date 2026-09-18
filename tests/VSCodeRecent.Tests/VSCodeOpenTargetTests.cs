using VSCodeRecent.Models;
using Xunit;

namespace VSCodeRecent.Tests;

/// <summary>
/// 打开目标生成命令行参数。WSL 的 distro 大小写规范就在这里落地：
/// distro 小写、路径保持原样（见 <see cref="VSCodeOpenTarget"/> 的说明）。
/// </summary>
public class VSCodeOpenTargetTests
{
    [Fact]
    public void WslFolder_UsesFolderUriArgument()
    {
        var wsl = new VSCodeOpenTarget.WslFolder("ubuntu", "/home/user/proj");

        Assert.Equal(
            "--folder-uri \"vscode-remote://wsl+ubuntu/home/user/proj\"",
            wsl.ToCodeArguments());
    }

    [Fact]
    public void WslFolder_DisplayPathIsUnc()
    {
        var wsl = new VSCodeOpenTarget.WslFolder("ubuntu-22.04", "/mnt/C/Users/Me/Proj");

        Assert.Equal(@"\\wsl.localhost\ubuntu-22.04\mnt\C\Users\Me\Proj", wsl.LocalPath);
    }

    [Fact]
    public void FolderUri_DoesNotDoubleSlash()
    {
        var wsl = new VSCodeOpenTarget.WslFolder("ubuntu", "/home/user");
        Assert.Equal("vscode-remote://wsl+ubuntu/home/user", wsl.FolderUri());
    }

    [Fact]
    public void LocalTargets_UseQuotedPathArgument()
    {
        Assert.Equal(@"""C:\my proj""", new VSCodeOpenTarget.LocalFolder(@"C:\my proj").ToCodeArguments());
        Assert.Equal(@"""C:\a\b.cs""", new VSCodeOpenTarget.LocalFile(@"C:\a\b.cs").ToCodeArguments());
    }

    [Fact]
    public void LocalTarget_ExposesItsOwnPathForExistenceChecks()
    {
        Assert.Equal(@"C:\x", new VSCodeOpenTarget.LocalFolder(@"C:\x").LocalPath);
    }

    /// <summary>
    /// 回归：反推 UNC 路径时 distro MUST 归一成小写。旧代码把整个 authority
    /// <c>ToLowerInvariant()</c>，会把路径首段的大小写一起毁掉；这里路径已与
    /// authority 分开，所以不会。
    /// </summary>
    [Fact]
    public void FromUncPath_LowercasesDistroOnly()
    {
        var wsl = Assert.IsType<VSCodeOpenTarget.WslFolder>(
            VSCodeOpenTarget.FromUncPath(@"\\wsl.localhost\Ubuntu-22.04\mnt\C\Users\Me"));

        Assert.Equal("ubuntu-22.04", wsl.Distro);
        Assert.Equal("/mnt/C/Users/Me", wsl.Path);
    }

    [Fact]
    public void FromUncPath_AcceptsLegacyWslDollarPrefix()
    {
        var wsl = Assert.IsType<VSCodeOpenTarget.WslFolder>(
            VSCodeOpenTarget.FromUncPath(@"\\wsl$\Ubuntu\home\u"));

        Assert.Equal("ubuntu", wsl.Distro);
        Assert.Equal("/home/u", wsl.Path);
    }

    [Theory]
    [InlineData(@"C:\local\path")]
    [InlineData(@"\\server\share")]
    [InlineData(@"\\wsl.localhost\onlydistro")]
    public void FromUncPath_RejectsNonWslPaths(string path)
    {
        Assert.Null(VSCodeOpenTarget.FromUncPath(path));
    }
}
