using System.Text.Json;
using Microsoft.Data.Sqlite;
using VSCodeRecent.Models;

namespace VSCodeRecent;

/// <summary>
/// 一个数据源读完之后的结果：读到什么，以及这个来源本身健不健康。
/// </summary>
/// <param name="Items">解析出的记录，未去重未排序。</param>
/// <param name="Succeeded">来源可读。</param>
/// <param name="Failure">失败原因，成功时为 null。</param>
internal readonly record struct SourceRead(
    IReadOnlyList<VSCodeItem> Items,
    bool Succeeded,
    string? Failure)
{
    public static SourceRead Ok(IReadOnlyList<VSCodeItem> items) => new(items, true, null);

    public static SourceRead Failed(string failure) => new([], false, failure);

    /// <summary>多个来源合并成一条 —— 只要有一个读成功，整体就算成功。</summary>
    public static SourceRead Combine(IEnumerable<SourceRead> reads)
    {
        var items = new List<VSCodeItem>();
        var failures = new List<string>();
        var anySucceeded = false;

        foreach (var read in reads)
        {
            items.AddRange(read.Items);

            if (read.Succeeded)
            {
                anySucceeded = true;
            }
            else if (read.Failure is not null)
            {
                failures.Add(read.Failure);
            }
        }

        return new SourceRead(
            items,
            anySucceeded,
            failures.Count == 0 ? null : string.Join("；", failures));
    }
}

/// <summary>
/// VSCode 最近记录的一个数据源。
///
/// 以前是三个静态方法各自 <c>catch → Debug.WriteLine</c>，失败被吞成「空列表」，
/// MSIX Release 包里没人看得到，界面也无从区分「没有数据」和「解析炸了」。
/// 现在失败是返回值的一部分。读取器只依赖本接口 —— 测试里换个假实现就能喂坏数据。
/// </summary>
internal interface IVSCodeHistorySource
{
    /// <summary>数据源名字，仅用于诊断与失败信息。</summary>
    string Name { get; }

    SourceRead Read();
}

/// <summary>共享存储 &lt;state.vscdb&gt; 里的 <c>history.recentlyOpenedPathsList</c>。</summary>
internal sealed class SharedStorageRecordSource : IVSCodeHistorySource
{
    internal const string HistoryKey = "history.recentlyOpenedPathsList";

    private readonly string _dbPath;

    public SharedStorageRecordSource(string dbPath) => _dbPath = dbPath;

    public string Name => $"共享存储 {_dbPath}";

    public SourceRead Read()
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM ItemTable WHERE key = $key";
            command.Parameters.AddWithValue("$key", HistoryKey);

            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                // 文件在但没有这条键：不是失败，就是没记录
                return SourceRead.Ok([]);
            }

            return ParseHistoryKey.RecordTargets(reader.GetString(0), out _) is { } items
                ? SourceRead.Ok(items)
                : SourceRead.Failed("记录值不是合法 JSON");
        }
        catch (Exception ex)
        {
            return SourceRead.Failed(ex.Message);
        }
    }
}

/// <summary>globalStorage 下的 *.vscdb：ItemTable 里 key 含 history 的行。</summary>
internal sealed class GlobalStorageDatabaseSource : IVSCodeHistorySource
{
    private readonly string _dbPath;

    public GlobalStorageDatabaseSource(string dbPath) => _dbPath = dbPath;

    public string Name => $"数据库 {_dbPath}";

    public SourceRead Read()
    {
        var items = new List<VSCodeItem>();

        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT key, value FROM ItemTable WHERE key LIKE '%history%'";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var key = reader.GetString(0);

                // 浏览器/终端历史不是项目来源，跳过以免噪声
                if (key.StartsWith("browser.history", StringComparison.Ordinal) ||
                    key.StartsWith("terminal.history", StringComparison.Ordinal))
                {
                    continue;
                }

                // 单个值不是 JSON 就忽略这一行，不影响同一个数据库里的其它行
                if (ParseHistoryKey.StorageTargets(reader.GetString(1), out _) is { } parsed)
                {
                    items.AddRange(parsed);
                }
            }
        }
        catch (Exception ex)
        {
            return SourceRead.Failed(ex.Message);
        }

        return SourceRead.Ok(items);
    }
}

/// <summary>传统格式 storage.json。</summary>
internal sealed class StorageJsonSource : IVSCodeHistorySource
{
    private readonly string _filePath;

    public StorageJsonSource(string filePath) => _filePath = filePath;

    public string Name => $"storage.json {_filePath}";

    public SourceRead Read()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return SourceRead.Failed("文件不存在");
            }

            return ParseHistoryKey.StorageTargets(File.ReadAllText(_filePath), out _) is { } items
                ? SourceRead.Ok(items)
                : SourceRead.Failed("文件不是合法 JSON");
        }
        catch (Exception ex)
        {
            return SourceRead.Failed(ex.Message);
        }
    }
}

/// <summary>
/// VSCode 写入的两种 JSON 形状 —— 全部解析规则集中在这里，只依赖字符串输入，
/// 不碰文件系统，因此可以脱离 VSCode 直接测试。
///
/// 两种形状的存在性检查规则不同，这是 VSCode 那边的既有事实：最近记录里
/// folderUri 必须真是目录、workspace/file 必须真是文件；而 storage.json 的
/// backupWorkspaces 只记录路径，不检查是否存在（项目可能暂时不在线）。
/// </summary>
internal static class ParseHistoryKey
{
    private enum Existence
    {
        /// <summary>不检查</summary>
        Any,

