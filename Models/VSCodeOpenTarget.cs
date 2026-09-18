namespace VSCodeRecent.Models;

/// <summary>
/// 打开目标。URI 只在这里解码、这里编码，不再压成 UNC 路径让别的模块靠前缀猜回来。
///
/// <para><b>WSL 的大小写规范（distro 全小写）。</b>生产侧
/// <see cref="FromWslUri"/> 与从 UNC 路径反推的 <see cref="FromUncPath"/> 各自把 distro
/// 归一成小写，所以「VSCode 记录 → 路径 → 命令行」整条往返是稳定的，不依赖 VSCode
/// 存的是哪种写法。注意真实的 distro 名本身是保留大小写的（WSL 认
/// <c>Ubuntu-22.04</c> 不认 <c>ubuntu-22.04</c>），所以 <see cref="FolderUri"/> 生成 URI 时
/// MUST 按 VSCode / WSL 自己的规则重建：wsl+ 后的 distro 小写，而路径第一段
/// （/mnt/c、/home/...）用 VSCode 记录里的原始大小写 —— 两者混写会打不开。</para>
/// </summary>
internal abstract record VSCodeOpenTarget
{
    /// <summary>喂给 <c>code</c> 的参数。本地路径已加好引号；URI 不需要（不含空格）。</summary>
    public abstract string ToCodeArguments();

    /// <summary>本机可访问的路径；远程或路径丢失时为 null。用于 TitlesChanged 轮询判断条目是否还有效。</summary>
    public abstract string? LocalPath { get; }

    /// <summary>本地文件夹，<c>code "C:\path"</c>。</summary>
    public sealed record LocalFolder(string Path) : VSCodeOpenTarget
    {
        public override string ToCodeArguments() => $"\"{Path}\"";

        public override string? LocalPath => Path;
    }

    /// <summary>本地文件，同样走参数形式。</summary>
    public sealed record LocalFile(string Path) : VSCodeOpenTarget
    {
        public override string ToCodeArguments() => $"\"{Path}\"";

        public override string? LocalPath => Path;
    }

    /// <summary>
    /// WSL 文件夹，<c>code --folder-uri vscode-remote://wsl+&lt;distro&gt;/&lt;path&gt;</c>。
    /// <paramref name="Distro"/> 是小写的 WSL 标识，<paramref name="Path"/> 是完整 Linux 路径。
    /// </summary>
    public sealed record WslFolder(string Distro, string Path) : VSCodeOpenTarget
    {
        public override string ToCodeArguments() => $"--folder-uri \"{FolderUri()}\"";

        /// <summary>\\wsl.localhost\&lt;distro&gt;\... —— 仅供显示与存在性探测。</summary>
        public override string? LocalPath => $@"\\wsl.localhost\{Distro}\{Path.TrimStart('/').Replace('/', '\\')}";

        public string FolderUri() => $"vscode-remote://wsl+{Distro.ToLowerInvariant()}{EnsureLeadingSlash(Path)}";

        private static string EnsureLeadingSlash(string path) =>
            path.StartsWith('/') ? path : "/" + path;
    }

    /// <summary>
    /// 从 <c>vscode-remote://wsl+&lt;distro&gt;/&lt;path&gt;</c> 来。取的是记录里的原始大小写，
    /// 归一化推迟到 <see cref="WslFolder.FolderUri"/> 生成命令行那一刻 —— 对 <c>wsl$</c>
    /// 形式的 URI，首段既是 distro 又承载路径大小写，提前归一化会把 <c>/mnt/c</c> 也毁掉。
    /// </summary>
    public static WslFolder FromWslUri(string distro, string path) => new(distro, path);

    /// <summary>
    /// 从 <c>\\wsl.localhost\&lt;Distro&gt;\&lt;path&gt;</c> 反推（历史里存的 WSL UNC 路径）。
    /// 这里 distro 可以安全地转小写：路径只剩在 distro 之后的部分，不含 distro 的大小写。
    /// </summary>
    public static WslFolder? FromUncPath(string uncPath)
    {
        string rest;
        if (uncPath.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase))
        {
            rest = uncPath[@"\\wsl.localhost\".Length..];
        }
        else if (uncPath.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase))
        {
            rest = uncPath[@"\\wsl$\".Length..];
        }
        else
        {
            return null;
        }

        var parts = rest.Split('\\', 2);
        if (parts.Length < 2 || parts[0].Length == 0)
        {
            return null;
        }

        return new WslFolder(parts[0].ToLowerInvariant(), "/" + parts[1].Replace('\\', '/'));
    }
}
