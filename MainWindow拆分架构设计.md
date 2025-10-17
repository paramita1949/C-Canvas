# MainWindow 拆分架构设计

版本：1.0 | 日期：2025-10-17

---

## 📐 架构概览

### 当前状态
```
┌─────────────────────────────────────┐
│     MainWindow.xaml.cs              │
│     (6003行 - 上帝类)               │
│                                     │
│  • 40+ 字段                         │
│  • 200+ 方法                        │
│  • 17个 #region                     │
│  • 混合多种职责                     │
└─────────────────────────────────────┘
```

### 目标状态
```
                    ┌──────────────────────┐
                    │   MainWindow.Core    │
                    │    (核心协调层)      │
                    │      <1000行         │
                    └──────────┬───────────┘
                               │
         ┌─────────────────────┼─────────────────────┐
         │                     │                     │
    ┌────▼────┐          ┌────▼────┐          ┌────▼────┐
    │  Media  │          │Projection│         │TreeView │
    │  (600)  │          │  (400)   │         │  (500)  │
    └─────────┘          └──────────┘         └─────────┘
         │                     │                     │
    ┌────▼────┐          ┌────▼────┐          ┌────▼────┐
    │ Image   │          │  Event  │          │Keyframe │
    │Processing│         │Handlers │         │ (1295)  │
    │  (600)  │          │  (500)  │         │ [已存在] │
    └─────────┘          └──────────┘         └─────────┘
         │                                          │
    ┌────▼────┐                              ┌────▼────┐
    │Original │                              │  Text   │
    │  (558)  │                              │ Editor  │
    │ [已存在] │                              │[已存在] │
    └─────────┘                              └─────────┘
```

---

## 🎯 部分类详细设计

### MainWindow.Core.cs（核心协调层）

#### 职责
- 窗口生命周期管理
- Manager实例化和配置
- 全局状态协调
- 基础UI初始化

#### 包含内容
```csharp
public partial class MainWindow : Window, INotifyPropertyChanged
{
    #region 字段声明（仅引用）
    // Manager引用
    private DatabaseManager _dbManager;
    private ConfigManager _configManager;
    // ... 其他Manager
    
    // 核心状态
    private int _currentImageId;
    private string _imagePath;
    #endregion

    #region 构造函数和初始化
    public MainWindow()
    {
        InitializeComponent();
        InitializeGpuProcessor();
        InitializeManagers();
        InitializeUI();
    }
    
    private void InitializeManagers() { }
    private void InitializeGpuProcessor() { }
    private void InitializeUI() { }
    #endregion

    #region 窗口事件
    private void Window_Closing() { }
    protected override void OnClosed() { }
    #endregion

    #region 配置管理
    private void LoadSettings() { }
    private void SaveSettings() { }
    #endregion

    #region 辅助方法
    public void ShowStatus(string message) { }
    #endregion
}
```

#### 预计行数：~800行

---

### MainWindow.Media.cs（媒体播放模块）

#### 职责
- 视频/音频播放器管理
- 媒体控制（播放/暂停/停止）
- 播放列表管理
- 进度条处理
- 视频轨道检测

#### 包含内容
```csharp
public partial class MainWindow
{
    #region 媒体播放字段
    private VideoPlayerManager _videoPlayerManager;
    private VideoView _mainVideoView;
    private bool _isUpdatingProgress;
    private string _pendingProjectionVideoPath;
    private DispatcherTimer _projectionTimeoutTimer;
    #endregion

    #region 媒体播放器初始化
    private void InitializeVideoPlayer()
    {
        // 创建VideoPlayerManager
        // 订阅事件
        // 设置VideoView
    }
    #endregion

    #region 媒体播放事件处理
    private void VideoPlayerManager_VideoTrackDetected(object sender, bool hasVideo)
    private void OnVideoPlayStateChanged(object sender, bool isPlaying)
    private void OnVideoMediaChanged(object sender, string mediaPath)
    private void OnVideoMediaEnded(object sender, EventArgs e)
    private void OnVideoProgressUpdated(object sender, (float, long, long) progress)
    #endregion

    #region 媒体控制方法
    private void LoadAndDisplayVideo(string videoPath)
    private void SwitchToImageMode()
    private void BuildPlaylist(string currentMediaPath)
    #endregion

    #region 媒体投屏辅助
    private void EnableVideoProjection()
    private void DisableVideoProjection()
    #endregion
}
```

