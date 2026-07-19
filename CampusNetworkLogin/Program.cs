using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace CampusNetworkLogin;

class Program
{
    // 必须保持为静态字段，防止 JIT 优化提前 GC 回收 Mutex 导致单实例保护失效
    private static Mutex? s_mutex;

    /// <summary>
    /// 当第二个实例请求显示窗口时触发
    /// </summary>
    public static event Action? ShowMainWindowRequested;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // 确保只有一个实例运行
        s_mutex = new Mutex(true, "DrcomNET-SingleInstance", out var createdNew);
        if (!createdNew)
        {
            s_mutex.Dispose();
            // 通知已有实例显示窗口
            TryActivateExistingInstance();
            return;
        }

        try
        {
            // 启动 NamedPipe 服务端，监听第二个实例的激活请求
            _ = Task.Run(() => RunPipeServer());

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            s_mutex.ReleaseMutex();
            s_mutex.Dispose();
            s_mutex = null;
        }
    }

    /// <summary>
    /// 尝试通过 NamedPipe 通知已有实例显示窗口
    /// </summary>
    private static void TryActivateExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", "DrcomNET-IPC", PipeDirection.Out);
            client.Connect(1000);
            using var writer = new StreamWriter(client);
            writer.WriteLine("SHOW");
            writer.Flush();
        }
        catch { /* 已有实例可能已退出 */ }
    }

    /// <summary>
    /// NamedPipe 服务端，持续监听激活请求
    /// </summary>
    private static void RunPipeServer()
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream("DrcomNET-IPC", PipeDirection.In);
                server.WaitForConnection();
                using var reader = new StreamReader(server);
                var line = reader.ReadLine();
                if (line == "SHOW")
                {
                    ShowMainWindowRequested?.Invoke();
                }
            }
            catch
            {
                // 管道断开或出错，继续监听
                try { Thread.Sleep(100); } catch { break; }
            }
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect();
}
