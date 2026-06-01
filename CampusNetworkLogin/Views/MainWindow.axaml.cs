using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CampusNetworkLogin.Helpers;
using CampusNetworkLogin.Models;
using CampusNetworkLogin.Services;
using System;
using System.Text;
using System.Threading.Tasks;

namespace CampusNetworkLogin.Views;

public partial class MainWindow : Window
{
    private readonly Task<ConfigModel> _configLoadTask;
    private readonly ConfigService _configService = new();
    private readonly AutoStartService _autoStartService = new();
    private readonly DrcomAuthService _authService = new();
    private ConfigModel _config = new();
    private bool _isLoggingIn;
    private bool _initialized;
    private bool _autoStartHandlerAttached;
    private TrayIcon? _trayIcon;

    private readonly StringBuilder _logBuilder = new();
    private int _logLineCount;
    private const int MaxLogLines = 200;

    private static readonly IBrush StatusConnected = new SolidColorBrush(Color.Parse("#34C759"));
    private static readonly IBrush StatusDisconnected = new SolidColorBrush(Color.Parse("#FF453A"));
    private static readonly IBrush StatusFailed = new SolidColorBrush(Color.Parse("#EF4444"));
    private static readonly IBrush StatusOffline = new SolidColorBrush(Color.Parse("#64748B"));

    public MainWindow() : this(Task.FromResult(new ConfigModel()))
    {
    }

