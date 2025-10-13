# 原图模式录制播放系统完善计划

## 📋 项目概述

基于已完善的关键帧录制播放系统，完善原图模式（Original Mode）的录制播放功能。原图模式用于管理标记为原图循环的文件夹中的相似图片切换播放。

### 核心特点对比

| 特性 | 关键帧模式 | 原图模式 |
|------|-----------|---------|
| **操作对象** | 单张图片内的关键帧位置 | 多张相似图片之间的切换 |
| **数据表** | `keyframe_timings` | `original_mode_timings` |
| **播放动作** | 滚动到关键帧位置（有动画） | 切换到下一张图片（无动画） |
| **时间记录** | 每个关键帧停留时间 | 每张图片停留时间 |
| **循环逻辑** | 回到第一个关键帧 | 回到第一张图片 |
| **脚本格式** | `1  3.5s` | `100 -> 101 : 2.5` |

---

## ✅ 已完成部分（当前状态）

### 1. 数据库层 ✅
- [x] `OriginalModeTiming` 模型定义
- [x] `OriginalMark` 模型定义
- [x] `CanvasDbContext` 配置和外键关系
- [x] 数据库表和索引创建
- [x] 兼容Python版本的schema结构

**位置：** `Database/Models/OriginalMark.cs`, `Database/CanvasDbContext.cs`

### 2. 仓储层 ✅
- [x] `IOriginalModeRepository` 接口定义
- [x] `OriginalModeRepositoryImpl` 实现
- [x] 时间序列查询和保存
- [x] 相似图片识别算法（基于文件名模式）
- [x] 批量保存时间序列

**位置：** `Repositories/Interfaces/IOriginalModeRepository.cs`, `Repositories/Implementations/OriginalModeRepositoryImpl.cs`

### 3. 服务层 ✅
- [x] `IRecordingService` 接口
- [x] `IPlaybackService` 接口
- [x] `OriginalRecordingService` 录制服务实现
- [x] `OriginalPlaybackService` 播放服务实现
- [x] 基础的播放循环逻辑
- [x] 播放次数判断逻辑

**位置：** `Services/Implementations/OriginalRecordingService.cs`, `Services/Implementations/OriginalPlaybackService.cs`

### 4. 管理层 ✅
- [x] `OriginalManager` 原图标记管理
- [x] 相似图片查找和切换
- [x] 循环模式 vs 顺序模式切换
- [x] 项目树图标更新逻辑

**位置：** `Managers/OriginalManager.cs`

### 5. DTO定义 ✅
- [x] `OriginalTimingSequenceDto` 时间序列数据传输
- [x] `SimilarImageDto` 相似图片信息
- [x] `PlaybackProgressEventArgs` 播放进度事件
- [x] `SwitchImageEventArgs` 图片切换事件

**位置：** `Database/Models/DTOs/`

---

## 🚧 待完善部分（按优先级排序）

### 阶段1：UI层基础集成 🔴 高优先级

#### 1.1 MainWindow原图模式事件绑定
- [ ] 识别当前图片是否为原图模式
  - 检查图片本身是否有原图标记
  - 检查图片所在文件夹是否有原图标记
  - 更新UI显示状态（按钮可用性）

- [ ] 录制按钮事件处理
  - 检查是否有相似图片（至少2张）
  - 调用 `OriginalRecordingService.StartRecordingAsync()`
  - 更新按钮状态：`录制` -> `停止` （背景色：#ffcccc -> #ff6666）
  - 监听图片切换事件，调用 `RecordTimingAsync()`
  - 停止录制时保存数据到数据库

- [ ] 播放按钮事件处理
  - 检查是否有录制的时间数据
  - 调用 `OriginalPlaybackService.StartPlaybackAsync()`
  - 更新按钮状态：`播放` -> `停止` （背景色：#f0f0f0 -> #90EE90）
  - 监听 `SwitchImageRequested` 事件，加载切换图片
  - 显示倒计时组件

- [ ] 暂停/继续按钮事件处理
  - 暂停时调用 `PausePlaybackAsync()`
  - 继续时调用 `ResumePlaybackAsync()`
  - 更新按钮文本：`暂停` <-> `继续`
  - 处理暂停动画显示

