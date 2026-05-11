using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using CampusNetworkLogin.Models;
using CampusNetworkLogin.Services;

namespace CampusNetworkLogin.Forms;

public partial class MainForm : Form
{
    public const int WM_NCLBUTTONDOWN = 0xA1;
    public const int HT_CAPTION = 0x2;
    [DllImport("user32.dll")]
    public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    private readonly ConfigService _configService = new();
    private readonly AutoStartService _autoStartService = new();
    private readonly DrcomAuthService _authService = new();
    private ConfigModel _config = new();

    private NotifyIcon _trayIcon = null!;
    private ContextMenuStrip _trayMenu = null!;
    private bool _isLoggingIn = false;

    private readonly Color _primaryColor = Color.FromArgb(80, 140, 255);
    private readonly Color _bgColor = Color.FromArgb(245, 247, 250);
    private readonly Color _cardColor = Color.White;
    private readonly Color _textColor = Color.FromArgb(30, 30, 40);
    private readonly Color _secondaryTextColor = Color.FromArgb(120, 125, 135);
    private readonly Color _borderColor = Color.FromArgb(225, 228, 235);
    private readonly Color _successColor = Color.FromArgb(52, 199, 89);
    private readonly Color _errorColor = Color.FromArgb(255, 69, 58);
    private readonly Color _warningColor = Color.FromArgb(255, 159, 10);

    public MainForm()
    {
        SetupForm();
        LoadConfig();
        InitializeComponent();
        SetupTray();
        SetupAuthEvents();
        AutoDetectNetworkInfo();
    }

    private void SetupForm()
    {
        Text = "Drcom .NET for JLU";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        ClientSize = new Size(480, 620);
        BackColor = _bgColor;
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        ShowInTaskbar = true;
        Icon = GetAppIcon();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(_borderColor, 1);
        var rect = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        using var path = CreateRoundedPath(rect, 12);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (ClientSize.Width > 0 && ClientSize.Height > 0)
            Region = CreateRoundedRegion(ClientSize.Width, ClientSize.Height, 12);
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        // 标题区域
        var titlePanel = new Panel { Bounds = new Rectangle(0, 0, 480, 70), BackColor = _cardColor };

        var titleIcon = new Label { Text = "🌐", Font = new Font("Segoe UI", 22F), Location = new Point(20, 18), Size = new Size(40, 40), TextAlign = ContentAlignment.MiddleCenter };

        var titleLabel = new Label { Text = "Drcom .NET for JLU", Font = new Font("Segoe UI", 16F, FontStyle.Bold), ForeColor = _textColor, Location = new Point(68, 20), Size = new Size(240, 30), TextAlign = ContentAlignment.MiddleLeft };

        var statusIndicator = new Label { Text = "● 未连接", Font = new Font("Segoe UI", 9F), ForeColor = _secondaryTextColor, Location = new Point(68, 48), Size = new Size(200, 20), TextAlign = ContentAlignment.MiddleLeft };
        statusIndicator.Name = "statusIndicator";

        var closeBtn = new Button { Text = "×", Font = new Font("Segoe UI", 16F), FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 }, ForeColor = _secondaryTextColor, BackColor = Color.Transparent, Location = new Point(440, 8), Size = new Size(32, 32), Cursor = Cursors.Hand };
        closeBtn.Click += (_, _) => HideToTray();
        closeBtn.MouseEnter += (_, _) => closeBtn.ForeColor = _textColor;
        closeBtn.MouseLeave += (_, _) => closeBtn.ForeColor = _secondaryTextColor;

