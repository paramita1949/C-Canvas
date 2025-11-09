# 圣经投影SkiaSharp全面改造方案

## 📋 改造目标

将圣经模式的**主屏幕**和**投影屏幕**都改用SkiaSharp渲染，解决当前WPF控件与SkiaSharp渲染高度不一致导致的滚动错位问题。

---

## 🔍 当前架构分析

### 当前问题

| 组件 | 当前技术 | 问题 |
|------|---------|------|
| 主屏幕 | WPF `ItemsControl` + `TextBlock` | WPF自动布局计算高度 |
| 投影屏幕 | SkiaSharp 渲染 | 手动计算渲染高度 |
| **结果** | **高度不一致** | **滚动按比例同步，存在误差** |

### 当前代码位置

**主屏幕XAML**：`UI/MainWindow.xaml` (第1407-1435行)
```xml
<ItemsControl x:Name="BibleVerseList" Margin="20,0,20,0">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <Border MouseLeftButtonDown="BibleVerse_Click">
                <TextBlock x:Name="VerseTextBlock" 
                           FontSize="20" 
                           TextWrapping="Wrap"/>
            </Border>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

**投影渲染**：`UI/MainWindow.Bible.cs` (第1998-2044行)
```csharp
private void RenderBibleToProjection()
{
    var skBitmap = RenderVersesToProjection(versesList);
    _projectionManager?.UpdateProjectionText(skBitmap);
}
```

**滚动同步**：`Managers/ProjectionManager.cs` (第491-511行)
```csharp
// ❌ 当前需要按比例计算
double scrollRatio = mainScrollTop / mainExtentHeight;
projScrollTop = scrollRatio * projExtentHeight;
```

---

## 🎯 改造方案

### 方案概述

**核心思路**：参考歌词模式，主屏幕也使用SkiaSharp渲染到Image控件，确保主屏和投影使用**完全相同的渲染逻辑**。

### 技术架构对比

| 模式 | 主屏幕 | 投影屏幕 | 滚动同步 | 状态 |
|------|--------|----------|----------|------|
| 歌词模式 | WPF TextBox | SkiaSharp | ✅ 直接复制位置 | 正常 |
| **圣经模式（改造后）** | **SkiaSharp → Image** | **SkiaSharp** | **✅ 直接复制位置** | **目标** |
| 圣经模式（当前） | WPF ItemsControl | SkiaSharp | ❌ 按比例计算 | 错位 |

---

## 🔧 详细改造步骤

### 第一步：修改XAML布局

**位置**：`UI/MainWindow.xaml` 第1393-1442行

**改造前**：
```xml
<ScrollViewer x:Name="BibleVerseScrollViewer">
    <StackPanel>
        <!-- 章节标题 -->
        <Border x:Name="BibleChapterTitleBorder">
            <TextBlock x:Name="BibleChapterTitle" FontSize="32"/>
        </Border>
        
        <!-- 经文列表 (ItemsControl) -->
        <ItemsControl x:Name="BibleVerseList">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Border MouseLeftButtonDown="BibleVerse_Click">
                        <TextBlock x:Name="VerseTextBlock"/>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </StackPanel>
</ScrollViewer>
```

**改造后**：
```xml
<ScrollViewer x:Name="BibleVerseScrollViewer" 
              ScrollChanged="BibleScrollViewer_ScrollChanged"
              MouseLeftButtonDown="BibleImage_Click">
    <!-- ✅ 使用Image控件显示SkiaSharp渲染结果 -->
    <Image x:Name="BibleRenderImage" 
           Stretch="None"
           HorizontalAlignment="Left"
           VerticalAlignment="Top"
           Background="Black"/>
