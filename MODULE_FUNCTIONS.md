# 咏慕投影 (CanvasCast) - 模块函数详细文档

> 用于快速定位和修改代码功能的函数级参考手册。本文档涵盖了系统的核心分层架构及各模块的关键函数说明。

---

## 📑 快速索引

- [1. UI 层 - MainWindow 分部类](#1-ui-层---mainwindow-分部类)
- [2. Core 层 - 核心引擎](#2-core-层---核心引擎)
- [3. Services 层 - 业务逻辑服务](#3-services-层---业务逻辑服务)
- [4. Managers 层 - 资源与显示管理器](#4-managers-层---资源与显示管理器)
- [5. Database 层 - 数据访问与仓储](#5-database-层---数据访问与仓储)
- [6. Utils 层 - 工具类](#6-utils-层---工具类)
- [7. ViewModels 层 - 视图模型](#7-viewmodels-层---视图模型)

---

## 1. UI 层 - MainWindow 分部类

`MainWindow` 采用了分部类（Partial Class）设计，将复杂的逻辑拆分到多个文件中。

### MainWindow.Bible.cs (圣经功能)
- **InitializeBibleService()**: 初始化圣经服务，加载 SQLite 数据库。
- **LoadChapterVersesAsync(string book, int chapter)**: 异步加载指定章节的经文。
- **ProcessPinyinEscapeKeyAsync()**: 处理 ESC 键取消拼音搜索的逻辑。
- **AddToHistory(string book, int chapter, int start, int end)**: 管理最近 20 条历史记录槽位。

### MainWindow.Lyrics.cs (歌词/赞美诗)
- **EnterLyricsMode() / ExitLyricsMode()**: 切换专用歌词编辑 UI。
- **RenderLyricsToProjection()**: 使用高 DPI Canvas 渲染文本并同步至投影窗口。
- **SaveLyricsProject()**: 将当前歌词文本持久化到数据库。

### MainWindow.TextEditor.cs (文本编辑与画布)
- **CreateTextProjectAsync(string name)**: 初始化新文本项目。
- **AddTextBoxToCanvas(DraggableTextBox textBox)**: 动态添加可拖拽文本框。
- **UpdateSplitLayout(ViewSplitMode mode)**: 重绘画布分割线（水平/垂直/四分屏）。

---

## 2. Core 层 - 核心引擎

### SkiaTextRenderer.cs (🔥 核心渲染引擎)
- **SKBitmap RenderTextBox(TextBoxRenderContext context)**: 渲染通用文本框。支持阴影、背景圆角、边框、下划线及编辑态光标。
- **SKBitmap RenderBibleText(BibleRenderContext context)**: 专门渲染圣经经文。处理“标题+经文”布局，支持自动计算内容高度。
- **SKBitmap RenderLyrics(LyricsRenderContext context)**: 渲染歌词。支持自动换行和三种对齐方式。
- **private SKFont CreateFont(TextStyle style)**: 根据配置创建 SkiaSharp 字体对象，支持伪加粗和字间距。

### ImageProcessor.cs (图像处理)
- **LoadImage(string path)**: 优化的图像流加载，集成 LRU 缓存。
- **ApplyYellowTextEffect(SKBitmap bitmap)**: 🔥 核心算法。将白字黑底的图像转换为目标主题颜色。
- **private bool DetectBackgroundType(SKBitmap bitmap)**: 多点采样算法，判断图像是否为深色背景。

### TextLayoutEngine.cs (布局计算)
- **TextLayout CalculateLayout(string text, TextStyle style, float maxWidth)**: 计算文本布局，返回每行文本、位置及总尺寸。
- **List<string> WrapText(string text, SKFont font, float maxWidth)**: 执行核心自动换行算法，处理中英文混合及标点挤压。

### PakManager.cs (资源包管理)
- **void LoadPak(string path)**: 解析 `.pak` 资源包文件头并建立内存索引。
- **byte[] GetResource(string path)**: 从索引中提取原始字节流。

---

## 3. Services 层 - 业务逻辑服务

### BibleService.cs (圣经数据服务)
- **Task<List<BibleVerse>> GetChapterVersesAsync(int book, int chapter)**: 获取整章经文。
  - **关键逻辑**: 自动处理 `-` 符号的节，将其合并到前一节（例如：10、11 节合并）。
- **Task<List<BibleSearchResult>> SearchVersesAsync(string keyword, int? bookId)**: 关键词全文搜索，支持限定书卷。
- **void UpdateDatabasePath()**: 切换译本（如和合本、精简本）时更新路径并清空内存缓存。

### CompositePlaybackService.cs (🔥 合成播放服务)
- **Task StartPlaybackAsync(int imageId)**: 启动合成播放。自动根据是否有关键帧或录制数据选择 4 种播放模式之一。
- **Task UpdateTotalAndRestartAsync()**: ⚠️ 手动校准。将当前已播放位置设定为新的总时长并重新循环。
- **void SetSpeed(double speed)**: 设置播放倍速（0.1x - 5.0x），实时调整正在进行的滚动动画。

---

## 4. Managers 层 - 资源与显示管理器

### ProjectionManager.cs (投影管理器)
- **bool ToggleProjection()**: 切换投影窗口的开关状态。
- **void UpdateBibleProjectionWithVisualBrush(ScrollViewer bibleScrollViewer)**: 🆕 使用 `VisualBrush` 方式投影，确保投影效果与主屏幕 100% 像素一致。
- **void SyncLyricsScroll(ScrollViewer lyricsScrollViewer)**: 实时同步主界面滚动条位置到投影窗口。
- **void UpdateProjectionWithVideo(VisualBrush videoBrush, SKBitmap textLayer)**: 渲染视频背景叠加透明文本层的复合画面。

### VideoPlayerManager.cs (视频播放管理器)
- **void InitializeMediaPlayer(VideoView videoView)**: 基于 LibVLC 初始化媒体播放器并绑定视图。
- **bool Play(string mediaPath = null)**: 播放指定路径的媒体。包含小窗检测和修复逻辑。
- **void EnableProjection() / DisableProjection()**: 处理播放器句柄在主窗口和投影窗口间的动态迁移。

---

## 5. Database 层 - 数据访问与仓储

### DatabaseManager.cs (高级数据库操作)
- **void DeleteFolder(int folderId, bool forceDelete = false)**: 删除文件夹。
  - **🔒 安全逻辑**: 若开启 `forceDelete`，则禁用外键约束 (`PRAGMA foreign_keys = OFF`) 以执行极速级联删除。
- **List<MediaFile> AddMediaFiles(IEnumerable<string> paths, int? folderId)**: 使用 `BulkInsert` 批量插入，性能较常规 EF 提升 100 倍。

### RepositoryBase<TEntity> (仓储基类)
- **Task<TEntity> GetByIdAsync(int id)**: 通用按 ID 查询。
- **Task UpdateAsync(TEntity entity)**: 通用异步更新。

---

## 6. Utils 层 - 工具类

### AnimationHelper.cs (动画辅助)
- **static Storyboard AnimateScroll(ScrollViewer sv, double from, double to, TimeSpan duration)**: 针对 120 FPS 优化的平滑滚动动画，支持“超级平滑”贝塞尔曲线。
- **static void FadeIn(UIElement element, TimeSpan duration)**: 通用渐显动画。

### KeyboardShortcutManager.cs (快捷键分发)
- **Task<bool> HandleKeyAsync(Key key, ModifierKeys modifiers)**: 核心分发器。根据当前 UI 状态（圣经模式、编辑模式、播放模式）分发按键逻辑。

### ImageCache.cs (LRU 缓存)
- **Task<BitmapImage> GetOrLoadAsync(string path)**: 异步加载图像。若缓存命中则立即返回，否则执行加载并维护内存淘汰队列。

---

## 7. ViewModels 层 - 视图模型

### PlaybackControlViewModel.cs (播放控制中心)
- **Task SetCurrentImageAsync(int imageId, PlaybackMode mode)**: 核心业务逻辑。切换当前操作的图片，并自动检测脚本完整性。
- **Task RecordKeyframeTimeAsync(int keyframeId)**: 在录制模式下，记录特定关键帧的触发时刻戳。
- **string GetFormattedScriptInfoAsync()**: 获取并格式化当前播放脚本的元数据（时长、帧数等）。

---

## 🔒 标注说明
- 🔥 **高频使用**: 系统核心路径上的关键函数。
- ⚠️ **复杂逻辑**: 包含精细算法或多线程同步，修改时需极其谨慎。
- 🔒 **安全相关**: 涉及权限校验、设备绑定或敏感数据删除。
