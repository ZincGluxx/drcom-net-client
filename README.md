# Drcom .NET for JLU

[![Release](https://img.shields.io/github/v/release/ZincGluxx/drcom-net-client?style=flat-square&color=blue)](https://github.com/ZincGluxx/drcom-net-client/releases/latest)
[![C#](https://img.shields.io/badge/Language-C%23-239120?style=flat-square&logo=c-sharp&logoColor=white)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/Platform-Windows-0078D6?style=flat-square&logo=windows&logoColor=white)]()
[![Avalonia](https://img.shields.io/badge/UI-Avalonia-8B5CF6?style=flat-square)](https://avaloniaui.net/)

一款基于 **.NET** 与 **Avalonia** 开发的吉林大学 (JLU) 校园网 Dr.COM 登录客户端。经过重新设计，现已升级为面向日常使用的校园网登录面板，集成认证、保活、网络诊断、托盘守护与 Native AOT 发布。

👉 **[点击这里跳转到 Latest Release 下载最新安装包](https://github.com/ZincGluxx/drcom-net-client/releases/latest)**

本项目的核心认证逻辑移植自广为流传的第三方 Python 认证实现，在保证协议通信稳定的前提下，为 Windows 用户提供了更贴合现代化风格的原生图形界面、系统托盘与开机自启等能力。

## ✨ 主要特性

* **紧凑登录面板**：小窗口集中管理账号、密码、自动登录、开机自启、关闭到托盘和登录状态。
* **静态网络设置**：可直接修改 Windows 网卡的静态 IP、子网掩码、网关和 DNS，适合校园网固定地址场景。
* **连通性检测**：内置内网 `jlu.edu.cn` 与外网 `www.baidu.com` Ping 检测，快速判断当前网络状态。
* **登录前检查**：登录前校验账号、密码和本机网络信息，减少无意义的认证重试。
* **托盘级静默守护**：支持开机自启、自动登录、关闭到托盘、托盘登录/下线/刷新网络信息，并利用异步 Socket 循环发送心跳包保活。
* **原生 AOT 版 (Native AOT)**：纯正的本机代码版本，启动迅速，零外部运行时依赖。

## 🚀 编译与构建

### 1. 环境准备

* [.NET 10 SDK](https://dotnet.microsoft.com/) 或更高版本
* Visual Studio C++ x64 工具链（仅 Native AOT 发布需要）
* [Inno Setup 6](https://jrsoftware.org/isinfo.php)（仅用于制作安装包）

### 2. 构建主程序

在项目根目录运行：

```powershell
dotnet publish CampusNetworkLogin/CampusNetworkLogin.csproj -c Release -r win-x64 -o artifacts/publish_aot -p:PublishAot=true -p:SelfContained=true
```

构建成功后，发布文件位于 `artifacts/publish_aot/`。

### 3. 构建安装包

1. 确保已完成上一步 `publish`。
2. 使用 Inno Setup 打开根目录的 `setup.iss`，或在命令行运行：

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" setup.iss
```

3. 编译完成后生成 `DrcomNET_v1.0.5_Setup.exe`。

## 📂 项目结构

```
CampusNetworkLogin/
├── Views/         # Avalonia 界面 (MainWindow / NetworkSettingsWindow)
├── Services/      # 核心服务 (DrcomAuthService, ConfigService, AutoStartService, NetworkInfo/SettingsService)
├── Helpers/       # 辅助工具 (PrivacyHelper)
├── Models/        # 配置模型 (ConfigModel, AppJsonContext)
├── Resources/     # 应用图标
└── Program.cs     # 入口 + NamedPipe 多实例激活
setup.iss          # Inno Setup 安装脚本 (旧版检测/自动关闭/64位)
```

## 📝 更新日志

### v1.0.5 (2026-07)
- 🧠 **内存优化**：保活阶段 Socket 接收缓冲区复用，消除每 20s 循环内多次 1KB 重复分配；日志 StringBuilder 限制 4KB 上限防止无限增长。
- 🔌 **事件泄漏修复**：`NetworkChange.NetworkAvailabilityChanged` 在窗口关闭时正确退订，避免持有引用泄漏。
- 🎨 **UI 紧凑化**：压缩 MainWindow 各区域 Margin/Padding/Spacing，窗口高度 450→435，内容更充实。
- 🔐 **密码显隐切换**：密码框右侧新增"显示/隐藏"按钮，兼容 Avalonia 12.0.3（无需 `RevealButtonEnabled`）。
- 📐 **NetworkSettingsWindow 修复**：按钮添加 `HorizontalAlignment="Stretch"` 确保等宽拉伸，修复不同 DPI 下错位。
- 🔵 **步骤指示器修正**：登录步骤指示器（挑战→认证→保活）时序调整，连接成功后保持全绿完成状态可见。
- 📦 **安装脚本升级**：新增旧版检测卸载提示、安装前自动关闭运行中程序、`x64compatible` 架构标识。

### v1.0.4 (2026)
- ✨ 重构为更小的校园网登录工具窗口，移除日志展示，避免界面拥挤。
- 🛠️ 增加 Windows 静态 IP / 子网掩码 / 网关 / DNS 设置弹窗。
- 🌐 增加内网 `jlu.edu.cn` 与外网 `www.baidu.com` 连通性检测。
- 🔒 认证服务器固定为内置地址，界面不再暴露服务器输入框。
- 🔧 修复主窗口与静态网络设置窗口的标题栏、表单和按钮错位问题。

### v1.0.3 (2025)
- ✨ 彻底重构 UI，采用可调整大小的 **校园网登录工作台**。左侧为登录与启动配置，右侧为认证状态、网络诊断、快速操作和实时日志。
- 🧭 增加网卡、IP、MAC、网关、DNS、主机名展示与诊断复制能力。
- ✅ 增加登录前输入校验与首次登录自动网络检测。
- 🧩 托盘菜单增加刷新网络信息与复制网络诊断。
- 🪟 全面适配 **Windows 11 Mica 材质**与原生无边框阴影 (去除硬编码 `WindowDecorations="None"`)。
- 🔧 修复上个版本去边框操作导致的所有字体层叠干扰、阴影锯齿及拖拽失效问题。
- 🎉 支持 NativeAOT 独立纯本地编译发布，极致启动。

### v1.0.2 / 1.0.1 (2025)
- ✨ 重构为 Avalonia UI 框架
- 🪟 自定义圆角窗口，无系统装饰
- 📏 3:4 小窗口比例 (300×400)
- 🚀 优化启动速度（延迟加载配置与托盘）
- 📋 日志页显示详细的登录成功/失败结果
- 📦 Inno Setup 安装包 (10.8MB)

### v1.0.0
- 🎉 首个正式版本
- 🔐 Dr.COM 认证协议（挑战-响应 + 心跳保活）
- 🖥️ Windows 原生 WPF 界面
- 🧩 系统托盘与开机自启

## 🙏 鸣谢

* 本项目的底层 Dr.COM 核心通信报文构造、加密 Hash 逻辑参考、复刻自现有的开源 Python 实现。
* 本软件仅供学习、研究网络协议及 C# 桌面端开发之用。