        var minBtn = new Button { Text = "−", Font = new Font("Segoe UI", 16F), FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 }, ForeColor = _secondaryTextColor, BackColor = Color.Transparent, Location = new Point(404, 8), Size = new Size(32, 32), Cursor = Cursors.Hand };
        minBtn.Click += (_, _) => WindowState = FormWindowState.Minimized;
        minBtn.MouseEnter += (_, _) => minBtn.ForeColor = _textColor;
        minBtn.MouseLeave += (_, _) => minBtn.ForeColor = _secondaryTextColor;

        titlePanel.MouseMove += (s, e) => {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        };

        titlePanel.Controls.AddRange([titleIcon, titleLabel, statusIndicator, closeBtn, minBtn]);
        Controls.Add(titlePanel);

        // 分隔线
        Controls.Add(new Panel { Bounds = new Rectangle(20, 70, 440, 1), BackColor = _borderColor });

        // 内容区域
        var contentY = 85;

        var serverField = new TextBox { Name = "serverField", Text = "10.100.61.3", Visible = false };
        Controls.Add(serverField);

                contentY = AddFieldRow("邮箱", null, "输入校园网邮箱账号", contentY, out var usernameField);
                usernameField.Name = "usernameField";

                contentY = AddPasswordRow("密码", contentY, out var passwordField);
                passwordField.Name = "passwordField";

                contentY = AddFieldRow("本机 IP", NetworkInfoService.GetLocalIpAddress(), null, contentY, out var ipField);
                ipField.Name = "ipField";
                ipField.ReadOnly = true;
                ipField.BackColor = Color.FromArgb(240, 242, 246);

                contentY = AddFieldRow("MAC 地址", NetworkInfoService.GetMacAddress(), null, contentY, out var macField);
                macField.Name = "macField";
                macField.ReadOnly = true;
                macField.BackColor = Color.FromArgb(240, 242, 246);

        contentY += 15;
        var optionsPanel = new FlowLayoutPanel { Bounds = new Rectangle(20, contentY, 440, 36), FlowDirection = FlowDirection.LeftToRight, WrapContents = false };

        var autoStartCheck = CreateCheckBox("开机自启动", _config.StartWithWindows);
        autoStartCheck.Name = "autoStartCheck";
        autoStartCheck.CheckedChanged += (_, _) => { try { _autoStartService.SetEnabled(autoStartCheck.Checked); } catch { } };

        var autoLoginCheck = CreateCheckBox("启动时自动登录", _config.AutoLogin);
        autoLoginCheck.Name = "autoLoginCheck";

        var minimizeCheck = CreateCheckBox("登录后最小化到托盘", _config.MinimizeToTray);
        minimizeCheck.Name = "minimizeCheck";

        optionsPanel.Controls.AddRange([autoStartCheck, autoLoginCheck, minimizeCheck]);
        Controls.Add(optionsPanel);

        contentY += 45;

        Controls.Add(new Label { Text = "运行日志", Font = new Font("Segoe UI", 9F), ForeColor = _secondaryTextColor, Location = new Point(20, contentY), Size = new Size(80, 20) });
        contentY += 22;

        var logBox = new TextBox { Multiline = true, ReadOnly = true, BackColor = Color.FromArgb(240, 242, 246), ForeColor = _textColor, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9F), Location = new Point(20, contentY), Size = new Size(440, 90), ScrollBars = ScrollBars.Vertical, Name = "logBox" };
        Controls.Add(new Panel { Bounds = new Rectangle(18, contentY - 2, 444, 94), BackColor = _cardColor });
        Controls.Add(logBox);

        contentY += 110;

        var btnPanel = new FlowLayoutPanel { Bounds = new Rectangle(20, contentY, 440, 48), FlowDirection = FlowDirection.RightToLeft, WrapContents = false };

        var loginBtn = new Button { Text = "立即登录", Font = new Font("Segoe UI", 12F, FontStyle.Bold), ForeColor = Color.White, BackColor = _primaryColor, FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 }, Size = new Size(140, 42), Cursor = Cursors.Hand, TextAlign = ContentAlignment.MiddleCenter };
        loginBtn.Name = "loginBtn";
        loginBtn.Click += LoginButton_Click;
        loginBtn.MouseEnter += (_, _) => loginBtn.BackColor = Color.FromArgb(60, 120, 245);
        loginBtn.MouseLeave += (_, _) => loginBtn.BackColor = _primaryColor;

        var saveBtn = new Button { Text = "保存配置", Font = new Font("Segoe UI", 11F), ForeColor = _primaryColor, BackColor = _cardColor, FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderColor = _borderColor }, Size = new Size(120, 42), Cursor = Cursors.Hand, Margin = new Padding(0, 0, 15, 0), TextAlign = ContentAlignment.MiddleCenter };
        saveBtn.Click += SaveConfig_Click;

        btnPanel.Controls.AddRange([loginBtn, saveBtn]);
        Controls.Add(btnPanel);

        var bottomPanel = new Panel { Bounds = new Rectangle(0, ClientSize.Height - 26, 480, 26), BackColor = Color.FromArgb(235, 238, 242) };
        bottomPanel.Controls.Add(new Label { Text = "v1.0.0 | Drcom .NET for JLU", Font = new Font("Segoe UI", 8F), ForeColor = _secondaryTextColor, Location = new Point(12, 4), Size = new Size(240, 18) });
        Controls.Add(bottomPanel);

        ResumeLayout(false);
        PerformLayout();
    }

    private int AddFieldRow(string label, string? text, string? placeholder, int y, out TextBox field)
    {
        var row = CreateFieldRow(label, text, placeholder, out field);
        row.Location = new Point(20, y);
        Controls.Add(row);
        return y + 60;
    }

    private static FlowLayoutPanel CreateFieldRow(string label, string? text, string? placeholder, out TextBox field)
    {
        var panel = new FlowLayoutPanel { Size = new Size(440, 54), FlowDirection = FlowDirection.TopDown, WrapContents = false };

        panel.Controls.Add(new Label { Text = label, Font = new Font("Segoe UI", 9F), ForeColor = Color.FromArgb(120, 125, 135), Size = new Size(440, 18), Margin = new Padding(0, 0, 0, 4) });

        field = new TextBox { Font = new Font("Segoe UI", 11F), ForeColor = Color.FromArgb(30, 30, 40), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Size = new Size(440, 30), Margin = new Padding(0) };
        if (text != null) field.Text = text;
        if (placeholder != null) field.PlaceholderText = placeholder;
        
        panel.Controls.Add(field);
        return panel;
    }

    private int AddPasswordRow(string label, int y, out TextBox field)
    {
        var panel = new FlowLayoutPanel { Location = new Point(20, y), Size = new Size(440, 54), FlowDirection = FlowDirection.TopDown, WrapContents = false };

        panel.Controls.Add(new Label { Text = label, Font = new Font("Segoe UI", 9F), ForeColor = Color.FromArgb(120, 125, 135), Size = new Size(440, 18), Margin = new Padding(0, 0, 0, 4) });

        field = new TextBox { Font = new Font("Segoe UI", 11F), ForeColor = Color.FromArgb(30, 30, 40), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, UseSystemPasswordChar = true, Size = new Size(440, 30), Margin = new Padding(0) };
        panel.Controls.Add(field);
        Controls.Add(panel);
        return y + 60;
    }

    private static CheckBox CreateCheckBox(string text, bool checkedState)
    {
        return new CheckBox { Text = text, Font = new Font("Segoe UI", 9.5F), ForeColor = Color.FromArgb(30, 30, 40), Checked = checkedState, AutoSize = true, Margin = new Padding(0, 0, 16, 0) };
    }

    private static Region CreateRoundedRegion(int width, int height, int radius)
    {
        using var path = CreateRoundedPath(new Rectangle(0, 0, width, height), radius);
        return new Region(path);
    }

    private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int r = radius * 2;
        path.AddArc(rect.X, rect.Y, r, r, 180, 90);
        path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90);
        path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
        path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Icon GetAppIcon()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("CampusNetworkLogin.Resources.icon.ico");
            if (stream != null)
                return new Icon(stream);
        }
        catch { }

        using var ms = new MemoryStream();
        using (var bmp = new Bitmap(32, 32))
        {
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(80, 140, 255));
            using var brush = new SolidBrush(Color.White);
            g.DrawString("N", new Font("Segoe UI", 18, FontStyle.Bold), brush, 5, 3);
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        }
        ms.Position = 0;
        return Icon.FromHandle(new Bitmap(ms).GetHicon());
    }

    // ========== 功能逻辑 ==========

    private void LoadConfig() => _config = _configService.Load();

    private void ApplyConfigToUI()
    {
        SetText("serverField", _config.Server);
        SetText("usernameField", _config.Username);
        SetText("passwordField", _config.Password);
        // 不再覆盖自动获取的IP和MAC，除非没网
        if (!string.IsNullOrEmpty(_config.HostIp) && _config.HostIp != "0.0.0.0") SetText("ipField", _config.HostIp);
        if (!string.IsNullOrEmpty(_config.Mac) && _config.Mac != "0x888888888888") SetText("macField", _config.Mac);

        SetChecked("autoStartCheck", _config.StartWithWindows);
        SetChecked("autoLoginCheck", _config.AutoLogin);
        SetChecked("minimizeCheck", _config.MinimizeToTray);
    }

        private void SetText(string name, string? value)
    {
        var tb = FindControl<TextBox>(this.Controls, name);
        if (tb != null && value != null)
            tb.Text = value;
    }

    private void SetChecked(string name, bool value)
    {
        var cb = FindControl<CheckBox>(this.Controls, name);
        if (cb != null)
            cb.Checked = value;
    }

    private ConfigModel ReadConfigFromUI()
    {
        return new ConfigModel
        {
            Server = "10.100.61.3",
            Username = GetText("usernameField", ""),
            Password = GetText("passwordField", ""),
            HostIp = NetworkInfoService.GetLocalIpAddress(),
            Mac = NetworkInfoService.GetMacAddress(),
            HostName = Environment.MachineName,
            HostOs = "Windows 10",
            PrimaryDns = "10.10.10.10",
            DhcpServer = "0.0.0.0",
            AutoLogin = GetChecked("autoLoginCheck"),
            StartWithWindows = GetChecked("autoStartCheck"),
            MinimizeToTray = GetChecked("minimizeCheck"),
        };
    }

        private string GetText(string name, string fallback)
        {
            var tb = FindControl<TextBox>(this.Controls, name);
            return tb != null ? tb.Text : fallback;
        }

        private bool GetChecked(string name)
        {
            var cb = FindControl<CheckBox>(this.Controls, name);
            return cb != null && cb.Checked;
        }

        private static T? FindControl<T>(Control.ControlCollection controls, string name) where T : Control
        {
            foreach (Control c in controls)
            {
                if (c.Name == name && c is T t)
                    return t;
                if (c.HasChildren)
                {
                    var result = FindControl<T>(c.Controls, name);
                    if (result != null) return result;
                }
            }
            return null;
        }

        private void AutoDetectNetworkInfo()
        {
            if (_config.HostIp == "0.0.0.0" || string.IsNullOrEmpty(_config.HostIp))
            {
                var ipField = FindControl<TextBox>(this.Controls, "ipField");
                if (ipField != null && (string.IsNullOrEmpty(ipField.Text) || ipField.Text == "自动获取"))
                    ipField.Text = NetworkInfoService.GetLocalIpAddress();
            }
            if (_config.Mac == "0x888888888888")
            {
                var macField = FindControl<TextBox>(this.Controls, "macField");
                if (macField != null)
                    macField.Text = NetworkInfoService.GetMacAddress();
            }
        }

    private void SetupTray()
    {
        _trayMenu = new ContextMenuStrip();
        _trayIcon = new NotifyIcon
        {
            Text = "校园网登录助手 - 未连接",
            Icon = GetAppIcon(),
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
        BuildTrayMenu();
    }

    private void BuildTrayMenu()
    {
        _trayMenu.Items.Clear();
        _trayMenu.Items.Add("显示窗口", null, (_, _) => ShowFromTray());
        _trayMenu.Items.Add("立即登录", null, async (_, _) => await DoLogin());
        _trayMenu.Items.Add("断开连接", null, (_, _) => Disconnect());
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(new ToolStripMenuItem(_authService.IsLoggedIn ? "状态: 已连接 ✓" : "状态: 未连接") { Enabled = false });
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("退出", null, (_, _) => ExitApp());
    }

    private void SetupAuthEvents()
    {
        _authService.OnLog += AppendLog;
        _authService.OnConnectionChanged += connected =>
        {
            if (InvokeRequired) Invoke(() => UpdateConnectionStatus(connected));
            else UpdateConnectionStatus(connected);
        };
    }

        private void UpdateConnectionStatus(bool connected)
    {
        var indicator = FindControl<Label>(this.Controls, "statusIndicator");
        if (indicator != null)
        {
            indicator.Text = connected ? "● 已连接" : "● 未连接";
            indicator.ForeColor = connected ? _successColor : _errorColor;
        }
        if (_trayIcon != null)
        {
            _trayIcon.Text = connected ? "Drcom .NET for JLU - 已连接" : "Drcom .NET for JLU - 未连接";
            BuildTrayMenu();
        }
        var loginBtn = FindControl<Button>(this.Controls, "loginBtn");
        if (loginBtn != null)
        {
            loginBtn.Text = connected ? "已连接" : (_isLoggingIn ? "登录中..." : "立即登录");
            loginBtn.Enabled = !_isLoggingIn || connected;
        }
    }

    private async void LoginButton_Click(object? sender, EventArgs e)
    {
        if (_isLoggingIn) { Disconnect(); return; }
        await DoLogin();
    }

    private async Task DoLogin()
    {
        if (_isLoggingIn) return;
        SaveConfig_Click(null, EventArgs.Empty);

        var config = ReadConfigFromUI();
        _authService.UpdateConfig(config.Server, config.Username, config.Password,
            config.HostIp, config.Mac, config.HostName, config.HostOs,
            config.PrimaryDns, config.DhcpServer);

                _isLoggingIn = true;
        UpdateConnectionStatus(false);

        var loginBtn = FindControl<Button>(this.Controls, "loginBtn");
        if (loginBtn != null)
        {
            loginBtn.Text = "登录中...";
            loginBtn.BackColor = _warningColor;
        }

        AppendLog("正在连接认证服务器...");
        await _authService.StartLoginAsync();

        _isLoggingIn = false;
        var loginBtn2 = FindControl<Button>(this.Controls, "loginBtn");
        if (loginBtn2 != null)
        {
            loginBtn2.BackColor = _primaryColor;
            loginBtn2.Text = _authService.IsLoggedIn ? "已连接" : "立即登录";
        }

        ShowToastFeedback(_authService.IsLoggedIn ? "成功" : "失败", 
            _authService.IsLoggedIn ? "校园网认证成功！" : "校园网认证失败，请检查账号密码或网络状态。");

        if (_authService.IsLoggedIn && GetChecked("minimizeCheck"))
            HideToTray();
    }

        private void Disconnect()
    {
        _authService.Stop();
        _isLoggingIn = false;
        UpdateConnectionStatus(false);
        var loginBtn = FindControl<Button>(this.Controls, "loginBtn");
        if (loginBtn != null)
        {
            loginBtn.Text = "立即登录";
            loginBtn.BackColor = _primaryColor;
        }
        AppendLog("已断开连接");
    }

    private void SaveConfig_Click(object? sender, EventArgs? e)
    {
        try
        {
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
        if (InvokeRequired) Invoke(() => InternalAppendLog(message));
        else InternalAppendLog(message);
    }

    private void ShowToastFeedback(string title, string message)
    {
        if (InvokeRequired)
        {
            Invoke(() => ShowToastFeedback(title, message));
            return;
        }
        
        var isSuccess = title == "成功";
        var toastColor = isSuccess ? _successColor : _errorColor;

        var toast = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            BackColor = Color.White,
            Size = new Size(300, 80),
            ShowInTaskbar = false,
            TopMost = true,
            Opacity = 0,
        };

        var screen = Screen.FromControl(this).WorkingArea;
        toast.Location = new Point(screen.Right - toast.Width - 20, screen.Bottom - toast.Height - 20);
        toast.Region = CreateRoundedRegion(toast.Width, toast.Height, 8);

        toast.Controls.Add(new Label { Text = title, Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = toastColor, Location = new Point(15, 10), AutoSize = true });
        toast.Controls.Add(new Label { Text = message, Font = new Font("Segoe UI", 10), ForeColor = _textColor, Location = new Point(15, 40), AutoSize = true, MaximumSize = new Size(270, 0) });
        
        toast.Paint += (s, e) =>
        {
            using var pen = new Pen(_borderColor, 1);
            e.Graphics.DrawPath(pen, CreateRoundedPath(new Rectangle(0, 0, toast.Width - 1, toast.Height - 1), 8));
            e.Graphics.FillRectangle(new SolidBrush(toastColor), new Rectangle(0, 0, 5, toast.Height));
        };

        var timer = new System.Windows.Forms.Timer { Interval = 15 };
        var lifeTimer = new System.Windows.Forms.Timer { Interval = 10000 };

        double opacity = 0;
        int state = 0; // 0=fade in, 1=wait, 2=fade out
        
        toast.Show();

        lifeTimer.Tick += (s, e) => { lifeTimer.Stop(); state = 2; };
        
        timer.Tick += (s, e) =>
        {
            if (state == 0)
            {
                opacity += 0.1;
                toast.Opacity = opacity;
                if (opacity >= 1) { timer.Stop(); lifeTimer.Start(); }
            }
            else if (state == 2)
            {
                opacity -= 0.1;
                toast.Opacity = opacity;
                if (opacity <= 0) { timer.Stop(); lifeTimer.Stop(); toast.Close(); }
            }
        };
        timer.Start();

        // Click to dismiss
        toast.Click += (s, e) => { state = 2; timer.Start(); };
        foreach (Control c in toast.Controls) 
            c.Click += (s, e) => { state = 2; timer.Start(); };
    }

        private void InternalAppendLog(string message)
        {
            var logBox = FindControl<TextBox>(this.Controls, "logBox");
            if (logBox == null) return;
            var lines = logBox.Lines.ToList();
            lines.Add(message);
            if (lines.Count > 200) lines.RemoveRange(0, lines.Count - 200);
            logBox.Lines = [.. lines];
            logBox.SelectionStart = logBox.Text.Length;
            logBox.ScrollToCaret();

            // 同时写入日志文件以便调试
            try
            {
                var logPath = Path.Combine(Application.StartupPath, "drcom_debug.log");
                File.AppendAllText(logPath,
                    $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch { }
        }

    private void HideToTray() { Hide(); ShowInTaskbar = false; }

    private void ShowFromTray() { Show(); ShowInTaskbar = true; WindowState = FormWindowState.Normal; Activate(); BringToFront(); }

    private void ExitApp()
    {
        _authService.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Exit();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Region = CreateRoundedRegion(ClientSize.Width, ClientSize.Height, 12);
        ApplyConfigToUI();

        if (_config.AutoLogin && !string.IsNullOrEmpty(_config.Username) && !string.IsNullOrEmpty(_config.Password))
        {
            var timer = new System.Windows.Forms.Timer { Interval = 800 };
            timer.Tick += async (_, _) => { timer.Stop(); await DoLogin(); };
            timer.Start();
        }
    }
}