**参考文件：**
- Python版本：`Canvas/playback/playback_controller.py` 行68-238
- C#关键帧实现：`UI/MainWindow.Keyframe.cs`

**预计工作量：** 2-3天

---

#### 1.2 图片切换逻辑集成
- [ ] 在录制时监听方向键切换相似图片
  - 调用 `OriginalManager.SwitchSimilarImage()`
  - 触发 `OriginalRecordingService.RecordTimingAsync()`

- [ ] 在播放时响应 `SwitchImageRequested` 事件
  - 加载目标图片到Canvas
  - 更新项目树选中状态
  - 强制更新投影窗口（如果存在）

- [ ] 实现图片缓存优化
  - 预加载相似图片列表
  - 避免频繁IO操作

**参考文件：**
- Python版本：`Canvas/playback/keytime.py` 行1830-1900（`_switch_to_similar_image`）

**预计工作量：** 1-2天

---

### 阶段2：暂停时间累加机制 🟠 中优先级

#### 2.1 暂停时间记录
- [ ] 在暂停时记录暂停开始时间
- [ ] 计算已播放时间和暂停累计时间
- [ ] 更新数据库中的持续时间（异步）

**核心逻辑：**
```csharp
// 暂停时记录时间
pauseStartTime = DateTime.Now;

// 继续时计算暂停时长
var pauseDuration = (DateTime.Now - pauseStartTime).TotalSeconds;
var playedDuration = (pauseStartTime - frameStartTime).TotalSeconds;
var finalDisplayTime = playedDuration + totalPauseDuration;

// 异步更新数据库
await _originalModeRepository.UpdateDurationAsync(
    baseImageId, 
    similarImageId, 
    finalDisplayTime
);

// 立即跳到下一张图片（不继续倒计时）
await PlayNextImageAsync(cancellationToken);
```

**参考文件：**
- Python版本：`Canvas/playback/keytime.py` 行1546-1744
- 关键帧实现：`Services/Implementations/KeyframePlaybackService.cs`

**预计工作量：** 1-2天

---

#### 2.2 Repository更新方法
- [ ] 在 `IOriginalModeRepository` 添加 `UpdateDurationAsync` 方法
- [ ] 实现根据 `BaseImageId` + `SimilarImageId` 更新持续时间
- [ ] 清除相关缓存

```csharp
Task UpdateOriginalDurationAsync(
    int baseImageId, 
    int similarImageId, 
    double newDuration
);
```

**预计工作量：** 0.5天

---

### 阶段3：倒计时显示组件 🟠 中优先级

#### 3.1 原图模式倒计时显示
- [ ] 复用关键帧模式的倒计时组件
- [ ] 显示当前图片索引（如：`2/5`）
- [ ] 显示剩余时间（精确到0.1秒）
- [ ] 显示播放次数（如：`第1次/共3次`）

- [ ] 暂停动画
  - 暂停时倒计时数字递增动画
  - 背景色变化提示

**参考文件：**
- Python版本：`Canvas/playback/keytime.py` 行1315-1378
- UI实现：`UI/MainWindow.xaml.cs` 中的倒计时相关方法

**预计工作量：** 1天

---

### 阶段4：脚本编辑功能 🟡 普通优先级

#### 4.1 ScriptEditWindow支持原图模式
- [ ] 检测当前是原图模式
- [ ] 加载原图时间序列数据
- [ ] 格式化显示脚本内容

**脚本格式：**
```
# 原图模式脚本格式
100 -> 101 : 2.5    # 图片ID 100 停留2.5秒后切换到 101
101 -> 102 : 3.0    # 图片ID 101 停留3.0秒后切换到 102
102 -> 103 : 2.8    # 图片ID 102 停留2.8秒后切换到 103
103 -> 100 : 3.2    # 图片ID 103 停留3.2秒后切换到 100（循环）
```

**参考文件：**
- Python版本：`Canvas/playback/keytime.py` 行2170-2233
- 关键帧实现：`Managers/ScriptManager.cs`（需要创建）

**预计工作量：** 1-2天

---

#### 4.2 脚本解析和保存
- [ ] 解析脚本文本内容
  - 正则表达式：`(\d+)\s*->\s*(\d+)\s*:\s*([\d.]+)`
  - 验证图片ID是否存在
  - 验证时间值合法性

