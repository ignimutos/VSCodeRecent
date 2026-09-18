using VSCodeRecent.Models;
using Xunit;

namespace VSCodeRecent.Tests;

/// <summary>
/// URI → 打开目标。用例全部来自旧 <c>ParseUri</c> 的行为，
/// 包括那个把 <c>+</c> 吃掉、让条目静默消失的缺陷。
/// </summary>
public class VSCodeUriTests
{
    [Fact]
    public void WindowsFileUri_BecomesBackslashPath()
    {
        var target = Parse("file:///c%3A/work/proj");

        var folder = Assert.IsType<VSCodeOpenTarget.LocalFolder>(target);
        Assert.Equal(@"c:\work\proj", folder.Path);
        Assert.Equal(@"""c:\work\proj""", folder.ToCodeArguments());
    }

    /// <summary>
    /// 回归：旧代码在剥 <c>file://</c> 前缀前后各解一次码，而 UrlDecode 把 <c>+</c>
    /// 当空格，于是含 <c>+</c> 的目录名被改成带空格的路径，条目随后消失。
    /// </summary>
    [Theory]
    [InlineData("file:///c%3A/work/my%2Bproj", @"c:\work\my+proj")]
    [InlineData("file:///c%3A/work/a%2Bb%2Bc", @"c:\work\a+b+c")]
    [InlineData("file:///d%3A/proj+plus", @"d:\proj+plus")]
    public void PlusInPath_IsPreserved(string uri, string expected)
    {
        var folder = Assert.IsType<VSCodeOpenTarget.LocalFolder>(Parse(uri));
        Assert.Equal(expected, folder.Path);
    }

    [Fact]
    public void PercentEncodedSpace_IsRestored()
    {
        var folder = Assert.IsType<VSCodeOpenTarget.LocalFolder>(Parse("file:///c%3A/work/my%20proj"));
        Assert.Equal(@"c:\work\my proj", folder.Path);
    }

    [Fact]
    public void UnixAbsolutePath_GetsLeadingSlash()
    {
        var folder = Assert.IsType<VSCodeOpenTarget.LocalFolder>(Parse("file:///home/user/proj"));
        Assert.Equal("/home/user/proj", folder.Path);
    }

    [Fact]
    public void WslUri_PreservesDistroAndPathCase()
    {
        var wsl = Assert.IsType<VSCodeOpenTarget.WslFolder>(
            Parse("vscode-remote://wsl+Ubuntu-22.04/home/user/MyProj"));

        Assert.Equal("Ubuntu-22.04", wsl.Distro);
        Assert.Equal("/home/user/MyProj", wsl.Path);
    }

    /// <summary>
    /// 路径里 /mnt/c 这种大小写有意义，解析时 MUST 保留原样；
    /// 归一化推迟到生成命令行那一刻。
    /// </summary>
    [Fact]
    public void WslUri_KeepsPathCaseIncludingMountLetter()
    {
        var wsl = Assert.IsType<VSCodeOpenTarget.WslFolder>(
            Parse("vscode-remote://wsl+Ubuntu-22.04/mnt/C/Users/Me/Proj"));

        Assert.Equal("Ubuntu-22.04", wsl.Distro);
        Assert.Equal("/mnt/C/Users/Me/Proj", wsl.Path);
    }

    /// <summary>
    /// 回归：distro 段是大小写敏感的，MUST NOT 转小写后再拿去解析 —— 主仓库
    /// 原来的 <c>TryBuildWslUri</c> 就是全小写，那时能开只是「解析时又还原了大小写」
    /// 这个巧合。现在解析与生成分开，两边都保留原样。
    /// </summary>
    [Fact]
    public void WslUri_DoesNotLowercaseDistroInPath()
    {
        var wsl = Assert.IsType<VSCodeOpenTarget.WslFolder>(
            Parse("vscode-remote://wsl+Ubuntu-22.04/home/user/proj"));

        Assert.Equal("Ubuntu-22.04", wsl.Distro);
    }

    /// <summary>UNC 形式的 wsl$ 前缀也认（这是路径，不是 URI）。</summary>
    [Fact]
    public void WslDollarUncPath_IsOpenable()
    {
        var wsl = Assert.IsType<VSCodeOpenTarget.WslFolder>(
            Parse(@"file:///\\wsl$\Ubuntu-22.04\home\user\proj"));

        Assert.Equal("ubuntu-22.04", wsl.Distro);
        Assert.Equal("/home/user/proj", wsl.Path);
    }

    /// <summary>生成命令行时 distro 小写，但路径段原样 —— 两者混写会打不开。</summary>
    [Fact]
    public void FolderUri_LowercasesDistroOnly()
    {
        var wsl = Assert.IsType<VSCodeOpenTarget.WslFolder>(
            Parse("vscode-remote://wsl+Ubuntu-22.04/mnt/C/Proj"));

        Assert.Equal("vscode-remote://wsl+ubuntu-22.04/mnt/C/Proj", wsl.FolderUri());
    }

    /// <summary>authority 与路径段里的编码斜杠都要还原成真斜杠。</summary>
    [Fact]
    public void EncodedSlashes_AreRestored()
    {
        var wsl = Assert.IsType<VSCodeOpenTarget.WslFolder>(
            Parse("vscode-remote://wsl%2Bubuntu/home/u/a%2Fb"));

        Assert.Equal("ubuntu", wsl.Distro);
        Assert.Equal("/home/u/a/b", wsl.Path);
    }

    [Fact]
    public void UncWslPath_IsOpenable()
    {
        var wsl = Assert.IsType<VSCodeOpenTarget.WslFolder>(
            Parse(@"file:///\\wsl.localhost\Ubuntu-22.04\home\user\proj"));

        Assert.Equal("ubuntu-22.04", wsl.Distro);
        Assert.Equal("/home/user/proj", wsl.Path);
    }

    [Theory]
    [InlineData("vscode-remote://ssh-remote+host/home/user/proj")]
    [InlineData("vscode-remote://dev-container+abc123/workspace")]
    [InlineData("")]
    [InlineData(null)]
    public void NonWslRemoteOrEmpty_FailsWithReason(string? uri)
    {
        Assert.Null(VSCodeUri.Parse(uri, out var error));
        Assert.NotNull(error);
    }

    private static VSCodeOpenTarget Parse(string? uri)
    {
        var target = VSCodeUri.Parse(uri, out var error);
        Assert.True(target is not null, $"解析失败: {error}");
        return target!;
    }
}
