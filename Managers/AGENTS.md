# MANAGERS KNOWLEDGE BASE

继承根目录 `AGENTS.md`；本文件仅补充 `Managers/` 局部规则。

## OVERVIEW
`Managers/` 是桌面端编排层：管理投影窗口、视频播放链路、文本项目导入导出与关键帧导航。

## WHERE TO LOOK
| Task | Location | Notes |
|------|----------|-------|
| 投影主链路 | `Managers/ProjectionManager.cs` | 多屏检测、同步节流、文字/视频叠加 |
| D3D11 视频路径 | `Managers/ProjectionManager_D3D11Video.cs`, `Managers/VlcD3D11Renderer.cs` | 锁定模式视频渲染 |
| 视频播放控制 | `Managers/VideoPlayerManager.cs`, `Managers/VideoBackgroundManager.cs` | 主屏/投影播放器协同 |
| 文本项目与幻灯片 | `Managers/TextProjectManager.cs`, `Managers/SlideImportManager.cs`, `Managers/SlideExportManager.cs` | 项目载入与 `.hdp` 进出 |
| 关键帧子域 | `Managers/Keyframes/*.cs` | 仓储、导航、管理 |
| 屏幕与系统辅助 | `Managers/WpfScreenHelper.cs`, `Managers/ScreenInfo.cs` | DPI 与显示器信息 |

## CONVENTIONS
- `Managers` 负责“编排”，纯算法优先放 `Services/Algorithms`，持久化优先放 `Repositories/`。
- UI 事件回调进入 Manager 后，如需触控件，必须回 `Dispatcher`。
- `ProjectionManager` 与 `VideoPlayerManager` 改动要保持主屏/投影双路径一致。
- 同步节流（约 8ms）属于既有性能假设，变更前先验证投影流畅性。

## ANTI-PATTERNS
- 不要在 Manager 里直接塞业务配置常量到 UI 文件。
- 不要在修复 bug 时顺带重构整条播放/投影链路（最小修复原则）。
- 不要绕过已有 manager 接口直接操作底层播放器实例。
