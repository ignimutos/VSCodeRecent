namespace VSCodeRecent;

/// <summary>VSCode 的一个存储位置。</summary>
internal enum VSCodeStorageKind
{
    /// <summary>&lt;home&gt;\.vscode-shared\sharedStorage\state.vscdb</summary>
    SharedStorageDb,

    /// <summary>%APPDATA%\Code\User\globalStorage —— 目录，内含多个 *.vscdb</summary>
    GlobalStorageDir,

    /// <summary>&lt;data&gt; —— 便携版数据目录本身，用户手填时给的就是它</summary>
    PortableDataDir,
}

/// <summary>一个位置，以及探测它的结果。</summary>
internal sealed record VSCodeProbe(VSCodeStorageKind Kind, string Path, bool Exists);

/// <summary>
/// 「VSCode 装在哪、数据放在哪」—— 全仓库只有这一个模块知道。
///
/// 数据源按类型分别枚举，每个类型内部「标准安装 → 便携版(Scoop 等)」都试一遍。
/// 便携版的 &lt;data&gt; 目录通过「运行中的 Code.exe」和「PATH 里的 code 命令」反推
/// （做法参考 Flow.Launcher.Plugin.VscodeResource）。
///
/// 单例只缓存文件系统探测结果，**不认识设置** —— 过滤规则由调用方决定。
/// </summary>
internal sealed class VSCodeInstall
{
    /// <summary>便携版数据的显式覆盖路径，指向 &lt;data&gt; 目录。</summary>
    public const string PortableDataEnvVar = "VSCODERECENT_VSCODE_DATA";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private static readonly string[] PortableVariantNames = ["vscode", "vscode-insiders", "vscode-oss"];

    /// <summary>
    /// PATH 上可能出现的 code 命令。列表页和便携版探测 MUST 用同一份：
    /// 以前两处各写一份且内容不同（一处有 code.cmd、一处没有），
    /// 于是「PATH 上只有 code.cmd」时两个模块会给出相反答案。
    /// 顺序即优先级：Scoop 的 shim 目录同时有 .exe 和 .cmd，要先认 exe。
    /// </summary>
    private static readonly string[] CodeExecutableNames = ["Code.exe", "code.exe", "code.cmd"];

    private static VSCodeInstall? _shared;
    private static readonly object SharedGate = new();

    /// <summary>页面和内联搜索共用一个实例，从而共用缓存。</summary>
    public static VSCodeInstall Shared
    {
        get
        {
            lock (SharedGate)
            {
                return _shared ??= new VSCodeInstall();
            }
        }
    }

    private readonly object _gate = new();
    private IReadOnlyList<string>? _cachedExecutables;

    /// <summary>当前进程可见的 code 可执行文件（PATH + 运行中的进程）。没有则返回空。</summary>
    public IReadOnlyList<string> CodeExecutables()
    {
        lock (_gate)
        {
            return _cachedExecutables ??= FindCodeExecutables();
        }
    }

    /// <summary>PATH 上第一个可用的 code 命令，找不到返回 null。</summary>
    public string? CodeExecutable() => CodeExecutables().FirstOrDefault();

    /// <summary>
    /// 依次列出实际探测过的数据位置及存在情况。读不到数据时用来排查
    /// （常见原因：便携版 VSCode，数据在 &lt;data&gt; 而不是 %APPDATA%\Code）。
    /// 返回的是数据，不是拼好的文案 —— 怎么画 ✓/✗ 由界面决定。
    /// </summary>
    public IEnumerable<VSCodeProbe> Probe()
    {
        foreach (var path in SharedStoragePaths())
        {
            yield return new VSCodeProbe(VSCodeStorageKind.SharedStorageDb, path, File.Exists(path));
        }

        foreach (var dir in GlobalStorageDirs())
        {
            yield return new VSCodeProbe(VSCodeStorageKind.GlobalStorageDir, dir, Directory.Exists(dir));
        }

        foreach (var dir in PortableDataDirs())
        {
            yield return new VSCodeProbe(VSCodeStorageKind.PortableDataDir, dir, true);
        }
    }

