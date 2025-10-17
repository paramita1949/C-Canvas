# Canvas Cast 中优先级执行计划

制定日期：2025-10-17
执行周期：1-2个月
负责人：开发团队

---

## 📋 总览

### 目标
1. **统一命名规范** - 提高代码可读性和维护性
2. **拆分MainWindow类** - 解决上帝类问题，降低复杂度

### 当前状态
- MainWindow.xaml.cs: **6003行**
- 已有部分拆分：MainWindow.Keyframe.cs (1295行), MainWindow.Original.cs (558行), MainWindow.TextEditor.cs
- 字段数量：**40+**
- 方法数量：**200+**
- Region数量：**17个**

---

## 🎯 阶段一：统一命名规范（预计2周）

### 任务1.1: 字段命名统一 ⏰ 3天

**目标**：统一所有私有字段使用下划线前缀

#### 当前问题
```csharp
// 不一致的命名
private VideoPlayerManager videoPlayerManager;  // 无前缀
private Utils.GlobalHotKeyManager _globalHotKeyManager;  // 有前缀
private ImageProcessor imageProcessor;  // 无前缀
```

#### 统一方案
```csharp
// 统一使用下划线前缀（符合C#命名规范）
private VideoPlayerManager _videoPlayerManager;
private GlobalHotKeyManager _globalHotKeyManager;
private ImageProcessor _imageProcessor;
```

#### 执行步骤
1. **第1天**: 重命名Manager类相关字段（11个）
   - [ ] _videoPlayerManager
   - [ ] _dbManager
   - [ ] _configManager
   - [ ] _importManager
   - [ ] _imageSaveManager
   - [ ] _searchManager
   - [ ] _sortManager
   - [ ] _projectionManager
   - [ ] _originalManager
   - [ ] _preloadCacheManager
   - [ ] _globalHotKeyManager

2. **第2天**: 重命名核心功能字段（15个）
   - [ ] _imageProcessor
   - [ ] _imagePath
   - [ ] _currentZoom
   - [ ] _isDragging
   - [ ] _dragStartPoint
   - [ ] _isColorEffectEnabled
   - [ ] _currentTargetColor
   - [ ] _currentTargetColorName
   - [ ] _currentFolderId
   - [ ] _projectTreeItems
   - [ ] _currentImageId
   - [ ] _originalMode
   - [ ] _originalDisplayMode
   - [ ] _draggedItem
   - [ ] _dragOverItem
   - [ ] _isDragInProgress

3. **第3天**: 重命名视频和其他字段（10个）
   - [ ] _mainVideoView
   - [ ] _isUpdatingProgress
   - [ ] _pendingProjectionVideoPath
   - [ ] _projectionTimeoutTimer
   - [ ] _lastPlayModeClickTime
   - [ ] _lastMediaPrevClickTime
   - [ ] _lastMediaNextClickTime
   - [ ] 测试编译
   - [ ] 功能回归测试

#### 验收标准
- ✅ 所有私有字段使用下划线前缀
- ✅ 编译无错误
- ✅ 功能测试通过

---

### 任务1.2: 常量定义统一 ⏰ 2天

**目标**：提取魔法数字为常量

#### 当前问题
```csharp
// 硬编码的魔法数字
if ((DateTime.Now - lastPlayModeClickTime).TotalMilliseconds < 300) return;
```

#### 统一方案
```csharp
// 在Constants类或MainWindow中定义
private const int BUTTON_DEBOUNCE_MILLISECONDS = 300;
private const int PROJECTION_TIMEOUT_MILLISECONDS = 3000;
private const int VIDEO_TRACK_DETECT_TIMEOUT = 5000;
```

#### 执行步骤
1. **第1天**: 识别和提取魔法数字
   - [ ] 搜索所有硬编码的数字
   - [ ] 分类：时间、尺寸、计数等
   - [ ] 在Constants.cs中定义常量

2. **第2天**: 替换和测试
   - [ ] 替换代码中的魔法数字
   - [ ] 编译测试
   - [ ] 功能测试