</ScrollViewer>
```

**改造说明**：
1. 移除 `ItemsControl`、`TextBlock`、`Border` 等WPF控件
2. 改用 `Image` 控件显示SkiaSharp渲染的位图
3. 保留 `ScrollViewer` 用于滚动功能
4. 添加点击事件处理，用于经文高亮功能

---

### 第二步：主屏幕渲染实现

**位置**：`UI/MainWindow.Bible.cs` 新增方法

#### 2.1 主屏幕渲染入口

```csharp
/// <summary>
/// 渲染圣经经文到主屏幕（使用SkiaSharp）
/// </summary>
private void RenderBibleToMainScreen()
{
    if (BibleVerseScrollViewer == null || BibleRenderImage == null)
        return;

    try
    {
        // 获取主屏幕ScrollViewer的尺寸
        double viewportWidth = BibleVerseScrollViewer.ActualWidth;
        double viewportHeight = BibleVerseScrollViewer.ActualHeight;
        
        if (viewportWidth <= 0 || viewportHeight <= 0)
            return;

        // 获取当前显示的所有经文
        var versesList = GetCurrentVerses(); // 复用现有方法
        
        if (versesList.Count == 0)
            return;

        // ✅ 使用SkiaSharp渲染（与投影使用相同逻辑）
        var skBitmap = RenderBibleVerses(
            versesList, 
            (int)viewportWidth,  // 主屏幕宽度
            isMainScreen: true   // 标记为主屏幕
        );
        
        if (skBitmap != null)
        {
            // 转换为WPF BitmapSource并显示
            BibleRenderImage.Source = SkiaWpfHelper.ConvertToWpfBitmap(skBitmap);
            BibleRenderImage.Width = skBitmap.Width;
            BibleRenderImage.Height = skBitmap.Height;
            
            skBitmap.Dispose();
        }
    }
    catch (Exception ex)
    {
        #if DEBUG
        System.Diagnostics.Debug.WriteLine($"❌ [圣经主屏渲染] 失败: {ex.Message}");
        #else
        _ = ex;
        #endif
    }
}
```

#### 2.2 统一渲染方法

```csharp
/// <summary>
/// 渲染圣经经文（主屏和投影共用）
/// </summary>
private SKBitmap RenderBibleVerses(
    List<BibleVerse> verses, 
    int width, 
    bool isMainScreen = false)
{
    // 使用主屏幕高度计算，确保内容高度一致
    int screenHeight = isMainScreen 
        ? (int)BibleVerseScrollViewer.ActualHeight 
        : _projectionManager.GetProjectionScreenSize().height;

    // 构建渲染上下文
    var verseItems = new List<Core.BibleVerseItem>();
    
    // 添加章节标题（如果需要）
    if (!string.IsNullOrEmpty(_currentChapterTitle))
    {
        verseItems.Add(new Core.BibleVerseItem
        {
            IsTitle = true,
            Text = _currentChapterTitle,
            IsHighlighted = false
        });
    }
    
    // 添加所有经文
    foreach (var verse in verses)
    {
        if (verse.Verse == 0)
        {
            // 标题行
            verseItems.Add(new Core.BibleVerseItem
            {
                IsTitle = true,
                Text = verse.Scripture ?? "",
                IsHighlighted = false
            });
        }
        else
        {
            // 普通经文行
            verseItems.Add(new Core.BibleVerseItem
            {
                IsTitle = false,
                VerseNumber = verse.VerseNumberText,
                Text = verse.Scripture ?? "",
                IsHighlighted = verse.IsHighlighted  // ✅ 支持高亮
            });
        }
    }

    // 创建渲染上下文
    var context = new Core.BibleRenderContext
    {
        Verses = verseItems,
        Size = new SKSize(width, screenHeight),
        Padding = new SKRect(20f, 20f, 20f, 20f),
        BackgroundColor = SKColors.Black,
        
        // 样式配置（从ConfigManager读取）
        TitleStyle = new Core.TextStyle
        {
            FontFamily = _configManager.BibleFontFamily,
            FontSize = _configManager.BibleTitleFontSize,
            TextColor = SKColor.Parse(_configManager.BibleTitleColor),
            IsBold = _configManager.BibleTitleBold,
            LineSpacing = 1.2f
        },
        VerseStyle = new Core.TextStyle
        {
            FontFamily = _configManager.BibleFontFamily,
            FontSize = _configManager.BibleVerseFontSize,
            TextColor = SKColor.Parse(_configManager.BibleVerseColor),
            IsBold = _configManager.BibleVerseBold,
            LineSpacing = _configManager.BibleVerseLineSpacing
        },
        VerseNumberStyle = new Core.TextStyle
        {
            FontFamily = _configManager.BibleFontFamily,
            FontSize = _configManager.BibleVerseNumberFontSize,
            TextColor = SKColor.Parse(_configManager.BibleVerseNumberColor),
            IsBold = _configManager.BibleVerseNumberBold,
            LineSpacing = 1.2f
        },
        
        VerseSpacing = _configManager.BibleVerseSpacing,
        HighlightColor = SKColor.Parse(_configManager.BibleHighlightColor)
    };

    // ✅ 使用SkiaTextRenderer渲染
    return _skiaRenderer.RenderBibleText(context);
}
```

#### 2.3 获取当前经文列表

```csharp
/// <summary>
/// 获取当前显示的经文列表
/// </summary>
private List<BibleVerse> GetCurrentVerses()
{
    var versesList = new List<BibleVerse>();
    
    // 从原来的ItemsSource获取数据
    if (BibleVerseList?.ItemsSource is IEnumerable verses)
    {
        foreach (var item in verses)
        {
            if (item is BibleVerse verse)
            {
                versesList.Add(verse);
            }
        }
    }
    
    return versesList;
}
```

---

### 第三步：交互功能实现

#### 3.1 经文点击高亮

**改造前**：通过 `MouseLeftButtonDown="BibleVerse_Click"` 在Border上触发

**改造后**：在Image上检测点击位置，计算对应的经文

```csharp
/// <summary>
/// 圣经Image点击事件 - 检测点击位置对应的经文
/// </summary>
private void BibleImage_Click(object sender, MouseButtonEventArgs e)
{
    if (BibleRenderImage == null)
        return;

    // 获取点击位置
    var clickPosition = e.GetPosition(BibleRenderImage);
    double clickY = clickPosition.Y;
    
    #if DEBUG
    System.Diagnostics.Debug.WriteLine($"📍 [圣经点击] 点击Y坐标: {clickY}");
    #endif

    // 计算点击位置对应的经文
    var clickedVerse = GetVerseAtPosition(clickY);
    
    if (clickedVerse != null && !clickedVerse.IsTitle)
    {
        // 切换高亮状态
        ToggleVerseHighlight(clickedVerse);
        
        // 重新渲染主屏幕和投影
        RenderBibleToMainScreen();
        if (_projectionManager != null && _projectionManager.IsProjecting)
        {
            RenderBibleToProjection();
        }
    }
}

/// <summary>
/// 根据Y坐标获取对应的经文
/// </summary>
private BibleVerse GetVerseAtPosition(double y)
{
    // 需要实现：遍历当前经文列表，根据累计高度判断点击位置
    // 参考 SkiaTextRenderer.RenderBibleText 的布局计算逻辑
    
    var verses = GetCurrentVerses();
    if (verses.Count == 0)
        return null;

    // 调整坐标（考虑滚动位置）
    double adjustedY = y + BibleVerseScrollViewer.VerticalOffset;
    
    // 使用与渲染相同的布局计算
    float currentY = 20f; // Padding.Top
    
    foreach (var verse in verses)
    {
        if (verse.Verse == 0)
        {
            // 标题行（跳过，不支持点击）
            float titleHeight = CalculateTitleHeight(verse);
            currentY += titleHeight + 15f;
        }
        else
        {
            // 经文行
            float verseHeight = CalculateVerseHeight(verse);
            
            // 检查点击位置是否在当前经文范围内
            if (adjustedY >= currentY && adjustedY < currentY + verseHeight)
            {
                return verse;
            }
            
            currentY += verseHeight + _configManager.BibleVerseSpacing;
        }
    }
    
    return null;
}

/// <summary>
/// 切换经文高亮状态（单选模式）
/// </summary>
private void ToggleVerseHighlight(BibleVerse clickedVerse)
{
    var verses = GetCurrentVerses();
    
    // 如果点击的是已高亮的经文，取消高亮
    if (clickedVerse.IsHighlighted)
    {
        clickedVerse.IsHighlighted = false;
    }
    else
    {
        // 取消其他所有经文的高亮
        foreach (var verse in verses)
        {
            verse.IsHighlighted = false;
        }
        
        // 高亮当前点击的经文
        clickedVerse.IsHighlighted = true;
    }
}

/// <summary>
/// 计算标题高度（与SkiaTextRenderer逻辑一致）
/// </summary>
private float CalculateTitleHeight(BibleVerse verse)
{
    var style = new Core.TextStyle
    {
        FontFamily = _configManager.BibleFontFamily,
        FontSize = _configManager.BibleTitleFontSize,
        IsBold = _configManager.BibleTitleBold,
        LineSpacing = 1.2f
    };
    
    float contentWidth = (float)BibleVerseScrollViewer.ActualWidth - 40f; // Padding
    var layout = _skiaRenderer.CalculateLayout(verse.Scripture, style, contentWidth);
    return layout.TotalSize.Height;
}

