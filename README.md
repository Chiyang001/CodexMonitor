# Codex 额度悬浮窗

Windows 原生小工具，显示当前本机 Codex ChatGPT 登录账号的 **实际额度窗口、剩余百分比和重置倒计时**。根据接口数据自适应 Plus、Pro、Free 等套餐：两项额度显示双卡，单项额度显示单卡，支持分钟、小时、每日和每周等周期。内置四套不同配色和布局的主题，默认采用深海薄荷分区卡片设计，默认 208 × 122 逻辑像素，按 Windows 显示缩放适配。标题显示接口返回的套餐名称，不显示同步状态或同步时间。

## 使用

双击 `Install.cmd` 一键安装，再通过桌面或开始菜单快捷方式手动启动。无需管理员权限，无需安装 Node.js、Python 或 .NET SDK。程序使用 Windows 自带的 .NET Framework 4.x。

安装后在桌面和开始菜单创建 `Codex Quota Monitor` 快捷方式，不会添加开机或登录自启动项；更新安装时会移除旧版创建的登录启动快捷方式。手动启动后常驻托盘，每 4 秒检查 Codex 是否运行：Codex 启动时显示悬浮窗，退出后隐藏并关闭额度读取连接。

也可以直接运行 `dist/CodexMonitor.exe`。所有启动方式均由用户手动操作。旧版 `-NoStartup`、`-NoLaunch` 安装参数保留兼容，安装不会自动运行程序。

- 拖动窗口空白区域移动；自动保存位置。
- 拖动任意边缘或角落调整大小，可独立改变宽高，内容自动适配并保存窗口大小。
- 右键窗口或托盘打开设置菜单，可立即刷新、切换置顶、调整透明度。
- 右键菜单“界面主题”可切换深海薄荷（分区卡片）、晴空纸白（极简线条）、星夜紫（双环仪表）、暖砂金（双栏面板），即时生效并保存选择。
- 菜单“窗口大小”提供 85%、100%、125%、150%、200% 预设比例；100% 恢复默认尺寸。
- 鼠标移入窗口后，右上角显示 ×，点击可暂时隐藏；双击托盘图标恢复显示，下一次启动 Codex 时也会恢复。
- 右键窗口或托盘可调整透明度、重置位置和退出。
- 再次启动程序会显示已有窗口，不会创建重复实例。

额度每 **30 秒**读取一次，倒计时每秒刷新。网络错误时保留上次数据，超过 75 秒未更新时数字和进度条变为灰色，错误详情仅在鼠标悬停提示中显示。尚未读到数据时显示 `—`，不会当成剩余 0%。已过重置时间的窗口显示 `等待额度重置`，并加快至每 10 秒重新读取，不推算已恢复的额度。

## 数据和登录

使用官方 [Codex App Server](https://learn.chatgpt.com/docs/app-server) 的 `initialize`、`account/read` 和 `account/rateLimits/read` 接口，通过本机 `codex app-server` 的标准输入/输出通讯。优先读取 `rateLimitsByLimitId.codex`；读取 primary、secondary 中实际提供的窗口，按 windowDurationMins 生成周期标签，剩余值为 `100 - usedPercent`；缺少周期时使用“当前额度”。套餐优先使用额度接口的 planType，缺失时使用 account/read 的 planType。

这是独立的 Windows 辅助程序，不需要修改 Codex 应用或安装到 Codex 插件市场。它复用 Codex 的登录状态，登录、凭据读取和令牌刷新由官方 app-server 处理。程序不直接读取登录凭据内容，不保存账号、额度、令牌或服务端诊断日志，不创建对话或发起模型请求。仅在 `%LOCALAPPDATA%\CodexMonitor\settings.json` 保存位置、大小、置顶、透明度和主题。窗口、托盘和 EXE 文件使用相同的圆环 Logo。

自动发现 `%LOCALAPPDATA%\OpenAI\Codex\bin` 下的 Codex CLI，备用搜索 PATH。特殊安装可用 `CODEX_MONITOR_CODEX_PATH` 环境变量指定 `codex.exe` 绝对路径。支持 `CODEX_HOME`；本地 `auth.json` 修改后清除旧额度并重新连接。额度属于当前本机登录账号，可能被其它电脑上的使用同时改变。

需要在 Codex 中使用 ChatGPT 账号登录。API Key 登录没有对应套餐的 5 小时/周额度，鼠标悬停时会提示原因。接口未返回的额度窗口不显示，也不会推断为无限额度或剩余 100%；没有有效额度窗口时显示“暂无额度窗口”。标准 Windows Store 安装（包括进程名为 `ChatGPT.exe` 的新版 Codex）和常规 Codex 安装目录可自动识别；自定义桌面安装目录需扩展 `CodexDiscovery.IsDesktopPath`。

## 卸载

先从托盘选择退出，然后执行 `%LOCALAPPDATA%\CodexMonitor\Uninstall.ps1`：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\CodexMonitor\Uninstall.ps1"
```

卸载移除程序、快捷方式和登录启动项，保留窗口设置。新版本不创建登录启动项。

## 开发和验证

```powershell
.\build.ps1
```

使用系统自带 C# 编译器生成 `dist/CodexMonitor.exe` 并执行测试，同时从同一套 Logo 绘制代码生成并嵌入 EXE 图标。测试覆盖套餐与单项/双项/空额度自适应、未知周期、四套主题多尺寸渲染、额度分组选择、缺失字段、窗口匹配、百分比范围、重置倒计时、桌面进程识别、RPC 握手、乱序回复、错误、断开连接、原生边缘缩放、最小尺寸和大小设置兼容性。

可选只读实机探测（开发时需 Node.js）：`node scripts/probe.cjs`。生成真实窗口 PNG：`dist\CodexMonitor.exe --capture "C:\absolute\path\quota.png"`。截图只包含悬浮窗内的额度；目录需提前创建。