#### 提取的常量清单
```csharp
public static class UIConstants
{
    // 防抖时间
    public const int BUTTON_DEBOUNCE_MILLISECONDS = 300;
    
    // 超时时间
    public const int PROJECTION_TIMEOUT_MILLISECONDS = 3000;
    public const int VIDEO_TRACK_DETECT_TIMEOUT = 5000;
    
    // 默认值
    public const double DEFAULT_ZOOM = 1.0;
    public const double DEFAULT_FOLDER_FONT_SIZE = 26.0;
    public const double DEFAULT_FILE_FONT_SIZE = 26.0;
    
    // 其他常量
    // ...
}
```

---

### 任务1.3: 异步方法命名规范 ⏰ 2天

**目标**：所有异步方法添加Async后缀

#### 当前问题
```csharp
// 没有Async后缀，但内部使用await
private void BtnRecord_Click(object sender, RoutedEventArgs e)
{
    await Task.Delay(1000);
}
```

#### 统一方案
```csharp
// 添加Async后缀
private async void BtnRecord_ClickAsync(object sender, RoutedEventArgs e)
{
    await Task.Delay(1000);
}
```

#### 执行步骤
1. **第1天**: 识别异步方法
   - [ ] 搜索所有包含await的方法
   - [ ] 列出需要重命名的方法清单

2. **第2天**: 重命名和测试
   - [ ] 批量重命名
   - [ ] 更新XAML中的事件绑定
   - [ ] 编译测试

---

## 🏗️ 阶段二：拆分MainWindow类（预计3-4周）

### 总体拆分策略

#### 拆分原则
1. **按功能领域拆分**：每个部分类负责一个功能领域
2. **保持依赖最小化**：减少部分类之间的耦合
3. **向后兼容**：不破坏现有功能
4. **渐进式重构**：逐步拆分，持续测试

#### 目标结构
```
MainWindow (核心协调) - 1000行以内
├── MainWindow.Core.cs (核心功能) - 800行
├── MainWindow.Media.cs (媒体播放) - 600行
├── MainWindow.Projection.cs (投影管理) - 400行  
├── MainWindow.TreeView.cs (项目树) - 500行
├── MainWindow.Keyframe.cs (关键帧) - 1295行 [已存在]
├── MainWindow.Original.cs (原图模式) - 558行 [已存在]
├── MainWindow.TextEditor.cs (文本编辑) [已存在]
├── MainWindow.ImageProcessing.cs (图像处理) - 600行
└── MainWindow.EventHandlers.cs (事件处理) - 500行
```

---

### 任务2.1: 分析和设计 ⏰ 3天

#### 第1天：功能模块分析
- [ ] 分析MainWindow.xaml.cs的所有方法
- [ ] 按功能领域分组
- [ ] 识别共享依赖

#### 第2天：设计部分类结构
- [ ] 设计每个部分类的职责
- [ ] 定义共享接口和属性
- [ ] 规划迁移顺序

#### 第3天：创建迁移计划
- [ ] 确定优先级
- [ ] 评估风险
- [ ] 准备测试用例

#### 输出文档
```markdown
# MainWindow拆分设计文档

## 模块划分

### MainWindow.Core.cs（核心协调）
职责：
- 窗口初始化
- Manager实例管理
- 基础配置
- 窗口事件

方法列表：
- InitializeComponent()
- InitializeGpuProcessor()
- InitializeUI()
- Window_Closing()
- 等...

### MainWindow.Media.cs（媒体播放）
职责：
- 视频播放器管理
- 媒体控制
- 进度条处理

方法列表：
- InitializeVideoPlayer()
- VideoPlayerManager_VideoTrackDetected()
- OnVideoPlayStateChanged()
- OnVideoMediaChanged()
- OnVideoMediaEnded()
- OnVideoProgressUpdated()
- LoadAndDisplayVideo()
- SwitchToImageMode()
- 等...

### [其他模块类似...]
```

---

### 任务2.2: 创建MainWindow.Media.cs ⏰ 4天

**优先级**：第一个新部分类

#### 第1天：准备工作
- [ ] 创建MainWindow.Media.cs文件
- [ ] 设置namespace和partial class声明
- [ ] 添加必要的using语句

```csharp
using System;
using System.Windows;
using LibVLCSharp.WPF;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        #region 媒体播放相关字段
        // 从MainWindow.xaml.cs迁移
        #endregion

        #region 媒体播放初始化
        // 迁移InitializeVideoPlayer等方法
        #endregion

        #region 媒体播放事件处理
        // 迁移所有视频相关事件处理方法
        #endregion

        #region 媒体播放控制
        // 迁移播放控制方法
        #endregion
    }
}
```

