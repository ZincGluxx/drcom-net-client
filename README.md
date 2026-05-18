# Drcom .NET for JLU

[![Release](https://img.shields.io/github/v/release/ZincGluxx/drcom-net-client?style=flat-square&color=blue)](https://github.com/ZincGluxx/drcom-net-client/releases/latest)
[![C#](https://img.shields.io/badge/Language-C%23-239120?style=flat-square&logo=c-sharp&logoColor=white)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/Platform-Windows-0078D6?style=flat-square&logo=windows&logoColor=white)]()

一款基于 **.NET 8** 与 **Avalonia 11** 开发的吉林大学 (JLU) 校园网 Dr.COM 登录客户端。

👉 **[点击这里跳转到 Latest Release 下载最新安装包](https://github.com/ZincGluxx/drcom-net-client/releases/latest)**

本项目的核心认证逻辑移植自广为流传的第三方 Python 认证实现，在保证协议通信稳定的前提下，为 Windows 用户提供了原生图形界面、系统托盘与开机自启等能力。

## ✨ 主要特性

* **框架依赖分发**：采用 .NET 8 框架依赖（Framework-Dependent）发布，安装包体积较小；用户需在本机安装 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0)。
* **安装包**：使用 Inno Setup（LZMA 压缩）生成安装程序，一键安装。
* **开机自启与静默管理**：支持开机自启，打开应用即可自动登录校园网。
* **原生守护**：使用 C# 异步 Socket 实现客户端心跳保活，减少休眠断网、后台掉线等情况。
* **现代 UI**：Avalonia Fluent 主题，支持自定义标题栏、分页设置与运行日志。
* **系统托盘**：最小化/关闭窗口后驻留托盘，可从托盘登录、断开或退出。

## 🚀 编译与构建

### 1. 环境准备

* [.NET 8.0 SDK](https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0) 或更高版本
* [Inno Setup 6](https://jrsoftware.org/isinfo.php)（仅用于制作安装包）

### 2. 构建主程序

在项目根目录运行：

```powershell
dotnet publish CampusNetworkLogin/CampusNetworkLogin.csproj -c Release -o publish
```

构建成功后，发布文件位于仓库根目录的 `publish/` 文件夹（已加入 `.gitignore`）。

### 3. 构建安装包

1. 确保已完成上一步 `publish`。
2. 使用 Inno Setup 打开根目录的 `setup.iss`，或在命令行运行：

```powershell
ISCC setup.iss
```

3. 编译完成后，在项目根目录生成 `校园网登录_v1.0.1_Setup.exe`（版本号以 `setup.iss` 中定义为准）。

## 📂 项目结构说明

* `CampusNetworkLogin/` —— C# 主工程
  * `Views/` —— Avalonia 界面（`MainWindow.axaml`）
  * `Services/` —— 核心服务（`DrcomAuthService` 协议与心跳、`ConfigService`、`AutoStartService` 等）
  * `Models/` —— 配置模型
  * `Resources/` —— 应用图标等资源
  * `CampusNetworkLogin.csproj` —— .NET 8 项目配置（框架依赖、`win-x64`）
* `setup.iss` —— Inno Setup 安装包脚本
* `drcom.sln` —— Visual Studio 解决方案

## 🙏 鸣谢与声明

* 本项目的底层 Dr.COM 核心通信报文构造、加密 Hash 逻辑参考、复刻自现有的开源 Python 实现。
* 本软件仅供学习、研究网络协议及 C# 桌面端开发之用。
