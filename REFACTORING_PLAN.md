# 🔧 MainWindow 代码重构优化计划

> **生成日期:** 2026-01-30  
> **分析工具:** /refactor 技能  
> **项目:** Canvas (ImageColorChanger)

---

## 📊 当前状态分析

### 代码规模统计

| 文件 | 行数 | 状态 | 建议 |
|------|------|------|------|
| **MainWindow.xaml.cs** | 3,089 | 🔴 过大 | 需要进一步拆分 |
| MainWindow.TextEditor.cs | 6,850 | 🔴 严重过大 | 急需拆分 |
| MainWindow.Bible.cs | 5,059 | 🔴 严重过大 | 急需拆分 |
| MainWindow.Keyframe.cs | 2,465 | 🟡 偏大 | 建议拆分 |
| MainWindow.ProjectTree.cs | 1,430 | 🟡 偏大 | 可接受 |
| MainWindow.DragDrop.cs | 1,230 | 🟡 偏大 | 可接受 |
| MainWindow.Auth.cs | 919 | 🟢 正常 | ✅ |
| MainWindow.Lyrics.cs | 902 | 🟢 正常 | ✅ |
| MainWindow.Original.cs | 488 | 🟢 正常 | ✅ |
| MainWindow.Import.cs | 380 | 🟢 正常 | ✅ |
| MainWindow.SplitSlide.cs | 368 | 🟢 正常 | ✅ |
| MainWindow.Settings.cs | 357 | 🟢 正常 | ✅ |
| MainWindow.ContextMenu.cs | 302 | 🟢 正常 | ✅ |
| MainWindow.Lifecycle.cs | 281 | 🟢 正常 | ✅ |
| MainWindow.Color.cs | 192 | 🟢 良好 | ✅ |
| MainWindow.Zoom.cs | 185 | 🟢 良好 | ✅ |
| MainWindow.HotKey.cs | 176 | 🟢 良好 | ✅ |
| MainWindow.ShortcutSupport.cs | 166 | 🟢 良好 | ✅ |
| MainWindow.Media.cs | 127 | 🟢 良好 | ✅ |

**总计:** 24,966 行代码分布在 19 个文件中

### 健康度评分

| 指标 | 值 | 目标 | 状态 |
|------|-----|------|------|
| 最大单文件行数 | 6,850 | < 500 | 🔴 |
| 平均文件行数 | 1,314 | < 300 | 🟡 |
| 文件数量 | 19 | - | ✅ 已合理拆分 |
| partial class 使用 | ✅ | - | ✅ |

---

## 🚨 发现的主要问题

### 问题 1: `MainWindow.TextEditor.cs` 过大 (6,850 行) 🔴

**严重程度:** 严重

**分析:**
- 文本编辑器功能复杂，包含多种编辑模式
- 应该拆分为独立的子模块

**建议拆分方案:**

```
MainWindow.TextEditor.cs (6,850 行)
    ├── MainWindow.TextEditor.Core.cs      (~800 行) - 核心编辑功能
    ├── MainWindow.TextEditor.Formatting.cs (~1,000 行) - 格式化功能
    ├── MainWindow.TextEditor.RichText.cs   (~1,200 行) - 富文本处理
    ├── MainWindow.TextEditor.Preview.cs    (~800 行) - 预览功能
    ├── MainWindow.TextEditor.IO.cs         (~600 行) - 导入导出
    └── MainWindow.TextEditor.UI.cs         (~1,000 行) - UI事件处理
```

---

### 问题 2: `MainWindow.Bible.cs` 过大 (5,059 行) 🔴

**严重程度:** 严重

**分析:**
- 圣经功能模块过于庞大
- 包含导航、搜索、显示等多种职责

**建议拆分方案:**

```
MainWindow.Bible.cs (5,059 行)
    ├── MainWindow.Bible.Navigation.cs  (~1,200 行) - 书卷章节导航
    ├── MainWindow.Bible.Search.cs      (~800 行) - 搜索功能
    ├── MainWindow.Bible.Display.cs     (~1,000 行) - 经文显示
    ├── MainWindow.Bible.History.cs     (~600 行) - 历史记录
    └── MainWindow.Bible.Insert.cs      (~800 行) - 经文插入
```