#### 第2-3天：迁移代码
- [ ] 迁移字段（第5320-5340行区域）
  - _videoPlayerManager
  - _mainVideoView
  - _isUpdatingProgress
  - _pendingProjectionVideoPath
  - _projectionTimeoutTimer

- [ ] 迁移方法
  - [ ] InitializeVideoPlayer()
  - [ ] VideoPlayerManager_VideoTrackDetected()
  - [ ] OnVideoPlayStateChanged()
  - [ ] OnVideoMediaChanged()
  - [ ] OnVideoMediaEnded()
  - [ ] OnVideoProgressUpdated()
  - [ ] LoadAndDisplayVideo()
  - [ ] SwitchToImageMode()
  - [ ] BuildPlaylist()

#### 第4天：测试和验证
- [ ] 编译测试
- [ ] 功能测试
  - 视频播放
  - 音频播放
  - 播放控制（播放/暂停/停止）
  - 进度条拖动
  - 上一个/下一个
- [ ] 代码审查

#### 验收标准
- ✅ MainWindow.xaml.cs减少约600行
- ✅ 所有媒体相关功能正常
- ✅ 无编译错误和警告

---

### 任务2.3: 创建MainWindow.Projection.cs ⏰ 3天

**优先级**：第二个新部分类

#### 迁移内容
- [ ] 投影相关字段
  - projectionManager引用
  - 投影状态变量

- [ ] 投影相关方法
  - [ ] EnableVideoProjection()
  - [ ] DisableVideoProjection()
  - [ ] UpdateProjection()
  - [ ] 投影窗口事件处理

#### 验收标准
- ✅ MainWindow.xaml.cs减少约400行
- ✅ 投影功能完全正常

---

### 任务2.4: 创建MainWindow.TreeView.cs ⏰ 4天

**优先级**：第三个新部分类

#### 迁移内容
- [ ] 项目树相关字段
  - _projectTreeItems
  - _draggedItem, _dragOverItem
  - _isDragInProgress

- [ ] 项目树相关方法（约30个）
  - [ ] LoadProject()
  - [ ] LoadFolderFromTreeItem()
  - [ ] TreeView事件处理方法
  - [ ] 拖拽相关方法
  - [ ] 右键菜单方法

#### 验收标准
- ✅ MainWindow.xaml.cs减少约500行
- ✅ 项目树功能完全正常

---

### 任务2.5: 创建MainWindow.ImageProcessing.cs ⏰ 4天

**优先级**：第四个新部分类

#### 迁移内容
- [ ] 图像处理相关字段
  - _imageProcessor
  - _imagePath
  - _currentImageId
  - _currentZoom
  - _isColorEffectEnabled
  - _currentTargetColor

- [ ] 图像处理相关方法
  - [ ] LoadImage()
  - [ ] ClearImageDisplay()
  - [ ] 缩放相关方法
  - [ ] 拖动相关方法
  - [ ] 变色相关方法

#### 验收标准
- ✅ MainWindow.xaml.cs减少约600行
- ✅ 图像处理功能完全正常

---

### 任务2.6: 创建MainWindow.EventHandlers.cs ⏰ 3天

**优先级**：第五个新部分类

#### 迁移内容
- [ ] 通用事件处理方法
  - [ ] 窗口事件（Closing, SizeChanged等）
  - [ ] 键盘事件（PreviewKeyDown）
  - [ ] 鼠标事件（通用的）
  - [ ] 菜单事件

#### 验收标准
- ✅ MainWindow.xaml.cs减少约500行
- ✅ 所有事件处理正常

---

### 任务2.7: 重构MainWindow.Core.cs ⏰ 3天

**优先级**：最后阶段

#### 目标
精简主文件，只保留核心协调逻辑

#### 保留内容
- 字段声明（只保留引用）
- 初始化方法
- Manager实例化
- 基础配置加载
- 简单的辅助方法

#### 验收标准
- ✅ MainWindow.xaml.cs精简到1000行以内
- ✅ 结构清晰，职责明确
- ✅ 所有功能正常

---

## 📊 进度追踪

### 总体进度