/// <summary>
/// 计算经文高度（与SkiaTextRenderer逻辑一致）
/// </summary>
private float CalculateVerseHeight(BibleVerse verse)
{
    var style = new Core.TextStyle
    {
        FontFamily = _configManager.BibleFontFamily,
        FontSize = _configManager.BibleVerseFontSize,
        IsBold = _configManager.BibleVerseBold,
        LineSpacing = _configManager.BibleVerseLineSpacing
    };
    
    string verseNumberText = $"{verse.VerseNumberText} ";
    float numberWidth = MeasureTextWidth(verseNumberText, _configManager.BibleVerseNumberFontSize);
    
    float contentWidth = (float)BibleVerseScrollViewer.ActualWidth - 40f; // Padding
    float firstLineWidth = contentWidth - numberWidth;
    
    var lines = _skiaRenderer.WrapText(verse.Scripture, style, firstLineWidth);
    return lines.Count * style.FontSize * style.LineSpacing;
}
```

#### 3.2 鼠标悬停效果（可选）

由于改用Image控件，无法直接实现WPF的鼠标悬停高亮效果。可以考虑：

**方案A**：放弃悬停效果，只保留点击高亮（推荐）
- 简单直接，性能好
- 用户体验影响较小

**方案B**：实现悬停效果
- 监听 `MouseMove` 事件
- 实时计算鼠标位置对应的经文
- 重新渲染（性能开销较大）

**建议**：采用方案A，放弃悬停效果。

---

### 第四步：滚动同步改造

**位置**：`Managers/ProjectionManager.cs`

#### 4.1 修改滚动同步方法

**改造前**（第491-511行）：
```csharp
// ❌ 按比例计算（因为高度不一致）
double mainScrollTop = bibleScrollViewer.VerticalOffset;
double mainExtentHeight = bibleScrollViewer.ExtentHeight;
double projExtentHeight = _projectionScrollViewer.ExtentHeight;

double scrollRatio = mainScrollTop / mainExtentHeight;
projScrollTop = scrollRatio * projExtentHeight;

_projectionScrollViewer.ScrollToVerticalOffset(projScrollTop);
```

**改造后**：
```csharp
/// <summary>
/// 同步圣经滚动位置到投影（改为直接复制位置，与歌词一致）
/// </summary>
public void SyncBibleScroll(ScrollViewer bibleScrollViewer)
{
    if (!_syncEnabled || _projectionWindow == null || bibleScrollViewer == null)
        return;

    try
    {
        // 性能节流
        var currentTime = DateTime.Now;
        if (currentTime - _lastSyncTime < _syncThrottleInterval)
            return;
        _lastSyncTime = currentTime;

        _mainWindow.Dispatcher.Invoke(() =>
        {
            if (_projectionScrollViewer == null)
                return;

            // ✅ 直接使用相同的滚动位置（因为主屏和投影使用相同的渲染逻辑）
            double mainScrollTop = bibleScrollViewer.VerticalOffset;
            double projScrollTop = mainScrollTop;
            
            _projectionScrollViewer.ScrollToVerticalOffset(projScrollTop);

            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"📊 [圣经滚动同步] 主屏: {mainScrollTop:F2}, 投影: {projScrollTop:F2}");
            #endif
        });
    }
    catch (Exception ex)
    {
        #if DEBUG
        System.Diagnostics.Debug.WriteLine($"❌ [圣经滚动同步] 失败: {ex.Message}");
        #else
        _ = ex;
        #endif
    }
}
```

#### 4.2 添加滚动事件处理

**位置**：`UI/MainWindow.Bible.cs`

```csharp
/// <summary>
/// 圣经滚动事件 - 同步到投影
/// </summary>
private void BibleScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
{
    // 如果投影已开启且在圣经模式，同步滚动位置
    if (_isBibleMode && _projectionManager != null && _projectionManager.IsProjecting)
    {
        // ✅ 同步投影滚动位置（传入圣经ScrollViewer）
        _projectionManager.SyncBibleScroll(BibleVerseScrollViewer);
    }
}
```

---

### 第五步：投影渲染改造

**位置**：`UI/MainWindow.Bible.cs` 修改现有方法

**改造要点**：
1. 投影渲染使用与主屏幕相同的 `RenderBibleVerses` 方法
2. 确保使用投影屏幕的宽度和高度

```csharp
/// <summary>
/// 渲染圣经经文到投影
/// </summary>
private void RenderBibleToProjection()
{
    try
    {
        if (BibleVerseList?.ItemsSource == null)
            return;

        // 获取当前显示的所有经文
        var versesList = GetCurrentVerses();
        
        if (versesList.Count == 0)
            return;

        // 获取投影屏幕的实际尺寸
        var (screenWidth, screenHeight) = _projectionManager.GetProjectionScreenSize();

        // ✅ 使用统一的渲染方法（与主屏幕完全一致）
        var skBitmap = RenderBibleVerses(
            versesList, 
            screenWidth,      // 投影屏幕宽度
            isMainScreen: false
        );
        
        if (skBitmap != null)
        {
            _projectionManager?.UpdateProjectionText(skBitmap);
            skBitmap.Dispose();
        }
    }
    catch (Exception ex)
    {
        #if DEBUG
        System.Diagnostics.Debug.WriteLine($"❌ [圣经投影渲染] 失败: {ex.Message}");
        #else
        _ = ex;
        #endif
    }
}
```

---

### 第六步：触发渲染的时机

**需要触发主屏幕和投影渲染的场景**：

1. **加载经文时**
   - 方法：`LoadBibleVerses()`
   - 改造：添加 `RenderBibleToMainScreen()` 调用

2. **切换章节时**
   - 方法：`LoadChapter()`
   - 改造：添加 `RenderBibleToMainScreen()` 调用

3. **点击经文时**
   - 方法：`BibleImage_Click()`
   - 改造：已在方法中实现

4. **样式设置改变时**
   - 方法：`ApplyBibleStyleOnce()`
   - 改造：改为 `RenderBibleToMainScreen()` + `RenderBibleToProjection()`

5. **投影开启时**
   - 方法：`OnProjectionStateChanged()`
   - 改造：调用 `RenderBibleToProjection()`

6. **窗口大小改变时**
   - 事件：`BibleVerseScrollViewer.SizeChanged`
   - 改造：添加事件处理，重新渲染主屏幕

**示例代码**：
```csharp
/// <summary>
/// ScrollViewer尺寸改变时重新渲染
/// </summary>
private void BibleScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
{
    // 延迟渲染，避免频繁调用
    _renderTimer?.Stop();
    _renderTimer = new DispatcherTimer
    {
        Interval = TimeSpan.FromMilliseconds(200)
    };
    _renderTimer.Tick += (s, args) =>
    {
        _renderTimer.Stop();
        RenderBibleToMainScreen();
    };
    _renderTimer.Start();
}
```

---

## 📊 改造前后对比

### 架构对比

| 项目 | 改造前 | 改造后 |
|------|--------|--------|
| 主屏幕技术 | WPF ItemsControl + TextBlock | SkiaSharp → Image |
| 投影屏幕技术 | SkiaSharp | SkiaSharp |
| 渲染逻辑 | 主屏和投影**不同** | 主屏和投影**完全一致** |
| 高度计算 | WPF自动 + SkiaSharp手动 | 都用SkiaSharp手动 |
| 滚动同步 | 按比例计算（有误差） | 直接复制位置（精确） |
| 交互功能 | WPF事件系统（简单） | 手动计算点击位置（复杂） |
| 性能 | 中等 | 更好（GPU加速） |

### 功能影响

| 功能 | 改造前 | 改造后 | 影响评估 |
|------|--------|--------|----------|
| 经文显示 | ✅ 正常 | ✅ 正常 | 无影响 |
| 滚动浏览 | ✅ 正常 | ✅ 正常 | 无影响 |
| 点击高亮 | ✅ 正常 | ✅ 正常（需实现） | 需要重写逻辑 |
| 鼠标悬停高亮 | ✅ 支持 | ❌ 放弃 | 建议放弃 |
| 样式设置 | ✅ 正常 | ✅ 正常 | 无影响 |
| 投影同步 | ❌ 有误差 | ✅ 完美同步 | **核心改进** |
| 性能 | 中等 | 更好 | 正面影响 |

---

## 🔍 关键功能影响分析

### 1. 智能识别滚动功能（鼠标滚轮/下帧按钮）

#### 当前实现

**代码位置**：`UI/MainWindow.Bible.cs` 第3811-3926行

**核心逻辑**：
```csharp
// 1. 鼠标滚轮事件
BibleVerseScrollViewer_PreviewMouseWheel()
  → HandleVerseScroll(direction, _scrollVerseCount)

