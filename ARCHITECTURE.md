# 咏慕投影 (CanvasCast) 架构文档

本文档详细介绍了咏慕投影（CanvasCast）项目的架构设计、技术栈选择、核心组件以及开发指南。旨在为开发者提供清晰的项目全景图，确保代码的可维护性和扩展性。

---

## 1. 项目概述 (Project Overview)

**咏慕投影 (CanvasCast)** 是一款基于 .NET 8.0 开发的高性能专业 WPF 桌面应用程序，专门为教堂、会议及各种演示场景设计。它支持多屏幕投影、圣经经文显示、歌词同步、视频/图片播放以及基于 SkiaSharp 的高性能文本渲染。

### 核心功能
*   **多屏幕投影管理**：支持主屏控制与投影屏同步显示，具备像素级对齐能力。
*   **高性能渲染**：利用 SkiaSharp 进行复杂的文本和图像处理，确保在高分辨率下依然流畅。
*   **多媒体支持**：基于 LibVLCSharp 集成，支持 4K 视频播放及硬件加速渲染。
*   **播放状态管理**：内置状态机，严格控制录制与播放流程。
*   **资源打包系统**：自定义 PAK 格式资源包，优化部署体积和加载速度。

---

## 2. 技术栈架构 (Technology Stack)

### 基础框架
*   **开发环境**：.NET 8.0 SDK (Windows x64)
*   **UI 框架**：WPF (Windows Presentation Foundation)
*   **UI 库**：Material Design In XAML (5.1.0)
*   **MVVM 工具**：CommunityToolkit.Mvvm (8.2.2)

### 图形与渲染
*   **2D 渲染**：SkiaSharp (3.119.1) - 用于文本效果、投影层合成。
*   **视频渲染**：LibVLCSharp (3.9.4) + SharpDX (4.2.0) - 实现 Direct3D11 视频渲染。
*   **硬件加速**：通过 D3DImage 桥接 GPU 渲染内容到 WPF。

### 数据持久化
*   **ORM 框架**：Entity Framework Core 8.0.0
*   **数据库**：SQLite (本地 `pyimages.db` 与 `bible.db`)
*   **性能增强**：EFCore.BulkExtensions.Sqlite (用于大批量数据导入)

### 依赖注入 (DI)
*   **容器**：Microsoft.Extensions.DependencyInjection 8.0.0
*   **缓存**：Microsoft.Extensions.Caching.Memory 8.0.1 (用于经文及渲染位图缓存)

---

## 3. 架构模式与设计哲学 (Architecture Patterns)

### 3.1 核心架构：MVVM + 分层
项目采用标准的 MVVM 模式，并结合分层架构：
1.  **Presentation (UI)**：WPF 视图与 `MainWindow` 的多个部分类（Partial Classes）。
2.  **ViewModels**：处理 UI 逻辑与数据绑定。
3.  **Managers (编排层)**：负责高层业务流转（如 `ProjectionManager`, `VideoPlayerManager`）。
4.  **Services (业务逻辑层)**：执行具体业务算法（如 `BibleService`, `PlaybackService`）。
5.  **Repositories (数据访问层)**：抽象数据库操作。
6.  **Database**：EF Core 上下文与实体模型。

### 3.2 依赖注入 (DI) 生命周期
所有核心组件均通过 `ServiceCollectionExtensions.cs` 进行注册。层级划分如下：
*   **Singleton (单例)**：无状态或全局共享的服务。如 `SkiaTextRenderer` (渲染引擎)、`PlaybackStateMachine` (状态机)、`ConfigManager` (配置)。
*   **Scoped (范围)**：按业务请求范围注册。如 `CanvasDbContext` (数据库上下文)、`IKeyframeRepository` (关键帧仓库)。
*   **Transient (瞬时)**：每次请求创建新实例。如 `PlaybackControlViewModel` (UI 逻辑绑定)。

### 3.3 状态驱动设计 (State-Driven Design)
系统的核心逻辑受播放状态机驱动，确保操作的原子性和合法性。例如，系统禁止在 `Recording` 状态下直接跳转到 `Paused` 以外的非法状态。

