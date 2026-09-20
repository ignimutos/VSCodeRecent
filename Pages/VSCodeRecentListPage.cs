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
            Icon = IconFor(item),
        };

    /// <summary>
    /// 按记录挑图标，规则与 VS Code 资源管理器一致 —— 关联表取自 Material Icon Theme
    /// （MIT，见 Assets\MaterialIcons\NOTICE.md）。
    ///
    /// <para><b>只看一套彩色图，不再分明暗。</b>图标本身自带颜色，在明暗主题下都成立；
    /// 省掉 .dark 变体后也顺带避开了宿主的一个 bug：<c>IconBox._lastTheme</c> 未初始化时
    /// <c>SourceRequested</c> 会在 <c>Loaded</c> 之前发请求，导致
    /// <c>FromRelativePaths(light, dark)</c> 按行随机取到反主题那一版而发白。
    /// 只给一个路径就没有这个选择，重开面板与否都一样。</para>
    /// </summary>
    private static IconInfo IconFor(VSCodeItem item)
    {
        var theme = MaterialIconTheme.Shared;
        var icon = item.Kind switch
        {
            // 工作区不按文件名查表：.code-workspace 在主题里指向 VS Code 官方 logo，
            // 已刻意排除（见 NOTICE.md），统一用中性的 folder-open。
            ItemKind.Workspace => theme.Workspace,
            ItemKind.Folder => theme.Folder,
            _ => theme.ForFileName(item.FileName),
        };

        return IconHelpers.FromRelativePath(MaterialIconTheme.RelativePath(icon));
    }

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