    /// <summary>&lt;data&gt;\shared-data\sharedStorage\state.vscdb —— 共享存储，VSCode 1.75+ 默认。</summary>
    public IEnumerable<string> SharedStoragePaths()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var standard = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".vscode-shared", "sharedStorage", "state.vscdb");
        if (seen.Add(standard))
        {
            yield return standard;
        }

        // 便携版：<data>\shared-data\sharedStorage\state.vscdb
        foreach (var dataDir in PortableDataDirs())
        {
            var portable = Path.Combine(dataDir, "shared-data", "sharedStorage", "state.vscdb");
            if (seen.Add(portable))
            {
                yield return portable;
            }
        }
    }

    /// <summary>
    /// 标准 globalStorage 目录 + 便携版 globalStorage 目录。两者的 storage.json
    /// 以及目录内的 *.vscdb 都要读。
    /// </summary>
    public IEnumerable<string> GlobalStorageDirs()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var standard = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Code", "User", "globalStorage");
        if (seen.Add(standard))
        {
            yield return standard;
        }

        // 便携版：<data>\user-data\User\globalStorage
        foreach (var dataDir in PortableDataDirs())
        {
            var portable = Path.Combine(dataDir, "user-data", "User", "globalStorage");
            if (seen.Add(portable))
            {
                yield return portable;
            }
        }
    }

    /// <summary>
    /// 便携版 VSCode 的 &lt;data&gt; 目录。三种线索：
    /// 1. 环境变量显式指定（即使目录还不存在也保留，用户手填错了要看得见）
    /// 2. 正在运行的 Code.exe 所在目录
    /// 3. PATH 里的 code 命令；Scoop 的 shims 目录可反推出 &lt;root&gt;\apps\vscode\current
    /// </summary>
    public IEnumerable<string> PortableDataDirs()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var explicitDir = Environment.GetEnvironmentVariable(PortableDataEnvVar);
        if (!string.IsNullOrWhiteSpace(explicitDir))
        {
            var full = TryGetFullPath(explicitDir);
            if (full is not null && seen.Add(full))
            {
                yield return full;
            }
        }

        foreach (var dataDir in PortableDataDirsCore())
        {
            if (seen.Add(dataDir))
            {
                yield return dataDir;
            }
        }
    }

    private IEnumerable<string> PortableDataDirsCore()
    {
        // 1) 运行中 / PATH 上的 Code.exe，取同级或上一级的 data 目录
        foreach (var exePath in CodeExecutables())
        {
            var dataDir = DataDirNear(exePath);
            if (dataDir is not null)
            {
                yield return dataDir;
            }
        }

        // 2) Scoop：PATH 上是 <root>\shims，真正的应用在 <root>\apps\<name>\current
        foreach (var dir in PathDirectories())
        {
            if (!Path.GetFileName(dir).Equals("shims", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var scoopRoot = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(scoopRoot))
            {
                continue;
            }

            foreach (var name in PortableVariantNames)
            {
                var dataDir = DataDirNear(Path.Combine(scoopRoot, "apps", name, "current", "Code.exe"));
                if (dataDir is not null)
                {
                    yield return dataDir;
                }
            }
        }
    }

    /// <summary>PATH 上的 code 可执行文件 + 运行中的 Code.exe，已去重。</summary>
    private static IReadOnlyList<string> FindCodeExecutables()
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in PathDirectories())
        {
            foreach (var exeName in CodeExecutableNames)
            {
                var exe = Path.Combine(dir, exeName);
                if (File.Exists(exe) && seen.Add(exe))
                {
                    found.Add(exe);
                }
            }
        }

        foreach (var exe in RunningCodeExecutables())
        {
            if (seen.Add(exe))
            {
                found.Add(exe);
            }
        }

        return found;
    }

    private static IEnumerable<string> PathDirectories()
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var rawDir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var dir = rawDir.Trim().Trim('"');
            if (dir.Length > 0)
            {
                yield return dir;
            }
        }
    }

    private static IEnumerable<string> RunningCodeExecutables()
    {
        System.Diagnostics.Process[] processes;
        try
        {
            processes = System.Diagnostics.Process.GetProcessesByName("Code");
        }
        catch
        {
            yield break;
        }

        foreach (var process in processes)
        {
            using (process)
            {
                string? path = null;
                try
                {
                    path = process.MainModule?.FileName;
                }
                catch
                {
                    // 32/64 位或权限问题，跳过
                }

                if (!string.IsNullOrEmpty(path))
                {
                    yield return path;
                }
            }
        }
    }

    /// <summary>
    /// 由 code 可执行文件的位置推断 &lt;data&gt; 目录。
    /// 便携版把 data 放在 Code.exe 同级或其上一级（Scoop: apps\vscode\current\data）。
    /// </summary>
    private static string? DataDirNear(string exePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(dir))
            {
                return null;
            }

            foreach (var candidate in new[]
            {
                Path.Combine(dir, "data"),
                Path.Combine(dir, "..", "data"),
            })
            {
                var full = Path.GetFullPath(candidate);
                if (Directory.Exists(full))
                {
                    return full;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DataDirNear({exePath}) error: {ex.Message}");
        }

        return null;
    }

    private static string? TryGetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TryGetFullPath({path}) error: {ex.Message}");
            return null;
        }
    }
}
