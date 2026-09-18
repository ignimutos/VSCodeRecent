using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using VSCodeRecent.Commands;
using VSCodeRecent.Models;

namespace VSCodeRecent;

/// <summary>
/// 最近项目列表页。顶层命令 "VSCode Recent" 回车后进入这里。
///
/// 页面自己持有设置与读取器 —— 之前是靠把「显示文件」推进读取器的单例来生效，
/// 页面不认识自己的设置；现在取值随每次调用进来，读取器只管缓存。
///
/// <para><b>继承 DynamicListPage 而不是 ListPage。</b>普通 ListPage 的筛选由宿主做
/// 前缀模糊匹配，页面拿不到输入框内容；DynamicListPage 会把每次输入送进
/// <see cref="UpdateSearchText"/>，页面自己筛。本页要按子串匹配（直接打项目名就能命中），
/// 而且要能清空筛选，所以必须自己接管。</para>
/// </summary>
internal sealed partial class VSCodeRecentListPage : DynamicListPage
{
    private readonly VSCodeRecentSettings _settings;
    private readonly VSCodeRecentHistory _history;

    public VSCodeRecentListPage(VSCodeRecentSettings settings, VSCodeRecentHistory history)
    {
        _settings = settings;
        _history = history;

        // 页面级别的插件图标保留 —— 这是进入页面后唯一能看出是哪个插件的地方。
        // 去掉的是每一行记录的类型图标（那个由 Command.Icon 回退而来，已一并清理）。
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Title = "VSCode Recent";
        Name = "Open";

        // 改设置后列表要重建，否则开关不生效（Item 列表刷新靠 ItemsChanged）。
        // 本页面由提供者构造一次并长期存活，所以不需要退订。
        settings.Settings.SettingsChanged += (_, _) => RaiseItemsChanged();
    }

    /// <summary>设置里的「显示文件」当前值。</summary>
    private bool IncludeFiles => _settings.ShowFiles.Value;

    /// <summary>
    /// 根列表的 Fallback 内联搜索复用同一个页面实例：把预筛词写进
    /// <see cref="ListPage.SearchText"/>，宿主进入本页时会把筛选框也填成这个词，
    /// 用户看到的就是自己敲的内容，接着改也顺。
    ///
    /// <para><b>为什么不再单独存一份「固定关键词」并优先用它。</b>宿主只在页面刷新时
    /// 读一次 <c>model.SearchText</c>（ListViewModel 里 <c>SearchText = model.SearchText</c>），
    /// 之后用户的每次输入走的是 <see cref="UpdateSearchText"/>。另存一份并优先采用，
    /// 就等于把 <c>SearchText</c> 永久压住 —— 表现为「进页面后清空搜索框，
    /// 列表还是只剩那次搜索的结果」。</para>
    ///
    /// <para>这里不自己调 RaiseItemsChanged：<c>SearchText</c> 的 setter
    /// （DynamicListPage）会转手调 <see cref="UpdateSearchText"/>。</para>
    /// </summary>
    public void SetFixedQuery(string? query) => SearchText = query?.Trim() ?? string.Empty;

    /// <summary>
    /// 宿主送来的每次输入。查询词已经在 <see cref="ListPage.SearchText"/> 上，
    /// 这里只负责刷列表 —— 再存一份状态就会再次不同步。
    /// </summary>
    public override void UpdateSearchText(string oldSearch, string newSearch) => RaiseItemsChanged();

    public override IListItem[] GetItems()
    {
        var (all, succeeded, failure) = _history.Read();

        if (all.Count == 0)
        {
            return [DiagnosticItem(succeeded, failure)];
        }

        // 页内搜索：这里显式按子串匹配，便于直接打项目名命中。
        var query = SearchText?.Trim();
        var matched = _history.Search(query, IncludeFiles);

        if (matched.Count == 0)
        {
            return
            [
                new ListItem(new NoOpCommand())
                {
                    Title = $"没有匹配 “{query}” 的项目",
                    Subtitle = $"共 {all.Count} 条最近记录",
                    Icon = new IconInfo(""), // SearchAndApps / 放大镜
                },
            ];
        }

        var groups = ItemGroups.Split(matched);

        // 只有一个分组时不画标题 —— 默认视图（关掉「显示文件」）就只有项目，
        // 顶一行「项目 (4)」是白占位置。
        if (groups.Count == 1)
        {
            return [.. groups[0].Items.Select(ToListItem)];
        }

        // Section 构造时会自己插入一条带标题的 Separator，所以这里不用再手动加。
        return
        [
            .. groups.SelectMany(group =>
                new Section(group.Title, [.. group.Items.Select(ToListItem)])),
        ];
    }

