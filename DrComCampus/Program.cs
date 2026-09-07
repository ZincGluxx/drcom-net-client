using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace DrComCampus;

internal static class Program
{
    internal static bool SuppressAutoLogin { get; private set; }
    internal static bool ForceHardwareRendering { get; private set; }

    // 必须保持为静态字段，防止 JIT 优化提前 GC 回收 Mutex 导致单实例保护失效
    private static Mutex? s_singleInstanceMutex;
    private static readonly CancellationTokenSource s_pipeCancellationSource = new();
    private static Task? s_pipeListenerTask;

    /// <summary>
    /// 当第二个实例请求显示窗口时触发
    /// </summary>
    public static event Action? ShowPrimaryWindowRequested;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        SuppressAutoLogin = Array.Exists(
            args,
            static arg => string.Equals(arg, "--no-auto-login", StringComparison.OrdinalIgnoreCase));
        ForceHardwareRendering = Array.Exists(
            args,
            static arg => string.Equals(arg, "--hardware-rendering", StringComparison.OrdinalIgnoreCase));

        // 诊断实例使用独立互斥量，避免干扰用户正在运行的正式实例。
        var mutexName = SuppressAutoLogin
            ? "DrComCampus-Diagnostics-SingleInstance"
            : "DrComCampus-SingleInstance";
        s_singleInstanceMutex = new Mutex(true, mutexName, out var createdNew);
        if (!createdNew)
        {
            s_singleInstanceMutex.Dispose();
            // 通知已有实例显示窗口
            TryActivateExistingInstance();
            return;
        }

        try
        {
            // 诊断实例不监听正式实例的激活管道。
            if (!SuppressAutoLogin)
            {
                s_pipeListenerTask = RunPipeServerAsync(s_pipeCancellationSource.Token);
            }

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            s_pipeCancellationSource.Cancel();
            try { s_pipeListenerTask?.Wait(TimeSpan.FromSeconds(1)); } catch { }
            s_pipeCancellationSource.Dispose();
            s_singleInstanceMutex.ReleaseMutex();
            s_singleInstanceMutex.Dispose();
            s_singleInstanceMutex = null;
        }
    }

    /// <summary>
    /// 尝试通过 NamedPipe 通知已有实例显示窗口
    /// </summary>
    private static void TryActivateExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", "DrComCampus-IPC", PipeDirection.Out);
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
    private static async Task RunPipeServerAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    "DrComCampus-IPC", PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                using var reader = new StreamReader(server);
                var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (line == "SHOW")
                {
                    ShowPrimaryWindowRequested?.Invoke();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch when (!token.IsCancellationRequested)
            {
                try { await Task.Delay(100, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect();

        // 登录客户端长期处于静态界面。软件渲染避免常驻 ANGLE 与显卡驱动栈，
        // 显著降低内存和线程数；特殊设备可用 --hardware-rendering 回退。
        if (!ForceHardwareRendering)
        {
            builder.With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.Software],
                CompositionMode = [Win32CompositionMode.RedirectionSurface],
                ShouldRenderOnUIThread = false,
            });
        }

        return builder;
    }
}