---

## 4. 目录结构详解 (Directory Structure)

```
D:\img\Canvas\
├── App.xaml / App.xaml.cs          # 程序入口，DI 容器初始化
├── ImageColorChanger.csproj        # 项目配置，包含自定义打包构建任务
├── /UI/                            # WPF 视图层
│   ├── MainWindow.xaml             # 主窗口布局
│   ├── MainWindow.*.cs             # 关键点：使用分部类按模块拆分主窗口逻辑
│   └── /Controls/                  # 跨页面复用的自定义 UI 控件
├── /Core/                          # 核心引擎与底层逻辑
│   ├── SkiaTextRenderer.cs         # SkiaSharp 渲染引擎核心
│   ├── GPUContext.cs               # GPU 上下文管理
│   ├── PakManager.cs               # 自定义 PAK 资源包读取
│   └── ServiceCollectionExtensions.cs  # 全局依赖注入注册中心
├── /Services/                      # 业务逻辑服务
│   ├── /Implementations/           # 服务具体实现
│   ├── /Interfaces/                # 服务契约
│   └── /StateMachine/              # 播放状态机实现
├── /Managers/                      # 高层管理器（中台）
│   ├── ProjectionManager.cs        # 核心：负责多显示器同步与投影窗口生命周期
│   ├── VideoPlayerManager.cs       # 视频播放生命周期管理
│   └── ImportManager.cs            # 媒体资源导入逻辑
├── /Database/                      # 数据持久化层
│   ├── CanvasDbContext.cs          # EF Core 上下文与性能优化配置
│   ├── /Models/                    # 数据库实体定义
│   └── /Repositories/              # 数据仓库实现
├── /ViewModels/                    # MVVM 视图模型
├── /Utils/                         # 工具类（缓存、日志、DPI 助手等）
└── /BuildTools/                    # 构建工具（PAK 打包器源代码）
```

---

## 5. 核心组件详解 (Core Components)

### 5.1 SkiaTextRenderer (文本渲染引擎)
该组件用于替代 WPF 原生文本渲染，提供工业级的效果支持。
*   **两步渲染法**：在处理圣经/歌词时，系统先预计算所有文本块的布局和总高度，再动态创建对应尺寸的 `SKBitmap` 进行一次性绘制。
*   **渲染流水线**：
    1.  **缓存检查**：通过 `GetCacheKey()` 在 `IMemoryCache` 中检索位图。
    2.  **装饰绘制**：依次绘制阴影 (Shadow)、背景 (Background) 和边框 (Border)。
    3.  **文本绘制**：逐行绘制文本，支持字间距 (LetterSpacing) 和行间距 (LineSpacing) 调整。

```csharp
// 核心绘制逻辑片段
foreach (var line in layout.Lines) {
    float x = CalculateAlignmentX(context, line);
    canvas.DrawText(line.Text, x, y, context.Alignment, font, paint);
}
```

### 5.2 ProjectionManager (投影管理器)
负责管理多屏检测及内容同步。
*   **同步性能**：采用 8ms (约 120FPS) 的节流计时器，确保同步过程丝滑且不占用过多 CPU。
*   **Airspace 方案**：针对 WPF 传统的“空气域”问题（视频无法被常规 UI 覆盖），项目通过 `VlcD3D11Renderer` 将视频直接渲染到显存，并作为 `D3DImage` 在 WPF 中呈现。
*   **镜像技术**：使用 `RenderTargetBitmap` 对主屏内容进行即时快照，并同步到投影窗口。

### 5.3 PlaybackStateMachine (播放状态机)
通过硬编码的状态转移矩阵确保逻辑严密性。
```csharp
private readonly Dictionary<PlaybackStatus, HashSet<PlaybackStatus>> _validTransitions = new() {
    [PlaybackStatus.Idle] = new() { PlaybackStatus.Recording, PlaybackStatus.Playing },
    [PlaybackStatus.Playing] = new() { PlaybackStatus.Paused, PlaybackStatus.Stopped, PlaybackStatus.Idle },
    [PlaybackStatus.Paused] = new() { PlaybackStatus.Playing, PlaybackStatus.Stopped }
};
```

