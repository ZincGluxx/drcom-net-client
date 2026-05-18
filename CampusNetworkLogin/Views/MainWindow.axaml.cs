using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.Media;
using System;
using System.Threading.Tasks;
using CampusNetworkLogin.Models;
using CampusNetworkLogin.Services;
using System.Collections.Generic;
using System.IO;

namespace CampusNetworkLogin.Views;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService = new();
    private readonly AutoStartService _autoStartService = new();
    private readonly DrcomAuthService _authService = new();
    private ConfigModel _config = new();
    private bool _isLoggingIn = false;
    private readonly List<string> _logLines = new();

    // 托盘图标
    private TrayIcon? _trayIcon;

    // 分页按钮默认颜色
    private static readonly SolidColorBrush TabActiveBg = new(Color.Parse("#EFF6FF"));
    private static readonly SolidColorBrush TabActiveFg = new(Color.Parse("#2563EB"));
    private static readonly SolidColorBrush TabInactiveBg = new(Colors.Transparent);
    private static readonly SolidColorBrush TabInactiveFg = new(Color.Parse("#64748B"));

        public MainWindow()
    {
        InitializeComponent();

        InitializeTitleBarDrag();
        SetupAuthEvents();

        // 延迟初始化：避免窗口打开卡顿
        Dispatcher.UIThread.Post(async () =>
        {
            LoadConfig();
            ApplyConfigToUI();
            AutoDetectNetworkInfo();
            CreateTrayIcon();

            if (_config.AutoLogin && !string.IsNullOrEmpty(_config.Username) && !string.IsNullOrEmpty(_config.Password))
            {
                await Task.Delay(500);
                await DoLogin();
            }
        }, DispatcherPriority.Background);
    }

            private void InitializeTitleBarDrag()
    {
        var titleBar = this.GetControl<Border>("TitleBar");
        if (titleBar != null)
        {
            titleBar.PointerPressed += (s, e) => BeginMoveDrag(e);
        }
    }

    // ========== 分页切换 ==========
    private void SwitchToPage(Grid page, Button activeBtn)
    {
        PageLogin.IsVisible = false;
        PageConfig.IsVisible = false;
        PageLog.IsVisible = false;
        page.IsVisible = true;

        // 重置所有标签样式
        TabLogin.Background = TabInactiveBg;
        TabLogin.Foreground = TabInactiveFg;
        TabConfig.Background = TabInactiveBg;
        TabConfig.Foreground = TabInactiveFg;
        TabLog.Background = TabInactiveBg;
        TabLog.Foreground = TabInactiveFg;

        // 高亮当前标签
        activeBtn.Background = TabActiveBg;
        activeBtn.Foreground = TabActiveFg;
    }

    private void TabLogin_Click(object? sender, RoutedEventArgs e) => SwitchToPage(PageLogin, TabLogin);
    private void TabConfig_Click(object? sender, RoutedEventArgs e) => SwitchToPage(PageConfig, TabConfig);
    private void TabLog_Click(object? sender, RoutedEventArgs e) => SwitchToPage(PageLog, TabLog);

                /// <summary>
        /// 创建系统托盘图标
        /// </summary>
        private void CreateTrayIcon()
        {
            WindowIcon? icon = null;
            try
            {
                                // 从输出目录加载图标
                                var baseDir = System.AppContext.BaseDirectory;
                                var icoPath = System.IO.Path.Combine(baseDir, "Resources", "icon.ico");
                                // 回退到编译输出目录
                                if (!System.IO.File.Exists(icoPath))
                                {
                                    icoPath = System.IO.Path.Combine(baseDir, "icon.ico");
                                    if (!System.IO.File.Exists(icoPath))
                                        icoPath = null;
                                }
                                if (icoPath != null && System.IO.File.Exists(icoPath))
                                    icon = new WindowIcon(icoPath);
            }
            catch
            {
                // 托盘图标加载失败不影响主程序运行
            }

            var menu = new NativeMenu();

            var showItem = new NativeMenuItem("显示窗口");
            showItem.Click += (s, e) => ShowWindow();
            menu.Add(showItem);

            var loginItem = new NativeMenuItem("登录");
            loginItem.Click += async (s, e) => await DoLogin();
            menu.Add(loginItem);

            var logoutItem = new NativeMenuItem("断开连接");
            logoutItem.Click += (s, e) => Disconnect();
            menu.Add(logoutItem);

            menu.Add(new NativeMenuItemSeparator());

            var quitItem = new NativeMenuItem("退出程序");
            quitItem.Click += (s, e) => QuitApp();
            menu.Add(quitItem);

            _trayIcon = new TrayIcon
            {
                Icon = icon,
                ToolTipText = "校园网登录 - 未连接",
                Menu = menu,
                IsVisible = true
            };

            // 点击托盘图标显示窗口
            _trayIcon.Clicked += (s, e) => ShowWindow();
        }

    /// <summary>
    /// 显示窗口（从托盘恢复）
    /// </summary>
    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    /// <summary>
    /// 真正退出程序
    /// </summary>
    private void QuitApp()
    {
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

    private void LoadConfig() => _config = _configService.Load();

    private void ApplyConfigToUI()
    {
        ConfigUsernameField.Text = _config.Username;
        UsernameField.Text = _config.Username;
        PasswordField.Text = _config.Password;
        ServerField.Text = _config.Server;

        if (!string.IsNullOrEmpty(_config.HostIp) && _config.HostIp != "0.0.0.0")
            IpStatusText.Text = $"IP: {_config.HostIp}";
        if (!string.IsNullOrEmpty(_config.Mac) && _config.Mac != "0x888888888888")
            MacStatusText.Text = $"MAC: {_config.Mac}";

        AutoStartCheck.IsChecked = _config.StartWithWindows;
        AutoLoginCheck.IsChecked = _config.AutoLogin;

        AutoStartCheck.IsCheckedChanged += (s, e) => {
            try { _autoStartService.SetEnabled(AutoStartCheck.IsChecked ?? false); } catch { }
        };
    }

    private void AutoDetectNetworkInfo()
    {
        if (_config.HostIp == "0.0.0.0" || string.IsNullOrEmpty(_config.HostIp))
        {
            var ip = NetworkInfoService.GetLocalIpAddress();
            if (ip != "0.0.0.0")
                IpStatusText.Text = $"IP: {ip}";
        }
        if (_config.Mac == "0x888888888888")
        {
            var mac = NetworkInfoService.GetMacAddress();
            if (mac != "0x888888888888")
                MacStatusText.Text = $"MAC: {mac}";
        }
    }

    private ConfigModel ReadConfigFromUI()
    {
        return new ConfigModel
        {
            Server = string.IsNullOrEmpty(ServerField.Text) ? "10.100.61.3" : ServerField.Text,
            Username = ConfigUsernameField.Text ?? "",
            Password = PasswordField.Text ?? "",
            HostIp = NetworkInfoService.GetLocalIpAddress(),
            Mac = NetworkInfoService.GetMacAddress(),
            HostName = Environment.MachineName,
            HostOs = "Windows 10",
            PrimaryDns = "10.10.10.10",
            DhcpServer = "0.0.0.0",
            AutoLogin = AutoLoginCheck.IsChecked ?? false,
            StartWithWindows = AutoStartCheck.IsChecked ?? false,
            MinimizeToTray = true
        };
    }

    private void SetupAuthEvents()
    {
        _authService.OnLog += AppendLog;
        _authService.OnConnectionChanged += connected =>
        {
            Dispatcher.UIThread.InvokeAsync(() => UpdateConnectionStatus(connected));
        };
    }

    private void UpdateConnectionStatus(bool connected)
    {
        StatusIndicator.Text = connected ? "● 已连接" : "● 未连接";
        StatusIndicator.Foreground = new SolidColorBrush(connected ? Color.Parse("#34C759") : Color.Parse("#FF453A"));

        LoginBtn.IsVisible = !connected;
        LogoutBtn.IsVisible = connected;

        // 更新托盘提示
        if (_trayIcon != null)
            _trayIcon.ToolTipText = connected ? "校园网登录 - 已连接" : "校园网登录 - 未连接";
    }

    private async void LoginBtn_Click(object? sender, RoutedEventArgs e)
    {
        await DoLogin();
    }

    private void LogoutBtn_Click(object? sender, RoutedEventArgs e)
    {
        Disconnect();
    }

        private async Task DoLogin()
    {
        if (_isLoggingIn) return;
        SaveBtn_Click(null, null);

        var config = ReadConfigFromUI();
        // 同步快速登录账号到配置
        if (!string.IsNullOrEmpty(UsernameField.Text))
        {
            config.Username = UsernameField.Text;
            ConfigUsernameField.Text = UsernameField.Text;
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
            await _authService.StartLoginAsync();

            if (_authService.IsLoggedIn)
            {
                AppendLog("✅ 登录成功！已连接到校园网");
                StatusIndicator.Text = "● 已连接";
                StatusIndicator.Foreground = new SolidColorBrush(Color.Parse("#34C759"));
            }
            else
            {
                AppendLog("❌ 登录失败：认证服务器返回失败，请检查账号密码");
                StatusIndicator.Text = "● 登录失败";
                StatusIndicator.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
            }
        }
        catch (Exception ex)
        {
            AppendLog($"❌ 登录失败：{ex.Message}");
            StatusIndicator.Text = "● 登录失败";
            StatusIndicator.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
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
        AppendLog("🔌 已断开连接");
        StatusIndicator.Text = "● 已离线";
        StatusIndicator.Foreground = new SolidColorBrush(Color.Parse("#64748B"));
    }

    private void SaveBtn_Click(object? sender, RoutedEventArgs? e)
    {
        try
        {
            // 双向同步快速登录账号与配置账号
            if (!string.IsNullOrEmpty(UsernameField.Text))
                ConfigUsernameField.Text = UsernameField.Text;
            if (!string.IsNullOrEmpty(ConfigUsernameField.Text))
                UsernameField.Text = ConfigUsernameField.Text;

            _config = ReadConfigFromUI();
            _configService.Save(_config);
            AppendLog("配置已保存");
        }
        catch (Exception ex)
        {
            AppendLog($"保存失败: {ex.Message}");
        }
    }

    private void AppendLog(string message)
    {
        Dispatcher.UIThread.InvokeAsync(() => {
            _logLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            if (_logLines.Count > 200) _logLines.RemoveRange(0, _logLines.Count - 200);
            LogBox.Text = string.Join(Environment.NewLine, _logLines);
            LogBox.CaretIndex = LogBox.Text.Length;
        });
    }

    // ========== 自定义标题栏按钮 ==========
    private void MinBtn_Click(object? sender, RoutedEventArgs e)
    {
        // 最小化到托盘
        Hide();
    }

    private void CloseBtn_Click(object? sender, RoutedEventArgs e)
    {
        // 关闭按钮：隐藏到托盘（不退出程序）
        Hide();
    }

    /// <summary>
    /// 拦截窗口关闭事件，改为隐藏到托盘
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_trayIcon != null)
        {
            // 不是真正退出，隐藏到托盘
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