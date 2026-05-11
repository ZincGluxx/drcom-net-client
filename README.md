# Drcom .NET for JLU

一款基于 **.NET 8 (Windows Forms)** 开发的吉林大学 (JLU) 校园网 Dr.COM 登录客户端。

本项目的核心认证逻辑移植自广为流传的第三方 Python 认证脚本 (`newclient.py`)，在保证协议通信稳定的前提下，为 Windows 用户提供了原生的图形化图形界面，并实现了极其现代化的打包分发体验。

## ✨ 主要特性

* **免环境依赖分发**：采用 .NET 8 裁剪的自包含（Trimmed Self-Contained）发布模式，主程序及依赖库被精简至极小的体积。用户无需在电脑上预装任何 .NET Runtime 即可直接使用。
* **独立的安装包**：借助 Inno Setup 进行高效压缩（LZMA2），最终生成的安装包仅十几 MB，方便一键 "下一步" 安装。
* **开机自启与静默管理**：支持开机自启，打开应用即可全自动登录校园网，无需每次手动输入密码。
* **原生守护**：完全使用 C# 异步 Socket 重写的客户端心跳保活逻辑，有效避免休眠断网、后台掉线等情况。
* **现代 UI**：使用了原生 WinForms 提供简单直观的用户交互。

## 🚀 编译与构建

### 1. 环境准备
* [.NET 8.0 SDK](https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0) 或更高版本
* [Inno Setup 6](https://jrsoftware.org/isinfo.php)（仅用于制作安装包）

### 2. 构建主程序
为了生成包含依赖、无外部 .NET 框架体积且已裁剪的发布文件，请在项目根目录运行：
```powershell
dotnet publish CampusNetworkLogin/CampusNetworkLogin.csproj -c Release
```
构建成功后，所有需要分发给用户的文件（主程序及 DLLs）均会在 `CampusNetworkLogin/bin/Release/net8.0-windows/win-x64/publish/` 下生成。

### 3. 构建安装包
我们已内置了优化好的高压缩比打包脚本：
1. 确保已安装 Inno Setup 6。
2. 使用 Inno Setup 打开 `CampusNetworkLogin/installer.iss`（或者在命令行运行 `ISCC` 编译）。
3. 编译完成后，会在项目根目录生成形如 `Drcom_NET_Setup_Standalone_v1.0.exe` 的安装包。

## 📂 项目结构说明

* `CampusNetworkLogin/` —— C# WinForms 源代码主工程。
  * `Services/` —— 核心服务层代码（涵盖 `DrcomAuthService.cs` 协议算法、心跳、自启等）。
  * `Models/` —— 配置模型。
  * `Forms/` —— 界面展示UI。
  * `CampusNetworkLogin.csproj` —— 标准的 .NET 8 发布配置（启用了针对 WinForms 的 Trimmed 和 Self-Contained 支持）。
  * `installer.iss` —— Inno Setup 生成安装包脚本。
* `newclient.py` —— 协议分析与移植参考的原版 Python UDP 通信源码。

## 🙏 鸣谢与声明

* 本项目的底层 Dr.COM 核心通信报文构造、加密 Hash 逻辑参考、复刻自现有的开源 Python 脚本（请参见项目内的 `newclient.py`）。
* 本软件仅供学习、研究网络协议及 C# 桌面端跨平台裁剪部署之用。