    private static ListItem ToListItem(VSCodeItem item) =>
        new(new OpenInVSCodeCommand(item.Target))
        {
            Title = item.Title,
            Subtitle = $"{item.TypeLabel} · {item.Path}",
            Icon = IconForType(item.Kind),
        };

    /// <summary>
    /// 按类型给图标。明暗两套取自 Fluent System Icons（MIT，见 Assets\Icons\NOTICE.md），
    /// 与 Windows 自身图标同源，风格一致。
    ///
    /// TODO(宿主 bug): 目前首次打开面板 / 切主题时，部分行会取到反主题的变体而发白。
    /// 根因在 CmdPal 宿主，不在扩展：IconBox 的 _lastTheme 字段未初始化（ElementTheme.Default），
    /// 而 SourceRequested 在订阅瞬间就发请求（早于 Loaded 里 _lastTheme = ActualTheme），
    /// 于是 IconProvider 的 `args.Theme == ElementTheme.Light ? Light : Dark` 落到 Dark；
    /// Loaded 后的 Refresh 会重发正确请求，但两次都通过 ReferenceEquals(sourceKey, SourceKey)
    /// 检查，谁后完成谁赢，所以结果按行随机。关掉面板重开即恢复。
    /// 上游已在 PR #50181–#50192（"CmdPal Icons"）里把主题纳入缓存标识；等该版本后再复核，
    /// 确认修复即可删掉本注释。
    /// </summary>
    private static IconInfo IconForType(ItemKind kind) => kind switch
    {
        ItemKind.Workspace => IconHelpers.FromRelativePaths(
            "Assets\\Icons\\workspace.png", "Assets\\Icons\\workspace.dark.png"),
        ItemKind.File => IconHelpers.FromRelativePaths(
            "Assets\\Icons\\file.png", "Assets\\Icons\\file.dark.png"),
        _ => IconHelpers.FromRelativePaths(
            "Assets\\Icons\\folder.png", "Assets\\Icons\\folder.dark.png"), // folder / 未知
    };

    /// <summary>
    /// 一条都读不到时的提示，并列出实际探测过的数据位置。
    /// </summary>
    /// <param name="succeeded">
    /// false 表示数据源本身读失败了（比如库被锁、格式变了），标题文案要跟着变 ——
    /// 以前这两种情况都显示「没有读到」，用户无从判断该不该去排查。
    /// </param>
    private ListItem DiagnosticItem(bool succeeded, string? failure)
    {
        var moreCommands = new List<IContextItem>
        {
            new CommandContextItem(new NoOpCommand())
            {
                Title = "便携版可手动指定数据目录",
                Subtitle = $"设置环境变量 {VSCodeInstall.PortableDataEnvVar} 指向 VSCode 的 data 目录",
            },
        };

        moreCommands.AddRange(
            _history.DescribeProbedPaths()
                .Select(line => new CommandContextItem(new CopyTextCommand(line))
                {
                    Title = line,
                    Subtitle = "复制路径",
                }));

        return new ListItem(new NoOpCommand())
        {
            Title = succeeded ? "没有读到 VSCode 最近项目" : "读取 VSCode 最近记录失败",
            Subtitle = succeeded
                ? "找不到 VSCode 数据文件；展开下方命令查看探测过的位置"
                : failure ?? "数据源读取失败；展开下方命令查看探测过的位置",
            Icon = new IconInfo(""), // Warning

            // 用 MoreCommands 承载诊断信息，点开即可看到具体路径
            MoreCommands = [.. moreCommands],
        };
    }
}
