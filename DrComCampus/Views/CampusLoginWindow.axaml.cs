using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using DrComCampus.Helpers;
using DrComCampus.Models;
using DrComCampus.Services;

namespace DrComCampus.Views;

public sealed partial class CampusLoginWindow : Window, IDisposable
{
    private const string AuthServer = "10.100.61.3";
    private const string DefaultDns = "10.10.10.10";

    // 状态机
    private enum ConnectionState { Disconnected, Connecting, Connected, Offline }
    private readonly record struct ProbeResult(bool Success, string Text);

    private readonly Task<AppConfiguration> _configLoadTask;
    private readonly ConfigurationService _configService = new();
    private readonly DrComAuthenticationService _authService = new();
    private AppConfiguration _config = new();
    private NetworkSnapshot _networkSnapshot = new("0.0.0.0", "0x888888888888", "--", "255.255.255.0", "--", "--", false);
    private bool _isLoggingIn;
    private bool _initialized;
    private bool _autoStartHandlerAttached;
    private TrayIcon? _trayIcon;
    private ConnectionState _state = ConnectionState.Disconnected;
    private ConnectionState? _renderedState;
    private LoginStep? _renderedLoginStep;
    private DispatcherTimer? _durationTimer;
    private DispatcherTimer? _autoTrayTimer;
    private CancellationTokenSource? _networkChangeCts;
    private readonly Action _showPrimaryWindowHandler;
    private bool _isClosed;

    private static readonly IBrush s_connectedBrush = new SolidColorBrush(Color.Parse("#34C759"));
    private static readonly IBrush s_disconnectedBrush = new SolidColorBrush(Color.Parse("#FF453A"));
    private static readonly IBrush s_connectingBrush = new SolidColorBrush(Color.Parse("#D97706"));
    private static readonly IBrush s_offlineBrush = new SolidColorBrush(Color.Parse("#64748B"));
    private static readonly IBrush s_readyBrush = new SolidColorBrush(Color.Parse("#16A34A"));
    private static readonly IBrush s_warningBrush = new SolidColorBrush(Color.Parse("#D97706"));
    private static readonly IBrush s_pendingStepBrush = new SolidColorBrush(Color.Parse("#CBD5E1"));
    private static readonly IBrush s_activeStepBrush = new SolidColorBrush(Color.Parse("#2563EB"));
    private static readonly IBrush s_completedStepBrush = new SolidColorBrush(Color.Parse("#34C759"));

    public CampusLoginWindow() : this(Task.FromResult(new AppConfiguration()))
    {
    }

    internal CampusLoginWindow(Task<AppConfiguration> configLoadTask)
    {
        _configLoadTask = configLoadTask;
        _showPrimaryWindowHandler = OnShowPrimaryWindowRequested;
        InitializeComponent();

        try
        {
            var icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "icon.ico");
            if (System.IO.File.Exists(icoPath))
            {
                Icon = new WindowIcon(icoPath);
            }
        }
        catch { /* ignore */ }

        SubscribeToAuthenticationEvents();
        SubscribeToNetworkChanges();
        SubscribeToInstanceActivation();