// 2. 智能对齐算法
HandleVerseScroll(direction, count)
  → FindClosestVerseIndex(currentOffset)  // 找到最接近顶部的经文
  → CalculateVerseOffset(verseIndex)      // 计算经文的精确Y位置
  → 判断是否已对齐（阈值5像素）
  → 智能修复或移动指定节数
  → ScrollToVerseInstant(targetVerseIndex) // 跳转

// 3. 核心依赖
CalculateVerseOffset(int verseIndex)
  → 遍历每一节，累加高度
  → 使用 TextBlock.ActualHeight（WPF布局）
  → 返回经文的Y坐标
```

**关键点**：
- 依赖 `ItemsControl` 的 `Container` 获取每个经文的实际高度
- 使用 `TextBlock.ActualHeight` 获取渲染后的高度
- 需要精确计算每一节的Y坐标偏移量

#### 新架构实现方案

**✅ 完全可以实现，且更精确**

**改造要点**：

1. **复用SkiaSharp的布局计算**
```csharp
/// <summary>
/// 计算经文在Image中的Y坐标偏移（与SkiaSharp渲染逻辑一致）
/// </summary>
private float CalculateVerseOffsetNew(int verseIndex)
{
    var verses = GetCurrentVerses();
    if (verseIndex < 0 || verseIndex >= verses.Count)
        return 0;

    float currentY = 20f; // Padding.Top
    bool isFirstTitle = true;
    
    for (int i = 0; i <= verseIndex; i++)
    {
        if (i == verseIndex)
            return currentY; // 返回当前节的起始位置
        
        var verse = verses[i];
        
        if (verse.Verse == 0) // 标题
        {
            if (!isFirstTitle)
                currentY += 60f; // 记录分隔间距
            isFirstTitle = false;
            
            float titleHeight = CalculateTitleHeightWithSkia(verse);
            currentY += titleHeight + 15f; // 标题后间距
        }
        else // 经文
        {
            float verseHeight = CalculateVerseHeightWithSkia(verse);
            currentY += verseHeight + (float)_configManager.BibleVerseSpacing;
        }
    }
    
    return currentY;
}

/// <summary>
/// 使用SkiaSharp布局引擎计算标题高度（与渲染完全一致）
/// </summary>
private float CalculateTitleHeightWithSkia(BibleVerse verse)
{
    var style = new Core.TextStyle
    {
        FontFamily = _configManager.BibleFontFamily,
        FontSize = (float)_configManager.BibleTitleFontSize,
        IsBold = true,
        LineSpacing = 1.2f
    };
    
    float contentWidth = (float)BibleVerseScrollViewer.ActualWidth - 40f; // 减去Padding
    
    // 使用SkiaTextRenderer的布局引擎
    var layout = _skiaRenderer.CalculateLayout(verse.Scripture, style, contentWidth);
    return layout.TotalSize.Height;
}

/// <summary>
/// 使用SkiaSharp布局引擎计算经文高度（与渲染完全一致）
/// </summary>
private float CalculateVerseHeightWithSkia(BibleVerse verse)
{
    var verseStyle = new Core.TextStyle
    {
        FontFamily = _configManager.BibleFontFamily,
        FontSize = (float)_configManager.BibleFontSize,
        IsBold = false,
        LineSpacing = 1.2f
    };
    
    var numberStyle = new Core.TextStyle
    {
        FontFamily = _configManager.BibleFontFamily,
        FontSize = (float)_configManager.BibleVerseNumberFontSize,
        IsBold = false,
        LineSpacing = 1.2f
    };
    
    // 计算节号宽度
    string verseNumberText = $"{verse.VerseNumberText} ";
    float numberWidth = _skiaRenderer.MeasureTextWidth(verseNumberText, numberStyle);
    
    // 第一行宽度 = 总宽度 - 节号宽度
    float contentWidth = (float)BibleVerseScrollViewer.ActualWidth - 40f; // 减去Padding
    float firstLineWidth = contentWidth - numberWidth;
    
    // 使用SkiaTextRenderer的换行算法
    var lines = _skiaRenderer.WrapText(verse.Scripture, verseStyle, firstLineWidth);
    return lines.Count * verseStyle.FontSize * verseStyle.LineSpacing;
}
```

2. **滚轮事件保持不变**
```csharp
// ✅ 无需改动，继续使用现有逻辑
private void BibleVerseScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
{
    e.Handled = true;
    int direction = e.Delta > 0 ? -1 : 1;
    HandleVerseScroll(direction, _scrollVerseCount);
}

