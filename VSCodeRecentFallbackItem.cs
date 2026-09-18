using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using VSCodeRecent.Commands;
using VSCodeRecent.Models;

namespace VSCodeRecent;

/// <summary>
/// 根列表的 Fallback 命中项。在 Command Palette 根搜索框直接敲项目名时，
/// 不必先进入 "VSCode Recent" 页面。
///
/// <para><b>唯一命中就直接打开，不进页面。</b>只有一条命中时把 <see cref="FallbackCommandItem.Command"/>
/// 换成那条记录的打开命令，回车即打开目标 —— 否则用户会「选中 → 回车 → 进页面 → 再选一次 → 再回车」，
/// 白白多两步。这与微软自己的 Indexer 扩展一致（0 条清空、1 条直接给目标命令、
/// 多条才给进列表页的入口）。</para>
///
/// <para>标题形态对齐 EverythingExtension 等第三方扩展：多条时写成
/// 「插件名 + 查询词」，图标用插件自己的 StoreLogo。</para>
/// </summary>
internal sealed partial class VSCodeRecentFallbackItem : FallbackCommandItem
{
    /// <summary>太短的词几乎必然误命中，也会让根列表每敲一个字就抖动，直接忽略。</summary>
    private const int MinQueryLength = 2;

    private readonly VSCodeRecentHistory _history;
    private readonly VSCodeRecentSettings _settings;

    /// <summary>列表页由提供者持有并复用 —— 这里只引用它，避免多份设置订阅。</summary>
    private readonly VSCodeRecentListPage _listPage;

    public VSCodeRecentFallbackItem(
        VSCodeRecentSettings settings,
        VSCodeRecentHistory history,
        VSCodeRecentListPage listPage)
        : base(new NoOpCommand(), "打开 VSCode 最近项目", "VSCodeRecent.recent.fallback")
    {
        _settings = settings;
        _history = history;
        _listPage = listPage;

        // 没有命中时 Title 留空，宿主就不会把这一项显示出来
        Title = string.Empty;
        Subtitle = string.Empty;
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
    }

    public override void UpdateQuery(string query)
    {
        query = query?.Trim() ?? string.Empty;

        // 先清干净，再按命中情况重新填 —— 命中数变化时旧文案不留残影
        Title = string.Empty;
        Subtitle = string.Empty;

        if (query.Length < MinQueryLength)
        {
            Command = new NoOpCommand();
            return;
        }

        // 设置可能在面板里刚被改过，每次按当前值过滤
        var matched = _history.Search(query, _settings.ShowFiles.Value);
        if (matched.Count == 0)
        {
            Command = new NoOpCommand();
            return;
        }

        if (matched.Count == 1)
        {
            // 唯一命中：回车直接打开，不绕道列表页
            var single = matched[0];
            Title = $"在 VSCode 中打开 “{single.Title}”";
            Subtitle = $"{single.TypeLabel} · {single.Path}";
            Command = new OpenInVSCodeCommand(single.Target);
            return;
        }

        // 多条命中：进列表页挑。预筛词同时填进筛选框，用户接着改也顺。
        _listPage.SetFixedQuery(query);

        Title = $"在 VSCode Recent 中打开 “{query}”";
        Subtitle = $"命中 {matched.Count} 个最近项目";
        Command = _listPage;
    }
}
