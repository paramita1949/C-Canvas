# MainWindow 细化拆分方案 (Phase 6-12)

**基于**: 已完成5个模块拆分（媒体、缩放、颜色、热键、导入）  
**当前行数**: 5119行  
**目标**: 再减少 800-1000行  
**策略**: 从简单到复杂，逐步拆分

---

## 📋 拆分优先级矩阵

| 模块 | 行数 | 复杂度 | 耦合度 | 独立性 | 优先级 |
|------|------|--------|--------|--------|--------|
| **窗口事件** | ~55行 | ⭐ | ⭐⭐ | ⭐⭐⭐⭐⭐ | 🔥 P1 (高) |
| **设置管理** | ~120行 | ⭐⭐ | ⭐⭐ | ⭐⭐⭐⭐ | 🔥 P1 (高) |
| **拖拽事件** | ~150行 | ⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐ | 🔶 P2 (中) |
| **投影事件** | ~100行 | ⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐ | 🔶 P2 (中) |
| **辅助方法** | ~200行 | ⭐⭐ | ⭐⭐ | ⭐⭐⭐⭐ | 🔶 P2 (中) |
| **键盘核心** | ~200行 | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐ | 🔻 P3 (低) |
| **项目树** | ~300行 | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐ | 🔻 P3 (低) |

**拆分顺序**: P1 → P2 → P3 (评估后决定)

---

## 🚀 Phase 6: 窗口事件处理 (~55行)

### 📍 位置
- **Region**: `#region 窗口事件处理` (3269-3323行)
- **文件**: `UI/MainWindow.xaml.cs`

### 📦 包含内容
```csharp
Window_Closing()  // 窗口关闭，清理资源
```

### ✅ 独立性分析
- ✅ **高度独立** - 只在窗口关闭时调用
- ✅ **无返回值** - 不影响其他逻辑
- ✅ **清理型方法** - 适合独立管理

### 🎯 拆分方案
**目标文件**: `UI/MainWindow.Lifecycle.cs`

**包含方法**:
1. `Window_Closing` - 窗口关闭事件
2. (未来扩展) `Window_Loaded` - 窗口加载事件
3. (未来扩展) `Window_StateChanged` - 窗口状态变化

**预计减少**: 55行

---

## 🚀 Phase 7: 设置管理 (~120行)

### 📍 位置
- **散落在多处** - 需要搜索 `Settings` 相关方法

### 🔍 需要搜索的方法
```csharp
SaveSettings()
LoadSettings()
SetFolderFontSize()
SetFileFontSize()
SetFolderTagFontSize()
```

### ✅ 独立性分析
- ✅ **高度独立** - 专门处理配置持久化
- ✅ **明确职责** - 读取/保存设置
- ✅ **低耦合** - 通过ConfigManager交互

### 🎯 拆分方案
**目标文件**: `UI/MainWindow.Settings.cs`

**包含方法**:
1. `SaveSettings` - 保存所有设置
2. `LoadSettings` - 加载所有设置
3. `SetFolderFontSize` - 设置文件夹字号
4. `SetFileFontSize` - 设置文件字号
5. `SetFolderTagFontSize` - 设置文件夹标签字号

**预计减少**: 120行

---

## 🚀 Phase 8: 拖拽事件处理 (~150行)

### 📍 位置
- **Region**: `#region 拖拽事件处理` (3690-3840行)

### 📦 包含内容
```csharp
ProjectTree_PreviewMouseLeftButtonDown()  // 鼠标按下
ProjectTree_PreviewMouseMove()            // 鼠标移动
ProjectTree_DragOver()                    // 拖拽悬停
ProjectTree_Drop()                        // 拖拽放下
FindAncestor<T>()                        // 辅助方法
```

### ⚠️ 耦合度分析
- ⚠️ **中度耦合** - 依赖项目树数据结构
- ✅ **事件独立** - 可以独立处理拖拽逻辑
- ⚠️ **数据库操作** - 需要调用 `_dbManager`

### 🎯 拆分方案
**目标文件**: `UI/MainWindow.DragDrop.cs`

**包含方法**:
1. `ProjectTree_PreviewMouseLeftButtonDown`
2. `ProjectTree_PreviewMouseMove`
3. `ProjectTree_DragOver`
4. `ProjectTree_Drop`
5. `FindAncestor<T>` (辅助方法)

**预计减少**: 150行

---

## 🚀 Phase 9: 投影事件处理 (~100行)

### 📍 位置
- **散落在多处** - 需要搜索投影相关的事件处理

### 🔍 需要搜索的方法
```csharp
OnProjectionStateChanged()       // 投影状态变化
UpdateProjection()              // 更新投影内容
BtnProjection_Click()           // 投影按钮点击
```

### ⚠️ 耦合度分析
- ⚠️ **高度耦合** - 与图片处理、视频播放相关
- ⚠️ **状态同步** - 需要同步主窗口和投影窗口
- ⚠️ **复杂逻辑** - 涉及多种模式切换

### 🎯 拆分方案
**目标文件**: `UI/MainWindow.ProjectionEvents.cs`

**包含方法**:
1. `OnProjectionStateChanged` - 投影状态变化事件
2. `BtnProjection_Click` - 投影按钮点击
3. (其他投影相关的事件处理方法)