    public MainWindow(Task<ConfigModel> configLoadTask)
    {
        _configLoadTask = configLoadTask;
        InitializeComponent();
        // NativeAOT 下从文件加载窗口图标
        try
        {
            var icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "icon.ico");
            if (System.IO.File.Exists(icoPath))
                Icon = new WindowIcon(icoPath);
        }
        catch { /* ignore */ }
        SetupAuthEvents();
        Opened += (_, _) => Dispatcher.UIThread.Post(() => Opacity = 1, DispatcherPriority.Render);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_initialized) return;
        _initialized = true;
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        var config = await _configLoadTask.ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _config = config;
            ApplyConfigToUI();

            if (!_autoStartHandlerAttached)
            {
                _autoStartHandlerAttached = true;
                this.FindControl<ToggleSwitch>("AutoStartCheck")!.IsCheckedChanged += (_, _) =>
                {
                    try { _autoStartService.SetEnabled(this.FindControl<ToggleSwitch>("AutoStartCheck")!.IsChecked ?? false); }
                    catch { /* ignore */ }
                };
            }

            if (_logBuilder.Length > 0)
                this.FindControl<TextBlock>("LogText")!.Text = _logBuilder.ToString();

        }, DispatcherPriority.Background);

        _ = RefreshNetworkInfoAsync();
        Dispatcher.UIThread.Post(() => _ = CreateTrayIconAsync(), DispatcherPriority.Background);

        if (_config.AutoLogin &&
            !string.IsNullOrEmpty(_config.Username) &&
            !string.IsNullOrEmpty(_config.Password))
        {
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(800);
                await DoLogin();
            }, DispatcherPriority.Background);
        }
    }

    private async Task RefreshNetworkInfoAsync()
    {
        await Task.Delay(200).ConfigureAwait(false);
        var network = await Task.Run(NetworkInfoService.GetNetworkInfo).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
            ApplyNetworkInfo(network.Ip, network.Mac), DispatcherPriority.Background);
    }

    private async Task CreateTrayIconAsync()
    {
        await Task.Delay(1500).ConfigureAwait(false);

        WindowIcon? icon = null;
        try
        {
            var icoPath = await Task.Run(() =>
            {
                var baseDir = AppContext.BaseDirectory;
                var path = System.IO.Path.Combine(baseDir, "Resources", "icon.ico");
                if (System.IO.File.Exists(path)) return path;
                path = System.IO.Path.Combine(baseDir, "icon.ico");
                return System.IO.File.Exists(path) ? path : null;
            }).ConfigureAwait(false);

            if (icoPath != null)
                icon = new WindowIcon(icoPath);
        }
        catch
        {
            // ignore
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_trayIcon != null) return;

            var menu = new NativeMenu();
            var showItem = new NativeMenuItem("显示窗口");
            showItem.Click += (_, _) => ShowFromTray();
            menu.Add(showItem);

            var loginItem = new NativeMenuItem("登录");
            loginItem.Click += async (_, _) => await DoLogin();
            menu.Add(loginItem);

            var logoutItem = new NativeMenuItem("断开连接");
            logoutItem.Click += (_, _) => Disconnect();
            menu.Add(logoutItem);
            menu.Add(new NativeMenuItemSeparator());

            var quitItem = new NativeMenuItem("退出程序");
            quitItem.Click += (_, _) => QuitApp();
            menu.Add(quitItem);

            _trayIcon = new TrayIcon
            {
                Icon = icon,
                ToolTipText = "校园网登录 - 未连接",
                Menu = menu,
                IsVisible = true
            };
            _trayIcon.Clicked += (_, _) => ShowFromTray();
        }, DispatcherPriority.Background);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void QuitApp()
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        _authService.Stop();

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
            Environment.Exit(0);
    }

    private void ApplyConfigToUI()
    {
        UsernameField.Text = _config.Username;
        this.FindControl<TextBox>("PasswordField")!.Text = _config.Password;
        this.FindControl<TextBox>("ServerField")!.Text = _config.Server;
        this.FindControl<ToggleSwitch>("AutoStartCheck")!.IsChecked = _config.StartWithWindows;
        this.FindControl<ToggleSwitch>("AutoLoginCheck")!.IsChecked = _config.AutoLogin;
        UpdateNetworkStatusDisplay();
    }

    private void UpdateNetworkStatusDisplay()
    {
        IpStatusText.Text = $"IP: {PrivacyHelper.MaskIp(_config.HostIp)}";
        MacStatusText.Text = $"MAC: {PrivacyHelper.MaskMac(_config.Mac)}";
    }

    private void ApplyNetworkInfo(string ip, string mac)
    {
        if ((_config.HostIp == "0.0.0.0" || string.IsNullOrEmpty(_config.HostIp)) && ip != "0.0.0.0")
            _config.HostIp = ip;

        if (_config.Mac == "0x888888888888" && mac != "0x888888888888")
            _config.Mac = mac;

        UpdateNetworkStatusDisplay();
    }

    private ConfigModel ReadConfigFromUI()
    {
        var hostIp = !string.IsNullOrEmpty(_config.HostIp) && _config.HostIp != "0.0.0.0"
            ? _config.HostIp : "0.0.0.0";
        var mac = !string.IsNullOrEmpty(_config.Mac) && _config.Mac != "0x888888888888"
            ? _config.Mac : "0x888888888888";

        return new ConfigModel
        {
            Server = string.IsNullOrEmpty(this.FindControl<TextBox>("ServerField")!.Text) ? "10.100.61.3" : this.FindControl<TextBox>("ServerField")!.Text!,
            Username = UsernameField.Text ?? "",
            Password = this.FindControl<TextBox>("PasswordField")!.Text ?? "",
            HostIp = hostIp,
            Mac = mac,
            HostName = Environment.MachineName,
            HostOs = "Windows 10",
            PrimaryDns = "10.10.10.10",
            DhcpServer = "0.0.0.0",
            AutoLogin = this.FindControl<ToggleSwitch>("AutoLoginCheck")!.IsChecked ?? false,
            StartWithWindows = this.FindControl<ToggleSwitch>("AutoStartCheck")!.IsChecked ?? false,
            MinimizeToTray = true
        };
    }

    private void SetupAuthEvents()
    {
        _authService.OnLog += AppendLog;
        _authService.OnConnectionChanged += connected =>
        {
            Dispatcher.UIThread.Post(() => UpdateConnectionStatus(connected), DispatcherPriority.Background);
        };
    }

    private void UpdateConnectionStatus(bool connected)
    {
        StatusIndicator.Text = connected ? "● 已连接" : "● 未连接";
        StatusIndicator.Foreground = connected ? StatusConnected : StatusDisconnected;
        LoginBtn.IsVisible = !connected;
        LogoutBtn.IsVisible = connected;

        if (_trayIcon != null)
            _trayIcon.ToolTipText = connected ? "校园网登录 - 已连接" : "校园网登录 - 未连接";
    }

    private async void LoginBtn_Click(object? sender, RoutedEventArgs e) => await DoLogin();

    private void LogoutBtn_Click(object? sender, RoutedEventArgs e) => Disconnect();

    private async Task SaveConfigAsync()
    {
        var config = ReadConfigFromUI();
        await Task.Run(() => _configService.Save(config)).ConfigureAwait(false);
        _config = config;
    }

    private async Task DoLogin()
    {
        if (_isLoggingIn) return;

        try
        {
            await SaveConfigAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppendLog($"保存配置失败: {ex.Message}");
        }

        var config = ReadConfigFromUI();
        if (!string.IsNullOrEmpty(UsernameField.Text))
        {
            config.Username = UsernameField.Text;
        }

        if (config.HostIp == "0.0.0.0" || config.Mac == "0x888888888888")
        {
            var network = await Task.Run(NetworkInfoService.GetNetworkInfo).ConfigureAwait(true);
            if (config.HostIp == "0.0.0.0" && network.Ip != "0.0.0.0") config.HostIp = network.Ip;
            if (config.Mac == "0x888888888888" && network.Mac != "0x888888888888") config.Mac = network.Mac;
            _config.HostIp = config.HostIp;
            _config.Mac = config.Mac;
            ApplyNetworkInfo(network.Ip, network.Mac);
        }

        _authService.UpdateConfig(config.Server, config.Username, config.Password,
            config.HostIp, config.Mac, config.HostName, config.HostOs,
            config.PrimaryDns, config.DhcpServer);

        _isLoggingIn = true;
        LoginBtn.IsEnabled = false;
        UpdateConnectionStatus(false);
        AppendLog("正在连接认证服务器...");

        try
        {
            await _authService.StartLoginAsync().ConfigureAwait(true);

            if (_authService.IsLoggedIn)
            {
                AppendLog("登录成功，已连接到校园网");
                StatusIndicator.Text = "● 已连接";
                StatusIndicator.Foreground = StatusConnected;
            }
            else
            {
                AppendLog("登录失败：请检查账号密码");
                StatusIndicator.Text = "● 登录失败";
                StatusIndicator.Foreground = StatusFailed;
            }
        }
        catch (Exception ex)
        {
            AppendLog($"登录失败：{ex.Message}");
            StatusIndicator.Text = "● 登录失败";
            StatusIndicator.Foreground = StatusFailed;
        }

        _isLoggingIn = false;
        LoginBtn.IsEnabled = true;
        UpdateConnectionStatus(_authService.IsLoggedIn);
    }

    private void Disconnect()
    {
        _authService.Stop();
        _isLoggingIn = false;
        LoginBtn.IsEnabled = true;
        UpdateConnectionStatus(false);
        AppendLog("已断开连接");
        StatusIndicator.Text = "● 已离线";
        StatusIndicator.Foreground = StatusOffline;
    }

    private async void SaveBtn_Click(object? sender, RoutedEventArgs? e)
    {
        try
        {
            await SaveConfigAsync().ConfigureAwait(true);
            AppendLog("配置已保存");
        }
        catch (Exception ex)
        {
            AppendLog($"保存失败: {ex.Message}");
        }
    }

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Dispatcher.UIThread.Post(() =>
        {
            if (_logLineCount >= MaxLogLines)
            {
                var text = _logBuilder.ToString();
                var idx = text.IndexOf('\n');
                if (idx >= 0)
                {
                    _logBuilder.Remove(0, idx + 1);
                    _logLineCount--;
                }
            }

            _logBuilder.AppendLine(line);
            _logLineCount++;

            var logTextControl = this.FindControl<SelectableTextBlock>("LogText");
            if (logTextControl != null)
                logTextControl.Text = _logBuilder.ToString();
        }, DispatcherPriority.Background);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_trayIcon != null)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _authService.Stop();
        _trayIcon?.Dispose();
        base.OnClosed(e);
    }
}