// ✅ 修改为使用新的计算方法
private void HandleVerseScroll(int direction, int count)
{
    double currentOffset = BibleVerseScrollViewer.VerticalOffset;
    int currentVerseIndex = FindClosestVerseIndex(currentOffset);
    
    // 使用新的 CalculateVerseOffsetNew 方法
    double currentVerseOffset = CalculateVerseOffsetNew(currentVerseIndex);
    double offsetDiff = currentOffset - currentVerseOffset;
    
    // 后续逻辑完全不变...
}
```

**优势**：
- ✅ 高度计算与渲染**完全一致**（都用SkiaSharp）
- ✅ 不依赖WPF的Container和ActualHeight
- ✅ 即使经文未渲染也能计算位置
- ✅ 更加精确可靠

---

### 2. 点击经文变色功能

#### 当前实现

**代码位置**：`UI/MainWindow.Bible.cs` 第1560-1703行

**核心逻辑**：
```csharp
// 1. 点击事件（Border）
BibleVerse_Click(sender, e)
  → 获取点击的 Border 和 BibleVerse 对象
  → 切换 IsHighlighted 属性
  → UpdateVerseHighlight(border, verse) // 更新TextBlock颜色

// 2. 高亮状态
BibleVerse.IsHighlighted (bool)
  → 存储在数据模型中
  → INotifyPropertyChanged 支持

// 3. 颜色应用
if (verse.IsHighlighted)
    textBlock.Foreground = BibleHighlightColor;
else
    textBlock.Foreground = BibleTextColor;
```

**依赖**：
- WPF的Border点击事件
- 通过 `sender` 获取点击的Border
- 直接操作TextBlock的Foreground属性

#### 新架构实现方案

**✅ 完全可以实现**

**改造要点**：

1. **Image点击事件 + 位置计算**
```csharp
/// <summary>
/// 圣经Image点击事件
/// </summary>
private void BibleImage_Click(object sender, MouseButtonEventArgs e)
{
    if (BibleRenderImage == null)
        return;

    // 获取点击位置（相对于Image）
    var clickPosition = e.GetPosition(BibleRenderImage);
    double clickY = clickPosition.Y;
    
    // 调整坐标（加上滚动偏移）
    double adjustedY = clickY + BibleVerseScrollViewer.VerticalOffset;
    
    #if DEBUG
    System.Diagnostics.Debug.WriteLine($"📍 [圣经点击] 点击Y坐标: {clickY:F1}, 调整后: {adjustedY:F1}");
    #endif

    // 找到点击位置对应的经文
    var clickedVerse = GetVerseAtYPosition(adjustedY);
    
    if (clickedVerse != null && clickedVerse.Verse != 0) // 不允许点击标题
    {
        // 切换高亮状态
        ToggleVerseHighlight(clickedVerse);
        
        // 重新渲染（主屏和投影）
        RenderBibleToMainScreen();
        if (_projectionManager != null && _projectionManager.IsProjecting)
        {
            RenderBibleToProjection();
        }
        
        #if DEBUG
        System.Diagnostics.Debug.WriteLine($"✅ [圣经点击] 点击经文: {clickedVerse.Reference}, 高亮={clickedVerse.IsHighlighted}");
        #endif
    }
}

/// <summary>
/// 根据Y坐标找到对应的经文
/// </summary>
private BibleVerse GetVerseAtYPosition(double y)
{
    var verses = GetCurrentVerses();
    if (verses.Count == 0)
        return null;

    float currentY = 20f; // Padding.Top
    bool isFirstTitle = true;
    
    foreach (var verse in verses)
    {
        float verseStartY = currentY;
        float verseHeight;
        
        if (verse.Verse == 0) // 标题
        {
            if (!isFirstTitle)
            {
                currentY += 60f;
                verseStartY = currentY;
            }
            isFirstTitle = false;
            
            verseHeight = CalculateTitleHeightWithSkia(verse);
            
            // 标题不可点击，跳过检测
            currentY += verseHeight + 15f;
        }
        else // 经文
        {
            verseHeight = CalculateVerseHeightWithSkia(verse);
            
            // 检查点击位置是否在当前经文范围内
            if (y >= verseStartY && y < verseStartY + verseHeight)
            {
                return verse;
            }
            
            currentY += verseHeight + (float)_configManager.BibleVerseSpacing;
        }
    }
    
    return null;
}

/// <summary>
/// 切换经文高亮状态（单选模式）
/// </summary>
private void ToggleVerseHighlight(BibleVerse clickedVerse)
{
    var verses = GetCurrentVerses();
    
    if (clickedVerse.IsHighlighted)
    {
        // 取消高亮
        clickedVerse.IsHighlighted = false;
    }
    else
    {
        // 先取消所有其他经文的高亮
        foreach (var verse in verses)
        {
            verse.IsHighlighted = false;
        }
        
        // 高亮当前点击的经文
        clickedVerse.IsHighlighted = true;
    }
}
```

2. **渲染时应用高亮颜色**
```csharp
// ✅ 在 RenderBibleVerses 方法中已经支持
verseItems.Add(new Core.BibleVerseItem
{
    IsTitle = false,
    VerseNumber = verse.VerseNumberText,
    Text = verse.Scripture ?? "",
    IsHighlighted = verse.IsHighlighted  // ✅ 传递高亮状态
});

// SkiaTextRenderer 会根据 IsHighlighted 使用不同颜色
// Core/SkiaTextRenderer.cs 第259-263行
var verseColor = layout.Verse.IsHighlighted 
    ? context.HighlightColor 
    : context.VerseStyle.TextColor;
```

**优势**：
- ✅ 高亮状态仍然存储在 `BibleVerse.IsHighlighted`（无需改动数据模型）
- ✅ 点击检测逻辑与渲染逻辑完全一致（都用SkiaSharp计算）
- ✅ 投影会自动同步高亮状态（同一数据源）

**劣势**：
- ❌ 失去鼠标悬停高亮效果（建议放弃，影响较小）
- ⚠️ 点击检测需要精确计算，增加代码复杂度

---

### 3. 字体大小、边距、节距配置

#### 当前配置项

**位置**：`Core/ConfigManager.cs` 第948-1002行

**配置项**：
```csharp
// 字体
BibleFontFamily                 // 字体家族
BibleFontSize                   // 经文字体大小（默认：46）
BibleTitleFontSize              // 标题字体大小（默认：61.3 = 46 × 1.333）
BibleVerseNumberFontSize        // 节号字体大小（默认：46）