#### 方法清单（约15个）
1. InitializeVideoPlayer()
2. VideoPlayerManager_VideoTrackDetected()
3. OnVideoPlayStateChanged()
4. OnVideoMediaChanged()
5. OnVideoMediaEnded()
6. OnVideoProgressUpdated()
7. LoadAndDisplayVideo()
8. SwitchToImageMode()
9. BuildPlaylist()
10. EnableVideoProjection()
11. DisableVideoProjection()
12. VideoView相关事件处理（3-4个）

#### 依赖
- VideoPlayerManager
- ProjectionManager（投屏时）
- ImageProcessor（切换回图片时）

#### 预计行数：~600行

---

### MainWindow.Projection.cs（投影管理模块）

#### 职责
- 投影窗口管理
- 投影内容同步
- 投影相关事件处理
- 全局热键管理（投影模式）

#### 包含内容
```csharp
public partial class MainWindow
{
    #region 投影相关字段
    // ProjectionManager引用在Core中
    // 投影状态变量
    #endregion

    #region 投影控制方法
    private void UpdateProjection()
    private void OnProjectionStateChanged(object sender, bool isActive)
    #endregion

    #region 全局热键
    private void EnableGlobalHotKeys()
    private void DisableGlobalHotKeys()
    #endregion
}
```

#### 方法清单（约8个）
1. UpdateProjection()
2. OnProjectionStateChanged()
3. EnableGlobalHotKeys()
4. DisableGlobalHotKeys()
5. 投影窗口初始化相关（4个）

#### 依赖
- ProjectionManager
- GlobalHotKeyManager
- ImageProcessor

#### 预计行数：~400行

---

### MainWindow.TreeView.cs（项目树模块）

#### 职责
- 项目树加载和显示
- 文件夹/文件选择
- 拖拽排序
- 右键菜单
- 搜索功能

#### 包含内容
```csharp
public partial class MainWindow
{
    #region 项目树字段
    private ObservableCollection<ProjectTreeItem> _projectTreeItems;
    private ProjectTreeItem _draggedItem;
    private ProjectTreeItem _dragOverItem;
    private bool _isDragInProgress;
    #endregion

    #region 项目树加载
    private void LoadProject()
    private void LoadTextProjects()
    private void LoadFolderFromTreeItem()
    #endregion

    #region TreeView事件
    private void ProjectTreeView_SelectedItemChanged()
    private void ProjectTreeView_MouseDoubleClick()
    private void ProjectTreeView_PreviewMouseLeftButtonDown()
    private void ProjectTreeView_PreviewMouseMove()
    private void ProjectTreeView_PreviewDrop()
    private void ProjectTreeView_PreviewDragOver()
    #endregion

    #region 拖拽功能
    private void StartDrag()
    private void HandleDrop()
    private void UpdateSortOrder()
    #endregion

    #region 右键菜单
    private void ShowTreeViewContextMenu()
    private void CreateFolderMenuItem()
    private void CreateFileMenuItem()
    // ... 其他菜单项
    #endregion

    #region 搜索功能
    private void SearchBox_TextChanged()
    private void PerformSearch()
    #endregion
}
```

#### 方法清单（约30个）
- 项目加载：3个
- TreeView事件：8个
- 拖拽功能：5个
- 右键菜单：10个
- 搜索功能：4个

#### 依赖
- DatabaseManager
- ImportManager
- SearchManager
- SortManager

#### 预计行数：~500行

---

### MainWindow.ImageProcessing.cs（图像处理模块）

#### 职责
- 图片加载和显示
- 图片缩放
- 图片拖动
- 变色效果
- 图片保存