---

## 6. 数据流向 (Data Flow)

### 6.1 图像/文本渲染流
1.  **用户触发**：在 UI 输入或选择媒体。
2.  **渲染请求**：`ViewModel` 构造 `RenderContext`。
3.  **引擎合成**：`SkiaTextRenderer` 生成位图，存入 `ImageCache`。
4.  **分发同步**：`ProjectionManager` 捕获变更，将位图推送到辅助显示器的 `Image` 控件。

### 6.2 同步滚动流
1.  **主屏滚动**：用户滚动主界面 `ScrollViewer`。
2.  **位置捕获**：`ProjectionManager` 监听 `ScrollChanged`。
3.  **偏移计算**：根据比例或像素值计算目标位置。
4.  **投影更新**：调用 `ScrollToVerticalOffset` 完成视觉同步。

---

## 7. 数据库设计 (Database Schema)

项目使用 SQLite，并在 `CanvasDbContext` 中集成了多项企业级优化：
*   **性能配置**：启用 `PRAGMA journal_mode=WAL` (预写日志) 允许并发读写；设置 `PRAGMA synchronous=NORMAL` 优化磁盘写入。
*   **索引优化**：在 `MediaFiles.Path`, `Keyframes.ImageId`, `Slides.SortOrder` 等高频查询字段建立了复合索引。
*   **向后兼容**：通过 `InitializeDatabase` 检查数据库版本，自动执行 `ALTER TABLE` 脚本补充缺失字段。

### 核心实体关系 (ER)
*   `Folder` (1) <---> (N) `MediaFile`
*   `MediaFile` (1) <---> (N) `Keyframe` (1) <---> (N) `KeyframeTiming`
*   `TextProject` (1) <---> (N) `Slide` (1) <---> (N) `TextElement`

---

## 8. 关键设计决策 (Key Design Decisions)

1.  **SkiaSharp vs WPF TextBlock**：SkiaSharp 允许我们在不同 DPI 的屏幕上强制保持一致的排版像素，且支持复杂的阴影和混合模式。
2.  **分部类 (Partial Classes)**：解决 `MainWindow.xaml.cs` 臃肿问题，按业务逻辑（圣经、歌词、导入、设置）进行物理隔离。
3.  **PAK 打包系统**：减少安装包碎片化，保护核心资源不被直接修改。
4.  **D3DImage 视频路径**：绕过 WPF 的渲染管线限制，直接利用 GPU 硬件加速处理 4K 视频流。

---

## 9. 开发与维护指南 (Development Guidelines)

### 9.1 扩展新业务模块
1.  在 `Core/ServiceCollectionExtensions.cs` 中注册新服务的接口与实现。
2.  若涉及数据，在 `Database/Models` 定义实体并配置索引。
3.  在 `MainWindow` 对应的分部类中添加 UI 交互逻辑。

### 9.2 调试规范
*   **日志控制**：所有调试信息必须包裹在 `#if DEBUG` 中。
*   **异常处理**：在 `Managers` 层捕获业务异常并转换为用户友好的提示。
*   **线程安全**：修改 UI 属性必须确保在 `Dispatcher` 线程执行。

---

## 10. 模块依赖图 (Module Dependency Graph)

```text
[UI 层]
   |
[ViewModel 层] <--- (CommunityToolkit.Mvvm)
   |
[Manager 层] <--- (ProjectionManager, VideoPlayerManager)
   |
[Service 层] <--- (BibleService, PlaybackService, SkiaTextRenderer)
   |
[Repository 层] <--- (Data Access Abstraction)
   |
[Database 层] <--- (EF Core / SQLite)
```

---

*咏慕投影 - 为神的话语提供最清晰的展现。*
*© 2025 咏慕投影. All Rights Reserved.*
