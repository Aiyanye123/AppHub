# AppHub

轻量级 Windows 应用总览启动器（WPF / .NET 8）。

AppHub 用于集中管理常用应用，提供统一的添加、启动、状态查看、关闭和配置能力，减少多应用切换成本。

## 功能概览

- 支持添加 `*.exe` 与 `*.lnk` 应用
- 应用编辑：名称、分组、参数、工作目录、图标、进程跟踪
- 启动策略：总是新开实例 / 优先激活已运行窗口
- 关闭策略：优雅关闭 + 可选强制结束
- 总览页支持搜索、分组筛选、排序、拖拽重排、置顶
- 主题切换（浅色 / 深色）与背景效果配置
- 托盘驻留（关闭窗口默认最小化到托盘）
- 配置本地持久化与日志目录管理

## 运行环境

- Windows 10 19041+ 或 Windows 11
- .NET SDK 8.0+

目标框架：`net8.0-windows10.0.19041.0`

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
|-- src/
|   `-- AppHub/
|       |-- AppHub.csproj
|       |-- AppHub/                # 业务代码（Models/Services/ViewModels 等）
|       |-- views/ controls/        # XAML 视图与控件
|       `-- app.xaml mainwindow.xaml
|-- tests/
|   `-- AppHub.Tests/
|-- 设计文档.md
`-- 架构文档.md
```

## 说明

- 本项目当前仅支持 Windows 桌面环境。
- 如需了解细节设计与模块职责，请查看 `设计文档.md` 和 `架构文档.md`。
