# AppHub

轻量级 Windows 应用总览启动器（WPF / .NET 8）。

AppHub 用于集中管理常用应用，提供统一的添加、启动、状态查看、关闭与配置能力，减少多应用切换成本。

## 功能概览

- 支持添加 `*.exe`、`*.lnk`、文件夹
- 支持在总览页直接拖入 `*.exe`、`*.lnk`、文件夹添加应用
- 支持按钮添加：`添加应用`、`添加文件夹`
- 支持应用编辑：名称、分组、参数、工作目录、图标、进程跟踪
- 支持启动策略：新开实例 / 优先激活已运行窗口
- 支持关闭策略：优雅关闭 + 可选强制结束
- 支持总览搜索、分组筛选、排序、拖拽重排、置顶
- 支持浅色/深色主题、托盘驻留、本地配置与日志管理

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

示例：

```powershell
dotnet run --project src/AppHub/AppHub.csproj -- --autostart=true --log-path="C:\Logs\AppHub"
```

## 配置与日志

- 配置文件：`%LocalAppData%\AppHub\config.json`
- 图标缓存：`%LocalAppData%\AppHub\icons\{app-id}.png`
- 默认日志目录：`%LocalAppData%\AppHub\logs\`

## 项目结构

```text
.
|-- AppHub.sln
|-- README.md
|-- 设计文档.md
|-- 架构文档.md
|-- src/
|   `-- AppHub/
|       |-- AppHub.csproj
|       |-- AppHub/                 # 业务代码（Models/Services/ViewModels 等）
|       |-- views/ controls/        # XAML 视图与控件
|       `-- app.xaml mainwindow.xaml
```

## 说明

- 当前仅支持 Windows 桌面环境。
- 详细设计与模块职责见：`设计文档.md`、`架构文档.md`。
