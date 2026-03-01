# UI KNOWLEDGE BASE

继承根目录 `AGENTS.md`；本文件仅补充 UI 局部规则。

## OVERVIEW
`UI/` 是仓库最高复杂度区域：`MainWindow` 通过大量分部类承载业务编排，而非纯 MVVM 页面拆分。

## WHERE TO LOOK
| Task | Location | Notes |
|------|----------|-------|
| 启动与主窗口壳 | `UI/MainWindow.xaml`, `UI/MainWindow.xaml.cs` | 主布局 + 主入口事件 |
| 服务接线/退订 | `UI/MainWindow.ServiceWiring.cs` | `GetRequiredService`、事件订阅与释放 |
| 圣经功能 | `UI/MainWindow.Bible.*.cs` | Core/Navigation/Search/Settings/Helpers |
| 文本编辑与幻灯片 | `UI/MainWindow.TextEditor.*.cs` | Core/Toolbar/Slides/Canvas/UI/Helpers |
| 投影交互桥接 | `UI/MainWindow.Projection*.cs`, `UI/MainWindow.UIBootstrap.cs` | 与 `Managers/ProjectionManager` 协作 |
| 拖拽与项目树 | `UI/MainWindow.DragDrop.*.cs`, `UI/MainWindow.ProjectTree.*.cs` | 外部导入 + 树操作事件 |
| 自定义控件 | `UI/Controls/*.cs`, `UI/Controls/*.xaml` | 富文本、样式面板、拼音提示等 |

## CONVENTIONS
- 新功能优先落到对应 `MainWindow.<Domain>.cs`，保持域边界，不把业务回填到 `MainWindow.xaml.cs`。
- 分部类共享同一实例字段，新增状态前先确认是否已有同义字段可复用。
- UI 状态更新默认走 `Dispatcher`；跨线程回调不得直接改控件。
- 诊断日志统一 `#if DEBUG`，Release 不保留调试输出副作用。
- 调试与修复必须遵循 `docs/rules/diagnostic-first.md`（先证据后修改，禁止盲改）。
- 默认启用强化调试技能：`skills/systematic-debugging/SKILL.md`（遇到异常先复现与取证，再做单点修复）。

## ANTI-PATTERNS
- 禁止直接 `Arrange()` Canvas 子元素做投影渲染。
- 禁止复用控件时省略 `Stretch/Alignment/Margin` 的显式重置。
- 禁止只 `Storyboard.Stop()` 不清理动画占用属性。
- 禁止把事件解绑遗漏到 `Closing` 之外，避免重复订阅和资源泄漏。
