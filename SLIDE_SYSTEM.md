# 咏慕投影 - 幻灯片系统详细文档

## 📑 目录
1. [系统架构概览](#1-系统架构概览)
2. [数据模型详解](#2-数据模型详解)
3. [UI层详细分析](#3-ui层详细分析)
4. [Manager层详细分析](#4-manager层详细分析)
5. [渲染层详细分析](#5-渲染层详细分析)
6. [业务流程详解](#6-业务流程详解)
7. [函数调用链分析](#7-函数调用链分析)
8. [常见操作场景](#8-常见操作场景)
9. [开发指南和注意事项](#9-开发指南和注意事项)

---

## 1. 系统架构概览

咏慕投影的幻灯片系统采用了 **WPF (Windows Presentation Foundation)** 作为主要的 UI 框架，并结合 **SkiaSharp** 图形引擎进行高性能的投影渲染。

### 核心架构组件：
*   **UI层 (WPF)**: 负责用户交互、画布编辑、属性设置面板。核心组件为 `MainWindow.TextEditor.cs` 和 `DraggableTextBox.cs`。
*   **Manager层**: 负责业务逻辑、数据持久化及导入导出。核心组件为 `TextProjectManager.cs`、`SlideImportManager.cs` 和 `SlideExportManager.cs`。
*   **渲染层 (SkiaSharp + WPF)**: 
    *   **编辑模式**: 直接使用 WPF 控件渲染（WYSIWYG）。
    *   **投影模式**: 使用 SkiaSharp 将背景图、分割区域图片和文本框（通过 WPF 的 `RenderTargetBitmap` 捕获）进行多层合成，生成最终的高清位图推送到投影窗口。
*   **存储层 (SQLite + EF Core)**: 使用实体框架进行数据访问。

---

## 2. 数据模型详解

### 2.1 TextProject (文本项目)
代表一个完整的演示文稿。
*   `Id`: 主键。
*   `Name`: 项目名称。
*   `BackgroundImagePath`: 项目默认背景图。
*   `CanvasWidth/Height`: 画布物理分辨率（默认 1920x1080）。
*   `Slides`: 包含的幻灯片集合。

### 2.2 Slide (幻灯片)
项目中的一页，包含布局和背景。
*   `ProjectId`: 外键，所属项目。
*   `SplitMode`: 画面分割模式（-1:无, 0:单画面, 1:左右, 2:上下, 3:四宫格, 4:三分割）。
*   `SplitRegionsData`: JSON 格式，存储每个区域的图片路径。
*   `VideoBackgroundEnabled`: 是否启用视频背景。
*   `Elements`: 该页包含的文本元素集合。

### 2.3 TextElement (文本元素)
画布上的可编辑文本框。
*   `X, Y, Width, Height`: 位置和尺寸。
*   `ZIndex`: 图层顺序。
*   `Content`: 纯文本内容。
*   `Styles`: 包含字体、字号、颜色、粗斜体、对齐、边框、背景、阴影、间距等数十个样式字段。
*   `RichTextSpans`: 关联的富文本片段，实现同一框内多种样式。

### 2.4 RichTextSpan (富文本片段)
*   `Text`: 该段文字内容。
*   `SpanOrder`: 排序顺序。
*   `Styles`: 覆盖父元素的样式（字体、字号、颜色等）。

---

## 3. UI层详细分析 (MainWindow.TextEditor.cs)

### 3.1 核心状态字段
*   `_currentTextProject`: 当前正在编辑的项目。
*   `_currentSlide`: 当前正在编辑的幻灯片。
*   `_textBoxes`: 画布上 `DraggableTextBox` 实例列表。
*   `_selectedTextBox`: 当前选中的文本框。
*   `_regionImages`: 分屏模式下区域图片的控件映射。

### 3.2 关键函数说明

#### 🔥 UpdateProjectionFromCanvas()
*   **功能**: 将编辑器内容同步到投影。
*   **逻辑**: 
    1. 检查投影状态。
    2. 执行淡出动画。
    3. 调用渲染流程。
    4. 执行淡入动画。

#### 🔥 ComposeCanvasWithSkia(int targetWidth, int targetHeight, bool transparentBackground)
*   **参数**: 目标尺寸及是否背景透明。
*   **功能**: 核心合成函数。
*   **流程**:
    1. 创建 Skia 画布（优先使用 GPU）。
    2. 绘制背景色/背景图。
    3. 绘制区域图片（支持 Uniform/Fill 模式及变色效果）。
    4. 逐个绘制文本框（调用 `textBox.GetRenderedBitmap`）。
    5. 绘制分割线和角标。

#### LoadSlide(Slide slide)
*   **功能**: 加载并显示指定幻灯片的内容。
*   **流程**: 清空画布 → 加载背景/视频 → 恢复分割配置 → 实例化所有 `DraggableTextBox`。

---

## 4. Manager层详细分析

### 4.1 TextProjectManager.cs
*   **LoadProjectAsync(int id)**: 深度加载项目数据，包括级联加载 `Elements` 和 `RichTextSpans`，并按 `ZIndex` 手动排序。
*   **SaveRichTextSpansAsync(int elementId, List<RichTextSpan> spans)**: 事务化操作，先删除旧片段再批量插入新片段。

### 4.2 SlideImportManager.cs / SlideExportManager.cs
*   **数据格式**: `.hdp` (JSON 结构)。
*   **导出逻辑**: 遍历项目树 -> 图片转 Base64 缩略图 -> JSON 序列化。
*   **导入逻辑**: 解析 JSON -> 名称冲突处理 -> 级联创建实体 -> **对称关系重映射**（非常关键，因为 ID 会变）。

---

## 5. 渲染层详细分析

### 5.1 SkiaTextRenderer.cs
*   **RenderTextBox(TextBoxRenderContext context)**: 渲染逻辑。
    1. 缓存查询（基于样式和内容生成的 CacheKey）。
    2. `TextLayoutEngine` 计算自动换行。
    3. 顺序绘制：阴影 -> 背景 -> 边框 -> 文本（逐行）-> 下划线 -> 光标/选中区。

### 5.2 DraggableTextBox.cs (WPF)
*   封装了 `RichTextBox` 以支持高级文本编辑。
*   **ExtractRichTextSpansFromFlowDocument()**: 遍历 WPF 的 `FlowDocument` 片段，提取样式差异并转换为 `RichTextSpan` 模型保存。
*   **GetRenderedBitmap(double scaleX, double scaleY)**: 使用 `RenderTargetBitmap` 将控件渲染为位图，用于合成投影，确保字体渲染与编辑器 100% 一致。

---

## 6. 业务流程详解

### 6.1 保存幻灯片流程
1. 用户点击“保存”按钮。
2. 遍历所有 `DraggableTextBox`，调用 `ExitEditMode`。
3. 提取富文本样式并同步到 `Data.RichTextSpans`。
4. 调用 `TextProjectManager.UpdateElementsAsync` 批量更新。
5. 生成 300px 宽度的缩略图并保存到 `Thumbnails/` 目录。
6. 自动触发 `UpdateProjectionFromCanvas` 同步到大屏。

### 6.2 视频背景加载流程
1. 选择视频文件。
2. 创建 `MediaElement` 并添加到 `EditorCanvas`（ZIndex 最底层）。
3. 开启 `_projectionManager` 的视频渲染模式。
4. 在投影渲染时，使用 `VisualBrush` 镜像主界面的视频流，并在上方叠加 Skia 渲染的文字层。

---

## 7. 函数调用链分析

### 场景：用户修改文字并更新投影
```text
MainWindow: BtnSaveTextProject_Click
  └── DraggableTextBox: ExitEditMode
      └── DraggableTextBox: ExtractRichTextSpansFromFlowDocument (样式提取)
  └── TextProjectManager: SaveRichTextSpansAsync (数据库持久化)
  └── MainWindow: UpdateProjectionFromCanvas (启动更新)
      └── MainWindow: FadeOutAndUpdateProjection (动画)
          └── MainWindow: ComposeCanvasWithSkia (Skia合成)
              ├── Skia: DrawImage (背景/区域图)
              └── DraggableTextBox: GetRenderedBitmap (WPF控件截图)
                  └── Skia: DrawImage (绘制文字层)
          └── ProjectionManager: UpdateProjectionText (推送位图到显存)
```

---

## 8. 常见操作场景

| 场景 | 关键逻辑 |
| :--- | :--- |
| **添加文本** | `BtnAddText_Click` -> `AddElementAsync` -> `AddTextBoxToCanvas` |
| **分屏切换** | `SetSplitMode` -> `UpdateSplitLayout` -> 清空并重新生成 `_splitRegionBorders` |
| **图片拖入分屏** | `LoadImageToSplitRegion` -> 自动检测原图标记 -> 自动开启 Fill 模式 |
| **导入 .hdp** | `ImportProjectsAsync` -> JSON 反序列化 -> 实体级联保存 |

---

## 9. 开发指南和注意事项

*   🔥 **核心渲染原则**: 编辑器用 WPF 原生渲染，投影用 Skia 合成。
*   ⚠️ **坐标同步**: `TextElement` 中的 X/Y 是逻辑坐标。渲染投影时需根据 `scaleX/scaleY` 进行缩放转换。
*   🔒 **投影锁定**: 当 `_isProjectionLocked` 为 true 时，切换幻灯片不会自动更新投影，必须手动点击更新。
*   🚀 **性能优化**: 
    *   使用 `BitmapCache` 减少 WPF 重绘开销。
    *   Skia 渲染使用了 `GPUContext` 硬件加速。
    *   视频投影使用 `VisualBrush` 避免双重解码开销。

---
*文档版本: 1.0.0*
*最后更新: 2026-01-18*
