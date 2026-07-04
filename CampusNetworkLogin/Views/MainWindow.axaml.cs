using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CampusNetworkLogin.Helpers;
using CampusNetworkLogin.Models;
using CampusNetworkLogin.Services;
using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace CampusNetworkLogin.Views;

public partial class MainWindow : Window
{
    private const string AuthServer = "10.100.61.3";
    private const string DefaultDns = "10.10.10.10";

    private readonly Task<ConfigModel> _configLoadTask;
    private readonly ConfigService _configService = new();
    private readonly AutoStartService _autoStartService = new();
    private readonly DrcomAuthService _authService = new();
    private ConfigModel _config = new();
    private NetworkSnapshot _networkSnapshot = new("0.0.0.0", "0x888888888888", "--", "255.255.255.0", "--", "--", false);
    private bool _isLoggingIn;
    private bool _initialized;
    private bool _autoStartHandlerAttached;
    private bool _passwordVisible;
    private TrayIcon? _trayIcon;
    private readonly StringBuilder _logBuilder = new();

    private static readonly IBrush StatusConnected = new SolidColorBrush(Color.Parse("#34C759"));
    private static readonly IBrush StatusDisconnected = new SolidColorBrush(Color.Parse("#FF453A"));
    private static readonly IBrush StatusFailed = new SolidColorBrush(Color.Parse("#EF4444"));
    private static readonly IBrush StatusOffline = new SolidColorBrush(Color.Parse("#64748B"));
    private static readonly IBrush StatusReady = new SolidColorBrush(Color.Parse("#16A34A"));
    private static readonly IBrush StatusWarning = new SolidColorBrush(Color.Parse("#D97706"));

    public MainWindow() : this(Task.FromResult(new ConfigModel()))
    {
    }