// 颜色
BibleTextColor                  // 经文颜色（默认：#FF9A35 橙色）
BibleTitleColor                 // 标题颜色（默认：#FF0000 红色）
BibleVerseNumberColor           // 节号颜色（默认：#FFFF00 黄色）
BibleHighlightColor             // 高亮颜色（默认：#FFFF00 黄色）
BibleBackgroundColor            // 背景颜色（默认：#000000 黑色）

// 布局
BibleMargin                     // 左右边距（默认：15）
BibleVerseSpacing               // 节间距（默认：15）
```

#### 新架构实现

**✅ 完全支持，无任何影响**

**应用方式**：

```csharp
// 在 RenderBibleVerses 方法中使用配置
var context = new Core.BibleRenderContext
{
    // ✅ 字体配置
    TitleStyle = new Core.TextStyle
    {
        FontFamily = _configManager.BibleFontFamily,
        FontSize = (float)_configManager.BibleTitleFontSize,
        TextColor = SKColor.Parse(_configManager.BibleTitleColor),
        IsBold = true,
        LineSpacing = 1.2f
    },
    VerseStyle = new Core.TextStyle
    {
        FontFamily = _configManager.BibleFontFamily,
        FontSize = (float)_configManager.BibleFontSize,
        TextColor = SKColor.Parse(_configManager.BibleTextColor),
        IsBold = false,
        LineSpacing = 1.2f
    },
    VerseNumberStyle = new Core.TextStyle
    {
        FontFamily = _configManager.BibleFontFamily,
        FontSize = (float)_configManager.BibleVerseNumberFontSize,
        TextColor = SKColor.Parse(_configManager.BibleVerseNumberColor),
        IsBold = false,
        LineSpacing = 1.2f
    },
    
    // ✅ 布局配置
    Padding = new SKRect(
        (float)_configManager.BibleMargin,  // Left
        20f,                                 // Top
        (float)_configManager.BibleMargin,  // Right
        20f                                  // Bottom
    ),
    VerseSpacing = (float)_configManager.BibleVerseSpacing,
    
    // ✅ 颜色配置
    BackgroundColor = SKColor.Parse(_configManager.BibleBackgroundColor),
    HighlightColor = SKColor.Parse(_configManager.BibleHighlightColor)
};
```

**优势**：
- ✅ 所有配置项都能完整支持
- ✅ 配置改变后重新渲染即可生效
- ✅ 主屏和投影使用相同配置，确保一致

---

### 4. 下帧按钮功能（上一节/下一节）

#### 当前实现

**代码位置**：`UI/MainWindow.Bible.cs` 第3785-3799行

**核心逻辑**：
```csharp
// 上一节按钮
private void BtnBiblePrevVerse_Click(object sender, RoutedEventArgs e)
{
    HandleVerseScroll(-1, _scrollVerseCount); // 向上滚动N节
}

// 下一节按钮
private void BtnBibleNextVerse_Click(object sender, RoutedEventArgs e)
{
    HandleVerseScroll(1, _scrollVerseCount); // 向下滚动N节
}

// _scrollVerseCount：可配置滚动节数（1-10节）
```

#### 新架构实现

**✅ 完全不受影响**

**原因**：
- 按钮点击调用的是 `HandleVerseScroll` 方法
- 该方法已在【智能识别滚动功能】中改造
- 只需修改 `CalculateVerseOffset` 使用新的计算逻辑
- 按钮功能本身无需任何改动

**验证**：
```csharp
// ✅ 按钮事件不变
BtnBiblePrevVerse_Click → HandleVerseScroll(-1, _scrollVerseCount)
BtnBibleNextVerse_Click → HandleVerseScroll(1, _scrollVerseCount)

// ✅ HandleVerseScroll 使用新的计算方法
HandleVerseScroll(direction, count)
  → CalculateVerseOffsetNew(verseIndex) // 改用SkiaSharp计算
  → ScrollToVerseInstant(targetIndex)   // 滚动逻辑不变
```

---

---

### 5. 投影记录合并功能

#### 当前实现

**代码位置**：`UI/MainWindow.Bible.cs` 第2468-2598行

**核心逻辑**：
```csharp
// 1. 数据模型
ObservableCollection<BibleHistoryItem> _historySlots  // 20个历史槽位
ObservableCollection<BibleVerse> _mergedVerses       // 合并后的经文列表

// 2. 锁定/解锁操作（双击）
BibleHistoryItem_Click → 双击切换 IsLocked
  → 如果锁定：AddLockedRecordVerses(item)
  → 如果解锁：RemoveLockedRecordVerses(item)

// 3. 合并显示
LoadAndDisplayLockedRecords()
  → 获取所有锁定的记录（按槽位顺序）
  → 构建合并列表：
     - 为每个记录添加标题行（Verse=0）
     - 加载该记录的所有经文
  → 更新 _mergedVerses
  → 绑定到 BibleVerseList（ItemsControl）
  → 应用样式
  → 更新投影

// 4. 数据流
_historySlots (锁定状态) 
  → LoadAndDisplayLockedRecords() 
  → _mergedVerses (合并数据)
  → BibleVerseList.ItemsSource (UI显示)
  → RenderVersesToProjection() (投影渲染)
```

**关键特性**：
- **标题行标记**：`Verse == 0` 表示这是标题行（显示"创世记3章1-24节"）
- **数据绑定**：`BibleVerseList.ItemsSource = _mergedVerses`
- **增量更新**：使用 `ObservableCollection` 的 `Clear()` / `Add()` 自动通知UI
- **投影同步**：合并后自动调用投影渲染

#### 新架构实现方案

**✅ 完全支持，且更稳定**

**改造要点**：

1. **数据处理逻辑完全不变**
```csharp
// ✅ 数据模型保持不变
ObservableCollection<BibleVerse> _mergedVerses

