# Drcom .NET for JLU

[![Release](https://img.shields.io/github/v/release/ZincGluxx/drcom-net-client?style=flat-square&color=blue)](https://github.com/ZincGluxx/drcom-net-client/releases/latest)
[![C#](https://img.shields.io/badge/Language-C%23-239120?style=flat-square&logo=c-sharp&logoColor=white)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/Platform-Windows-0078D6?style=flat-square&logo=windows&logoColor=white)]()
[![Avalonia](https://img.shields.io/badge/UI-Avalonia-8B5CF6?style=flat-square)](https://avaloniaui.net/)

一款基于 **.NET 8** 与 **Avalonia 11** 开发的吉林大学 (JLU) 校园网 Dr.COM 登录客户端，小窗口设计，简洁高效。

👉 **[点击这里跳转到 Latest Release 下载最新安装包](https://github.com/ZincGluxx/drcom-net-client/releases/latest)**

本项目的核心认证逻辑移植自广为流传的第三方 Python 认证实现，在保证协议通信稳定的前提下，为 Windows 用户提供了原生图形界面、系统托盘与开机自启等能力。

## ✨ 主要特性

* **框架依赖分发**：采用 .NET 8 框架依赖（Framework-Dependent）发布，安装包体积小巧（~10MB）；用户需在本机安装 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0)。
* **安装程序**：使用 Inno Setup 生成安装向导，一键安装、自动创建桌面快捷方式、支持卸载。
* **小窗口 3:4 比例**：紧凑界面设计（300×400），适合日常快速登录使用。
* **开机自启与自动登录**：支持开机自启，配置自动登录后打开即连校园网。
* **原生守护**：使用 C# 异步 Socket 实现客户端心跳保活，减少休眠断网、后台掉线等情况。
* **现代 UI**：Avalonia 圆角窗口、自定义标题栏、三页分页（登录/配置/日志）。
* **系统托盘**：最小化/关闭窗口后驻留托盘，可从托盘登录、断开或退出。

## 🚀 编译与构建

### 1. 环境准备

* [.NET 8.0 SDK](https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0) 或更高版本
* [Inno Setup 6](https://jrsoftware.org/isinfo.php)（仅用于制作安装包）

### 2. 构建主程序

在项目根目录运行：

```powershell
dotnet publish CampusNetworkLogin/CampusNetworkLogin.csproj -c Release -o publish -r win-x64
```

构建成功后，发布文件位于仓库根目录的 `publish/` 文件夹（已加入 `.gitignore`）。

### 3. 构建安装包

1. 确保已完成上一步 `publish`。
2. 使用 Inno Setup 打开根目录的 `setup.iss`，或在命令行运行：

```powershell
ISCC setup.iss
```

3. 编译完成后，在项目根目录生成 `校园网登录_v1.0.1_Setup.exe`。

## 📂 项目结构

```
CampusNetworkLogin/
├── Views/         # Avalonia 界面 (MainWindow.axaml / .cs)
├── Services/      # 核心服务 (DrcomAuthService, ConfigService, AutoStartService, NetworkInfoService)
├── Models/        # 配置模型 (ConfigModel)
└── Resources/     # 应用图标
setup.iss          # Inno Setup 安装脚本
```

## 📝 更新日志

### v1.0.1 (2025)
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
