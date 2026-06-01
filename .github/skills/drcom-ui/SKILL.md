---
name: drcom-ui
description: '用于在 Dr.COM 客户端中修改或添加 Avalonia UI 组件的操作指南、布局细节和步骤。当修改用户界面、添加新视图或处理 Avalonia .axaml 样式时请使用此 skill。'
---

# Dr.COM 客户端 UI 开发指南

## UI 框架与设计约束
- **框架**: Avalonia UI 11
- **窗口尺寸**: 300x400 (3:4 比例)，紧凑型布局设计。
- **窗口样式**: 自定义圆角，无系统原生边框及标题栏（`WindowDecorations="None"`, `Background="Transparent"`）。
- **导航模式**: 客户端内部采用基于标签/页面的切换方式。

## 项目结构
所有与 UI 相关的文件皆位于 `CampusNetworkLogin/Views/` 目录下：
- `MainWindow.axaml`: 主窗口外壳。包含自定义标题栏（处理窗口拖拽）、窗口控制按钮（关闭/最小化），并负责承载各个子页面。
- `ConfigPageView.axaml`: 配置页面（处理账号、开机自启、自动登录等设置）。
- `LogPageView.axaml`: 日志显示页面（展示连接过程和报错信息）。

## 何时使用此 Skill
- 添加新的 UI 元素、控件或完整的页面。
- 修改 `.axaml` 文件中现有的布局、颜色或样式。
- 处理后置代码 (`.axaml.cs`) 中的 UI 相关逻辑。
- 诊断 Avalonia 样式或布局问题。

## 开发指南与最佳实践
1. **保持紧凑的窗口尺寸**: 确保所有新的 UI 元素都能适应 300x400 的尺寸。如果内容过长（例如日志视图），请使用 `ScrollViewer`。
2. **自定义标题栏拖拽**: 由于禁用了系统原生窗口装饰，任何拖拽移动逻辑都必须绑定到自定义的顶部区域。避免将可点击的控件放置在拖拽区域上方。
3. **Avalonia 语法**: 使用标准的 Avalonia UI `.axaml` 标记语法。
4. **Code-Behind 与 MVVM**: 遵循当前项目架构。本项目的 UI 逻辑主要是事件驱动的，并包含在 `.axaml.cs` 后置代码中，直接与 `Services` 交互。请保持逻辑简单易读。
5. **系统托盘集成**: 后台运行状态和托盘图标的逻辑在 UI 启动时初始化。如果启用了后台运行，请避免在关闭窗口时直接彻底退出程序。

## UI 设计规范 (Design System)
为了保持 Dr.COM 客户端界面的现代化、简洁和一致性，所有的 UI 组件修改和新增需遵循以下设计规范：

### 1. 色彩规范与材质 (Color & Materials)
- **主色调 (Primary)**: `#0078D4` (Windows 默认主蓝色)，Hover 态 `#005A9E`，按下态 `#004881`，用于主按钮、强调文本和激活状态。
- **背景材质 (Window Background)**:
  - 强烈推荐使用现代材质。可在 `MainWindow` 中开启毛玻璃/云母效果 (`TransparencyLevelHint="Mica, AcrylicBlur"`)。
  - 兜底背景色: 浅色模式 `#F3F3F3` 或 `#FAFAFA`，深色模式 `#202020`。
- **卡片/面板背景 (Surface)**:
  - 浅色模式: `#FFFFFF` (纯白，配合极微弱阴影区分层次)。
  - 深色模式: `#2D2D2D` 或透明度为 `5%` 的纯白。
- **文本色 (Text)**:
  - 主要文本 (Primary Text): 浅色模式 `#1A1A1A` / 深色模式 `#FFFFFF`。
  - 次要文本 (Secondary Text): 浅色模式 `#605E5C` / 深色模式 `#A0AAB2`，用于详情备注、版本号。
- **状态色 (Status)**:
  - 成功 (Success): `#10893E` (绿色，连接成功状态)
  - 错误 (Error): `#E81123` (红色，连接失败或警告信息)
  - 警告 (Warning): `#FCE100` (橙色)

### 2. 字体、排版与图标 (Typography & Iconography)
- **字体族 (Font Family)**: 优先使用系统默认现代字体，如 `system-ui, "Microsoft YaHei", "Segoe UI", sans-serif`。
- **字号阶层**:
  - **大标题 (Header)**: `20dpx` (字重 SemiBold 或 Bold，用于应用名称或显著的主状态展示)。
  - **中标题 (Sub-header)**: `16dpx` (字重 SemiBold，用于设置页分组标题)。
  - **正文 (Body)**: `14dpx` (常规，用于主体文本、输入框内容、按钮文字)。
  - **辅助文字 (Caption)**: `12dpx` (常规，用于版权信息、日志小字、输入校验提示)。
- **图标 (Icons)**: 建议使用 Fluent System Icons 或 Path 几何矢量图标，图标尺寸标准化为 `16x16` 或 `20x20`，图标与文字的间距固定为 `8px`。

### 3. 尺寸、间距、圆角与阴影 (Geometry & Elevation)
- **总体尺寸**: 严格限制窗口可视区域为 **300(宽) x 400(高)**。
- **布局栅格**: 采用 `4px`/`8px` 倍数原则，确保呼吸感。
  - 全局外边距 (Global Margin): `16px` 或 `20px`。
  - 控件间距 (Spacing): 紧密关联的控件 `8px`，一般层级 `12px`，大模块隔离 `24px`。
- **圆角 (Corner Radius)**:
  - 主窗口外沿: `CornerRadius="8"`。
  - 内部模块/卡片: `CornerRadius="6"` 或 `8`。
  - 按钮/输入框: `CornerRadius="4"` 或 `6`，微圆角更显精致灵动。
- **阴影 (BoxShadow)**:
  - 为内嵌卡片、常驻悬浮层添加轻量级阴影（如 `0 2 8 0 #0D000000`），增强空间的 Z 轴层次感。

### 4. 控件风格与动效 (Controls & Transitions)
- **输入框 (TextBox)**:
  - 推荐无外边框设计，仅保持底部边框或使用轻柔的背景色填充。获得焦点 (Focus) 时底部展示主色调高亮线，并附带平滑拉伸效果。
  - 必须带有 `PlaceholderText` (占位符提示，旧版 Avalonia 称为 Watermark，现已废弃)。
- **按钮 (Button)**:
  - **主操作**: 使用主题色背景 + 白色文字，Hover 时背景轻微提亮，点击时展现缩小/下压效果。
  - **次要操作**: 使用透明/灰透明背景 + 次级文字，Hover 时底色变灰深。
  - 动态反馈: 按钮务必设置过渡动画 `<BrushTransition Property="Background" Duration="0:0:0.15"/>` 增加交互质感。
- **页签切换与内容过渡 (Page Transitions)**:
  - 页面跳转（如“登录”到“设置”）应使用透明度渐显 (Fade) 或轻微的纵向位移 (Slide) 过渡，时长 `200ms`，避免生硬突变。
- **切换开关 (ToggleSwitch)**:
  - 开启状态填充满主题色，关闭状态保持空心轮廓，开关拨动自带弹性动画。