        /// <summary>必须是目录</summary>
        Directory,

        /// <summary>必须是文件</summary>
        File,
    }

    /// <summary>
    /// <c>history.recentlyOpenedPathsList</c>：<c>{"entries":[...]}</c>。
    /// 解析失败返回 null，原因写进 <paramref name="error"/>。
    /// </summary>
    public static IReadOnlyList<VSCodeItem>? RecordTargets(string json, out string? error) =>
        WithDocument(json, out error, RecordTargets);

    /// <summary>
    /// backupWorkspaces：<c>{"workspaces":[...],"folders":[...]}</c>。
    /// 解析失败返回 null，原因写进 <paramref name="error"/>。
    /// </summary>
    public static IReadOnlyList<VSCodeItem>? StorageTargets(string json, out string? error) =>
        WithDocument(json, out error, StorageTargets);

    private static IReadOnlyList<VSCodeItem>? WithDocument(
        string json,
        out string? error,
        Func<JsonElement, IReadOnlyList<VSCodeItem>> parse)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            using (doc)
            {
                error = null;
                return parse(doc.RootElement);
            }
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static IReadOnlyList<VSCodeItem> RecordTargets(JsonElement root)
    {
        var items = new List<VSCodeItem>();

        if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return items;
        }

        var order = 0;
        foreach (var entry in entries.EnumerateArray())
        {
            if (RecordTarget(entry, order) is { } item)
            {
                items.Add(item);
                order++;
            }
        }

        return items;
    }

    private static IReadOnlyList<VSCodeItem> StorageTargets(JsonElement root)
    {
        var items = new List<VSCodeItem>();

        if (!root.TryGetProperty("backupWorkspaces", out var backup))
        {
            return items;
        }

        if (backup.TryGetProperty("workspaces", out var workspaces) &&
            workspaces.ValueKind == JsonValueKind.Array)
        {
            var order = 0;
            foreach (var ws in workspaces.EnumerateArray())
            {
                if (ws.TryGetProperty("configURIPath", out var configPath) &&
                    Resolve(configPath.GetString(), ItemKind.Workspace, Existence.Any) is { } item)
                {
                    items.Add(item with { Order = order++ });
                }
            }
        }

        if (backup.TryGetProperty("folders", out var folders) && folders.ValueKind == JsonValueKind.Array)
        {
            var order = 0;
            foreach (var folder in folders.EnumerateArray())
            {
                if (folder.TryGetProperty("folderUri", out var folderUri) &&
                    Resolve(folderUri.GetString(), ItemKind.Folder, Existence.Any) is { } item)
                {
                    items.Add(item with { Order = order++ });
                }
            }
        }

        return items;
    }

    private static VSCodeItem? RecordTarget(JsonElement entry, int order)
    {
        if (entry.TryGetProperty("folderUri", out var folderUri))
        {
            return Resolve(folderUri.GetString(), ItemKind.Folder, Existence.Directory, order);
        }

        if (entry.TryGetProperty("workspace", out var workspace) &&
            workspace.TryGetProperty("configPath", out var configPath))
        {
            return Resolve(configPath.GetString(), ItemKind.Workspace, Existence.File, order);
        }

        // 最近记录里绝大多数是单独打开的文件，不处理会丢掉大部分数据
        if (entry.TryGetProperty("fileUri", out var fileUri))
        {
            return Resolve(fileUri.GetString(), ItemKind.File, Existence.File, order);
        }

        return null;
    }

    /// <summary>
    /// 解析 URI、按类型做存在性检查、生成条目。类型在这里就定下来 ——
    /// 标题形态（工作区去扩展名）依赖类型，不能留到后面再补。
    /// </summary>
    private static VSCodeItem? Resolve(string? uri, ItemKind kind, Existence required, int order = 0)
    {
        // 解析不出本机目标的（ssh-remote / dev-container）目前只能丢掉。
        // 见 VSCodeRecentHistory 的类注释：这些条目在界面上看不见，是已知缺口。
        if (VSCodeUri.Parse(uri, out _) is not { } target)
        {
            return null;
        }

        // WSL 走 UNC 形式：显示与存在性探测都用它，不受 9P 报错影响
        var local = target.LocalPath;
        if (string.IsNullOrEmpty(local))
        {
            return null;
        }

        var passes = required switch
        {
            Existence.Directory => DirectoryExists(local),
            Existence.File => FileExists(local),
            _ => true,
        };

        if (!passes)
        {
            return null;
        }

        // 工作区用文件名去扩展名（My.code-workspace → My），其余用文件名本身
        var title = kind == ItemKind.Workspace
            ? Path.GetFileNameWithoutExtension(local)
            : Path.GetFileName(local);

        return new VSCodeItem
        {
            Title = string.IsNullOrEmpty(title) ? local : title,
            Path = local,
            Kind = kind,
            Order = order,
            Target = target,
        };
    }

    /// <summary>WSL UNC 路径的探测会走 9P，失败时静默返回 false 而不是抛异常。</summary>
    private static bool DirectoryExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static bool FileExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch
        {
            return false;
        }
    }
}
