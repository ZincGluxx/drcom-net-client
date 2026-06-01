# Drcom .NET for JLU

[![Release](https://img.shields.io/github/v/release/ZincGluxx/drcom-net-client?style=flat-square&color=blue)](https://github.com/ZincGluxx/drcom-net-client/releases/latest)
[![C#](https://img.shields.io/badge/Language-C%23-239120?style=flat-square&logo=c-sharp&logoColor=white)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/Platform-Windows-0078D6?style=flat-square&logo=windows&logoColor=white)]()
[![Avalonia](https://img.shields.io/badge/UI-Avalonia-8B5CF6?style=flat-square)](https://avaloniaui.net/)

一款基于 **.NET** 与 **Avalonia** 开发的吉林大学 (JLU) 校园网 Dr.COM 登录客户端。经过重新设计，现已全面拥抱全新的单页并排布局与终端级日志。

👉 **[点击这里跳转到 Latest Release 下载最新安装包](https://github.com/ZincGluxx/drcom-net-client/releases/latest)**

本项目的核心认证逻辑移植自广为流传的第三方 Python 认证实现，在保证协议通信稳定的前提下，为 Windows 用户提供了更贴合现代化风格的原生图形界面、系统托盘与开机自启等能力。

## ✨ 主要特性

* **单页并排无边框设计**：全新的 600×420 布局设计。左侧为配置表单（学号、密码、网络、模式）与登录按钮，右侧为全黑深色终端风实时日志（支持文本选中复制），操作不再需要频繁切页。
* **Mica/半透明视觉设计**：利用 Windows 11 的原生特性，完全集成 Mica/Acrylic 设置，使客户端的背景能完美融合到桌面的壁纸与主题中。
* **托盘级静默守护**：支持开机自启并配置自动登录。可最小化至系统托盘，利用原生的异步 Socket 循环发送心跳包保活，解决休眠重连断网的问题。
* **安装程序双管齐下**：
  * **框架依赖版 (Framework-Dependent)**：小巧，仅需安装 .NET 8 或更高版本 Desktop Runtime。
  * **原生 AOT 版 (Native AOT)**：纯正的本机代码版本，极其迅速的冷启动时间，零外部运行时依赖。

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

### v1.0.3 (2025)
- ✨ 彻底重构 UI，摒弃多页切换，采用 600×420 **左右双列并行设计**。左屏精简表单，右屏黑底高亮终极 Terminal Log。
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
