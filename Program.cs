using System.Threading;
using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;

namespace VSCodeRecent;

public class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
        {
            global::Shmuelie.WinRTServer.ComServer server = new();

            ManualResetEvent extensionDisposedEvent = new(false);

            // 只实例化一个扩展对象，之后宿主每次索取 IExtension 都返回同一个实例。
            VSCodeRecentExtension extensionInstance = new(extensionDisposedEvent);
            server.RegisterClass<VSCodeRecentExtension, IExtension>(() => extensionInstance);
            server.Start();

            // 主线程阻塞，直到扩展对象被释放。
            extensionDisposedEvent.WaitOne();
            server.Stop();
            server.UnsafeDispose();
        }
        else
        {
            Console.WriteLine("Not being launched as a Extension... exiting.");
        }
    }
}