    public MainWindow(Task<ConfigModel> configLoadTask)
    {
        _configLoadTask = configLoadTask;
        InitializeComponent();

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
                AutoStartCheck.IsCheckedChanged += (_, _) =>
                {
                    try { _autoStartService.SetEnabled(AutoStartCheck.IsChecked ?? false); }
                    catch { /* ignore */ }
                };
            }
        }, DispatcherPriority.Background);

        _ = RefreshNetworkInfoAsync();
        Dispatcher.UIThread.Post(() => _ = CreateTrayIconAsync(), DispatcherPriority.Background);

        if (_config.AutoLogin &&
            !string.IsNullOrWhiteSpace(_config.Username) &&
            !string.IsNullOrWhiteSpace(_config.Password))
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
        await Task.Delay(150).ConfigureAwait(false);
        var network = await Task.Run(NetworkInfoService.GetNetworkSnapshot).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() => ApplyNetworkInfo(network), DispatcherPriority.Background);
    }

    private async Task CreateTrayIconAsync()
    {
        await Task.Delay(1000).ConfigureAwait(false);

        WindowIcon? icon = null;
        try
        {
            var icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "icon.ico");
            if (System.IO.File.Exists(icoPath))
                icon = new WindowIcon(icoPath);
        }
        catch { /* ignore */ }

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

            var refreshItem = new NativeMenuItem("刷新网络信息");
            refreshItem.Click += async (_, _) => await RefreshNetworkInfoAsync();
            menu.Add(refreshItem);

            var copyDiagnosticsItem = new NativeMenuItem("复制网络诊断");
            copyDiagnosticsItem.Click += async (_, _) => await CopyTextToClipboardAsync(BuildNetworkDiagnostics());
            menu.Add(copyDiagnosticsItem);
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

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
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
        PasswordField.Text = _config.Password;
        AutoStartCheck.IsChecked = _config.StartWithWindows;
        AutoLoginCheck.IsChecked = _config.AutoLogin;
        MinimizeToTrayCheck.IsChecked = _config.MinimizeToTray;
        UpdateNetworkStatusDisplay();
        UpdateConfigStatus(ReadConfigFromUI());
    }

    private void UpdateNetworkStatusDisplay()
    {
        IpStatusText.Text = $"IP: {PrivacyHelper.MaskIp(_config.HostIp)}";
        MacStatusText.Text = $"MAC: {PrivacyHelper.MaskMac(_config.Mac)}";
        AdapterStatusText.Text = $"网卡: {_networkSnapshot.AdapterName}";
        GatewayStatusText.Text = $"网关: {_networkSnapshot.Gateway}";
        DnsStatusText.Text = $"DNS: {_networkSnapshot.Dns}";
    }

    private void ApplyNetworkInfo(NetworkSnapshot snapshot)
    {
        _networkSnapshot = snapshot;

        if (snapshot.Ip != "0.0.0.0")
            _config.HostIp = snapshot.Ip;

        if (snapshot.Mac != "0x888888888888")
            _config.Mac = snapshot.Mac;

        if (snapshot.Gateway != "--")
            _config.Gateway = snapshot.Gateway;

        if (snapshot.Dns != "--")
            _config.PrimaryDns = snapshot.Dns;

        UpdateNetworkStatusDisplay();
        UpdateConfigStatus(ReadConfigFromUI());
    }

    private ConfigModel ReadConfigFromUI()
    {
        return new ConfigModel
        {
            Server = AuthServer,
            Username = (UsernameField.Text ?? "").Trim(),
            Password = PasswordField.Text ?? "",
            HostIp = string.IsNullOrWhiteSpace(_config.HostIp) ? "0.0.0.0" : _config.HostIp,
            Mac = string.IsNullOrWhiteSpace(_config.Mac) ? "0x888888888888" : _config.Mac,
            Gateway = string.IsNullOrWhiteSpace(_config.Gateway) ? "0.0.0.0" : _config.Gateway,
            HostName = Environment.MachineName,
            HostOs = NetworkInfoService.GetOsVersion(),
            PrimaryDns = _networkSnapshot.Dns == "--" ? DefaultDns : _networkSnapshot.Dns,
            DhcpServer = "0.0.0.0",
            AutoLogin = AutoLoginCheck.IsChecked ?? false,
            StartWithWindows = AutoStartCheck.IsChecked ?? false,
            MinimizeToTray = MinimizeToTrayCheck.IsChecked ?? true
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
        KeepAliveStatusText.Text = connected ? "保活运行中" : "保活未启动";
        KeepAliveStatusText.Foreground = connected ? StatusReady : StatusOffline;
        LoginBtn.IsVisible = !connected;
        LogoutBtn.IsVisible = connected;

        if (_trayIcon != null)
            _trayIcon.ToolTipText = connected ? "校园网登录 - 已连接" : "校园网登录 - 未连接";
    }

    private async void LoginBtn_Click(object? sender, RoutedEventArgs e) => await DoLogin();

    private void LogoutBtn_Click(object? sender, RoutedEventArgs e) => Disconnect();

    private async void RefreshNetworkBtn_Click(object? sender, RoutedEventArgs e)
    {
        RefreshNetworkBtn.IsEnabled = false;
        try
        {
            SetHint("正在刷新本机网络...");
            var network = await Task.Run(NetworkInfoService.GetNetworkSnapshot).ConfigureAwait(true);
            ApplyNetworkInfo(network);
            SetHint(network.IsReady ? "网络信息已刷新" : "未找到可用 IPv4/MAC");
        }
        catch (Exception ex)
        {
            SetHint($"刷新失败: {ex.Message}");
        }
        finally
        {
            RefreshNetworkBtn.IsEnabled = true;
        }
    }

    private async void EditNetworkBtn_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new NetworkSettingsWindow(_networkSnapshot);
        var applied = await dialog.ShowDialog<bool>(this);
        if (applied)
        {
            SetHint("静态网络设置已提交，正在刷新...");
            await RefreshNetworkInfoAsync();
        }
    }

    private async void PingInternalBtn_Click(object? sender, RoutedEventArgs e)
    {
        await RunPingAsync("jlu.edu.cn", PingInternalBtn, InternalStatusText);
    }

    private async void PingExternalBtn_Click(object? sender, RoutedEventArgs e)
    {
        await RunPingAsync("www.baidu.com", PingExternalBtn, ExternalStatusText);
    }

    private async Task RunPingAsync(string host, Button button, TextBlock target)
    {
        button.IsEnabled = false;
        target.Text = "检测中...";
        target.Foreground = StatusOffline;

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 2500).ConfigureAwait(true);
            if (reply.Status == IPStatus.Success)
            {
                target.Text = $"成功 {reply.RoundtripTime}ms";
                target.Foreground = StatusReady;
            }
            else
            {
                target.Text = $"失败 {reply.Status}";
                target.Foreground = StatusWarning;
            }
        }
        catch (Exception ex)
        {
            target.Text = $"失败 {ex.Message}";
            target.Foreground = StatusWarning;
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void TogglePasswordBtn_Click(object? sender, RoutedEventArgs e)
    {
        _passwordVisible = !_passwordVisible;
        PasswordField.PasswordChar = _passwordVisible ? '\0' : '●';
        TogglePasswordBtn.Content = _passwordVisible ? "隐藏" : "显示";
    }

    private async Task SaveConfigAsync()
    {
        var config = ReadConfigFromUI();
        await Task.Run(() => _configService.Save(config)).ConfigureAwait(false);
        _config = config;
        UpdateConfigStatus(config);
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
            SetHint($"保存配置失败: {ex.Message}");
        }

        var config = ReadConfigFromUI();
        if (config.HostIp == "0.0.0.0" || config.Mac == "0x888888888888")
        {
            SetHint("正在检测本机网络...");
            var network = await Task.Run(NetworkInfoService.GetNetworkSnapshot).ConfigureAwait(true);
            if (network.Ip != "0.0.0.0") config.HostIp = network.Ip;
            if (network.Mac != "0x888888888888") config.Mac = network.Mac;
            _config.HostIp = config.HostIp;
            _config.Mac = config.Mac;
            ApplyNetworkInfo(network);
        }

        var validationError = ValidateLoginConfig(config);
        if (validationError != null)
        {
            SetHint(validationError);
            StatusIndicator.Text = "● 待完善";
            StatusIndicator.Foreground = StatusWarning;
            UpdateConfigStatus(config);
            return;
        }

        _authService.UpdateConfig(config.Server, config.Username, config.Password,
            config.HostIp, config.Mac, config.HostName, config.HostOs,
            config.PrimaryDns, config.DhcpServer);

        _isLoggingIn = true;
        LoginBtn.IsEnabled = false;
        SaveBtn.IsEnabled = false;
        UpdateConnectionStatus(false);
        SetHint("正在连接认证服务器...");

        try
        {
            var loginSucceeded = await _authService.StartLoginAsync().ConfigureAwait(true);

            if (loginSucceeded && _authService.IsLoggedIn)
            {
                SetHint("登录成功");
                StatusIndicator.Text = "● 已连接";
                StatusIndicator.Foreground = StatusConnected;
            }
            else
            {
                SetHint("登录失败，请检查账号密码");
                StatusIndicator.Text = "● 登录失败";
                StatusIndicator.Foreground = StatusFailed;
            }
        }
        catch (Exception ex)
        {
            SetHint($"登录失败: {ex.Message}");
            StatusIndicator.Text = "● 登录失败";
            StatusIndicator.Foreground = StatusFailed;
        }

        _isLoggingIn = false;
        LoginBtn.IsEnabled = true;
        SaveBtn.IsEnabled = true;
        UpdateConnectionStatus(_authService.IsLoggedIn);
    }

    private void Disconnect()
    {
        _authService.Stop();
        _isLoggingIn = false;
        LoginBtn.IsEnabled = true;
        SaveBtn.IsEnabled = true;
        UpdateConnectionStatus(false);
        SetHint("已断开连接");
        StatusIndicator.Text = "● 已离线";
        StatusIndicator.Foreground = StatusOffline;
    }

    private async void SaveBtn_Click(object? sender, RoutedEventArgs? e)
    {
        try
        {
            await SaveConfigAsync().ConfigureAwait(true);
            SetHint("配置已保存");
        }
        catch (Exception ex)
        {
            SetHint($"保存失败: {ex.Message}");
        }
    }

    private void AppendLog(string message)
    {
        _logBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        Dispatcher.UIThread.Post(() => SetHint(message), DispatcherPriority.Background);
    }

    private string? ValidateLoginConfig(ConfigModel config)
    {
        if (string.IsNullOrWhiteSpace(config.Username))
            return "请填写校园网账号";

        if (string.IsNullOrWhiteSpace(config.Password))
            return "请填写校园网密码";

        if (!IPAddress.TryParse(config.Server, out _))
            return "认证服务器配置错误";

        if (config.HostIp == "0.0.0.0" || config.Mac == "0x888888888888")
            return "本机网络信息未就绪";

        return null;
    }

    private void UpdateConfigStatus(ConfigModel config)
    {
        var validationError = ValidateLoginConfig(config);
        ConfigStatusText.Text = validationError == null ? "配置可用" : validationError;
        ConfigStatusText.Foreground = validationError == null ? StatusReady : StatusWarning;
    }

    private async Task CopyTextToClipboardAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null)
            throw new InvalidOperationException("当前环境不支持剪贴板");

        await clipboard.SetTextAsync(text).ConfigureAwait(true);
    }

    private string BuildNetworkDiagnostics()
    {
        var config = ReadConfigFromUI();
        return string.Join(Environment.NewLine,
            "Drcom NET 网络诊断",
            $"状态: {StatusIndicator.Text}",
            $"账号: {config.Username}",
            $"主机名: {config.HostName}",
            $"网卡: {_networkSnapshot.AdapterName}",
            $"IP: {_networkSnapshot.Ip}",
            $"掩码: {_networkSnapshot.SubnetMask}",
            $"MAC: {_networkSnapshot.Mac}",
            $"网关: {_networkSnapshot.Gateway}",
            $"DNS: {_networkSnapshot.Dns}");
    }

    private void SetHint(string text)
    {
        StatusHintText.Text = text;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        var minimizeToTray = MinimizeToTrayCheck.IsChecked ?? _config.MinimizeToTray;
        if (_trayIcon != null && minimizeToTray)
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
