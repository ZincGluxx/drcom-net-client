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

    // 状态机
    private enum ConnState { Disconnected, Connecting, Connected, Offline }

    private readonly Task<ConfigModel> _configLoadTask;
    private readonly ConfigService _configService = new();
    private readonly AutoStartService _autoStartService = new();
    private readonly DrcomAuthService _authService = new();
    private ConfigModel _config = new();
    private NetworkSnapshot _networkSnapshot = new("0.0.0.0", "0x888888888888", "--", "255.255.255.0", "--", "--", false);
    private bool _isLoggingIn;
    private bool _initialized;
    private bool _autoStartHandlerAttached;
    private TrayIcon? _trayIcon;
    private ConnState _state = ConnState.Disconnected;
    private DispatcherTimer? _durationTimer;
    private DispatcherTimer? _autoTrayTimer;
    private readonly StringBuilder _logBuilder = new();
    private const int MaxLogLength = 4096;

    private static readonly IBrush BrushConnected = new SolidColorBrush(Color.Parse("#34C759"));
    private static readonly IBrush BrushDisconnected = new SolidColorBrush(Color.Parse("#FF453A"));
    private static readonly IBrush BrushConnecting = new SolidColorBrush(Color.Parse("#D97706"));
    private static readonly IBrush BrushOffline = new SolidColorBrush(Color.Parse("#64748B"));
    private static readonly IBrush BrushReady = new SolidColorBrush(Color.Parse("#16A34A"));
    private static readonly IBrush BrushWarning = new SolidColorBrush(Color.Parse("#D97706"));
    private static readonly IBrush BrushStepPending = new SolidColorBrush(Color.Parse("#CBD5E1"));
    private static readonly IBrush BrushStepActive = new SolidColorBrush(Color.Parse("#2563EB"));
    private static readonly IBrush BrushStepDone = new SolidColorBrush(Color.Parse("#34C759"));

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
        SetupNetworkChangeListener();
        SetupMultiInstanceActivation();

        // Enter 键快捷登录
        KeyDown += OnWindowKeyDown;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return && !_isLoggingIn && _state != ConnState.Connected)
            _ = DoLogin();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_initialized) return;
        _initialized = true;

        // 淡入动画（AXAML 中已配置 DoubleTransition，此处触发）
        Opacity = 1;

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

    #region 网络变化监听

    private void SetupNetworkChangeListener()
    {
        try
        {
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        }
        catch { /* 某些环境不支持此 API */ }
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable && !_isLoggingIn && _state != ConnState.Connected && _state != ConnState.Connecting)
        {
            Dispatcher.UIThread.Post(async () =>
            {
                if (!string.IsNullOrWhiteSpace(_config.Username) &&
                    !string.IsNullOrWhiteSpace(_config.Password))
                {
                    SetHint("网络已恢复，正在尝试重连...");
                    await Task.Delay(1500);
                    await DoLogin();
                }
            });
        }
    }

    #endregion

    #region 多实例激活

    private void SetupMultiInstanceActivation()
    {
        Program.ShowMainWindowRequested += () =>
        {
            Dispatcher.UIThread.Post(() => ShowFromTray());
        };
    }

    #endregion

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
            copyDiagnosticsItem.Click += async (_, _) =>
            {
                try { await CopyTextToClipboardAsync(BuildNetworkDiagnostics()); }
                catch { /* 剪贴板不可用时静默忽略 */ }
            };
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
        _durationTimer?.Stop();
        _autoTrayTimer?.Stop();
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

    #region 认证事件 & 状态机

    private void SetupAuthEvents()
    {
        _authService.OnLog += (msg, level) =>
        {
            _logBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
            if (_logBuilder.Length > MaxLogLength)
                _logBuilder.Remove(0, _logBuilder.Length - MaxLogLength);
            // UI 提示区仅显示 Error 级别日志
            if (level == LogLevel.Error)
                Dispatcher.UIThread.Post(() => SetHint(msg), DispatcherPriority.Background);
        };

        _authService.OnStatusChanged += status =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                switch (status)
                {
                    case ConnectionStatus.Connecting:
                        SetState(ConnState.Connecting);
                        break;
                    case ConnectionStatus.Connected:
                        SetState(ConnState.Connected);
                        break;
                    case ConnectionStatus.Disconnected:
                        if (_state == ConnState.Connected || _state == ConnState.Connecting)
                            SetState(ConnState.Offline);
                        break;
                }
            }, DispatcherPriority.Background);
        };

        _authService.OnLoginStepChanged += step =>
        {
            Dispatcher.UIThread.Post(() => UpdateStepIndicator(step), DispatcherPriority.Background);
        };

        _authService.OnError += msg =>
        {
            Dispatcher.UIThread.Post(() => SetHint(msg), DispatcherPriority.Background);
        };
    }

    private void SetState(ConnState newState)
    {
        _state = newState;
        switch (newState)
        {
            case ConnState.Disconnected:
                StatusIndicator.Text = "● 未连接";
                StatusIndicator.Foreground = BrushDisconnected;
                KeepAliveStatusText.Text = "保活未启动";
                KeepAliveStatusText.Foreground = BrushOffline;
                LoginBtn.IsVisible = true;
                LogoutBtn.IsVisible = false;
                StepIndicatorBorder.IsVisible = false;
                StopDurationTimer();
                break;

            case ConnState.Connecting:
                StatusIndicator.Text = "● 连接中";
                StatusIndicator.Foreground = BrushConnecting;
                KeepAliveStatusText.Text = "正在认证...";
                KeepAliveStatusText.Foreground = BrushWarning;
                LoginBtn.IsVisible = false;
                LogoutBtn.IsVisible = false;
                StepIndicatorBorder.IsVisible = true;
                UpdateStepIndicator(LoginStep.None);
                break;

            case ConnState.Connected:
                StatusIndicator.Text = "● 已连接";
                StatusIndicator.Foreground = BrushConnected;
                KeepAliveStatusText.Text = "保活运行中";
                KeepAliveStatusText.Foreground = BrushReady;
                LoginBtn.IsVisible = false;
                LogoutBtn.IsVisible = true;
                // 保持步骤指示器可见，显示全部完成状态
                UpdateStepIndicator(LoginStep.Connected);
                StartDurationTimer();
                ScheduleAutoTray();
                break;

            case ConnState.Offline:
                StatusIndicator.Text = "● 已离线";
                StatusIndicator.Foreground = BrushOffline;
                KeepAliveStatusText.Text = "保活已停止";
                KeepAliveStatusText.Foreground = BrushOffline;
                LoginBtn.IsVisible = true;
                LogoutBtn.IsVisible = false;
                StepIndicatorBorder.IsVisible = false;
                StopDurationTimer();
                break;
        }

        if (_trayIcon != null)
        {
            _trayIcon.ToolTipText = newState switch
            {
                ConnState.Connected => "校园网登录 - 已连接",
                ConnState.Connecting => "校园网登录 - 连接中",
                _ => "校园网登录 - 未连接"
            };
        }
    }

    private void UpdateStepIndicator(LoginStep step)
    {
        var pendingBrush = BrushStepPending;
        var activeBrush = BrushStepActive;
        var doneBrush = BrushStepDone;
        var pendingFg = Color.Parse("#94A3B8");
        var activeFg = Color.Parse("#2563EB");
        var doneFg = Color.Parse("#34C759");

        // 默认全部 pending
        SetStepVisual(Step1Dot, Step1Text, pendingBrush, pendingFg);
        SetStepVisual(Step2Dot, Step2Text, pendingBrush, pendingFg);
        SetStepVisual(Step3Dot, Step3Text, pendingBrush, pendingFg);

        switch (step)
        {
            case LoginStep.Challenge:
                SetStepVisual(Step1Dot, Step1Text, activeBrush, activeFg);
                break;
            case LoginStep.Authenticating:
                SetStepVisual(Step1Dot, Step1Text, doneBrush, doneFg);
                SetStepVisual(Step2Dot, Step2Text, activeBrush, activeFg);
                break;
            case LoginStep.KeepAlive:
            case LoginStep.Connected:
                SetStepVisual(Step1Dot, Step1Text, doneBrush, doneFg);
                SetStepVisual(Step2Dot, Step2Text, doneBrush, doneFg);
                SetStepVisual(Step3Dot, Step3Text, step == LoginStep.Connected ? doneBrush : activeBrush,
                    step == LoginStep.Connected ? doneFg : activeFg);
                break;
        }
    }

    private static void SetStepVisual(Border dot, TextBlock text, IBrush brush, Color fg)
    {
        dot.Background = brush;
        text.Foreground = brush;
    }

    #endregion

    #region 连接时长计时器

    private void StartDurationTimer()
    {
        StopDurationTimer();
        ConnectionDurationText.IsVisible = true;
        UpdateDurationText();

        _durationTimer = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background,
            (_, _) => UpdateDurationText());
        _durationTimer.Start();
    }

    private void StopDurationTimer()
    {
        _durationTimer?.Stop();
        _durationTimer = null;
        ConnectionDurationText.IsVisible = false;
    }

    private void UpdateDurationText()
    {
        var since = _authService.ConnectedSince;
        if (since == null)
        {
            ConnectionDurationText.Text = "";
            return;
        }

        var elapsed = DateTime.Now - since.Value;
        if (elapsed.TotalHours >= 1)
            ConnectionDurationText.Text = $"已连接 {elapsed.Hours} 小时 {elapsed.Minutes} 分钟";
        else
            ConnectionDurationText.Text = $"已连接 {elapsed.Minutes} 分钟";
    }

    #endregion

    #region 登录成功后自动最小化到托盘

    private void ScheduleAutoTray()
    {
        _autoTrayTimer?.Stop();
        _autoTrayTimer = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, (_, _) =>
        {
            _autoTrayTimer?.Stop();
            _autoTrayTimer = null;

            var minimizeToTray = MinimizeToTrayCheck.IsChecked ?? _config.MinimizeToTray;
            if (minimizeToTray && _trayIcon != null)
            {
                Hide();
                if (_trayIcon != null)
                    _trayIcon.ToolTipText = "校园网登录 - 登录成功，已在托盘运行";
            }
        });
        _autoTrayTimer.Start();
    }

    #endregion

    private async void LoginBtn_Click(object? sender, RoutedEventArgs e) => await DoLogin();

    private void LogoutBtn_Click(object? sender, RoutedEventArgs e) => Disconnect();

    private void TogglePasswordBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (PasswordField.PasswordChar == '\0')
        {
            PasswordField.PasswordChar = '●';
            TogglePasswordBtn.Content = "显示";
        }
        else
        {
            PasswordField.PasswordChar = '\0';
            TogglePasswordBtn.Content = "隐藏";
        }
    }

    private async void RefreshNetworkBtn_Click(object? sender, RoutedEventArgs e)
    {
        RefreshNetworkBtn.IsEnabled = false;
        RefreshNetworkBtn.Content = "刷新中...";
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
            RefreshNetworkBtn.Content = "刷新";
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
        target.Foreground = BrushOffline;

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 2500).ConfigureAwait(true);
            if (reply.Status == IPStatus.Success)
            {
                target.Text = $"成功 {reply.RoundtripTime}ms";
                target.Foreground = BrushReady;
            }
            else
            {
                target.Text = $"失败 {reply.Status}";
                target.Foreground = BrushWarning;
            }
        }
        catch (Exception ex)
        {
            target.Text = $"失败 {ex.Message}";
            target.Foreground = BrushWarning;
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async Task SaveConfigAsync()
    {
        var config = ReadConfigFromUI();
        await _configService.SaveAsync(config).ConfigureAwait(false);
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
            SetState(ConnState.Disconnected);
            StatusIndicator.Text = "● 待完善";
            StatusIndicator.Foreground = BrushWarning;
            UpdateConfigStatus(config);
            return;
        }

        _authService.UpdateConfig(config.Server, config.Username, config.Password,
            config.HostIp, config.Mac, config.HostName, config.HostOs,
            config.PrimaryDns, config.DhcpServer);

        _isLoggingIn = true;
        LoginBtn.IsEnabled = false;
        SaveBtn.IsEnabled = false;
        SetState(ConnState.Connecting);
        SetHint("正在连接认证服务器...");

        try
        {
            var loginSucceeded = await _authService.StartLoginAsync().ConfigureAwait(true);

            if (loginSucceeded && _authService.IsLoggedIn)
            {
                SetHint("登录成功");
                // SetState(ConnState.Connected) 由 OnStatusChanged 事件驱动
            }
            else
            {
                SetHint("登录失败，请检查账号密码");
                SetState(ConnState.Disconnected);
            }
        }
        catch (AuthServerUnreachableException)
        {
            SetHint("认证服务器不可达，请检查网络连接");
            SetState(ConnState.Disconnected);
        }
        catch (TimeoutException)
        {
            SetHint("连接超时，请稍后重试");
            SetState(ConnState.Disconnected);
        }
        catch (Exception ex)
        {
            SetHint($"登录失败: {ex.Message}");
            SetState(ConnState.Disconnected);
        }

        _isLoggingIn = false;
        LoginBtn.IsEnabled = true;
        SaveBtn.IsEnabled = true;
    }

    private void Disconnect()
    {
        _authService.Stop();
        _isLoggingIn = false;
        LoginBtn.IsEnabled = true;
        SaveBtn.IsEnabled = true;
        SetState(ConnState.Offline);
        SetHint("已断开连接");
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
        ConfigStatusText.Foreground = validationError == null ? BrushReady : BrushWarning;
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
            if (_trayIcon != null)
                _trayIcon.ToolTipText = "校园网登录 - 已最小化到托盘";
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _durationTimer?.Stop();
        _autoTrayTimer?.Stop();
        _authService.Stop();
        try { _authService.Dispose(); } catch { }
        _trayIcon?.Dispose();
        base.OnClosed(e);
    }
}
