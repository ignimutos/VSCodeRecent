using System.Diagnostics;
using VSCodeRecent.Models;

namespace VSCodeRecent;

/// <summary>
/// 读取 VSCode 最近打开记录。
///
/// 只做三件事：枚举数据源、合并去重排序、缓存结果。位置探测在
/// <see cref="VSCodeInstall"/>，URI 编解码在 <see cref="VSCodeUri"/>，
/// JSON 解析在 <see cref="ParseHistoryKey"/>。
///
/// <para><b>这个类不认识设置。</b>「要不要显示文件」是调用方的事，通过
/// <see cref="GetRecentItems"/> 的参数传进来，不落在模块状态上。</para>
///
/// <para><b>已知缺口。</b>ssh-remote / dev-container 等非 WSL 远程的记录会被
/// <see cref="VSCodeUri"/> 判为无法本地打开而丢弃，界面上完全看不到它们，
/// 诊断信息里也不体现。要修得先有能表达这类位置的模型。</para>
/// </summary>
internal sealed class VSCodeRecentHistory
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private static VSCodeRecentHistory? _shared;
    private static readonly object SharedGate = new();

    /// <summary>页面和内联搜索共用一个实例，从而共用缓存。</summary>
    public static VSCodeRecentHistory Shared
    {
        get
        {
            lock (SharedGate)
            {
                return _shared ??= new VSCodeRecentHistory();
            }
        }
    }

    private readonly VSCodeInstall _install;
    private readonly object _gate = new();
    private IReadOnlyList<VSCodeItem>? _cache;
    private bool _cacheSucceeded;
    private string? _cacheFailure;
    private DateTime _cachedAt;

    public VSCodeRecentHistory(VSCodeInstall? install = null) => _install = install ?? VSCodeInstall.Shared;

    /// <summary>最近记录。默认走缓存（VSCode 刚打开的项目最迟一个 TTL 后出现）。</summary>
    public (IReadOnlyList<VSCodeItem> Items, bool Succeeded, string? Failure) Read(bool forceRefresh = false)
    {
        // 列表页和 Fallback 内联搜索会并发调用，缓存读写要串行化
        lock (_gate)
        {
            if (!forceRefresh &&
                _cache is not null &&
                DateTime.UtcNow - _cachedAt < CacheTtl)
            {
                return (_cache, _cacheSucceeded, _cacheFailure);
            }

            var reads = LoadAll();
            var merged = SourceRead.Combine(reads);

            // 排序要按来源分别做 —— 每个来源的 Order 各自从 0 开始，见 SortAndDedupe
            var items = SortAndDedupe(reads);

            _cache = items;
            _cacheSucceeded = merged.Succeeded;
            _cacheFailure = merged.Failure;
            _cachedAt = DateTime.UtcNow;
            return (_cache, _cacheSucceeded, _cacheFailure);
        }
    }

    /// <summary>最近项目列表。设置里的「显示文件」关掉时按类型过滤。</summary>
    public IReadOnlyList<VSCodeItem> GetRecentItems(bool includeFiles)
    {
        var (items, _, _) = Read();
        return Filter(items, includeFiles);
    }

    /// <summary>
    /// 按标题 / 路径做子串匹配。列表页和根列表的 Fallback 内联搜索共用同一套规则，
    /// 免得两边结果对不上。
    /// </summary>
    public IReadOnlyList<VSCodeItem> Search(string? query, bool includeFiles)
    {
        var items = GetRecentItems(includeFiles);
        if (string.IsNullOrWhiteSpace(query))
        {
            return items;
        }

        return
        [
            .. items.Where(item =>
                item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Path.Contains(query, StringComparison.OrdinalIgnoreCase)),
        ];
    }

    /// <summary>设置里的「显示文件」过滤。纯函数，取值随调用进来。</summary>
    public static IReadOnlyList<VSCodeItem> Filter(IReadOnlyList<VSCodeItem> items, bool includeFiles) =>
        includeFiles ? items : [.. items.Where(x => x.Kind.IsProject())];

    /// <summary>
    /// 每个位置各起一个来源：目录内的 *.vscdb 逐个一条，storage.json 一条。
    /// 返回空说明一个来源都没有 —— 调用方据此显示诊断信息。
    /// </summary>
    private IEnumerable<IVSCodeHistorySource> Sources()
    {
        foreach (var dbPath in _install.SharedStoragePaths())
        {
            if (File.Exists(dbPath))
            {
                yield return new SharedStorageRecordSource(dbPath);
            }
        }

        foreach (var dir in _install.GlobalStorageDirs())
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            List<string> files;
            try
            {
                files = [.. Directory.GetFiles(dir, "*.vscdb", SearchOption.AllDirectories)];
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Sources({dir}) error: {ex.Message}");
                continue;
            }

            foreach (var file in files)
            {
                yield return new GlobalStorageDatabaseSource(file);
            }

            var storageJson = Path.Combine(dir, "storage.json");
            if (File.Exists(storageJson))
            {
                yield return new StorageJsonSource(storageJson);
            }
        }
    }

    private IReadOnlyList<SourceRead> LoadAll() => [.. Sources().Select(source => source.Read())];

    /// <summary>
    /// 按来源优先级拼接、来源内按最近顺序排，再按路径去重。
    ///
    /// <para><b>排序语义：Order 升序即「最后打开时间倒序」，不按类型分组。</b>
    /// VSCode 写出的 entries 本身就是一个 MRU 列表，Order 与类型无关 —— 工作区与文件夹
    /// 在同一段里按时间交错，文件是紧接着的第二段（VSCode 存的时候先展开 workspaces
    /// 再展开 files）。以前先按 <see cref="ItemKind.Rank"/> 排，把列表切成了
    /// 工作区 / 文件夹 / 文件三个连成一片的类型块，于是**比某个文件夹更早打开的工作区
    /// 依然排在它前面** —— 时间顺序就没了。类型信息在展示层用（见
    /// <see cref="ItemGroups"/>），不该参与排序。</para>
    ///
    /// <para>类型只在 Order 并列时兜底：storage.json 的 workspaces 与 folders 是
    /// 两个数组，各自从 0 开始计数，单看 Order 分不出谁更近。</para>
    ///
    /// <para><b>为什么不用跨来源的全局 Order。</b>Order 只在单个数据源内有意义，
    /// 两个来源都从 0 开始；跨来源全局排会把 sharedStorage 与 storage.json 的记录
    /// 乱序交错。来源的枚举顺序就是优先级（见 <see cref="Sources"/>），先出现的保留，
    /// 后出现的同路径条目丢掉 —— 同一个项目常同时存在于多个来源里。</para>
    /// </summary>
    internal static IReadOnlyList<VSCodeItem> SortAndDedupe(IReadOnlyList<SourceRead> reads)
    {
        var deduped = new List<VSCodeItem>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var read in reads)
        {
            foreach (var item in read.Items.OrderBy(x => x.Order).ThenBy(x => x.Kind.Rank()))
            {
                if (seenPaths.Add(item.Path))
                {
                    deduped.Add(item);
                }
            }
        }

        return deduped;
    }

    /// <summary>
    /// 探测结果转成诊断文案：✓/✗ 是渲染，本类之外还给了
    /// <see cref="VSCodeInstall.Probe"/> 的结构化数据。
    /// </summary>
    public IReadOnlyList<string> DescribeProbedPaths()
    {
        var lines = _install.Probe()
            .Select(p => $"[{(p.Exists ? "✓" : "✗")}] {KindLabel(p.Kind)}: {p.Path}")
            .ToList();

        if (!lines.Any(l => l.Contains(VSCodeStorageKind.PortableDataDir.ToString())))
        {
            lines.Add("[✗] 未找到便携版 VSCode 的 data 目录");
        }

        lines.Add($"PATH 上的 code: {_install.CodeExecutable() ?? "未找到"}");
        lines.Add($"环境变量 {VSCodeInstall.PortableDataEnvVar}: " +
                  $"{Environment.GetEnvironmentVariable(VSCodeInstall.PortableDataEnvVar) ?? "(未设置)"}");

        return lines;
    }

    private static string KindLabel(VSCodeStorageKind kind) => kind switch
    {
        VSCodeStorageKind.SharedStorageDb => "共享存储",
        VSCodeStorageKind.GlobalStorageDir => "globalStorage",
        VSCodeStorageKind.PortableDataDir => "便携版 data",
        _ => kind.ToString(),
    };
}
