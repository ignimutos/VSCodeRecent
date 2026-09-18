using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace VSCodeRecent;

/// <summary>
/// 命令提供者。注意：不要手写 ICommandProvider 接口 —— 必须继承 Toolkit 的
/// CommandProvider 基类，由基类实现 ICommandProvider / INotifyItemsChanged /
/// IDisposable 的全部成员，这里只需 override TopLevelCommands()。
///
/// 列表页在这里构造一次，顶层命令与 Fallback 共用同一个实例（因而共用设置订阅与
/// 缓存），不再是「两处各建一个页面」。
/// </summary>
public partial class VSCodeCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;
    private readonly IFallbackCommandItem[] _fallbacks;
    private readonly VSCodeRecentSettings _settings = new();

    public VSCodeCommandsProvider()
    {
        DisplayName = "VSCode Recent";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");

        var history = VSCodeRecentHistory.Shared;
        var listPage = new VSCodeRecentListPage(_settings, history);

        _fallbacks = [new VSCodeRecentFallbackItem(_settings, history, listPage)];

        _commands =
        [
            new CommandItem(listPage)
            {
                Title = "VSCode Recent",
                Subtitle = "最近打开的 VSCode 项目",
            },
        ];

        Settings = _settings.Settings;
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    /// <summary>根列表内联搜索：直接敲项目名即可，不必先进入列表页。</summary>
    public override IFallbackCommandItem[] FallbackCommands() => _fallbacks;
}