**预计减少**: 100行

**⚠️ 注意**: 投影的核心逻辑（UpdateProjection等）建议保留在主文件

---

## 🚀 Phase 10: 辅助方法 (~200行)

### 📍 位置
- **Region**: `#region 辅助方法` (3042-3268行)

### 📦 包含内容
```csharp
ResetView()
ShowStatus()
LoadProjects()
LoadSearchScopes()
... (其他辅助方法)
```

### 📊 细分策略
由于辅助方法较多且功能各异，需要进一步细分：

#### 10.1 状态显示相关
```csharp
ShowStatus()
UpdateStatusBar()
```

#### 10.2 项目树相关
```csharp
LoadProjects()
LoadSearchScopes()
RefreshProjectTree()
```

#### 10.3 通用辅助
```csharp
ResetView()
IsVideoFile()
IsImageFile()
```

### 🎯 拆分方案
**目标文件**: `UI/MainWindow.Helpers.cs`

**包含方法**: 所有通用辅助方法

**预计减少**: 200行

---

## 🔻 Phase 11-12: 复杂模块（暂缓）

### ❌ 键盘核心事件 (~200行)
**位置**: `#region 键盘事件处理` (3325-3688行)

**不拆分的原因**:
1. ⚠️ **核心逻辑** - 处理所有键盘输入
2. ⚠️ **高度耦合** - 与视频、原图、关键帧模式紧密相关
3. ⚠️ **复杂判断** - 大量的条件分支
4. ⚠️ **难以测试** - 拆分后难以保证功能完整性

**建议**: 等待架构重构后再处理

---

### ❌ 项目树事件 (~300行)
**位置**: `#region 项目树事件` (1675-2775行)

**不拆分的原因**:
1. ⚠️ **大量方法** - 包含10+个事件处理方法
2. ⚠️ **数据绑定** - 与UI控件强绑定
3. ⚠️ **搜索逻辑** - 涉及复杂的搜索和过滤
4. ⚠️ **树形操作** - 递归查找、展开/折叠等

**建议**: 引入MVVM后，由ViewModel管理

---

## 📊 预期成果

### 本次拆分目标 (Phase 6-10)

| Phase | 模块 | 行数 | 状态 |
|-------|------|------|------|
| Phase 6 | 窗口事件 | ~55行 | ⏳ 待执行 |
| Phase 7 | 设置管理 | ~120行 | ⏳ 待执行 |
| Phase 8 | 拖拽事件 | ~150行 | ⏳ 待执行 |
| Phase 9 | 投影事件 | ~100行 | ⏳ 待执行 |
| Phase 10 | 辅助方法 | ~200行 | ⏳ 待执行 |
| **合计** | **5个模块** | **~625行** | - |

### 拆分前后对比

```
当前状态 (Phase 1-5完成):
├── MainWindow.xaml.cs: 5119行
└── 5个partial类: 1019行

预期状态 (Phase 6-10完成):
├── MainWindow.xaml.cs: ~4500行 (-625行, -12%)
└── 10个partial类: ~1650行
```

### 最终目标

```
主文件行数: 6040 → 4500 (-25.5%)
拆分模块: 10个 partial class
累计减少: ~1550行
```

---

## 🛠️ 执行策略

### 1. 逐个模块执行
- ✅ 每完成一个模块立即编译测试
- ✅ 确保编译通过后再进行下一个
- ✅ 遇到问题立即修复

### 2. 命名规范
```
MainWindow.Lifecycle.cs    - 窗口生命周期
MainWindow.Settings.cs     - 设置管理
MainWindow.DragDrop.cs     - 拖拽事件
MainWindow.ProjectionEvents.cs - 投影事件
MainWindow.Helpers.cs      - 辅助方法
```

### 3. Git提交策略
- 每个Phase单独提交
- 提交信息格式: `重构: 拆分MainWindow - [模块名] (Phase X/12)`
- 保持提交历史清晰

### 4. 文档更新
- 完成后更新 `MainWindow拆分最终报告.md`
- 添加新的模块说明
- 更新统计数据

---

## ⚠️ 风险评估

### 低风险模块 (可放心拆分)
- ✅ Phase 6: 窗口事件
- ✅ Phase 7: 设置管理

### 中风险模块 (需要仔细测试)
- ⚠️ Phase 8: 拖拽事件 (涉及UI交互)
- ⚠️ Phase 9: 投影事件 (状态同步)
- ⚠️ Phase 10: 辅助方法 (调用范围广)

### 高风险模块 (暂不拆分)
- ❌ Phase 11-12: 键盘核心、项目树

---

## 🎯 成功标准

### 功能完整性
- [x] 所有功能正常工作
- [x] 无编译错误和警告
- [x] UI交互流畅

### 代码质量
- [x] 命名规范统一
- [x] 每个partial类职责清晰
- [x] 无重复代码

### 可维护性
- [x] 模块间低耦合
- [x] 代码易于理解
- [x] 便于后续扩展

---

**准备开始执行**: Phase 6 (窗口事件处理)  
**预计耗时**: 每个Phase约15-20分钟  
**总耗时**: 约1.5-2小时

---

**更新时间**: 2025-10-17  
**状态**: ✅ 方案已制定，等待执行