- [ ] 保存修改到数据库
  - 批量更新 `original_mode_timings` 表
  - 清除旧数据并插入新数据（事务处理）

**预计工作量：** 1天

---

### 阶段5：循环优化 🟡 普通优先级

#### 5.1 避免重复切换到第一张图
- [ ] 检查最后一帧是否已经是第一张图
- [ ] 如果是，跳过切换，直接开始下一轮

**核心逻辑（已在Python版本实现）：**
```csharp
// 播放最后一帧时
if (_currentIndex == _timingSequence.Count - 1)
{
    var lastTiming = _timingSequence[_currentIndex];
    var firstImageId = _currentBaseImageId;
    
    if (lastTiming.SimilarImageId == firstImageId)
    {
        // 最后一帧已经是第一张图，跳过切换
        Logger.Debug("循环优化：已在主图，跳过切换");
        CompletedPlayCount++;
        _currentIndex = 0;
        return;
    }
}
```

**参考文件：**
- Python版本：`Canvas/playback/keytime.py` 行1708-1828（特别是471-510行）

**预计工作量：** 0.5天

---

### 阶段6：测试和调试 🟢 持续进行

#### 6.1 单元测试（可选）
- [ ] 录制服务测试
- [ ] 播放服务测试
- [ ] 相似图片查找测试
- [ ] 脚本解析测试

#### 6.2 集成测试
- [ ] 完整录制流程测试
  - 开始录制 -> 切换图片 -> 停止录制 -> 检查数据库

- [ ] 完整播放流程测试
  - 加载时间数据 -> 播放 -> 暂停 -> 继续 -> 停止

- [ ] 循环模式测试
  - 无限循环（播放次数=-1）
  - 有限次数（播放次数=3）

- [ ] 顺序模式测试
  - 到边界停止
  - 切换到不同系列图片

#### 6.3 性能测试
- [ ] 大量相似图片（100+张）的播放性能
- [ ] 快速切换图片时的响应性
- [ ] 内存占用监控

#### 6.4 边界情况测试
- [ ] 只有1张图片（应提示无法录制）
- [ ] 录制时手动切换到非相似图片
- [ ] 播放时删除相似图片
- [ ] 数据库损坏或缺失字段

**预计工作量：** 持续2-3天

---

### 阶段7：文档和优化 🟢 低优先级

#### 7.1 代码注释完善
- [ ] 补充XML文档注释
- [ ] 添加关键逻辑的行内注释
- [ ] 与Python版本的对应关系标注

#### 7.2 性能优化
- [ ] 图片预加载机制
- [ ] 数据库查询优化（缓存）
- [ ] UI更新防抖动

#### 7.3 用户体验优化
- [ ] 录制时显示当前相似图片数量
- [ ] 播放时显示进度条
- [ ] 错误提示信息优化
- [ ] 键盘快捷键支持

**预计工作量：** 1-2天

---

## 📅 时间规划

### 第1周（阶段1-2）
- **Day 1-2:** UI层基础集成（录制/播放按钮）
- **Day 3:** 图片切换逻辑集成
- **Day 4-5:** 暂停时间累加机制

### 第2周（阶段3-5）
- **Day 1:** 倒计时显示组件
- **Day 2-3:** 脚本编辑功能
- **Day 4:** 循环优化
- **Day 5:** 集成测试和调试

### 第3周（阶段6-7）
- **Day 1-3:** 全面测试和bug修复
- **Day 4-5:** 文档完善和性能优化

**总预计工作量：** 12-15天

---

## 🔧 技术要点

### 1. 与关键帧模式的代码复用
- **按钮状态管理**：复用 `UpdateButtonStatus()` 逻辑
- **倒计时显示**：复用倒计时组件，调整显示内容
- **脚本编辑**：复用ScriptEditWindow，区分模式

### 2. 关键差异处理
| 功能 | 关键帧模式 | 原图模式 |
|------|-----------|---------|
| 切换动作 | `SmoothScrollTo()` 平滑滚动 | 直接加载图片 |
| 时间记录 | KeyframeId -> Duration | SimilarImageId -> Duration |
| 循环检测 | 回到第一个关键帧 | 回到第一张图片，优化重复切换 |
| 脚本格式 | `1  3.5s` | `100 -> 101 : 2.5` |

