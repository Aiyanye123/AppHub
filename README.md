# AppHub

轻量级 Windows 应用总览启动器（WPF / .NET 8）。

AppHub 用于集中管理常用应用，提供统一的添加、启动、状态查看、关闭与配置能力，减少多应用切换成本。

## 功能概览

- 支持添加 `*.exe`、`*.lnk`、`*.bat`、文件夹
- 支持在总览页直接拖入 `*.exe`、`*.lnk`、`*.bat`、文件夹添加应用
- 支持按钮添加：`添加应用`、`添加文件夹`
- 支持应用编辑：名称、分组、参数、工作目录、图标、进程跟踪
- 支持启动策略：新开实例 / 优先激活已运行窗口
- 支持关闭策略：优雅关闭 + 可选强制结束
- 总览支持按名称、运行状态或分组排序。选用手动排序时，可以分别为“全部分组”和单个分组设置置顶。在“全部分组”清空搜索后可拖拽调整顺序。
- 托盘菜单分为运行中、常用和全部应用，单击应用可启动或切换窗口，关闭操作位于运行中应用的子菜单。支持启动整组、确认后关闭全部运行中应用，以及 Shift 单击输入启动参数。
- 支持全局热键呼出搜索（默认 `Ctrl+Alt+Space`）、任务栏常用应用跳转列表，以及可配置的关闭按钮和托盘左键行为。
- 支持浅色和深色主题、本地配置与日志管理。

## 运行环境

- Windows 10 19041+ / Windows 11
- .NET SDK 8.0+
- 目标框架：`net8.0-windows10.0.19041.0`

## 快速开始

```powershell
dotnet restore AppHub.sln
dotnet build AppHub.sln -c Debug
dotnet run --project src/AppHub/AppHub.csproj
```

## 运行测试

```powershell
dotnet test tests/AppHub.Tests/AppHub.Tests.csproj
```

## 命令行参数

- `--set-autostart=on|off` 或 `--autostart=true|false`
- `--log-dir=<path>` 或 `--log-path=<path>`
- `--start-in-background` 或 `--background` 可让 AppHub 启动后直接驻留托盘。
- `--launch <应用 ID>` 或 `--launch=<应用 ID>` 启动指定应用；已有 AppHub 实例时，将请求交给该实例处理。

示例：

```powershell
dotnet run --project src/AppHub/AppHub.csproj -- --autostart=true --log-path="C:\Logs\AppHub"
```

## 配置与日志

- 配置文件：`%LocalAppData%\AppHub\config.json`
- 自动提取的应用图标缓存在 `%LocalAppData%\AppHub\icons\`。
- 默认日志目录：`%LocalAppData%\AppHub\logs\`

新配置默认每 5 秒刷新运行状态，隐藏到托盘后每 20 秒刷新。已有配置沿用保存的刷新间隔，可在设置页调整。托盘间隔取前台间隔的 4 倍，最短 10 秒，最长 120 秒。

## 项目结构

```text
.
|-- AppHub.sln
|-- README.md
|-- src/
|   `-- AppHub/
|       |-- AppHub.csproj
|       |-- AppHub/                 # 业务代码（Models/Services/ViewModels 等）
|       |-- views/ controls/        # XAML 视图与控件
|       `-- app.xaml mainwindow.xaml
```

## 说明

- 当前仅支持 Windows 桌面环境。
- 功能设计、模块职责和配置格式见本地 [项目文档](项目文档.md)。