// ✅ 合并逻辑保持不变
LoadAndDisplayLockedRecords()
{
    var lockedItems = _historySlots
        .Where(x => x.IsLocked && x.BookId > 0)
        .OrderBy(x => x.Index)
        .ToList();
    
    var newVerses = new List<BibleVerse>();
    
    foreach (var item in lockedItems)
    {
        // 添加标题行（Verse=0）
        newVerses.Add(new BibleVerse 
        { 
            Verse = 0,
            Scripture = item.DisplayText
        });
        
        // 加载经文
        for (int verse = item.StartVerse; verse <= item.EndVerse; verse++)
        {
            var verseData = await _bibleService.GetVerseAsync(...);
            newVerses.Add(verseData);
        }
    }
    
    // ✅ 更新数据（不变）
    _mergedVerses.Clear();
    foreach (var verse in newVerses)
    {
        _mergedVerses.Add(verse);
    }
    
    // ⚠️ 改为调用新的渲染方法
    RenderBibleToMainScreen();  // 主屏幕渲染
    RenderBibleToProjection();  // 投影渲染
}
```

2. **渲染逻辑自动支持标题行**
```csharp
// ✅ RenderBibleVerses 已支持标题行（Verse=0）
private SKBitmap RenderBibleVerses(List<BibleVerse> verses, int width, bool isMainScreen)
{
    var verseItems = new List<Core.BibleVerseItem>();
    
    foreach (var verse in verses)
    {
        if (verse.Verse == 0)
        {
            // ✅ 标题行（与现有投影渲染逻辑一致）
            verseItems.Add(new Core.BibleVerseItem
            {
                IsTitle = true,
                Text = verse.Scripture,
                IsHighlighted = false
            });
        }
        else
        {
            // ✅ 普通经文行
            verseItems.Add(new Core.BibleVerseItem
            {
                IsTitle = false,
                VerseNumber = verse.VerseNumberText,
                Text = verse.Scripture,
                IsHighlighted = verse.IsHighlighted
            });
        }
    }
    
    // SkiaSharp渲染（主屏和投影使用相同逻辑）
    return _skiaRenderer.RenderBibleText(context);
}
```

3. **锁定/解锁操作不受影响**
```csharp
// ✅ UI交互逻辑完全不变
private async void BibleHistoryItem_Click(object sender, MouseButtonEventArgs e)
{
    // 双击检测
    var isDoubleClick = ...;
    
    if (isDoubleClick)
    {
        // 切换锁定状态
        item.IsLocked = !item.IsLocked;
        
        // 增量更新
        if (item.IsLocked)
            await AddLockedRecordVerses(item);
        else
            RemoveLockedRecordVerses(item);
        
        // ⚠️ 改为调用新的渲染方法
        RenderBibleToMainScreen();
        RenderBibleToProjection();
    }
}
```

**优势分析**：

| 方面 | 当前实现 | 新架构 | 改进 |
|------|---------|--------|------|
| **数据流程** | _mergedVerses → ItemsControl | _mergedVerses → SkiaSharp → Image | ✅ 数据流程不变 |
| **标题行支持** | WPF特殊样式 | SkiaSharp统一渲染 | ✅ 逻辑更统一 |
| **滚动同步** | 按比例计算（有误差） | 直接复制位置（精确） | ✅ **大幅改善** |
| **性能** | WPF布局+SkiaSharp投影 | 纯SkiaSharp | ✅ 更流畅 |
| **投影一致性** | 主屏WPF + 投影Skia（不一致） | 主屏Skia + 投影Skia（完全一致） | ✅ **核心改进** |

**测试验证**：
```csharp
// 测试场景
1. 锁定单个记录 → 验证显示正确
2. 锁定多个记录 → 验证顺序和分隔正确
3. 合并后滚动 → 验证主屏和投影同步精确
4. 点击高亮 → 验证标题不可点击，经文可高亮
5. 解锁记录 → 验证删除正确
6. 清空所有锁定 → 验证清空显示
```

**核心结论**：
- ✅ 数据处理逻辑**完全不变**（_mergedVerses, IsLocked, 标题行标记）
- ✅ SkiaSharp渲染**原生支持**标题行（Verse=0）
- ✅ 滚动同步会**显著改善**（从按比例计算改为直接复制）
- ✅ 实现难度**很低**（只需调用新的渲染方法）

---

## 🎯 功能对比总结表

| 功能 | 当前实现 | 新架构实现 | 影响评估 | 实现难度 |
|------|---------|-----------|---------|---------|
| **智能滚动识别** | WPF ActualHeight计算 | SkiaSharp布局引擎计算 | ✅ 无影响，更精确 | ⭐⭐⭐ 中等 |
| **鼠标滚轮对齐** | HandleVerseScroll | HandleVerseScroll（改用新计算） | ✅ 无影响 | ⭐⭐ 较低 |
| **下帧按钮** | HandleVerseScroll | HandleVerseScroll（改用新计算） | ✅ 无影响 | ⭐ 很低 |
| **点击经文变色** | Border点击事件 | Image点击+位置计算 | ⚠️ 需重写，但功能完整 | ⭐⭐⭐⭐ 较高 |
| **鼠标悬停高亮** | Border IsMouseOver触发 | 实现成本高 | ❌ 建议放弃 | ⭐⭐⭐⭐⭐ 很高 |
| **投影记录合并** | _mergedVerses + ItemsControl | _mergedVerses + SkiaSharp | ✅ 无影响，滚动同步更准确 | ⭐ 很低 |
| **字体大小** | ConfigManager配置 | ConfigManager配置 | ✅ 无影响 | ⭐ 很低 |
| **边距配置** | ConfigManager配置 | ConfigManager配置 | ✅ 无影响 | ⭐ 很低 |
| **节间距配置** | ConfigManager配置 | ConfigManager配置 | ✅ 无影响 | ⭐ 很低 |
| **高亮颜色** | ConfigManager配置 | ConfigManager配置 | ✅ 无影响 | ⭐ 很低 |
| **滚动节数配置** | _scrollVerseCount | _scrollVerseCount | ✅ 无影响 | ⭐ 很低 |

---

## 🚧 注意事项与风险

### 1. 智能滚动功能改造（关键）

**风险等级**：⭐⭐⭐ 中等

**改造要点**：
- `CalculateVerseOffset` 必须改用SkiaSharp布局引擎
- 确保计算逻辑与 `SkiaTextRenderer.RenderBibleText` 完全一致
- 需要封装 `CalculateTitleHeightWithSkia` 和 `CalculateVerseHeightWithSkia`

**验证方法**：
```csharp
// 对比测试：渲染后的实际位置 vs 计算的位置
for (int i = 0; i < verses.Count; i++)
{
    float calculatedY = CalculateVerseOffsetNew(i);
    float actualY = GetVerseActualYFromRenderedImage(i);
    
    float diff = Math.Abs(calculatedY - actualY);
    if (diff > 1.0f) // 误差大于1像素
    {
        Debug.WriteLine($"⚠️ 警告：节{i+1}位置计算误差 {diff:F2}px");
    }
}
```

### 2. 点击检测功能改造（关键）

**风险等级**：⭐⭐⭐⭐ 较高

**改造要点**：
- `GetVerseAtYPosition` 必须与渲染逻辑完全一致
- 需要处理滚动偏移量
- 需要精确的边界检测

**测试方案**：
1. 点击每一节的顶部、中部、底部，验证识别正确
2. 点击标题，验证不触发高亮
3. 点击节间距，验证识别到上方的节
4. 快速连续点击，验证状态正确切换

**调试辅助**：
```csharp
// 调试模式：绘制每一节的边框线
#if DEBUG
private void DrawVerseDebugBorders(SKCanvas canvas)
{
    var verses = GetCurrentVerses();
    float currentY = 20f;
    bool isFirstTitle = true;
    
    using var paint = new SKPaint
    {
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 2,
        Color = SKColors.Red
    };
    
    foreach (var verse in verses)
    {
        if (verse.Verse == 0) // 标题
        {
            if (!isFirstTitle) currentY += 60f;
            isFirstTitle = false;
            
            float height = CalculateTitleHeightWithSkia(verse);
            canvas.DrawRect(20, currentY, canvas.LocalClipBounds.Width - 40, height, paint);
            currentY += height + 15f;
        }
        else // 经文
        {
            float height = CalculateVerseHeightWithSkia(verse);
            canvas.DrawRect(20, currentY, canvas.LocalClipBounds.Width - 40, height, paint);
            currentY += height + (float)_configManager.BibleVerseSpacing;
        }
    }
}
#endif
```

### 3. 性能优化

**问题**：每次样式改变、高亮切换都要重新渲染整个图片

**解决方案**：
- 使用 `SkiaTextRenderer` 的缓存机制
- 避免频繁触发渲染（使用防抖）
- 渲染放到后台线程（需要测试）

### 4. 内存管理

**问题**：SkiaSharp位图需要手动释放

**解决方案**：
- 确保每次渲染后调用 `Dispose()`
- 避免位图泄漏
- 监控内存使用情况

### 5. 调试困难

**问题**：SkiaSharp渲染结果是图片，无法用WPF工具检查元素

**解决方案**：
- 添加详细的调试日志
- 输出布局信息（每个经文的Y坐标和高度）
- 考虑添加可视化调试功能（绘制边框线）

---

## 📝 实施建议

### 实施顺序

1. **第一阶段：渲染功能**（核心）
   - [ ] 修改XAML布局（Image替代ItemsControl）
   - [ ] 实现 `RenderBibleToMainScreen()`
   - [ ] 统一 `RenderBibleVerses()` 方法
   - [ ] 测试主屏幕显示效果

2. **第二阶段：滚动同步**（核心）
   - [ ] 修改 `SyncBibleScroll()` 方法
   - [ ] 添加 `BibleScrollViewer_ScrollChanged` 事件
   - [ ] 测试滚动同步效果

3. **第三阶段：交互功能**（重要）
   - [ ] 实现 `BibleImage_Click()` 点击检测
   - [ ] 实现 `GetVerseAtPosition()` 位置计算
   - [ ] 实现 `CalculateTitleHeight()` 和 `CalculateVerseHeight()`
   - [ ] 测试点击高亮功能

4. **第四阶段：完善细节**（可选）
   - [ ] 优化性能（缓存、防抖）
   - [ ] 添加调试日志
   - [ ] 处理边界情况
   - [ ] 代码重构和清理

5. **第五阶段：测试验证**
   - [ ] 功能测试（显示、滚动、点击）
   - [ ] 性能测试（渲染速度、内存占用）
   - [ ] 兼容性测试（不同分辨率、DPI）
   - [ ] 回归测试（确保其他功能正常）

### 风险评估

| 风险 | 概率 | 影响 | 缓解措施 |
|------|------|------|----------|
| 点击位置计算错误 | 高 | 高 | 充分测试，添加调试工具 |
| 性能下降 | 中 | 中 | 使用缓存，后台渲染 |
| 内存泄漏 | 中 | 高 | 严格 Dispose，监控内存 |
| 样式不一致 | 低 | 中 | 复用渲染逻辑，对比测试 |
| 其他功能受影响 | 低 | 高 | 完整回归测试 |

### 回退方案

如果改造失败，可以快速回退：

1. 保留 `ItemsControl` 相关代码（注释掉，不删除）
2. 使用Git分支管理改造代码
3. 准备回退脚本，一键还原
4. 保留改造前的数据库备份

---

## ✅ 成功标准

### 功能标准

- [ ] 圣经经文能正常显示在主屏幕和投影屏幕
- [ ] 滚动同步完美对齐（误差 < 1像素）
- [ ] 点击经文能正确高亮
- [ ] 样式设置能正常应用
- [ ] 投影开启/关闭功能正常
- [ ] 切换章节、书卷功能正常

### 性能标准

- [ ] 渲染速度 < 100ms（1080p）
- [ ] 内存占用无明显增加
- [ ] 滚动流畅（60fps）
- [ ] 无内存泄漏

### 质量标准

- [ ] 代码符合项目规范
- [ ] 添加充分的注释和文档
- [ ] 通过代码审查
- [ ] 通过完整测试

---

## 📚 参考代码

### 参考实现

1. **歌词模式**：`UI/MainWindow.Lyrics.cs` (第760-834行)
   - SkiaSharp渲染到Image的完整实现
   - 滚动同步逻辑（直接复制位置）

2. **文本框控件**：`UI/Controls/DraggableTextBox.cs` (第163-434行)
   - Image控件显示SkiaSharp渲染结果
   - 渲染结果转换为WPF BitmapSource

3. **SkiaSharp渲染器**：`Core/SkiaTextRenderer.cs` (第119-420行)
   - `RenderBibleText()` 方法实现
   - 布局计算逻辑

4. **投影管理器**：`Managers/ProjectionManager.cs`
   - 歌词滚动同步：第423-468行
   - 圣经滚动同步（改造前）：第491-511行

---

## 🎯 预期效果

改造完成后，圣经投影将实现：

✅ **完美的滚动同步**：主屏和投影精确对齐，无任何偏移  
✅ **统一的渲染逻辑**：主屏和投影使用完全相同的代码  
✅ **更好的性能**：SkiaSharp GPU加速渲染  
✅ **更高的代码质量**：逻辑统一，易于维护  
✅ **稳定的架构**：避免WPF和SkiaSharp混用的问题  

---

## 📞 支持与反馈

如果在改造过程中遇到问题：

1. 检查调试日志，定位问题
2. 对比参考代码，确认实现正确
3. 运行测试用例，验证功能
4. 查看Git历史，对比改动

改造完成后请更新本文档，记录：
- 实际改造内容
- 遇到的问题和解决方案
- 性能测试结果
- 后续优化建议