### 3. Python版本参考
所有实现应参考Python版本的逻辑：
- 录制：`Canvas/playback/playback_controller.py` 行68-166
- 播放：`Canvas/playback/keytime.py` 行1420-1900
- 暂停继续：`Canvas/playback/keytime.py` 行1505-1744
- 脚本编辑：`Canvas/playback/keytime.py` 行2170-2233

---

## ⚠️ 注意事项

### 1. 数据库一致性
- 确保 `OriginalModeTiming` 的 `FromImageId` 和 `ToImageId` 正确
- 保存时使用事务（Transaction）防止数据不一致
- 清除时间数据时同时清除相关缓存

### 2. 线程安全
- 播放服务运行在后台线程
- UI更新必须通过 `Dispatcher.Invoke()` 或 `Application.Current.Dispatcher`
- 避免在播放循环中执行耗时操作

### 3. 内存管理
- 相似图片预加载要控制数量（建议最多10张）
- 播放结束后及时释放资源
- 使用 `WeakReference` 缓存图片

### 4. 错误处理
- 所有异步操作使用 `try-catch` 包裹
- 数据库操作失败时回滚事务
- 播放异常时停止播放并提示用户

---

## 📊 进度追踪

### 当前进度：35% ✅
- [x] 数据库层（100%）
- [x] 仓储层（100%）
- [x] 服务层（100%）
- [x] 管理层（100%）
- [ ] UI层集成（0%）⬅️ **当前重点**
- [ ] 暂停时间累加（0%）
- [ ] 倒计时显示（0%）
- [ ] 脚本编辑（0%）
- [ ] 测试和优化（0%）

### 下一步行动
1. **立即开始：** 阶段1.1 - MainWindow原图模式事件绑定
2. **参考文件：**
   - `UI/MainWindow.Keyframe.cs` （关键帧模式实现）
   - `Canvas/playback/playback_controller.py` （Python版本）
3. **预期成果：** 可以正常录制和播放原图模式

---

## 🎯 验收标准

完成以下所有功能点即视为完成：

### 基础功能
- [ ] 能够识别原图模式（文件夹或图片有原图标记）
- [ ] 能够录制相似图片切换时间
- [ ] 能够播放录制的时间序列
- [ ] 能够暂停和继续播放
- [ ] 能够循环播放（无限或指定次数）

### 高级功能
- [ ] 暂停时间正确累加到持续时间
- [ ] 倒计时显示准确（误差<100ms）
- [ ] 支持脚本编辑和保存
- [ ] 循环优化（避免重复切换）

### 质量标准
- [ ] 无明显bug（崩溃、数据丢失等）
- [ ] 性能流畅（切换延迟<50ms）
- [ ] UI响应及时（按钮点击<100ms）
- [ ] 代码注释清晰
- [ ] 通过所有测试用例

---

## 📝 开发日志

### 2025-10-13
- ✅ 完成需求分析和TODO规划
- ✅ 确认数据库层、仓储层、服务层已完成
- 🎯 下一步：开始阶段1.1 - UI层事件绑定

---

## 🔗 相关文档

- [Python版本逻辑分析 - 原图模式](Canvas/palysdocs/LOGIC_ANALYSIS_04_原图模式逻辑.md)
- [Python版本逻辑分析 - 关键帧模式](Canvas/palysdocs/LOGIC_ANALYSIS_03_关键帧模式逻辑.md)
- [数据库结构](Canvas/palysdocs/LOGIC_ANALYSIS_02_数据库结构.md)
- [系统架构概述](Canvas/palysdocs/LOGIC_ANALYSIS_01_系统架构概述.md)

---

## 💡 备注

1. **代码规范**：遵循现有C#/WPF项目的代码风格
2. **调试信息**：关键步骤添加 `Logger.Debug()` 输出
3. **异常处理**：参考关键帧模式的异常处理方式
4. **性能优先**：播放流畅度优先于功能复杂度
5. **参考Python**：遇到问题时优先查看Python版本实现

---

**最后更新：** 2025-10-13  
**负责人：** AI Assistant  
**审核人：** 待定

