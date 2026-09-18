using VSCodeRecent.Models;

namespace VSCodeRecent;

/// <summary>
/// VSCode 记录里的 URI → <see cref="VSCodeOpenTarget"/>。全仓库只有这里碰 URI 的编解码。
///
/// <para><b>只用 Uri.UnescapeDataString，绝不用 HttpUtility.UrlDecode。</b>
/// 后者是表单编码语义，会把 <c>+</c> 也解成空格。VSCode 写的是标准百分号编码、<c>+</c>
/// 就是字面加号（<c>wsl+ubuntu</c> 的加号本身就是裸的），所以表单解码会毁掉所有含
/// <c>+</c> 的路径段。旧代码还有个更重的变体：剥 <c>file://</c> 前缀前后各解一次，
/// 第二次把第一次还原出来的 <c>+</c> 也吃掉，条目随后在存在性检查处静默消失。</para>
///
/// <para><b>只解码一次，且解码先于拆 authority。</b>authority 段里的 <c>%2F</c>
/// 必须还原成真斜杠才能正确切开 distro 与路径；反过来「先拆再解」拿不到 authority 的
/// 完整边界。两者顺序不能换。</para>
///
/// <para>解析不出本机可打开的目标时返回 null —— 目前包括 ssh-remote / dev-container
/// 等非 WSL 远程，原因记在 <paramref name="error"/> 里。</para>
/// </summary>
internal static class VSCodeUri
{
    private const string RemoteScheme = "vscode-remote://";

    /// <summary>
    /// WSL 在 remote URI 里的 authority 前缀。URI 只有 <c>wsl+&lt;distro&gt;</c> 这一种形式；
    /// <c>wsl$</c> 是 UNC 路径的前缀（<c>\\wsl$\&lt;distro&gt;</c>），由
    /// <see cref="VSCodeOpenTarget.FromUncPath"/> 处理，不走这里。
    /// </summary>
    private const string WslAuthorityPrefix = "wsl+";

    /// <summary>
    /// 解析 VSCode 记录里的 URI。可打开时返回目标，否则返回 null 并把原因写进
    /// <paramref name="error"/>。
    /// </summary>
    public static VSCodeOpenTarget? Parse(string? uri, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(uri))
        {
            error = "URI 为空";
            return null;
        }

        // 唯一一次解码
        var decoded = Uri.UnescapeDataString(uri);

        if (decoded.StartsWith(RemoteScheme, StringComparison.OrdinalIgnoreCase))
        {
            return ParseRemote(decoded, out error);
        }

        if (decoded.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return ParseFile(decoded, out error);
        }

        // 没有 scheme 的记录（老版本 / 手工写入）：可能是 UNC，也可能就是本地路径
        return ResolvePlain(decoded, out error);
    }

    private static VSCodeOpenTarget? ParseRemote(string decoded, out string? error)
    {
        error = null;

        var rest = decoded[RemoteScheme.Length..];

        // 拆出 authority：第一个未编码的 / ? # 之前的部分
        var authorityEnd = rest.IndexOfAny(['/', '?', '#']);
        if (authorityEnd < 0)
        {
            error = "远程 URI 缺少路径部分";
            return null;
        }

        var authority = rest[..authorityEnd];

        // 保留原始大小写：真实 distro 名是大小写敏感的，归一化推迟到生成命令行那一刻
        var distro = authority.StartsWith(WslAuthorityPrefix, StringComparison.OrdinalIgnoreCase)
            ? authority[WslAuthorityPrefix.Length..]
            : string.Empty;

        if (distro.Length == 0)
        {
            // ssh-remote / dev-container 等：无法用本地路径打开
            error = $"不支持的远程类型 “{authority}”（目前只支持 WSL）";
            return null;
        }

        var path = rest[(authorityEnd + 1)..];
        var cut = path.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            path = path[..cut];
        }

        path = "/" + path.TrimStart('/');
        return VSCodeOpenTarget.FromWslUri(distro, path);
    }

    private static VSCodeOpenTarget? ParseFile(string decoded, out string? error)
    {
        error = null;

        var path = decoded.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)
            ? decoded[8..]
            : decoded[7..];

        var cut = path.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            path = path[..cut];
        }

        if (path.Length == 0)
        {
            error = "文件 URI 缺少路径";
            return null;
        }

        return ResolvePlain(path, out error);
    }

    /// <summary>把已解码的路径解析成本地文件夹或 WSL 目标。此时不再做任何解码。</summary>
    private static VSCodeOpenTarget? ResolvePlain(string path, out string? error)
    {
        error = null;

        // WSL 的 UNC 形式：\\wsl.localhost\<Distro>\... / \\wsl$\<Distro>\...
        if (VSCodeOpenTarget.FromUncPath(path) is { } wsl)
        {
            return wsl;
        }

        // Windows 盘符：C:/path → C:\path
        if (path.Length > 2 && path[1] == ':')
        {
            return new VSCodeOpenTarget.LocalFolder(path.Replace('/', '\\'));
        }

        // Unix 绝对路径：剥前缀时丢了开头的 /，补回来
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        return new VSCodeOpenTarget.LocalFolder(path);
    }
}
