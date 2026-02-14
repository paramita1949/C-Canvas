# PROJECT KNOWLEDGE BASE

**Generated:** 2026-02-13 19:00:57
**Commit:** bb25fdb
**Branch:** refactor/split-mainwindow-partials

## OVERVIEW
Canvas 是一个 .NET 8 WPF 桌面投影应用（主仓）+ Cloudflare Pages Functions 管理后台（`cf-admin/`）的混合仓库。
主业务围绕多屏投影、文本/圣经渲染、播放状态机、SQLite 持久化；后台围绕授权验证、设备绑定、风控限流。

## STRUCTURE
```text
./
├── UI/              # MainWindow 分部类与自定义控件（最高复杂度）
├── Managers/        # 投影/视频/导入导出编排
├── Database/        # EF Core 上下文、实体、DTO、兼容升级
├── Services/        # 播放/圣经/鉴权等业务服务
├── Core/            # DI 注册、渲染与资源加载基础设施
├── Repositories/    # 数据访问接口与实现
├── cf-admin/        # Cloudflare Pages Functions 授权后台
└── BuildTools/      # 资源打包工具（生成 Resources.pak）
```

## WHERE TO LOOK
| Task | Location | Notes |
|------|----------|-------|
| 应用启动/DI | `App.xaml.cs`, `Core/ServiceCollectionExtensions.cs` | `OnStartup` + `AddCanvasCastServices()` |
| 主窗口功能入口 | `UI/MainWindow.xaml`, `UI/MainWindow.*.cs` | 功能按分部类拆分，不是单文件 |
| 投影/多屏/视频 | `Managers/ProjectionManager.cs`, `Managers/VideoPlayerManager.cs` | 投影链路核心热点 |
| 播放状态与服务 | `Services/StateMachine/PlaybackStateMachine.cs`, `Services/PlaybackServiceFactory.cs` | 状态机约束 + 模式工厂 |
| 数据模型与兼容升级 | `Database/CanvasDbContext.cs`, `Database/DatabaseManager.cs` | 非 EF Migration，手工兼容字段 |
| 授权后台 API | `cf-admin/functions/api/**`, `cf-admin/functions/_middleware.js` | 文件路由 + D1 + KV |

## CODE MAP
LSP 在当前环境不可用（缺少 `csharp-ls`），以下为结构中心点（基于引用与分层）:

| Symbol/File | Type | Location | Refs | Role |
|-------------|------|----------|------|------|
| `AddCanvasCastServices` | bootstrap | `Core/ServiceCollectionExtensions.cs` | high | 全仓依赖注册根 |
| `MainWindow` partial cluster | UI hub | `UI/MainWindow.*.cs` | high | 主界面功能编排中心 |
| `ProjectionManager` | manager hub | `Managers/ProjectionManager.cs` | high | 多屏同步/投影渲染中心 |
| `CanvasDbContext` | data hub | `Database/CanvasDbContext.cs` | high | 主数据上下文与索引定义 |
| `verify` API | edge entry | `cf-admin/functions/api/auth/verify.js` | high | 授权校验与设备绑定入口 |

## CONVENTIONS
- 主 UI 采用 **MainWindow 分部类** 承载功能域（Bible/TextEditor/Projection/ProjectTree 等），不是纯 MVVM 拆分。
- 服务解析常用 `App.GetRequiredService<T>()`（Service Locator 风格），MainWindow 在运行时手动接线。
- 数据库升级采用 `EnsureCreated + EnsureXxxColumnExists/ALTER TABLE` 兼容流程，不使用 `dotnet ef migrations`。
- 调试日志统一 `#if DEBUG` 包裹；Release 侧避免未使用异常变量警告。

## ANTI-PATTERNS (THIS PROJECT)
- 不要对 `Canvas` 子元素直接 `Arrange()`；需用 `VisualBrush` 或临时移除再渲染。
- 不要依赖依赖属性的“上次状态”；复用控件时必须显式重置布局属性。
- 不要只 `Storyboard.Stop()`；需 `BeginAnimation(property, null)` 释放动画占用。
- 不要在未明确要求时自动 Git 提交/推送。
- 不要臆测加“优化逻辑”（尤其迁移路径）；先对齐既有实现。

## UNIQUE STYLES
- `MainWindow` 文件名遵循 `MainWindow.<Domain>.cs`（如 `MainWindow.Bible.Navigation.cs`）。
- `Database/Models` 下按 `Bible/`、`DTOs/`、`Enums/` 细分模型域。
- `cf-admin/functions/api/{auth,admin,user}` 采用 Cloudflare Pages Functions 文件路由约定。

## COMMANDS
```bash
# Desktop app
dotnet restore
dotnet build ImageColorChanger.csproj --configuration Release --no-restore
dotnet publish ImageColorChanger.csproj -c Release -r win-x64 -o publish --self-contained false

# Cloudflare admin
cd cf-admin
npm run dev
npm run deploy
npm run db:init
npm run db:prod
```

## NOTES
- `ImageColorChanger.csproj` 含 `PackResources` 与发布清理 Target，构建会生成 `Resources.pak`。
- CI 位于 `.github/workflows/build-release.yml` 与 `.github/workflows/build-release dev.yml`，主分支与 Dev 分支发布策略不同。
- 当前仓库存在 `_research/` 镜像内容，不属于主产品代码路径。