---

### 问题 3: `MainWindow.xaml.cs` 仍然过大 (3,089 行) 🔴

**严重程度:** 中高

**分析:**
主文件仍包含以下应该抽取的职责：
- 视频播放控制 (约 400 行)
- 投影管理 (约 300 行)
- 初始化逻辑 (约 500 行)
- 播放控制 (约 300 行)

**建议继续拆分:**

```
MainWindow.xaml.cs (3,089 行)
    ├── MainWindow.Video.cs        (~400 行) - 视频播放控制
    ├── MainWindow.Projection.cs   (~300 行) - 投影相关
    ├── MainWindow.Playback.cs     (~300 行) - 录制/播放控制
    └── MainWindow.Init.cs         (~500 行) - 初始化逻辑
```

---

### 问题 4: `MainWindow.Keyframe.cs` 偏大 (2,465 行) 🟡

**严重程度:** 中

**建议拆分方案:**

```
MainWindow.Keyframe.cs (2,465 行)
    ├── MainWindow.Keyframe.Core.cs       (~800 行) - 核心管理
    ├── MainWindow.Keyframe.Animation.cs  (~600 行) - 动画逻辑
    ├── MainWindow.Keyframe.UI.cs         (~500 行) - UI控件
    └── MainWindow.Keyframe.Playback.cs   (~500 行) - 播放逻辑
```

---

### 问题 5: `ProjectTreeItem` 类内嵌在 MainWindow.xaml.cs 中

**严重程度:** 低

**当前位置:** MainWindow.xaml.cs 第 3359-3467 行

**建议:**
将 `ProjectTreeItem` 和 `TreeItemType` 移到独立文件：

```csharp
// 新文件: Models/ProjectTreeItem.cs
namespace ImageColorChanger.Models
{
    public class ProjectTreeItem : INotifyPropertyChanged
    {
        // ...
    }

    public enum TreeItemType
    {
        Project,
        Folder,
        File,
        // ...
    }
}
```

---

## 📋 重构执行计划

### Phase 1: 紧急修复 (预计 4 小时)

| 优先级 | 任务 | 预计时间 |
|--------|------|----------|
| 1 | 拆分 MainWindow.TextEditor.cs | 2 小时 |
| 2 | 拆分 MainWindow.Bible.cs | 2 小时 |

### Phase 2: 核心重构 (预计 3 小时)

| 优先级 | 任务 | 预计时间 |
|--------|------|----------|
| 3 | 从 MainWindow.xaml.cs 抽取 Video 模块 | 1 小时 |
| 4 | 从 MainWindow.xaml.cs 抽取 Projection 模块 | 1 小时 |
| 5 | 从 MainWindow.xaml.cs 抽取 Playback 模块 | 1 小时 |

### Phase 3: 优化完善 (预计 2 小时)

| 优先级 | 任务 | 预计时间 |
|--------|------|----------|
| 6 | 拆分 MainWindow.Keyframe.cs | 1.5 小时 |
| 7 | 抽取 ProjectTreeItem 到独立文件 | 0.5 小时 |

---

## 🛠️ 重构步骤详解

### Step 1: 创建新的 partial class 文件

```csharp
// MainWindow.Video.cs
namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        #region 视频播放控制

        // 从 MainWindow.xaml.cs 移动:
        // - InitializeVideoPlayer()
        // - OnVideoPlayStateChanged()
        // - OnVideoMediaChanged()
        // - OnVideoMediaEnded()
        // - OnVideoProgressUpdated()
        // - LoadAndDisplayVideo()
        // - BuildVideoPlaylist()
        // - SwitchToImageMode()
        // - EnableVideoProjection()
        // - DisableVideoProjection()

        #endregion
    }
}
```

### Step 2: 移动方法到新文件