| 阶段 | 任务 | 预计时间 | 状态 | 完成日期 |
|------|------|----------|------|----------|
| 阶段一 | 1.1 字段命名统一 | 3天 | 🔲 待开始 | - |
| 阶段一 | 1.2 常量定义统一 | 2天 | 🔲 待开始 | - |
| 阶段一 | 1.3 异步方法命名 | 2天 | 🔲 待开始 | - |
| 阶段二 | 2.1 分析和设计 | 3天 | 🔲 待开始 | - |
| 阶段二 | 2.2 MainWindow.Media.cs | 4天 | 🔲 待开始 | - |
| 阶段二 | 2.3 MainWindow.Projection.cs | 3天 | 🔲 待开始 | - |
| 阶段二 | 2.4 MainWindow.TreeView.cs | 4天 | 🔲 待开始 | - |
| 阶段二 | 2.5 MainWindow.ImageProcessing.cs | 4天 | 🔲 待开始 | - |
| 阶段二 | 2.6 MainWindow.EventHandlers.cs | 3天 | 🔲 待开始 | - |
| 阶段二 | 2.7 重构MainWindow.Core.cs | 3天 | 🔲 待开始 | - |

**总计**: 31个工作日（约6-7周）

### 状态图例
- 🔲 待开始
- 🔄 进行中
- ✅ 已完成
- ⚠️ 遇到问题
- 🔍 审查中

---

## 🧪 测试策略

### 单元测试
每个拆分后的部分类应有对应的测试：
- [ ] MainWindow.Media.Tests
- [ ] MainWindow.Projection.Tests
- [ ] MainWindow.TreeView.Tests
- [ ] MainWindow.ImageProcessing.Tests

### 集成测试
- [ ] 完整工作流测试
- [ ] 跨模块功能测试

### 回归测试清单
拆分后必须测试的功能：
- [ ] 项目加载和显示
- [ ] 图片切换和显示
- [ ] 视频播放
- [ ] 投影功能
- [ ] 关键帧录制和播放
- [ ] 原图模式
- [ ] 文本编辑器
- [ ] 热键功能
- [ ] 拖拽排序
- [ ] 搜索功能

---

## 📝 Git 工作流

### 分支策略
```
main (稳定版本)
  ├── feature/naming-convention (命名规范)
  └── feature/split-mainwindow (拆分MainWindow)
      ├── feature/split-media (媒体模块)
      ├── feature/split-projection (投影模块)
      ├── feature/split-treeview (树视图模块)
      ├── feature/split-imageprocessing (图像处理模块)
      └── feature/split-eventhandlers (事件处理模块)
```

### 提交规范
```
feat: 添加MainWindow.Media.cs部分类
refactor: 重命名所有Manager相关字段使用下划线前缀
test: 添加媒体播放模块测试用例
docs: 更新拆分进度文档
```

---

## ⚠️ 风险管理

### 潜在风险

1. **命名冲突** 
   - 风险：重命名可能导致XAML绑定失效
   - 缓解：逐步重命名，每次修改后立即测试

2. **功能破坏**
   - 风险：拆分过程中破坏现有功能
   - 缓解：充分的回归测试，Git版本控制

3. **性能下降**
   - 风险：拆分后可能影响性能
   - 缓解：性能测试，对比拆分前后

4. **代码重复**
   - 风险：拆分时可能产生重复代码
   - 缓解：识别共享逻辑，提取到辅助类

### 回滚计划
每个任务完成后：
1. 创建Git标签
2. 记录当前状态
3. 如遇重大问题，可快速回滚

---

## 📚 参考资料

### 代码规范
- [C# Coding Conventions (Microsoft)](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- [WPF Best Practices](https://docs.microsoft.com/en-us/dotnet/desktop/wpf/)

### 重构模式
- Martin Fowler - Refactoring: Improving the Design of Existing Code
- 上帝类重构模式
- 部分类最佳实践

---

## 📞 联系和支持

### 问题反馈
- 遇到问题时，在Git中创建Issue
- 标记优先级和模块

### 代码审查
- 每个任务完成后进行代码审查
- 至少一人审查通过才能合并

---

**文档版本**: 1.0
**最后更新**: 2025-10-17
**负责人**: 开发团队
**状态**: 📋 待执行