#### 包含内容
```csharp
public partial class MainWindow
{
    #region 图像处理字段
    private ImageProcessor _imageProcessor;
    private double _currentZoom;
    private bool _isDragging;
    private Point _dragStartPoint;
    private bool _isColorEffectEnabled;
    private SKColor _currentTargetColor;
    private string _currentTargetColorName;
    #endregion

    #region 图像加载
    private void LoadImage(string imagePath)
    private void ClearImageDisplay()
    #endregion

    #region 缩放功能
    private void ImageScrollViewer_PreviewMouseWheel()
    private void ZoomIn()
    private void ZoomOut()
    private void ResetZoom()
    #endregion

    #region 拖动功能
    private void ImageDisplay_MouseLeftButtonDown()
    private void ImageDisplay_MouseMove()
    private void ImageDisplay_MouseLeftButtonUp()
    #endregion

    #region 变色效果
    private void ToggleColorEffect()
    private void SelectColor()
    private void ApplyColorEffect()
    #endregion

    #region 图片保存
    private void SaveImage()
    private void SaveImageAs()
    #endregion
}
```

#### 方法清单（约20个）
- 图像加载：2个
- 缩放功能：5个
- 拖动功能：5个
- 变色效果：5个
- 图片保存：3个

#### 依赖
- ImageProcessor
- ImageSaveManager
- ConfigManager

#### 预计行数：~600行

---

### MainWindow.EventHandlers.cs（事件处理模块）

#### 职责
- 窗口事件处理
- 键盘事件处理
- 鼠标事件处理
- 菜单事件处理
- 通用UI事件

#### 包含内容
```csharp
public partial class MainWindow
{
    #region 键盘事件
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // ESC键处理
        // 方向键处理
        // PageUp/PageDown处理
        // F2键处理
    }
    #endregion

    #region 窗口事件
    private void Window_SizeChanged()
    private void ImageScrollViewer_SizeChanged()
    private void NavigationSplitter_DragCompleted()
    #endregion

    #region 菜单事件
    private void BtnImportFolder_Click()
    private void BtnExportImage_Click()
    private void BtnSettings_Click()
    private void BtnAbout_Click()
    // ... 其他菜单按钮
    #endregion

    #region 右键菜单事件
    private void ImageScrollViewer_RightClick()
    private void SetScrollSpeed_Click()
    private void SetScrollEasing_Click()
    #endregion

    #region 通用UI事件
    private void OnPropertyChanged(string propertyName)
    #endregion
}
```

#### 方法清单（约25个）
- 键盘事件：1个（内部多个分支）
- 窗口事件：5个
- 菜单事件：12个
- 右键菜单：5个
- 其他UI事件：2个

#### 依赖
- 几乎所有Manager（协调层）
- 其他部分类的方法

#### 预计行数：~500行

---

### MainWindow.Keyframe.cs（关键帧模块）[已存在]

#### 当前状态
- ✅ 已拆分
- 1295行
- 职责清晰

#### 职责
- 关键帧录制
- 关键帧播放
- 关键帧编辑
- 滚动动画控制

---

### MainWindow.Original.cs（原图模式）[已存在]

#### 当前状态
- ✅ 已拆分
- 558行
- 职责清晰

#### 职责
- 原图模式管理
- 相似图片查找
- 相似图片切换
- 原图录制和播放

---

### MainWindow.TextEditor.cs（文本编辑器）[已存在]

#### 当前状态
- ✅ 已拆分
- 职责清晰

#### 职责
- 文本项目管理
- 幻灯片编辑
- 文本播放

---

## 🔗 模块间依赖关系

### 依赖层次
```
Level 1: MainWindow.Core.cs
  │
  ├─→ 管理所有Manager实例
  ├─→ 提供全局状态
  └─→ 协调其他模块

Level 2: 功能模块（可独立工作）
  │
  ├─→ MainWindow.Media.cs
  ├─→ MainWindow.Projection.cs
  ├─→ MainWindow.TreeView.cs
  ├─→ MainWindow.ImageProcessing.cs
  ├─→ MainWindow.Keyframe.cs [已存在]
  ├─→ MainWindow.Original.cs [已存在]
  └─→ MainWindow.TextEditor.cs [已存在]

Level 3: 事件处理层（协调其他模块）
  │
  └─→ MainWindow.EventHandlers.cs
```