1. **剪切方法代码**（保持方法签名不变）
2. **更新 #region 标记**
3. **确保 using 语句完整**
4. **编译测试**

### Step 3: 验证功能

```powershell
# 编译项目
dotnet build

# 运行测试（如果有）
dotnet test
```

---

## 📐 重构后的目标架构

```
MainWindow (partial class)
│
├── MainWindow.xaml.cs          (~800 行) - 核心字段、构造函数、主要初始化
│
├── 功能模块
│   ├── MainWindow.Video.cs         (~400 行) - 视频播放
│   ├── MainWindow.Projection.cs    (~300 行) - 投影控制
│   ├── MainWindow.Playback.cs      (~300 行) - 录制/播放
│   ├── MainWindow.Zoom.cs          (~185 行) ✅
│   ├── MainWindow.Color.cs         (~192 行) ✅
│   └── MainWindow.Media.cs         (~127 行) ✅
│
├── UI 交互
│   ├── MainWindow.ContextMenu.cs   (~302 行) ✅
│   ├── MainWindow.DragDrop.cs      (~1,230 行) ⚠️
│   ├── MainWindow.HotKey.cs        (~176 行) ✅
│   └── MainWindow.ShortcutSupport.cs (~166 行) ✅
│
├── 业务功能
│   ├── MainWindow.TextEditor/      (拆分为 6 个文件)
│   ├── MainWindow.Bible/           (拆分为 5 个文件)
│   ├── MainWindow.Keyframe/        (拆分为 4 个文件)
│   ├── MainWindow.ProjectTree.cs   (~1,430 行) ⚠️
│   ├── MainWindow.Original.cs      (~488 行) ✅
│   ├── MainWindow.Lyrics.cs        (~902 行) ✅
│   └── MainWindow.Import.cs        (~380 行) ✅
│
└── 系统
    ├── MainWindow.Auth.cs          (~919 行) ✅
    ├── MainWindow.Settings.cs      (~357 行) ✅
    ├── MainWindow.Lifecycle.cs     (~281 行) ✅
    └── MainWindow.SplitSlide.cs    (~368 行) ✅
```

---

## ✅ 重构检查清单

### 重构前
- [ ] 确保所有测试通过
- [ ] 创建代码备份或提交
- [ ] 记录当前功能状态

### 重构中
- [ ] 每次只移动一个方法组
- [ ] 移动后立即编译验证
- [ ] 保持 public API 不变
- [ ] 更新 XML 文档注释

### 重构后
- [ ] 所有功能正常工作
- [ ] 代码可以正常编译
- [ ] 运行手动测试验证
- [ ] 更新架构文档

---

## 📚 相关资料

### 设计原则
- **单一职责原则 (SRP):** 每个类/模块只负责一个功能
- **开闭原则 (OCP):** 对扩展开放，对修改关闭
- **依赖倒置原则 (DIP):** 依赖抽象而非具体实现

### 推荐的类大小
| 指标 | 推荐值 | 最大值 |
|------|--------|--------|
| 类行数 | < 200 | < 500 |
| 方法行数 | < 20 | < 50 |
| 方法参数 | < 4 | < 7 |
| 嵌套深度 | < 3 | < 5 |

---

## 🎯 预期收益

| 收益 | 描述 |
|------|------|
| ✅ 可维护性 | 更容易定位和修改代码 |
| ✅ 可测试性 | 更容易为单个模块编写测试 |
| ✅ 可读性 | 每个文件职责清晰 |
| ✅ 团队协作 | 减少合并冲突 |
| ✅ 编译速度 | 增量编译更快 |

---

## 📝 备注

本文档由 `/refactor` 技能自动生成。建议在执行重构前：

1. **创建 Git 分支:** `git checkout -b refactor/mainwindow-split`
2. **分步提交:** 每完成一个模块拆分就提交一次
3. **测试验证:** 确保每次提交后应用功能正常

如需帮助执行某个重构步骤，请使用 `/refactor <具体任务>` 命令。
