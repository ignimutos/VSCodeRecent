using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using VSCodeRecent.Models;

namespace VSCodeRecent.Commands;

/// <summary>
/// 在 VSCode 中打开一个最近项目。
///
/// 不解析路径 —— 打开目标（含 WSL 的 remote URI）在解析记录时就算好了，
/// 见 <see cref="VSCodeOpenTarget"/>。本类只负责把它交给 code 命令。
/// </summary>
internal sealed partial class OpenInVSCodeCommand : InvokableCommand
{
    private readonly VSCodeOpenTarget _target;

    public OpenInVSCodeCommand(VSCodeOpenTarget target)
    {
        _target = target;
        Name = "Open in VSCode";

        // 不设 Icon：宿主在列表项没给图标时会回退到 Command.Icon，
        // 这里设了就等于给每一行都强行画一个图标。
    }

    public override ICommandResult Invoke()
    {
        try
        {
            StartCode(_target.ToCodeArguments());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OpenInVSCode error: {ex.Message}");
            return CommandResult.ShowToast($"打开失败: {ex.Message}");
        }

        return CommandResult.Hide();
    }

    private static void StartCode(string arguments)
    {
        // 不要用 UseShellExecute 直接启动 "code"：PATH 上的 code 实际上是
        // <安装目录>\bin\code.cmd（批处理）。ShellExecute 走 cmd.exe 执行它，
        // 并且会分配一个可见的控制台窗口，表现为每打开一次项目就弹一下黑窗口。
        // 这里显式走 cmd.exe，用 CreateNoWindow 让批处理在无窗口下运行。
        if (VSCodeInstall.Shared.CodeExecutable() is null)
        {
            throw new InvalidOperationException(
                "PATH 上找不到 code 命令（VSCode 里执行 “Shell Command: Install 'code' command in PATH”）");
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = $"/c code {arguments}",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }
}
