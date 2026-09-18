using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CommandPalette.Extensions;

namespace VSCodeRecent;

/// <summary>
/// COM 激活入口。Guid 必须与 Package.appxmanifest 中的 Class Id 一致。
/// </summary>
[Guid("A5F9B8E3-C4D2-A1F0-E9B8-C7D6E5F4A3B2")]
public sealed partial class VSCodeRecentExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _extensionDisposedEvent;
    private readonly VSCodeCommandsProvider _provider = new();

    public VSCodeRecentExtension(ManualResetEvent extensionDisposedEvent)
    {
        _extensionDisposedEvent = extensionDisposedEvent;
    }

    public object? GetProvider(ProviderType providerType)
    {
        return providerType switch
        {
            ProviderType.Commands => _provider,
            _ => null,
        };
    }

    public void Dispose() => _extensionDisposedEvent.Set();
}