### 共享数据
通过MainWindow.Core.cs共享：
- _dbManager
- _configManager
- _currentImageId
- _imagePath
- 其他全局状态

### 通信方式
1. **直接方法调用**：同一部分类内
2. **共享字段访问**：通过Core中的字段
3. **事件机制**：Manager层事件订阅

---

## 📏 度量标准

### 代码行数目标

| 文件 | 当前 | 目标 | 减少 |
|------|------|------|------|
| MainWindow.xaml.cs | 6003 | <1000 | 5000+ |
| MainWindow.Core.cs | - | ~800 | 新建 |
| MainWindow.Media.cs | - | ~600 | 新建 |
| MainWindow.Projection.cs | - | ~400 | 新建 |
| MainWindow.TreeView.cs | - | ~500 | 新建 |
| MainWindow.ImageProcessing.cs | - | ~600 | 新建 |
| MainWindow.EventHandlers.cs | - | ~500 | 新建 |
| MainWindow.Keyframe.cs | 1295 | 1295 | 已存在 |
| MainWindow.Original.cs | 558 | 558 | 已存在 |
| MainWindow.TextEditor.cs | ? | ? | 已存在 |

**总计**：约6000行 → 分散到10个文件

### 复杂度目标
- **单个方法**: <50行
- **方法复杂度**: <10
- **类耦合度**: <20

---

## 🎨 命名规范

### 文件命名
```
MainWindow.Core.cs          // 核心
MainWindow.Media.cs         // 媒体
MainWindow.Projection.cs    // 投影
MainWindow.TreeView.cs      // 树视图
MainWindow.ImageProcessing.cs  // 图像处理
MainWindow.EventHandlers.cs    // 事件
```

### Region命名
```csharp
#region 字段
#region 初始化
#region 事件处理
#region 控制方法
#region 辅助方法
```

### 方法命名
- 事件处理：`OnXxxChanged`, `XxxControl_Click`
- 初始化：`InitializeXxx`
- 业务逻辑：动词开头 `LoadImage`, `SaveSettings`

---

## ⚡ 性能考虑

### 避免性能下降
1. **不增加方法调用层次**
   - 部分类是编译期合并，无运行时开销

2. **保持热路径优化**
   - 图片切换
   - 键盘响应
   - 滚动性能

3. **延迟加载**
   - 不常用功能按需初始化

---

## 🔒 向后兼容

### 保证兼容性
1. **公共API不变**
   - 保持所有public方法签名
   - 保持所有public属性

2. **XAML绑定不变**
   - 事件处理器名称不变
   - 数据绑定路径不变

3. **Manager接口不变**
   - 不修改Manager调用方式

---

## 📝 实施原则

### DO（应该做）
✅ 按功能职责拆分
✅ 保持部分类间低耦合
✅ 每个文件有明确的职责
✅ 提取共享逻辑到辅助类
✅ 充分的单元测试
✅ 渐进式重构

### DON'T（不应该做）
❌ 为了拆分而拆分
❌ 在部分类间产生循环依赖
❌ 破坏现有功能
❌ 忽略性能影响
❌ 缺少测试的重构
❌ 一次性大规模重构

---

## 🚀 开始实施

### 第一步：准备
1. 阅读本设计文档
2. 阅读TODO_中优先级执行计划.md
3. 创建feature分支

### 第二步：命名规范
1. 按检查清单执行
2. 每天提交进度

### 第三步：拆分实施
1. 按模块优先级执行
2. 一次一个模块
3. 充分测试

### 第四步：验收
1. 代码审查
2. 性能测试
3. 文档更新

---

**设计版本**: 1.0  
**最后更新**: 2025-10-17  
**状态**: ✅ 设计完成，待实施

