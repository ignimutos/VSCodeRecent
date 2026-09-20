using System.Text.Json;
using System.Text.Json.Serialization;

namespace VSCodeRecent.Models;

/// <summary>
/// Material Icon Theme 的关联表，用来按文件名挑图标。
///
/// <para><b>表从哪来。</b><c>Assets\MaterialIcons\material-icons.json</c> 是从
/// <see href="https://github.com/PKief/vscode-material-icon-theme">pkief.material-icon-theme</see>
/// 的 <c>dist/material-icons.json</c> 裁出来的：只留 fileName / fileExtension 两张关联表，
/// 值改成不带路径、不带 .svg 的图标名（配套 PNG 见同目录），并剔除了排除掉的图标
/// （见 Assets\MaterialIcons\NOTICE.md）。运行时只读这一个文件，不依赖 VS Code 或该扩展。</para>
///
/// <para><b>为什么用嵌入资源而不是文件。</b>图标 PNG 必须留在磁盘上 ——
/// <c>IconHelpers.FromRelativePath</c> 只接受包内相对路径，没有从流构造图标的公开 API。
/// 但关联表没有这个约束，嵌进程序集可以避免「包打漏了 → 图标全变默认」这种静默失败。</para>
/// </summary>
internal sealed class MaterialIconTheme
{
    /// <summary>与 csproj 里 EmbeddedResource 的默认命名一致。</summary>
    private const string ResourceName = "VSCodeRecent.Assets.MaterialIcons.material-icons.json";

    private static MaterialIconTheme? _shared;

    /// <summary>加载失败也不会抛：退回全部默认图标，列表还能用。</summary>
    public static MaterialIconTheme Shared => _shared ??= Load();

    private readonly Dictionary<string, string> _fileNames;
    private readonly Dictionary<string, string> _fileExtensions;

    /// <summary>文件夹行（<see cref="ItemKind.Folder"/>）的图标。</summary>
    public string Folder { get; }

    /// <summary>工作区行（<see cref="ItemKind.Workspace"/>）的图标。</summary>
    public string Workspace { get; }

    /// <summary>文件名对不上任何规则时的兜底图标。</summary>
    public string File { get; }

    private MaterialIconTheme(
        Dictionary<string, string> fileNames,
        Dictionary<string, string> fileExtensions,
        string folder,
        string file,
        string workspace)
    {
        _fileNames = fileNames;
        _fileExtensions = fileExtensions;
        Folder = folder;
        File = file;
        Workspace = workspace;
    }

    /// <summary>
    /// 按 VS Code 的规则挑图标：**先精确匹配整个文件名，再按从长到短的扩展名匹配**。
    ///
    /// <para>顺序很关键。<c>package.json</c> 命中 fileName 规则拿到 nodejs 图标，
    /// 而不是按 <c>json</c> 扩展名拿到 json 图标；<c>config.yaml.tpl</c> 先试
    /// <c>yaml.tpl</c>（无规则）再试 <c>tpl</c> 拿到 smarty 图标。这两条都是
    /// VS Code 里的实际表现，改了就会和资源管理器里看到的不一致。</para>
    /// </summary>
    /// <param name="fileName">含扩展名的文件名，如 <c>config.yaml.tpl</c>；可为 null。</param>
    /// <returns>图标名（不含路径与扩展名）。</returns>
    public string ForFileName(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return File;
        }

        if (_fileNames.TryGetValue(fileName, out var hit))
        {
            return hit;
        }

        // 表里同时存在 .gitignore 这类以点开头的规则，必须整名比较后再走扩展名。
        var lower = fileName.ToLowerInvariant();
        if (!string.Equals(lower, fileName, StringComparison.Ordinal) &&
            _fileNames.TryGetValue(lower, out hit))
        {
            return hit;
        }

        // 从第一个点开始，逐个点往后取后缀，先长后短 —— 长后缀优先是为了让
        // .d.ts 这类组合扩展名赢过 .ts（表里两者都有规则时）。
        for (var i = lower.IndexOf('.'); i >= 0 && i < lower.Length - 1; i = lower.IndexOf('.', i + 1))
        {
            if (_fileExtensions.TryGetValue(lower[(i + 1)..], out hit))
            {
                return hit;
            }
        }

        return File;
    }

    /// <summary>图标名 → 包内相对路径。反斜杠形式与现有调用保持一致。</summary>
    public static string RelativePath(string icon) => $"Assets\\MaterialIcons\\{icon}.png";

    private static MaterialIconTheme Load()
    {
        try
        {
            // 用 typeof(...).Assembly 而不是 GetExecutingAssembly()：测试程序集也会调到这里，
            // 后者会去找测试程序集里并不存在的资源，于是静默退回全部默认图标。
            using var stream = typeof(MaterialIconTheme).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
            {
                return Fallback();
            }

            var payload = JsonSerializer.Deserialize<Payload>(stream);
            var defaults = payload?.Defaults;

            return new MaterialIconTheme(
                payload?.FileNames ?? [],
                payload?.FileExtensions ?? [],
                defaults?.Folder ?? "folder",
                defaults?.File ?? "file",
                defaults?.Workspace ?? "folder-open");
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // 关联表是随包发布的常量，坏掉只可能是打包出了问题；
            // 这时候重要的是别把列表也一起带崩。
            System.Diagnostics.Debug.WriteLine($"MaterialIconTheme load failed: {ex.Message}");
            return Fallback();
        }
    }

    private static MaterialIconTheme Fallback() => new([], [], "folder", "file", "folder-open");

    private sealed class Payload
    {
        [JsonPropertyName("defaults")]
        public Defaults? Defaults { get; init; }

        [JsonPropertyName("fileNames")]
        public Dictionary<string, string>? FileNames { get; init; }

        [JsonPropertyName("fileExtensions")]
        public Dictionary<string, string>? FileExtensions { get; init; }
    }

    private sealed class Defaults
    {
        [JsonPropertyName("folder")]
        public string? Folder { get; init; }

        [JsonPropertyName("file")]
        public string? File { get; init; }

        [JsonPropertyName("workspace")]
        public string? Workspace { get; init; }
    }
}