        // Enter 键快捷登录
        KeyDown += OnWindowKeyDown;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return && !_isLoggingIn && _state != ConnectionState.Connected)
        {
            _ = LoginAsync();
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        var config = await _configLoadTask.ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _config = config;
            ApplyConfigToUi();

            if (!_autoStartHandlerAttached)
            {
                _autoStartHandlerAttached = true;
                AutoStartToggle.IsCheckedChanged += (_, _) =>
                {
                    try { AutoStartService.SetEnabled(AutoStartToggle.IsChecked ?? false); }
                    catch { /* ignore */ }
                };
            }
        }, DispatcherPriority.Background);

        _ = RefreshNetworkInfoAsync();
        Dispatcher.UIThread.Post(() => _ = InitializeTrayIconAsync(), DispatcherPriority.Background);

        if (!Program.SuppressAutoLogin &&
            _config.AutoLogin &&
            !string.IsNullOrWhiteSpace(_config.Username) &&
            !string.IsNullOrWhiteSpace(_config.Password))
        {
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(800);
                await LoginAsync();
            }, DispatcherPriority.Background);
        }
    }

    #region 网络变化监听

    private void SubscribeToNetworkChanges()
    {
        try
        {
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        }
        catch { /* 某些环境不支持此 API */ }
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        NetworkInfoService.InvalidateCache();
        if (!e.IsAvailable || _isClosed || !_config.AutoReconnect)
        {
            return;
        }

        var next = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _networkChangeCts, next);
        previous?.Cancel();
        previous?.Dispose();
        _ = ReconnectAfterNetworkChangeAsync(next);
    }

    private async Task ReconnectAfterNetworkChangeAsync(CancellationTokenSource source)
    {
        try
        {
            await Task.Delay(1500, source.Token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_isClosed || _isLoggingIn || _state is ConnectionState.Connected or ConnectionState.Connecting ||
                    string.IsNullOrWhiteSpace(_config.Username) || string.IsNullOrWhiteSpace(_config.Password))
                {
                    return;
                }

                SetHint("网络已恢复，正在尝试重连...");
                _ = LoginAsync();
            }, DispatcherPriority.Background);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _networkChangeCts, null, source), source))
            {
                source.Dispose();
            }
        }
    }

    #endregion

    #region 多实例激活

    private void SubscribeToInstanceActivation()
    {
        Program.ShowPrimaryWindowRequested += _showPrimaryWindowHandler;
    }

    private void OnShowPrimaryWindowRequested() =>
        Dispatcher.UIThread.Post(ShowFromTray, DispatcherPriority.Background);

    #endregion

    private async Task RefreshNetworkInfoAsync()
    {
        await Task.Delay(150).ConfigureAwait(false);
        var network = await Task.Run(() => NetworkInfoService.GetNetworkSnapshot()).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() => ApplyNetworkInfo(network), DispatcherPriority.Background);
    }

    private async Task InitializeTrayIconAsync()
    {
        await Task.Delay(1000).ConfigureAwait(false);

        WindowIcon? icon = null;
        try
        {
            var icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "icon.ico");
            if (System.IO.File.Exists(icoPath))
            {
                icon = new WindowIcon(icoPath);
            }
        }
        catch { /* ignore */ }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_trayIcon != null)
            {
                return;
            }

            var menu = new NativeMenu();
            var showItem = new NativeMenuItem("显示窗口");
            showItem.Click += (_, _) => ShowFromTray();
            menu.Add(showItem);

            var loginItem = new NativeMenuItem("登录");
            loginItem.Click += async (_, _) => await LoginAsync();
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
                ToolTipText = "DrCom 校园网助手 - 未连接",
                Menu = menu,
                IsVisible = true
            };
            _trayIcon.Clicked += (_, _) => ShowFromTray();
        }, DispatcherPriority.Background);
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
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
        {
            desktop.Shutdown();
        }
        else
        {
            Environment.Exit(0);
        }
    }

    private void ApplyConfigToUi()
    {
        UsernameTextBox.Text = _config.Username;
        PasswordTextBox.Text = _config.Password;
        AutoStartToggle.IsChecked = AutoStartService.IsEnabled();
        AutoLoginToggle.IsChecked = _config.AutoLogin;
        AutoReconnectToggle.IsChecked = _config.AutoReconnect;
        MinimizeToTrayToggle.IsChecked = _config.MinimizeToTray;
        UpdateNetworkStatusDisplay();
        UpdateConfigStatus(ReadConfigFromUi());
    }

    private void UpdateNetworkStatusDisplay()
    {
        IpStatusText.Text = $"IP: {PrivacyHelper.MaskIp(_config.HostIp)}";
        MacStatusText.Text = $"MAC: {PrivacyHelper.MaskMac(_config.Mac)}";
        AdapterStatusText.Text = $"网卡: {_networkSnapshot.AdapterName}";
        GatewayStatusText.Text = $"网关: {_networkSnapshot.Gateway}";
        DnsStatusText.Text = $"DNS: {_networkSnapshot.DnsServer}";
        Ipv6StatusText.Text = $"IPv6: {PrivacyHelper.MaskIp(_networkSnapshot.Ipv6)}";
        Ipv6GatewayStatusText.Text = $"IPv6 网关: {_networkSnapshot.Ipv6Gateway}";
        Ipv6DnsStatusText.Text = $"IPv6 DNS: {_networkSnapshot.Ipv6Dns}";

        Ipv4CapabilityText.Text = _networkSnapshot.IsReady ? "IPv4  已就绪" : "IPv4  未就绪";
        Ipv4CapabilityText.Foreground = _networkSnapshot.IsReady ? s_readyBrush : s_warningBrush;
        Ipv6CapabilityText.Text = _networkSnapshot.HasGlobalIpv6 ? "IPv6  已获取" : "IPv6  未获取";
        Ipv6CapabilityText.Foreground = _networkSnapshot.HasGlobalIpv6 ? s_readyBrush : s_offlineBrush;
    }

    private void ApplyNetworkInfo(NetworkSnapshot snapshot)
    {
        _networkSnapshot = snapshot;

        if (snapshot.Ipv4Address != "0.0.0.0")
        {
            _config.HostIp = snapshot.Ipv4Address;
        }

        if (snapshot.MacAddress != "0x888888888888")
        {
            _config.Mac = snapshot.MacAddress;
        }

        if (snapshot.Gateway != "--")
        {
            _config.Gateway = snapshot.Gateway;
        }

        if (snapshot.DnsServer != "--")
        {
            _config.PrimaryDns = snapshot.DnsServer;
        }

        UpdateNetworkStatusDisplay();
        UpdateConfigStatus(ReadConfigFromUi());
    }

    private AppConfiguration ReadConfigFromUi()
    {
        return new AppConfiguration
        {
            Server = AuthServer,
            Username = (UsernameTextBox.Text ?? "").Trim(),
            Password = PasswordTextBox.Text ?? "",
            HostIp = string.IsNullOrWhiteSpace(_config.HostIp) ? "0.0.0.0" : _config.HostIp,
            Mac = string.IsNullOrWhiteSpace(_config.Mac) ? "0x888888888888" : _config.Mac,
            Gateway = string.IsNullOrWhiteSpace(_config.Gateway) ? "0.0.0.0" : _config.Gateway,
            HostName = Environment.MachineName,
            HostOs = NetworkInfoService.OsVersion,
            PrimaryDns = _networkSnapshot.DnsServer == "--" ? DefaultDns : _networkSnapshot.DnsServer,
            DhcpServer = "0.0.0.0",
            AutoLogin = AutoLoginToggle.IsChecked ?? false,
            AutoReconnect = AutoReconnectToggle.IsChecked ?? true,
            StartWithWindows = AutoStartToggle.IsChecked ?? false,
            MinimizeToTray = MinimizeToTrayToggle.IsChecked ?? true
        };
    }

    #region 认证事件 & 状态机

    private void SubscribeToAuthenticationEvents()
    {
        _authService.ConnectionStatusChanged += status =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                switch (status)
                {
                    case ConnectionStatus.Connecting:
                        SetState(ConnectionState.Connecting);
                        break;
                    case ConnectionStatus.Connected:
                        SetState(ConnectionState.Connected);
                        break;
                    case ConnectionStatus.Disconnected:
                        if (_state == ConnectionState.Connected || _state == ConnectionState.Connecting)
                        {
                            SetState(ConnectionState.Offline);
                        }

                        break;
                }
            }, DispatcherPriority.Background);
        };

        _authService.LoginStepChanged += step =>
        {
            Dispatcher.UIThread.Post(() => UpdateStepIndicator(step), DispatcherPriority.Background);
        };

        _authService.ErrorOccurred += msg =>
        {
            Dispatcher.UIThread.Post(() => SetHint(msg), DispatcherPriority.Background);
        };
    }

    private void SetState(ConnectionState newState)
    {
        _state = newState;
        if (_renderedState == newState)
        {
            return;
        }

        _renderedState = newState;
        switch (newState)
        {
            case ConnectionState.Disconnected:
                ConnectionStatusText.Text = "● 未连接";
                ConnectionStatusText.Foreground = s_disconnectedBrush;
                KeepAliveStatusText.Text = "保活未启动";
                KeepAliveStatusText.Foreground = s_offlineBrush;
                LoginButton.IsVisible = true;
                LogoutButton.IsVisible = false;
                StepIndicatorBorder.IsVisible = false;
                StopDurationTimer();
                break;

            case ConnectionState.Connecting:
                ConnectionStatusText.Text = "● 连接中";
                ConnectionStatusText.Foreground = s_connectingBrush;
                KeepAliveStatusText.Text = "正在认证...";
                KeepAliveStatusText.Foreground = s_warningBrush;
                LoginButton.IsVisible = false;
                LogoutButton.IsVisible = false;
                StepIndicatorBorder.IsVisible = true;
                UpdateStepIndicator(LoginStep.None);
                break;

            case ConnectionState.Connected:
                ConnectionStatusText.Text = "● 已连接";
                ConnectionStatusText.Foreground = s_connectedBrush;
                KeepAliveStatusText.Text = "保活运行中";
                KeepAliveStatusText.Foreground = s_readyBrush;
                LoginButton.IsVisible = false;
                LogoutButton.IsVisible = true;
                // 保持步骤指示器可见，显示全部完成状态
                UpdateStepIndicator(LoginStep.Connected);
                StartDurationTimer();
                ScheduleAutoTray();
                break;

            case ConnectionState.Offline:
                ConnectionStatusText.Text = "● 已离线";
                ConnectionStatusText.Foreground = s_offlineBrush;
                KeepAliveStatusText.Text = "保活已停止";
                KeepAliveStatusText.Foreground = s_offlineBrush;
                LoginButton.IsVisible = true;
                LogoutButton.IsVisible = false;
                StepIndicatorBorder.IsVisible = false;
                StopDurationTimer();
                break;
        }

        _trayIcon?.ToolTipText = newState switch
        {
            ConnectionState.Connected => "DrCom 校园网助手 - 已连接",
            ConnectionState.Connecting => "DrCom 校园网助手 - 连接中",
            _ => "DrCom 校园网助手 - 未连接"
        };
    }

    private void UpdateStepIndicator(LoginStep step)
    {
        if (_renderedLoginStep == step)
        {
            return;
        }

        _renderedLoginStep = step;

        var pendingBrush = s_pendingStepBrush;
        var activeBrush = s_activeStepBrush;
        var doneBrush = s_completedStepBrush;

        // 默认全部 pending
        SetStepVisual(Step1Dot, Step1Text, pendingBrush);
        SetStepVisual(Step2Dot, Step2Text, pendingBrush);
        SetStepVisual(Step3Dot, Step3Text, pendingBrush);

        switch (step)
        {
            case LoginStep.Challenge:
                SetStepVisual(Step1Dot, Step1Text, activeBrush);
                break;
            case LoginStep.Authenticating:
                SetStepVisual(Step1Dot, Step1Text, doneBrush);
                SetStepVisual(Step2Dot, Step2Text, activeBrush);
                break;
            case LoginStep.KeepAlive:
            case LoginStep.Connected:
                SetStepVisual(Step1Dot, Step1Text, doneBrush);
                SetStepVisual(Step2Dot, Step2Text, doneBrush);
                SetStepVisual(Step3Dot, Step3Text, step == LoginStep.Connected ? doneBrush : activeBrush);
                break;
        }
    }

    private static void SetStepVisual(Border dot, TextBlock text, IBrush brush)
    {
        dot.Background = brush;
        text.Foreground = brush;
    }

    #endregion

    #region 连接时长计时器

    private void StartDurationTimer()
    {
        _durationTimer?.Stop();
        ConnectionDurationText.IsVisible = true;
        UpdateDurationText();

        _durationTimer ??= new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background,
            (_, _) => UpdateDurationText());
        _durationTimer.Start();
    }

    private void StopDurationTimer()
    {
        _durationTimer?.Stop();
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
        {
            ConnectionDurationText.Text = $"已连接 {elapsed.Hours} 小时 {elapsed.Minutes} 分钟";
        }
        else
        {
            ConnectionDurationText.Text = $"已连接 {elapsed.Minutes} 分钟";
        }
    }

    #endregion

    #region 登录成功后自动最小化到托盘

    private void ScheduleAutoTray()
    {
        _autoTrayTimer?.Stop();
        _autoTrayTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, (_, _) =>
        {
            _autoTrayTimer?.Stop();

            var minimizeToTray = MinimizeToTrayToggle.IsChecked ?? _config.MinimizeToTray;
            if (minimizeToTray && _trayIcon != null)
            {
                Hide();
                _trayIcon?.ToolTipText = "DrCom 校园网助手 - 登录成功，已在托盘运行";
            }
        });
        _autoTrayTimer.Start();
    }

    #endregion

    private async void OnLoginButtonClick(object? sender, RoutedEventArgs e) => await LoginAsync();

    private void OnLogoutButtonClick(object? sender, RoutedEventArgs e) => Disconnect();

    private void OnTogglePasswordButtonClick(object? sender, RoutedEventArgs e)
    {
        if (PasswordTextBox.PasswordChar == '\0')
        {
            PasswordTextBox.PasswordChar = '●';
            TogglePasswordButton.Content = "显示";
        }
        else
        {
            PasswordTextBox.PasswordChar = '\0';
            TogglePasswordButton.Content = "隐藏";
        }
    }

    private async void OnRefreshNetworkButtonClick(object? sender, RoutedEventArgs e)
    {
        RefreshNetworkButton.IsEnabled = false;
        RefreshNetworkButton.Content = "刷新中...";
        try
        {
            SetHint("正在刷新本机网络...");
            var network = await Task.Run(() => NetworkInfoService.GetNetworkSnapshot(true)).ConfigureAwait(true);
            ApplyNetworkInfo(network);
            SetHint(network.IsReady
                ? network.HasGlobalIpv6 ? "双栈网络信息已刷新" : "IPv4 已就绪，暂未获取全局 IPv6"
                : "未找到可用 IPv4/MAC");
        }
        catch (Exception ex)
        {
            SetHint($"刷新失败: {ex.Message}");
        }
        finally
        {
            RefreshNetworkButton.Content = "刷新";
            RefreshNetworkButton.IsEnabled = true;
        }
    }

    private async void OnEditNetworkButtonClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new StaticIpv4SettingsWindow(_networkSnapshot);
        var applied = await dialog.ShowDialog<bool>(this);
        if (applied)
        {
            SetHint("静态网络设置已提交，正在刷新...");
            await RefreshNetworkInfoAsync();
        }
    }

    private async void OnInternalPingButtonClick(object? sender, RoutedEventArgs e)
    {
        await RunPingAsync("jlu.edu.cn", InternalPingButton, InternalStatusText);
    }

    private async void OnExternalPingButtonClick(object? sender, RoutedEventArgs e)
    {
        await RunPingAsync("www.baidu.com", ExternalPingButton, ExternalStatusText);
    }

    private static async Task RunPingAsync(string host, Button button, TextBlock target)
    {
        button.IsEnabled = false;
        target.Text = "检测中...";
        target.Foreground = s_offlineBrush;

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 2500).ConfigureAwait(true);
            if (reply.Status == IPStatus.Success)
            {
                target.Text = $"成功 {reply.RoundtripTime}ms";
                target.Foreground = s_readyBrush;
            }
            else
            {
                target.Text = $"失败 {reply.Status}";
                target.Foreground = s_warningBrush;
            }
        }
        catch (Exception ex)
        {
            target.Text = $"失败 {ex.Message}";
            target.Foreground = s_warningBrush;
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async void OnDualStackTestButtonClick(object? sender, RoutedEventArgs e)
    {
        DualStackTestButton.IsEnabled = false;
        Ipv4ProbeText.Text = "IPv4 检测中...";
        Ipv6ProbeText.Text = _networkSnapshot.HasGlobalIpv6 ? "IPv6 检测中..." : "IPv6 无全局地址";
        Ipv4ProbeText.Foreground = s_offlineBrush;
        Ipv6ProbeText.Foreground = s_offlineBrush;

        try
        {
            var ipv4Target = _networkSnapshot.Gateway == "--" ? "223.5.5.5" : _networkSnapshot.Gateway;
            var ipv4Task = ProbeAsync(ipv4Target);
            var ipv6Task = _networkSnapshot.HasGlobalIpv6
                ? ProbeAsync(_networkSnapshot.Ipv6Gateway == "--" ? "2400:3200::1" : _networkSnapshot.Ipv6Gateway)
                : Task.FromResult(new ProbeResult(false, "IPv6 未分配"));

            await Task.WhenAll(ipv4Task, ipv6Task).ConfigureAwait(true);
            var ipv4 = await ipv4Task;
            var ipv6 = await ipv6Task;
            SetProbeVisual(Ipv4ProbeText, "IPv4", ipv4);
            SetProbeVisual(Ipv6ProbeText, "IPv6", ipv6);
            SetHint(ipv4.Success && ipv6.Success
                ? "双栈连通正常"
                : ipv4.Success ? "IPv4 正常，IPv6 尚不可达" : "IPv4 认证网络尚不可达");
        }
        finally
        {
            DualStackTestButton.IsEnabled = true;
        }
    }

    private static async Task<ProbeResult> ProbeAsync(string target)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(target, 2500).ConfigureAwait(false);
            return reply.Status == IPStatus.Success
                ? new ProbeResult(true, $"可达 {reply.RoundtripTime}ms")
                : new ProbeResult(false, $"不可达 {reply.Status}");
        }
        catch (Exception ex)
        {
            return new ProbeResult(false, $"失败 {ex.Message}");
        }
    }

    private static void SetProbeVisual(TextBlock target, string protocol, ProbeResult result)
    {
        target.Text = $"{protocol} {result.Text}";
        target.Foreground = result.Success ? s_readyBrush : s_warningBrush;
    }

    private async void OnCopyDiagnosticsButtonClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await CopyTextToClipboardAsync(BuildNetworkDiagnostics());
            SetHint("双栈诊断信息已复制");
        }
        catch (Exception ex)
        {
            SetHint($"复制失败: {ex.Message}");
        }
    }

    private async Task SaveConfigAsync()
    {
        var config = ReadConfigFromUi();
        await _configService.SaveAsync(config);
        _config = config;
        UpdateConfigStatus(config);
    }

    private async Task LoginAsync()
    {
        if (_isLoggingIn)
        {
            return;
        }

        try
        {
            await SaveConfigAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SetHint($"保存配置失败: {ex.Message}");
        }

        var config = ReadConfigFromUi();
        if (config.HostIp == "0.0.0.0" || config.Mac == "0x888888888888")
        {
            SetHint("正在检测本机网络...");
            var network = await Task.Run(() => NetworkInfoService.GetNetworkSnapshot()).ConfigureAwait(true);
            if (network.Ipv4Address != "0.0.0.0")
            {
                config.HostIp = network.Ipv4Address;
            }

            if (network.MacAddress != "0x888888888888")
            {
                config.Mac = network.MacAddress;
            }

            _config.HostIp = config.HostIp;
            _config.Mac = config.Mac;
            ApplyNetworkInfo(network);
        }

        var validationError = ValidateLoginConfig(config);
        if (validationError != null)
        {
            SetHint(validationError);
            SetState(ConnectionState.Disconnected);
            ConnectionStatusText.Text = "● 待完善";
            ConnectionStatusText.Foreground = s_warningBrush;
            UpdateConfigStatus(config);
            return;
        }

        _authService.UpdateConfig(config.Server, config.Username, config.Password,
            config.HostIp, config.Mac, config.HostName,
            config.PrimaryDns, config.DhcpServer);

        _isLoggingIn = true;
        LoginButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        SetState(ConnectionState.Connecting);
        SetHint("正在连接认证服务器...");

        try
        {
            var loginSucceeded = await _authService.StartLoginAsync().ConfigureAwait(true);

            if (loginSucceeded && _authService.IsLoggedIn)
            {
                SetHint("登录成功");
                // SetState(ConnectionState.Connected) 由 ConnectionStatusChanged 事件驱动
            }
            else
            {
                SetHint("登录失败，请检查账号密码");
                SetState(ConnectionState.Disconnected);
            }
        }
        catch (AuthServerUnreachableException)
        {
            SetHint("认证服务器不可达，请检查网络连接");
            SetState(ConnectionState.Disconnected);
        }
        catch (TimeoutException)
        {
            SetHint("连接超时，请稍后重试");
            SetState(ConnectionState.Disconnected);
        }
        catch (Exception ex)
        {
            SetHint($"登录失败: {ex.Message}");
            SetState(ConnectionState.Disconnected);
        }

        _isLoggingIn = false;
        LoginButton.IsEnabled = true;
        SaveButton.IsEnabled = true;
    }

    private void Disconnect()
    {
        _authService.Stop();
        _isLoggingIn = false;
        LoginButton.IsEnabled = true;
        SaveButton.IsEnabled = true;
        SetState(ConnectionState.Offline);
        SetHint("已断开连接");
    }

    private async void OnSaveButtonClick(object? sender, RoutedEventArgs? e)
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

    private static string? ValidateLoginConfig(AppConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.Username))
        {
            return "请填写校园网账号";
        }

        if (!IsAscii(config.Username) || config.Username.Length > 36)
        {
            return "校园网账号须为不超过 36 位的 ASCII 字符";
        }

        if (string.IsNullOrWhiteSpace(config.Password))
        {
            return "请填写校园网密码";
        }

        // Dr.COM 的 ror 字段按 16 字节 MD5 摘要逐字节计算。
        if (!IsAscii(config.Password) || config.Password.Length > 16)
        {
            return "校园网密码须为不超过 16 位的 ASCII 字符";
        }

        if (!IPAddress.TryParse(config.Server, out _))
        {
            return "认证服务器配置错误";
        }

        if (config.HostIp == "0.0.0.0" || config.Mac == "0x888888888888")
        {
            return "本机网络信息未就绪";
        }

        return null;
    }

    private static bool IsAscii(string value)
    {
        foreach (var character in value)
        {
            if (character > 0x7F)
            {
                return false;
            }
        }

        return true;
    }

    private void UpdateConfigStatus(AppConfiguration config)
    {
        var validationError = ValidateLoginConfig(config);
        ConfigStatusText.Text = validationError ?? "配置可用";
        ConfigStatusText.Foreground = validationError == null ? s_readyBrush : s_warningBrush;
    }

    private async Task CopyTextToClipboardAsync(string text)
    {
        var clipboard = (TopLevel.GetTopLevel(this)?.Clipboard) ?? throw new InvalidOperationException("当前环境不支持剪贴板");
        await clipboard.SetTextAsync(text).ConfigureAwait(true);
    }

    private string BuildNetworkDiagnostics()
    {
        var config = ReadConfigFromUi();
        return string.Join(Environment.NewLine,
            "DrCom 校园网助手网络诊断",
            $"状态: {ConnectionStatusText.Text}",
            $"账号已配置: {!string.IsNullOrWhiteSpace(config.Username)}",
            $"主机名: {config.HostName}",
            "认证通道: IPv4 UDP/61440",
            $"网卡: {_networkSnapshot.AdapterName}",
            $"IPv4: {_networkSnapshot.Ipv4Address}",
            $"IPv4 掩码: {_networkSnapshot.SubnetMask}",
            $"MAC: {_networkSnapshot.MacAddress}",
            $"IPv4 网关: {_networkSnapshot.Gateway}",
            $"IPv4 DNS: {_networkSnapshot.DnsServer}",
            $"IPv6: {_networkSnapshot.Ipv6}",
            $"IPv6 网关: {_networkSnapshot.Ipv6Gateway}",
            $"IPv6 DNS: {_networkSnapshot.Ipv6Dns}");
    }

    private void SetHint(string text)
    {
        if (StatusHintText.Text != text)
        {
            StatusHintText.Text = text;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        var minimizeToTray = MinimizeToTrayToggle.IsChecked ?? _config.MinimizeToTray;
        if (_trayIcon != null && minimizeToTray)
        {
            e.Cancel = true;
            Hide();
            _trayIcon?.ToolTipText = "DrCom 校园网助手 - 已最小化到托盘";
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    public void Dispose()
    {
        if (_isClosed)
        {
            return;
        }

        _isClosed = true;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        Program.ShowPrimaryWindowRequested -= _showPrimaryWindowHandler;
        var networkChangeCts = Interlocked.Exchange(ref _networkChangeCts, null);
        networkChangeCts?.Cancel();
        networkChangeCts?.Dispose();
        _durationTimer?.Stop();
        _autoTrayTimer?.Stop();
        _authService.Stop();
        try { _authService.Dispose(); } catch { }
        _trayIcon?.Dispose();
        _trayIcon = null;
        GC.SuppressFinalize(this);
    }
}
