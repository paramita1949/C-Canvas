using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.EntityFrameworkCore;
using ImageColorChanger.Core;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Managers;
using ImageColorChanger.UI.Controls;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfColor = System.Windows.Media.Color;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using SkiaSharp;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow TextEditor Toolbar Events
    /// </summary>
    public partial class MainWindow
    {
        #region 工具栏事件处理

        /// <summary>
        /// 添加文本框按钮
        /// </summary>
        private async void BtnAddText_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTextProject == null)
            {
                WpfMessageBox.Show("请先创建或打开一个项目！", "提示", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_currentSlide == null)
            {
                WpfMessageBox.Show("请先选择一个幻灯片！", "提示", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // 🎯 固定位置创建新文本框,新文本在上层(ZIndex最大)
                const double newX = 100;
                const double newY = 100;
                const double newWidth = 600;  // 默认宽度
                const double newHeight = 200;  // 默认高度
                
                // 计算最大ZIndex,新文本始终在最上层
                int maxZIndex = 0;
                if (_textBoxes.Count > 0)
                {
                    maxZIndex = _textBoxes.Max(tb => tb.Data.ZIndex);
                }
                
                // ✅ 创建文本框时始终使用微软雅黑，不应用当前选择的字体
                // 字体应用必须框选文字才能应用
                string defaultFontFamily = "Microsoft YaHei UI";
                
                // 创建新元素 (关联到当前幻灯片)
                var newElement = new TextElement
                {
                    SlideId = _currentSlide.Id,  // 🆕 关联到幻灯片
                    X = newX,
                    Y = newY,
                    Width = newWidth,
                    Height = newHeight,
                    Content = "双击编辑",
                    FontSize = 60,  // 默认字号60
                    FontFamily = defaultFontFamily,  // ✅ 始终使用微软雅黑
                    FontColor = "#FFFFFF",  // 默认白色字体
                    ZIndex = maxZIndex + 1  // 新文本在最上层
                };

                // 保存到数据库
                await _textProjectManager.AddElementAsync(newElement);

                // 添加到画布
                var textBox = new DraggableTextBox(newElement);
                AddTextBoxToCanvas(textBox);
                
                // 🔧 新建文本框：自动进入编辑模式，全选占位符文本
                textBox.Focus();
                textBox.EnterEditModeForNew();
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 添加文本框失败: {ex.Message}");
                WpfMessageBox.Show($"添加文本框失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 复制文本框（立即创建新的副本）
        /// </summary>
        private async Task CopyTextBoxAsync(DraggableTextBox sourceTextBox)
        {
            if (sourceTextBox == null || _currentSlide == null)
                return;

            try
            {
                var sourceElement = sourceTextBox.Data;

                // 计算最大ZIndex,新文本框在最上层
                int maxZIndex = 0;
                if (_textBoxes.Count > 0)
                {
                    maxZIndex = _textBoxes.Max(tb => tb.Data.ZIndex);
                }

                // 使用 CloneElement 方法复制所有样式
                var newElement = _textProjectManager.CloneElement(sourceElement);

                // 修改位置和层级
                newElement.SlideId = _currentSlide.Id;
                newElement.X = sourceElement.X + 20;  // 向右偏移20像素
                newElement.Y = sourceElement.Y + 20;  // 向下偏移20像素
                newElement.ZIndex = maxZIndex + 1;

                // 保存到数据库
                await _textProjectManager.AddElementAsync(newElement);

                // 🆕 复制富文本片段（如果有）
                if (sourceElement.IsRichTextMode && sourceElement.RichTextSpans != null && sourceElement.RichTextSpans.Count > 0)
                {
                    var newSpans = new List<Database.Models.RichTextSpan>();
                    foreach (var sourceSpan in sourceElement.RichTextSpans.OrderBy(s => s.SpanOrder))
                    {
                        var newSpan = new Database.Models.RichTextSpan
                        {
                            TextElementId = newElement.Id,
                            SpanOrder = sourceSpan.SpanOrder,
                            Text = sourceSpan.Text,
                            FontFamily = sourceSpan.FontFamily,
                            FontSize = sourceSpan.FontSize,
                            FontColor = sourceSpan.FontColor,
                            IsBold = sourceSpan.IsBold,
                            IsItalic = sourceSpan.IsItalic,
                            IsUnderline = sourceSpan.IsUnderline,
                            BorderColor = sourceSpan.BorderColor,
                            BorderWidth = sourceSpan.BorderWidth,
                            BorderRadius = sourceSpan.BorderRadius,
                            BorderOpacity = sourceSpan.BorderOpacity,
                            BackgroundColor = sourceSpan.BackgroundColor,
                            BackgroundRadius = sourceSpan.BackgroundRadius,
                            BackgroundOpacity = sourceSpan.BackgroundOpacity,
                            ShadowColor = sourceSpan.ShadowColor,
                            ShadowOffsetX = sourceSpan.ShadowOffsetX,
                            ShadowOffsetY = sourceSpan.ShadowOffsetY,
                            ShadowBlur = sourceSpan.ShadowBlur,
                            ShadowOpacity = sourceSpan.ShadowOpacity
                        };
                        newSpans.Add(newSpan);
                    }

                    // 批量保存富文本片段
                    await _textProjectManager.SaveRichTextSpansAsync(newElement.Id, newSpans);

                    // 更新新元素的 RichTextSpans 集合
                    newElement.RichTextSpans = newSpans;
                }

                // 添加到画布
                var textBox = new DraggableTextBox(newElement);
                AddTextBoxToCanvas(textBox);

                // 选中新复制的文本框
                textBox.SetSelected(true);
                _selectedTextBox = textBox;

                // 显示浮动工具栏
                ShowTextBoxFloatingToolbar(textBox);

                // 标记已修改
                MarkContentAsModified();

                //System.Diagnostics.Debug.WriteLine($"✅ 复制文本框成功（包含 {newElement.RichTextSpans?.Count ?? 0} 个富文本片段）");
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 复制文本框失败: {ex.Message}");
                WpfMessageBox.Show($"复制文本框失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 删除指定的文本框（通用方法，支持按钮、右键菜单、快捷键调用，直接删除不弹窗确认）
        /// </summary>
        private async Task DeleteTextBoxAsync(DraggableTextBox textBox)
        {
            if (textBox == null)
                return;

            try
            {
                // 从数据库删除
                await _textProjectManager.DeleteElementAsync(textBox.Data.Id);

                // 从画布移除
                EditorCanvas.Children.Remove(textBox);
                _textBoxes.Remove(textBox);

                // 如果删除的是当前选中项，清除选中状态并隐藏工具栏
                if (_selectedTextBox == textBox)
                {
                    _selectedTextBox = null;
                    
                    // 隐藏圣经工具栏
                    if (BibleToolbar != null)
                    {
                        BibleToolbar.IsOpen = false;
//#if DEBUG
//                        //System.Diagnostics.Debug.WriteLine($"✅ [删除文本框] 圣经工具栏已隐藏");
//#endif
                    }
                }

                // 标记已修改
                MarkContentAsModified();

                //System.Diagnostics.Debug.WriteLine($"✅ 删除文本框成功");
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 删除文本框失败: {ex.Message}");
                WpfMessageBox.Show($"删除文本框失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 导入按钮点击（显示单图/多图菜单）
        /// </summary>
        private void BtnBackgroundImage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTextProject == null)
            {
                WpfMessageBox.Show("请先创建或选择一个文本项目", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 创建导入菜单
            var contextMenu = new ContextMenu();
            contextMenu.Style = (Style)this.FindResource("NoBorderContextMenuStyle");

            // 单图导入
            var singleImageItem = new MenuItem { Header = "单背景图" };
            singleImageItem.Click += (s, args) => ImportSingleImageAsSlide();
            contextMenu.Items.Add(singleImageItem);

            // 多图导入
            var multiImageItem = new MenuItem { Header = "多背景图" };
            multiImageItem.Click += async (s, args) => await ImportMultipleImagesAsSlidesAsync();
            contextMenu.Items.Add(multiImageItem);

            // 视频背景（一级菜单，点击直接导入）
            var videoBackgroundItem = new MenuItem { Header = "视频背景" };
            videoBackgroundItem.Click += async (s, args) => await ImportVideoAsSlideAsync();
            contextMenu.Items.Add(videoBackgroundItem);

            // 显示菜单
            contextMenu.PlacementTarget = BtnBackgroundImage;
            contextMenu.IsOpen = true;
        }

        /// <summary>
        /// 背景颜色按钮点击（直接选择颜色）
        /// </summary>
        private void BtnBackgroundColor_Click(object sender, RoutedEventArgs e)
        {
            BtnSelectBackgroundColor_Click(sender, e);
        }

        /// <summary>
        /// 原图拉伸模式切换按钮点击
        /// </summary>
        private async void BtnSplitStretchMode_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTextProject == null || _currentSlide == null)
                return;
            
            // 如果没有区域图片，不执行操作
            if (_regionImages.Count == 0)
                return;
            
            // 检查当前第一个区域图片的拉伸模式
            var firstImage = _regionImages.Values.FirstOrDefault();
            if (firstImage == null)
                return;
            
            // 根据当前状态切换
            bool isCurrentlyFill = firstImage.Stretch == System.Windows.Media.Stretch.Fill;
            var newStretch = isCurrentlyFill ? 
                System.Windows.Media.Stretch.Uniform :  // 当前是拉伸 → 切换为适中
                System.Windows.Media.Stretch.Fill;      // 当前是适中 → 切换为拉伸
            
            // 应用到所有区域图片（包括单画面模式的区域0）
            foreach (var kvp in _regionImages)
            {
                kvp.Value.Stretch = newStretch;
            }
            
            // 更新内部状态和按钮显示
            _splitStretchMode = (newStretch == System.Windows.Media.Stretch.Fill);
            UpdateStretchModeButton();
            
            // 保存到数据库
            await SaveSplitStretchModeAsync();
        }
        
        /// <summary>
        /// 更新拉伸模式按钮显示（根据当前图片的实际拉伸模式）
        /// </summary>
        private void UpdateStretchModeButton()
        {
            // 按钮显示当前的实际模式：
            // - 如果图片是拉伸模式(Fill)，显示"📐 拉伸"
            // - 如果图片是适中模式(Uniform)，显示"📐 适中"
            BtnSplitStretchMode.Content = _splitStretchMode ? "📐 拉伸" : "📐 适中";
        }
        
        /// <summary>
        /// 保存拉伸模式到数据库
        /// </summary>
        private async Task SaveSplitStretchModeAsync()
        {
            if (_currentSlide == null)
                return;
                
            try
            {
                var slideToUpdate = await _dbContext.Slides.FindAsync(_currentSlide.Id);
                if (slideToUpdate != null)
                {
                    slideToUpdate.SplitStretchMode = _splitStretchMode;
                    slideToUpdate.ModifiedTime = DateTime.Now;
                    await _dbContext.SaveChangesAsync();
                    
                    // 更新本地缓存
                    _currentSlide.SplitStretchMode = _splitStretchMode;
                    
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"💾 [SaveSplitStretchMode] 已保存拉伸模式: {_splitStretchMode}");
                    //#endif
                }
            }
            catch
            {
                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($"❌ [SaveSplitStretchMode] 失败");
                #endif
            }
        }
        
        /// <summary>
        /// 分割按钮点击（显示分割模式选择菜单）
        /// </summary>
        private void BtnSplitView_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTextProject == null || _currentSlide == null)
                return;

            var contextMenu = new ContextMenu();
            
            // 🔑 应用自定义样式
            contextMenu.Style = (Style)this.FindResource("NoBorderContextMenuStyle");

            // 获取当前分割模式（-1 表示未设置，不勾选任何项）
            int currentSplitMode = _currentSlide.SplitMode;

            // 单画面
            var singleItem = new MenuItem 
            { 
                Header = currentSplitMode == (int)Database.Models.Enums.ViewSplitMode.Single 
                    ? "✓ 单画面" : "   单画面",
                Height = 36
            };
            singleItem.Click += (s, args) => SetSplitMode(Database.Models.Enums.ViewSplitMode.Single);
            contextMenu.Items.Add(singleItem);

            // 左右分割
            var horizontalItem = new MenuItem 
            { 
                Header = currentSplitMode == (int)Database.Models.Enums.ViewSplitMode.Horizontal 
                    ? "✓ 左右分割" : "   左右分割",
                Height = 36
            };
            horizontalItem.Click += (s, args) => SetSplitMode(Database.Models.Enums.ViewSplitMode.Horizontal);
            contextMenu.Items.Add(horizontalItem);

            // 上下分割
            var verticalItem = new MenuItem 
            { 
                Header = currentSplitMode == (int)Database.Models.Enums.ViewSplitMode.Vertical 
                    ? "✓ 上下分割" : "   上下分割",
                Height = 36
            };
            verticalItem.Click += (s, args) => SetSplitMode(Database.Models.Enums.ViewSplitMode.Vertical);
            contextMenu.Items.Add(verticalItem);

            // 三分割
            var tripleSplitItem = new MenuItem 
            { 
                Header = currentSplitMode == (int)Database.Models.Enums.ViewSplitMode.TripleSplit 
                    ? "✓ 三分割" : "   三分割",
                Height = 36
            };
            tripleSplitItem.Click += (s, args) => SetSplitMode(Database.Models.Enums.ViewSplitMode.TripleSplit);
            contextMenu.Items.Add(tripleSplitItem);

            // 四宫格
            var quadItem = new MenuItem 
            { 
                Header = currentSplitMode == (int)Database.Models.Enums.ViewSplitMode.Quad 
                    ? "✓ 四宫格" : "   四宫格",
                Height = 36
            };
            quadItem.Click += (s, args) => SetSplitMode(Database.Models.Enums.ViewSplitMode.Quad);
            contextMenu.Items.Add(quadItem);

            contextMenu.PlacementTarget = sender as UIElement;
            contextMenu.IsOpen = true;
        }

        /// <summary>
        /// 设置分割模式
        /// </summary>
        private async void SetSplitMode(Database.Models.Enums.ViewSplitMode mode)
        {
            if (_currentSlide == null)
                return;

            try
            {
                // 更新数据库
                var slideToUpdate = await _dbContext.Slides.FindAsync(_currentSlide.Id);
                if (slideToUpdate != null)
                {
                    slideToUpdate.SplitMode = (int)mode;
                    slideToUpdate.ModifiedTime = DateTime.Now;
                    
                    // 切换分割模式时，清空分割区域数据
                    slideToUpdate.SplitRegionsData = null;
                    
                    await _dbContext.SaveChangesAsync();

                    // 更新本地缓存
                    _currentSlide.SplitMode = (int)mode;
                    _currentSlide.SplitRegionsData = slideToUpdate.SplitRegionsData;
                }

                // 更新预览画布显示分割布局
                UpdateSplitLayout(mode);

                MarkContentAsModified();
            }
            catch (Exception ex)
            {
//#if DEBUG
//                //System.Diagnostics.Debug.WriteLine($"❌ [SetSplitMode] 失败: {ex.Message}");
//#else
                _ = ex; // 避免未使用警告
//#endif
            }
        }

        /// <summary>
        /// 更新分割布局显示
        /// </summary>
        private void UpdateSplitLayout(Database.Models.Enums.ViewSplitMode mode)
        {
            // 清除旧的分割线和边框
            ClearSplitLines();
            ClearRegionBorders();
            
            // 如果模式值 < 0，表示未设置分割模式，不创建任何区域
            if ((int)mode < 0)
            {
                return;
            }
            
            double canvasWidth = EditorCanvas.ActualWidth > 0 ? EditorCanvas.ActualWidth : 1600;
            double canvasHeight = EditorCanvas.ActualHeight > 0 ? EditorCanvas.ActualHeight : 900;
            
            switch (mode)
            {
                case Database.Models.Enums.ViewSplitMode.Single:
                    // 🆕 单画面模式：创建一个占满整个画布的区域（不显示边框和标签）
                    CreateRegionBorder(0, 0, 0, canvasWidth, canvasHeight);
                    break;
                    
                case Database.Models.Enums.ViewSplitMode.Horizontal:
                    // 左右分割：绘制一条竖线
                    DrawVerticalLine(canvasWidth / 2, 0, canvasHeight);
                    // 创建两个区域边框
                    CreateRegionBorder(0, 0, 0, canvasWidth / 2, canvasHeight);
                    CreateRegionBorder(1, canvasWidth / 2, 0, canvasWidth / 2, canvasHeight);
                    break;
                    
                case Database.Models.Enums.ViewSplitMode.Vertical:
                    // 上下分割：绘制一条横线
                    DrawHorizontalLine(canvasHeight / 2, 0, canvasWidth);
                    // 创建两个区域边框
                    CreateRegionBorder(0, 0, 0, canvasWidth, canvasHeight / 2);
                    CreateRegionBorder(1, 0, canvasHeight / 2, canvasWidth, canvasHeight / 2);
                    break;
                    
                case Database.Models.Enums.ViewSplitMode.Quad:
                    // 四宫格：绘制十字线
                    DrawVerticalLine(canvasWidth / 2, 0, canvasHeight);
                    DrawHorizontalLine(canvasHeight / 2, 0, canvasWidth);
                    // 创建四个区域边框
                    CreateRegionBorder(0, 0, 0, canvasWidth / 2, canvasHeight / 2);
                    CreateRegionBorder(1, canvasWidth / 2, 0, canvasWidth / 2, canvasHeight / 2);
                    CreateRegionBorder(2, 0, canvasHeight / 2, canvasWidth / 2, canvasHeight / 2);
                    CreateRegionBorder(3, canvasWidth / 2, canvasHeight / 2, canvasWidth / 2, canvasHeight / 2);
                    break;
                    
                case Database.Models.Enums.ViewSplitMode.TripleSplit:
                    // 三分割：左边上下分割，右边整个竖分割
                    // 绘制一条竖线（左右分割 50%）
                    DrawVerticalLine(canvasWidth / 2, 0, canvasHeight);
                    // 绘制一条横线（左边上下分割）
                    DrawHorizontalLine(canvasHeight / 2, 0, canvasWidth / 2);
                    // 创建三个区域边框
                    CreateRegionBorder(0, 0, 0, canvasWidth / 2, canvasHeight / 2);  // 左上1
                    CreateRegionBorder(1, 0, canvasHeight / 2, canvasWidth / 2, canvasHeight / 2);  // 左下2
                    CreateRegionBorder(2, canvasWidth / 2, 0, canvasWidth / 2, canvasHeight);  // 右3
                    break;
            }
            
            // 默认选中第一个区域
            SelectRegion(0);
        }
        
        /// <summary>
        /// 清除分割线
        /// </summary>
        private void ClearSplitLines()
        {
            // 移除所有带有 "SplitLine" 标记的元素
            var linesToRemove = EditorCanvas.Children.OfType<Line>()
                .Where(l => l.Tag != null && l.Tag.ToString() == "SplitLine")
                .ToList();
                
            foreach (var line in linesToRemove)
            {
                EditorCanvas.Children.Remove(line);
            }
        }
        
        /// <summary>
        /// 绘制竖线
        /// </summary>
        private void DrawVerticalLine(double x, double y1, double y2)
        {
            var line = new Line
            {
                X1 = x,
                Y1 = y1,
                X2 = x,
                Y2 = y2,
                Stroke = new SolidColorBrush(WpfColor.FromRgb(SPLIT_LINE_COLOR_R, SPLIT_LINE_COLOR_G, SPLIT_LINE_COLOR_B)), // 🔧 使用统一常量
                StrokeThickness = SPLIT_LINE_THICKNESS_MAIN,
                StrokeDashArray = new DoubleCollection { SPLIT_LINE_DASH_LENGTH, SPLIT_LINE_DASH_GAP }, // 🔧 使用统一常量
                Tag = "SplitLine",
                IsHitTestVisible = false // 不响应鼠标事件
            };
            
            Canvas.SetZIndex(line, 1000); // 置于顶层
            EditorCanvas.Children.Add(line);
        }
        
        /// <summary>
        /// 绘制横线
        /// </summary>
        private void DrawHorizontalLine(double y, double x1, double x2)
        {
            var line = new Line
            {
                X1 = x1,
                Y1 = y,
                X2 = x2,
                Y2 = y,
                Stroke = new SolidColorBrush(WpfColor.FromRgb(SPLIT_LINE_COLOR_R, SPLIT_LINE_COLOR_G, SPLIT_LINE_COLOR_B)), // 🔧 使用统一常量
                StrokeThickness = SPLIT_LINE_THICKNESS_MAIN,
                StrokeDashArray = new DoubleCollection { SPLIT_LINE_DASH_LENGTH, SPLIT_LINE_DASH_GAP }, // 🔧 使用统一常量
                Tag = "SplitLine",
                IsHitTestVisible = false // 不响应鼠标事件
            };
            
            Canvas.SetZIndex(line, 1000); // 置于顶层
            EditorCanvas.Children.Add(line);
        }
        
        /// <summary>
        /// 创建区域边框
        /// </summary>
        private void CreateRegionBorder(int regionIndex, double x, double y, double width, double height)
        {
            // 🆕 判断是否是单画面模式
            bool isSingleMode = _currentSlide != null && _currentSlide.SplitMode == 0;
            
            var border = new WpfRectangle
            {
                Width = width,
                Height = height,
                Stroke = isSingleMode ? System.Windows.Media.Brushes.Transparent : new SolidColorBrush(WpfColor.FromRgb(128, 128, 128)), // 单画面模式透明
                StrokeThickness = isSingleMode ? 0 : 2, // 单画面模式无边框
                Fill = System.Windows.Media.Brushes.Transparent,
                Tag = $"RegionBorder_{regionIndex}",
                Cursor = System.Windows.Input.Cursors.Hand
            };
            
            Canvas.SetLeft(border, x);
            Canvas.SetTop(border, y);
            Canvas.SetZIndex(border, 999); // 低于分割线
            
            // 添加点击事件
            border.MouseLeftButtonDown += (s, e) =>
            {
                SelectRegion(regionIndex);
                e.Handled = true;
            };
            
            // 添加右键菜单事件
            border.MouseRightButtonDown += (s, e) =>
            {
                SelectRegion(regionIndex);
                ShowRegionContextMenu(s as UIElement);
                e.Handled = true;
            };
            
            _splitRegionBorders.Add(border);
            EditorCanvas.Children.Add(border);
            
            // 🆕 只在非单画面模式下显示序列号标签
            if (!isSingleMode)
            {
                var label = new System.Windows.Controls.Border
                {
                    Background = new SolidColorBrush(WpfColor.FromArgb(200, 255, 102, 0)), // 半透明橙色
                    CornerRadius = new System.Windows.CornerRadius(0, 0, REGION_LABEL_CORNER_RADIUS, 0), // 右下圆角
                    Padding = new System.Windows.Thickness(REGION_LABEL_PADDING_X, REGION_LABEL_PADDING_Y, REGION_LABEL_PADDING_X, REGION_LABEL_PADDING_Y),
                    Tag = $"RegionLabel_{regionIndex}",
                    IsHitTestVisible = false // 不响应鼠标事件
                };
                
                var labelText = new System.Windows.Controls.TextBlock
                {
                    Text = (regionIndex + 1).ToString(),
                    FontSize = REGION_LABEL_FONT_SIZE,
                    FontWeight = System.Windows.FontWeights.Bold,
                    Foreground = System.Windows.Media.Brushes.White
                };
                
                label.Child = labelText;
                
                // 定位到左上角
                Canvas.SetLeft(label, x); // 左上角
                Canvas.SetTop(label, y);
                Canvas.SetZIndex(label, 1001); // 置于最顶层
                
                EditorCanvas.Children.Add(label);
            }
        }
        
        /// <summary>
        /// 清除区域边框
        /// </summary>
        private void ClearRegionBorders()
        {
            // 清除边框
            foreach (var border in _splitRegionBorders)
            {
                EditorCanvas.Children.Remove(border);
            }
            _splitRegionBorders.Clear();
            
            // 🆕 清除序列号标签
            var labelsToRemove = EditorCanvas.Children.OfType<System.Windows.Controls.Border>()
                .Where(b => b.Tag != null && b.Tag.ToString().StartsWith("RegionLabel_"))
                .ToList();
            foreach (var label in labelsToRemove)
            {
                EditorCanvas.Children.Remove(label);
            }
            
            // 清除区域图片
            foreach (var image in _regionImages.Values)
            {
                EditorCanvas.Children.Remove(image);
            }
            _regionImages.Clear();
            _regionImagePaths.Clear();
            _regionImageColorEffects.Clear();
        }
        
        /// <summary>
        /// 选择区域
        /// </summary>
        private void SelectRegion(int regionIndex)
        {
            if (regionIndex < 0 || regionIndex >= _splitRegionBorders.Count)
                return;
                
            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"🎯 [SelectRegion] 选中区域: {regionIndex}");
            //#endif
            
            _selectedRegionIndex = regionIndex;
            
            // 🔑 设置画布焦点，使其能接收键盘事件
            EditorCanvas.Focus();
            
            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"🔑 [SelectRegion] 已设置画布焦点，IsFocused: {EditorCanvas.IsFocused}");
            //#endif
            
            // 更新所有边框的样式
            for (int i = 0; i < _splitRegionBorders.Count; i++)
            {
                var border = _splitRegionBorders[i];
                if (i == regionIndex)
                {
                    // 选中状态：绿色边框
                    border.Stroke = new SolidColorBrush(WpfColor.FromRgb(0, 255, 0));
                    border.StrokeThickness = 2;
                }
                else
                {
                    // 未选中状态：灰色细边框
                    border.Stroke = new SolidColorBrush(WpfColor.FromRgb(128, 128, 128));
                    border.StrokeThickness = 2;
                }
            }
        }
        
        /// <summary>
        /// 加载图片到选中的分割区域
        /// </summary>
        public async Task LoadImageToSplitRegion(string imagePath)
        {
            if (_currentSlide == null || _splitRegionBorders.Count == 0)
                return;
                
            try
            {
                // 🆕 检查图片是否来自原图标记或变色标记的文件夹
                (bool shouldUseStretch, bool shouldApplyColorEffect) = await Task.Run(() =>
                {
                    try
                    {
                        var mediaFile = _dbContext.MediaFiles.FirstOrDefault(m => m.Path == imagePath);
                        
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"🔍 [LoadImageToSplitRegion] 检查图片: {System.IO.Path.GetFileName(imagePath)}");
                        //System.Diagnostics.Debug.WriteLine($"   MediaFile找到: {mediaFile != null}");
                        //if (mediaFile != null)
                        //{
                        //    //System.Diagnostics.Debug.WriteLine($"   FolderId: {mediaFile.FolderId}");
                        //}
                        #endif
                        
                        if (mediaFile?.FolderId != null)
                        {
                            // 检查文件夹是否有原图标记
                            bool isOriginalFolder = _originalManager.CheckOriginalMark(
                                Database.Models.Enums.ItemType.Folder,
                                mediaFile.FolderId.Value
                            );

                            // 🎨 检查文件夹是否有变色标记
                            bool hasColorEffectMark = _dbManager.HasFolderAutoColorEffect(mediaFile.FolderId.Value);

                            #if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"   原图标记: {isOriginalFolder}");
                            //System.Diagnostics.Debug.WriteLine($"   变色标记: {hasColorEffectMark}");
                            //if (isOriginalFolder)
                            //{
                            //    //System.Diagnostics.Debug.WriteLine($"🎯 [LoadImageToSplitRegion] 检测到原图标记文件夹，自动使用拉伸模式");
                            //}
                            //if (hasColorEffectMark)
                            //{
                            //    //System.Diagnostics.Debug.WriteLine($"🎨 [LoadImageToSplitRegion] 检测到变色标记文件夹，自动应用变色效果");
                            //}
                            #endif

                            return (isOriginalFolder, hasColorEffectMark);
                        }
                        
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"   未找到MediaFile或FolderId为空");
                        #endif
                        
                        return (false, false);
                    }
                    catch
                    {
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"❌ [LoadImageToSplitRegion] 检查标记失败");
                        #endif
                        return (false, false);
                    }
                });
                
                // 获取区域边框信息
                var border = _splitRegionBorders[_selectedRegionIndex];
                double x = Canvas.GetLeft(border);
                double y = Canvas.GetTop(border);
                double width = border.Width;
                double height = border.Height;
                
                // 如果该区域已经有图片，先移除
                if (_regionImages.ContainsKey(_selectedRegionIndex))
                {
                    EditorCanvas.Children.Remove(_regionImages[_selectedRegionIndex]);
                    _regionImages.Remove(_selectedRegionIndex);
                }
                
                // 🚀 使用优化的图片加载（GPU加速 + 缓存）
                var bitmapSource = await Task.Run<BitmapSource>(() =>
                {
                    // 🎨 如果需要应用变色效果，使用 SkiaSharp 加载并处理
                    if (shouldApplyColorEffect)
                    {
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"🎨 [LoadImageToSplitRegion] 开始应用变色效果...");
                        #endif
                        
                        try
                        {
                            using var skBitmap = SkiaSharp.SKBitmap.Decode(imagePath);
                            if (skBitmap != null)
                            {
                                #if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"   SKBitmap加载成功，尺寸: {skBitmap.Width}x{skBitmap.Height}");
                                #endif
                                
                                // 应用变色效果
                                _imageProcessor.ApplyYellowTextEffect(skBitmap);

                                #if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"   变色效果已应用");
                                #endif
                                
                                // 转换为 WPF BitmapSource
                                var result = _imageProcessor.ConvertToBitmapSource(skBitmap);
                                
                                #if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"✅ [LoadImageToSplitRegion] 变色效果应用成功");
                                #endif
                                
                                return result;
                            }
                            else
                            {
                                #if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"❌ [LoadImageToSplitRegion] SKBitmap加载失败");
                                #endif
                            }
                        }
                        catch
                        {
                            #if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"❌ [LoadImageToSplitRegion] 应用变色效果失败");
                            #endif
                        }
                    }
                    else
                    {
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"📷 [LoadImageToSplitRegion] 正常加载（无变色效果）");
                        #endif
                    }

                    // 正常加载（无变色效果）
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(imagePath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad; // 立即加载到内存
                    bitmap.EndInit();
                    bitmap.Freeze(); // 🔥 冻结到GPU显存，跨线程共享
                    return bitmap;
                });
                
                // 决定使用的拉伸模式
                System.Windows.Media.Stretch stretchMode;
                if (shouldUseStretch)
                {
                    // 原图标记文件夹：拉伸填满
                    stretchMode = System.Windows.Media.Stretch.Fill;
                }
                else if (_currentSlide.SplitMode == 0 || _currentSlide.SplitMode == 4)
                {
                    // 单画面模式或三分割模式：默认拉伸填满
                    stretchMode = System.Windows.Media.Stretch.Fill;
                }
                else
                {
                    // 其他分割模式：根据用户设置
                    stretchMode = _splitStretchMode ? 
                        System.Windows.Media.Stretch.Fill : 
                        System.Windows.Media.Stretch.Uniform;
                }
                
                // 创建 Image 控件，应用拉伸模式
                var imageControl = new System.Windows.Controls.Image
                {
                    Source = bitmapSource,
                    Width = width,
                    Height = height,
                    Stretch = stretchMode,
                    Tag = $"RegionImage_{_selectedRegionIndex}",
                    CacheMode = new BitmapCache // 🔥 启用GPU缓存，减少重复渲染
                    {
                        RenderAtScale = CalculateOptimalRenderScale()  // 🔥 动态计算渲染质量：自适应1080p/2K/4K投影屏
                    }
                };
                
                Canvas.SetLeft(imageControl, x);
                Canvas.SetTop(imageControl, y);
                Canvas.SetZIndex(imageControl, 998); // 低于边框
                
                // 添加到画布
                EditorCanvas.Children.Add(imageControl);
                
                // 保存引用
                _regionImages[_selectedRegionIndex] = imageControl;
                _regionImagePaths[_selectedRegionIndex] = imagePath;
                _regionImageColorEffects[_selectedRegionIndex] = shouldApplyColorEffect; // 记录是否需要变色效果
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"💾 [LoadImageToSplitRegion] 保存变色状态: 区域{_selectedRegionIndex}, 需要变色={shouldApplyColorEffect}");
                //System.Diagnostics.Debug.WriteLine($"   当前所有区域变色状态: {string.Join(", ", _regionImageColorEffects.Select(kv => $"区域{kv.Key}={kv.Value}"))}");
                //#endif
                
                // 更新边框样式（有图片的区域显示黄色）
                border.Stroke = new SolidColorBrush(WpfColor.FromRgb(255, 215, 0)); // 金色
                
                // 🆕 同步更新拉伸按钮显示
                _splitStretchMode = (stretchMode == System.Windows.Media.Stretch.Fill);
                UpdateStretchModeButton();
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"✅ [LoadImageToSplitRegion] 图片已加载到区域 {_selectedRegionIndex}");
                //#endif
                
                // 保存分割配置到数据库
                await SaveSplitConfigAsync();
                
                MarkContentAsModified();
                
                // 🆕 自动切换到下一个区域（无论是否已有图片）
                AutoSelectNextRegion();
            }
            catch
            {
                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($"❌ [LoadImageToSplitRegion] 失败");
                #endif
            }
        }
        
        /// <summary>
        /// 自动切换到下一个区域（无论是否已有图片，都循环切换）
        /// </summary>
        private void AutoSelectNextRegion()
        {
            if (_splitRegionBorders.Count == 0)
                return;

            // 计算下一个区域的索引（直接循环到下一个，不检查是否有图片）
            int nextIndex = (_selectedRegionIndex + 1) % _splitRegionBorders.Count;

            #if DEBUG
            //System.Diagnostics.Debug.WriteLine($"🔄 [AutoSelectNextRegion] 自动切换到区域 {nextIndex}");
            #endif

            SelectRegion(nextIndex);
        }
        
        /// <summary>
        /// 检查是否处于分割模式（包括单画面模式）
        /// </summary>
        public bool IsInSplitMode()
        {
            return _currentSlide != null && 
                   _currentSlide.SplitMode >= 0 && 
                   _splitRegionBorders.Count > 0;
        }
        
        /// <summary>
        /// 清空选中区域的图片
        /// </summary>
        public async Task ClearSelectedRegionImage()
        {
            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"🗑️ [ClearSelectedRegionImage] 开始清空区域图片");
            //System.Diagnostics.Debug.WriteLine($"   _selectedRegionIndex: {_selectedRegionIndex}");
            //System.Diagnostics.Debug.WriteLine($"   包含图片: {_regionImages.ContainsKey(_selectedRegionIndex)}");
            //#endif
            
            if (_selectedRegionIndex < 0 || !_regionImages.ContainsKey(_selectedRegionIndex))
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"⚠️ [ClearSelectedRegionImage] 条件不满足，退出");
                //#endif
                return;
            }
                
            try
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"🗑️ [ClearSelectedRegionImage] 开始移除图片控件");
                //#endif
                
                // 移除图片控件
                var imageControl = _regionImages[_selectedRegionIndex];
                EditorCanvas.Children.Remove(imageControl);
                _regionImages.Remove(_selectedRegionIndex);
                _regionImagePaths.Remove(_selectedRegionIndex);
                _regionImageColorEffects.Remove(_selectedRegionIndex); // 同时清除变色效果记录
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"✅ [ClearSelectedRegionImage] 图片控件已移除");
                //#endif
                
                // 保持边框选中状态（绿色），不改变分割状态
                // 边框和分割线保持不变，只是清空了图片内容
                
                // 保存到数据库
                await SaveSplitConfigAsync();
                MarkContentAsModified();
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"✅ [ClearSelectedRegionImage] 已保存到数据库");
                //#endif
                
                ShowStatus($"✅ 已清空区域 {_selectedRegionIndex + 1} 的图片");
            }
            catch (Exception ex)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"❌ [ClearSelectedRegionImage] 失败: {ex.Message}");
                //#endif
                
                WpfMessageBox.Show($"清空区域图片失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 清空所有区域的图片
        /// </summary>
        public async Task ClearAllRegionImages()
        {
            if (_regionImages.Count == 0)
            {
                WpfMessageBox.Show("当前没有加载任何图片", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
                
            var result = WpfMessageBox.Show("确定要清空所有区域的图片吗？", "确认",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
                
            if (result != MessageBoxResult.Yes)
                return;
                
            try
            {
                // 移除所有图片控件
                foreach (var kvp in _regionImages.ToList())
                {
                    EditorCanvas.Children.Remove(kvp.Value);
                }
                _regionImages.Clear();
                _regionImagePaths.Clear();
                _regionImageColorEffects.Clear();
                
                // 恢复所有边框样式为灰色
                foreach (var border in _splitRegionBorders)
                {
                    border.Stroke = new SolidColorBrush(WpfColor.FromRgb(128, 128, 128));
                }
                
                // 保存到数据库
                await SaveSplitConfigAsync();
                MarkContentAsModified();
                
                ShowStatus($"✅ 已清空所有区域的图片");
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"清空所有图片失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 显示区域右键菜单
        /// </summary>
        private void ShowRegionContextMenu(UIElement target)
        {
            var contextMenu = new ContextMenu();
            
            // 应用自定义样式
            contextMenu.Style = (Style)this.FindResource("NoBorderContextMenuStyle");
            
            // 选项1：清空当前区域
            var clearCurrentItem = new MenuItem
            {
                Header = "🗑 清空当前区域",
                Height = 36
            };
            clearCurrentItem.Click += async (s, e) => await ClearSelectedRegionImage();
            contextMenu.Items.Add(clearCurrentItem);
            
            // 选项2：清空所有区域
            var clearAllItem = new MenuItem
            {
                Header = "🗑 清空所有区域",
                Height = 36
            };
            clearAllItem.Click += async (s, e) => await ClearAllRegionImages();
            contextMenu.Items.Add(clearAllItem);
            
            contextMenu.PlacementTarget = target;
            contextMenu.IsOpen = true;
        }
        
        /// <summary>
        /// 保存分割配置到数据库
        /// </summary>
        private async Task SaveSplitConfigAsync()
        {
            if (_currentSlide == null)
                return;
                
            try
            {
                // 将区域图片路径序列化为 JSON
                var regionDataList = _regionImagePaths
                    .Select(kvp => new Database.Models.DTOs.SplitRegionData
                    {
                        RegionIndex = kvp.Key,
                        ImagePath = kvp.Value
                    })
                    .ToList();
                
                string json = JsonSerializer.Serialize(regionDataList);
                
                // 更新数据库
                var slideToUpdate = await _dbContext.Slides.FindAsync(_currentSlide.Id);
                if (slideToUpdate != null)
                {
                    slideToUpdate.SplitRegionsData = json;
                    slideToUpdate.ModifiedTime = DateTime.Now;
                    await _dbContext.SaveChangesAsync();
                    
                    // 更新本地缓存
                    _currentSlide.SplitRegionsData = json;
                    
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"💾 [SaveSplitConfig] 已保存 {regionDataList.Count} 个区域配置");
                    //#endif
                }
            }
            catch
            {
                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($"❌ [SaveSplitConfig] 失败");
                #endif
            }
        }
        
        /// <summary>
        /// 投影前调整分割线和边框样式（细线）
        /// </summary>
        private void HideSplitLinesForProjection()
        {
            try
            {
                // 将所有分割线改为细线（投影样式：细实线）
                foreach (var child in EditorCanvas.Children.OfType<Line>())
                {
                    if (child.Tag != null && child.Tag.ToString() == "SplitLine")
                    {
                        // 保存原始样式到Tag
                        child.Tag = new { 
                            Type = "SplitLine", 
                            OriginalThickness = child.StrokeThickness,
                            OriginalDashArray = child.StrokeDashArray
                        };
                        
                        // 🔧 改为投影样式：细实线（使用统一常量）
                        child.StrokeThickness = SPLIT_LINE_THICKNESS_PROJECTION;
                        child.StrokeDashArray = null; // 实线
                    }
                }
                
                // 隐藏所有区域边框
                foreach (var border in _splitRegionBorders)
                {
                    border.Visibility = Visibility.Collapsed;
                }
                
                // 🔥 隐藏未加载图片的区域的序号标签
                var labels = EditorCanvas.Children.OfType<System.Windows.Controls.Border>()
                    .Where(b => b.Tag != null && b.Tag.ToString().StartsWith("RegionLabel_"))
                    .ToList();
                
                foreach (var label in labels)
                {
                    // 从Tag中提取区域索引
                    var tagStr = label.Tag.ToString();
                    if (int.TryParse(tagStr.Replace("RegionLabel_", ""), out int regionIndex))
                    {
                        // 检查该区域是否已加载图片
                        if (!_regionImages.ContainsKey(regionIndex))
                        {
                            // 未加载图片，隐藏标签
                            label.Visibility = Visibility.Collapsed;
                        }
                    }
                }
                
                //System.Diagnostics.Debug.WriteLine($"🎨 [投影] 已调整分割线为细线，隐藏边框和空白区域标签");
            }
            catch
            {
                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($"❌ [HideSplitLinesForProjection] 失败");
                #endif
            }
        }
        
        /// <summary>
        /// 投影后恢复分割线和边框显示
        /// </summary>
        private void RestoreSplitLinesAfterProjection()
        {
            try
            {
                // 恢复所有分割线的原始样式
                foreach (var child in EditorCanvas.Children.OfType<Line>())
                {
                    if (child.Tag != null)
                    {
                        var tagType = child.Tag.GetType();
                        if (tagType.GetProperty("Type") != null)
                        {
                            dynamic tag = child.Tag;
                            if (tag.Type == "SplitLine")
                            {
                                // 恢复原始粗细和虚线样式
                                child.StrokeThickness = tag.OriginalThickness;
                                child.StrokeDashArray = tag.OriginalDashArray;
                                
                                // 恢复简单的Tag
                                child.Tag = "SplitLine";
                            }
                        }
                    }
                }
                
                // 恢复所有区域边框
                foreach (var border in _splitRegionBorders)
                {
                    border.Visibility = Visibility.Visible;
                }
                
                // 🔥 恢复所有区域序号标签（包括未加载图片的）
                var labels = EditorCanvas.Children.OfType<System.Windows.Controls.Border>()
                    .Where(b => b.Tag != null && b.Tag.ToString().StartsWith("RegionLabel_"))
                    .ToList();
                
                foreach (var label in labels)
                {
                    label.Visibility = Visibility.Visible;
                }
                
                //System.Diagnostics.Debug.WriteLine($"🎨 [投影] 已恢复分割线、边框和标签");
            }
            catch
            {
                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($"❌ [RestoreSplitLinesAfterProjection] 失败");
                #endif
            }
        }
        
        /// <summary>
        /// 恢复分割配置
        /// </summary>
        private void RestoreSplitConfig(Slide slide)
        {
            try
            {
                // 🆕 恢复拉伸模式
                _splitStretchMode = slide.SplitStretchMode;
                UpdateStretchModeButton();
                
                // 检查是否有分割模式（-1 表示无分割模式）
                if (slide.SplitMode < 0)
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"📋 [RestoreSplitConfig] 无分割模式，清空分割区域");
                    //#endif
                    // 清空所有分割元素
                    ClearSplitLines();
                    ClearRegionBorders();
                    return;
                }
                
                // 先更新分割布局（包括单画面模式）
                var splitMode = (Database.Models.Enums.ViewSplitMode)slide.SplitMode;
                UpdateSplitLayout(splitMode);
                
                // 检查是否有区域数据
                if (string.IsNullOrEmpty(slide.SplitRegionsData))
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"📋 [RestoreSplitConfig] 分割模式={splitMode}，但无区域数据");
                    //#endif
                    return;
                }
                
                // 反序列化区域数据
                var regionDataList = JsonSerializer.Deserialize<List<Database.Models.DTOs.SplitRegionData>>(slide.SplitRegionsData);
                if (regionDataList == null || regionDataList.Count == 0)
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"📋 [RestoreSplitConfig] 反序列化失败或数据为空");
                    //#endif
                    return;
                }
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"📋 [RestoreSplitConfig] 开始恢复 {regionDataList.Count} 个区域");
                //#endif
                
                // 清空现有数据
                _regionImagePaths.Clear();
                _regionImageColorEffects.Clear();
                foreach (var image in _regionImages.Values)
                {
                    EditorCanvas.Children.Remove(image);
                }
                _regionImages.Clear();
                
                // 恢复每个区域的图片
                foreach (var regionData in regionDataList)
                {
                    if (string.IsNullOrEmpty(regionData.ImagePath) || !System.IO.File.Exists(regionData.ImagePath))
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"⚠️ [RestoreSplitConfig] 区域 {regionData.RegionIndex} 图片不存在: {regionData.ImagePath}");
                        //#endif
                        continue;
                    }
                    
                    if (regionData.RegionIndex >= _splitRegionBorders.Count)
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"⚠️ [RestoreSplitConfig] 区域索引超出范围: {regionData.RegionIndex}");
                        //#endif
                        continue;
                    }
                    
                    // 🆕 检查图片是否来自原图标记或变色标记的文件夹
                    bool shouldUseStretch = false;
                    bool shouldApplyColorEffect = false;
                    try
                    {
                        var mediaFile = _dbContext.MediaFiles.FirstOrDefault(m => m.Path == regionData.ImagePath);
                        
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"🔍 [RestoreSplitConfig] 区域 {regionData.RegionIndex} 检查图片: {System.IO.Path.GetFileName(regionData.ImagePath)}");
                        //System.Diagnostics.Debug.WriteLine($"   MediaFile找到: {mediaFile != null}");
                        //if (mediaFile != null)
                        //{
                        //    //System.Diagnostics.Debug.WriteLine($"   FolderId: {mediaFile.FolderId}");
                        //}
                        #endif
                        
                        if (mediaFile?.FolderId != null)
                        {
                            shouldUseStretch = _originalManager.CheckOriginalMark(
                                Database.Models.Enums.ItemType.Folder,
                                mediaFile.FolderId.Value
                            );

                            // 🎨 检查文件夹是否有变色标记
                            shouldApplyColorEffect = _dbManager.HasFolderAutoColorEffect(mediaFile.FolderId.Value);

                            #if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"   原图标记: {shouldUseStretch}");
                            //System.Diagnostics.Debug.WriteLine($"   变色标记: {shouldApplyColorEffect}");
                            //if (shouldUseStretch)
                            //{
                            //    //System.Diagnostics.Debug.WriteLine($"🎯 [RestoreSplitConfig] 区域 {regionData.RegionIndex} 来自原图标记文件夹，使用拉伸模式");
                            //}
                            //if (shouldApplyColorEffect)
                            //{
                            //    //System.Diagnostics.Debug.WriteLine($"🎨 [RestoreSplitConfig] 区域 {regionData.RegionIndex} 来自变色标记文件夹，应用变色效果");
                            //}
                            #endif
                        }
                        else
                        {
                            #if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"   未找到MediaFile或FolderId为空");
                            #endif
                        }
                    }
                    catch
                    {
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"❌ [RestoreSplitConfig] 检查标记失败");
                        #endif
                    }
                    
                    // 获取区域边框信息
                    var border = _splitRegionBorders[regionData.RegionIndex];
                    double x = Canvas.GetLeft(border);
                    double y = Canvas.GetTop(border);
                    double width = border.Width;
                    double height = border.Height;
                    
                    // 🚀 使用优化的图片加载（GPU加速 + 缓存）
                    BitmapSource bitmap;

                    // 🎨 如果需要应用变色效果，使用 SkiaSharp 加载并处理
                    if (shouldApplyColorEffect)
                    {
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"🎨 [RestoreSplitConfig] 区域 {regionData.RegionIndex} 开始应用变色效果...");
                        #endif
                        
                        try
                        {
                            using var skBitmap = SkiaSharp.SKBitmap.Decode(regionData.ImagePath);
                            if (skBitmap != null)
                            {
                                #if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"   SKBitmap加载成功，尺寸: {skBitmap.Width}x{skBitmap.Height}");
                                #endif
                                
                                // 应用变色效果
                                _imageProcessor.ApplyYellowTextEffect(skBitmap);

                                #if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"   变色效果已应用");
                                #endif
                                
                                // 转换为 WPF BitmapSource
                                bitmap = _imageProcessor.ConvertToBitmapSource(skBitmap);
                                
                                #if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"✅ [RestoreSplitConfig] 区域 {regionData.RegionIndex} 变色效果应用成功");
                                #endif
                            }
                            else
                            {
                                #if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"❌ [RestoreSplitConfig] SKBitmap加载失败");
                                #endif
                                
                                // 加载失败，使用正常方式
                                var bmp = new BitmapImage();
                                bmp.BeginInit();
                                bmp.UriSource = new Uri(regionData.ImagePath);
                                bmp.CacheOption = BitmapCacheOption.OnLoad;
                                bmp.EndInit();
                                bmp.Freeze();
                                bitmap = bmp;
                            }
                        }
                        catch
                        {
                            #if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"❌ [RestoreSplitConfig] 应用变色效果失败");
                            #endif

                            // 失败时使用正常方式
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.UriSource = new Uri(regionData.ImagePath);
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.EndInit();
                            bmp.Freeze();
                            bitmap = bmp;
                        }
                    }
                    else
                    {
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"📷 [RestoreSplitConfig] 区域 {regionData.RegionIndex} 正常加载（无变色效果）");
                        #endif
                        
                        // 正常加载（无变色效果）
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(regionData.ImagePath);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        bmp.Freeze(); // 🔥 冻结到GPU显存
                        bitmap = bmp;
                    }
                    
                    // 决定使用的拉伸模式
                    System.Windows.Media.Stretch stretchMode;
                    if (shouldUseStretch)
                    {
                        // 原图标记文件夹：拉伸填满
                        stretchMode = System.Windows.Media.Stretch.Fill;
                    }
                    else if (slide.SplitMode == 0 || slide.SplitMode == 4)
                    {
                        // 单画面模式或三分割模式：默认拉伸填满
                        stretchMode = System.Windows.Media.Stretch.Fill;
                    }
                    else
                    {
                        // 其他分割模式：根据用户设置
                        stretchMode = _splitStretchMode ? 
                            System.Windows.Media.Stretch.Fill : 
                            System.Windows.Media.Stretch.Uniform;
                    }
                    
                    // 创建 Image 控件，应用拉伸模式
                    var imageControl = new System.Windows.Controls.Image
                    {
                        Source = bitmap,
                        Width = width,
                        Height = height,
                        Stretch = stretchMode,
                        Tag = $"RegionImage_{regionData.RegionIndex}",
                        CacheMode = new BitmapCache // 🔥 启用GPU缓存
                        {
                            RenderAtScale = CalculateOptimalRenderScale()  // 🔥 动态计算渲染质量：自适应1080p/2K/4K投影屏
                        }
                    };
                    
                    Canvas.SetLeft(imageControl, x);
                    Canvas.SetTop(imageControl, y);
                    Canvas.SetZIndex(imageControl, 998);
                    
                    // 添加到画布
                    EditorCanvas.Children.Add(imageControl);
                    
                    // 保存引用
                    _regionImages[regionData.RegionIndex] = imageControl;
                    _regionImagePaths[regionData.RegionIndex] = regionData.ImagePath;
                    _regionImageColorEffects[regionData.RegionIndex] = shouldApplyColorEffect; // 记录是否需要变色效果
                    
                    // 更新边框样式（有图片的区域显示金色）
                    border.Stroke = new SolidColorBrush(WpfColor.FromRgb(255, 215, 0));
                    
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"✅ [RestoreSplitConfig] 已恢复区域 {regionData.RegionIndex}: {System.IO.Path.GetFileName(regionData.ImagePath)}");
                    //#endif
                }
                
                // 🆕 最终同步：检查实际加载的图片拉伸模式，确保按钮显示正确
                if (_regionImages.Count > 0)
                {
                    var firstImage = _regionImages.Values.FirstOrDefault();
                    if (firstImage != null)
                    {
                        bool actualStretchMode = (firstImage.Stretch == System.Windows.Media.Stretch.Fill);
                        if (_splitStretchMode != actualStretchMode)
                        {
                            _splitStretchMode = actualStretchMode;
                            UpdateStretchModeButton();
                        }
                    }
                }
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"✅ [RestoreSplitConfig] 分割配置恢复完成");
                //#endif
            }
            catch
            {
                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($"❌ [RestoreSplitConfig] 失败");
                #endif
            }
        }

        /// <summary>
        /// 导入单张图片为当前幻灯片背景
        /// </summary>
        private void ImportSingleImageAsSlide()
        {
            if (_currentSlide == null)
            {
                WpfMessageBox.Show("请先选择一个幻灯片", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            BtnLoadBackgroundImage_Click(null, null);
        }

        /// <summary>
        /// 导入多张图片，每张图创建一张新幻灯片
        /// </summary>
        private async Task ImportMultipleImagesAsSlidesAsync()
        {
            try
            {
                // 选择多张图片
                var dialog = new WpfOpenFileDialog
                {
                    Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif",
                    Title = "选择图片（可多选）",
                    Multiselect = true
                };

                if (dialog.ShowDialog() != true || dialog.FileNames == null || dialog.FileNames.Length == 0)
                    return;

                var fileNames = dialog.FileNames;

                // 使用 SortManager 按数字优先排序
                var sortManager = new SortManager();
                var sortedFiles = fileNames
                    .Select(f => new { Path = f, SortKey = sortManager.GetSortKey(System.IO.Path.GetFileName(f)) })
                    .OrderBy(x => x.SortKey.prefixNumber)
                    .ThenBy(x => x.SortKey.pinyinPart)
                    .ThenBy(x => x.SortKey.suffixNumber)
                    .Select(x => x.Path)
                    .ToArray();

                // 显示进度提示
                var progressMessage = $"正在导入 {sortedFiles.Length} 张图片...";
                ShowStatus(progressMessage);

                // 获取当前最大排序号
                var maxOrderValue = await _dbContext.Slides
                    .Where(s => s.ProjectId == _currentTextProject.Id)
                    .Select(s => (int?)s.SortOrder)
                    .MaxAsync();

                int currentOrder = maxOrderValue ?? 0;

                // 获取当前幻灯片总数（用于生成标题序号）
                var slideCount = await _dbContext.Slides
                    .Where(s => s.ProjectId == _currentTextProject.Id)
                    .CountAsync();

                // 批量创建幻灯片
                var newSlides = new List<Slide>();
                for (int i = 0; i < sortedFiles.Length; i++)
                {
                    var imagePath = sortedFiles[i];
                    var fileName = System.IO.Path.GetFileNameWithoutExtension(imagePath);

                    var newSlide = new Slide
                    {
                        ProjectId = _currentTextProject.Id,
                        Title = $"幻灯片 {slideCount + i + 1}",
                        SortOrder = currentOrder + i + 1,
                        BackgroundImagePath = imagePath,
                        BackgroundColor = null,
                        SplitMode = -1,
                        SplitStretchMode = false,
                        CreatedTime = DateTime.Now,
                        ModifiedTime = DateTime.Now
                    };

                    _dbContext.Slides.Add(newSlide);
                    newSlides.Add(newSlide);
                }

                // 批量保存到数据库
                await _dbContext.SaveChangesAsync();

                // 🔧 先禁用事件，避免 LoadSlideList 触发自动选中干扰缩略图生成
                SlideListBox.SelectionChanged -= SlideListBox_SelectionChanged;

                foreach (var slide in newSlides)
                {
                    // 加载幻灯片到 EditorCanvas
                    LoadSlide(slide);

                    // 等待渲染完成
                    await Task.Delay(150);

                    // 生成缩略图
                    var thumbnailPath = SaveSlideThumbnail(slide.Id);
                    if (!string.IsNullOrEmpty(thumbnailPath))
                    {
                        slide.ThumbnailPath = thumbnailPath;
                        await _dbContext.SaveChangesAsync();
                    }
                }

                SlideListBox.SelectionChanged += SlideListBox_SelectionChanged;

                // 缩略图生成完成后刷新列表
                LoadSlideList();
                ShowStatus($"✅ 成功导入 {sortedFiles.Length} 张图片");

                // 选中第一张新幻灯片
                if (newSlides.Count > 0)
                {
                    SlideListBox.SelectedItem = newSlides[0];
                }
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"批量导入失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 导入视频作为幻灯片背景
        /// </summary>
        private async Task ImportVideoAsSlideAsync()
        {
            if (_currentSlide == null)
            {
                WpfMessageBox.Show("请先选择一个幻灯片", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 选择视频文件
            var dialog = new WpfOpenFileDialog
            {
                Filter = "视频文件|*.mp4;*.avi;*.wmv;*.mov;*.mkv",
                Title = "选择视频背景"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
#if DEBUG
                var totalTime = System.Diagnostics.Stopwatch.StartNew();
                //System.Diagnostics.Debug.WriteLine($"📥 [视频导入] ===== 开始导入视频背景 =====");
#endif

                string videoPath = dialog.FileName;

#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"📥 [视频导入] 文件: {System.IO.Path.GetFileName(videoPath)}");
                //System.Diagnostics.Debug.WriteLine($"📥 [视频导入] 投影状态: {(_projectionManager.IsProjectionActive ? "已开启" : "未开启")}");
#endif

                // 更新当前幻灯片数据
                _currentSlide.BackgroundImagePath = videoPath;
                _currentSlide.VideoBackgroundEnabled = true;
                _currentSlide.VideoLoopEnabled = true;  // 默认开启循环
                _currentSlide.VideoVolume = 0.0;  // 默认静音

#if DEBUG
                var dbStartTime = System.Diagnostics.Stopwatch.StartNew();
#endif
                // 保存到数据库
                await SaveVideoBackgroundSettingsAsync();
#if DEBUG
                dbStartTime.Stop();
                //System.Diagnostics.Debug.WriteLine($"💾 [视频导入] 数据库保存完成 (耗时: {dbStartTime.ElapsedMilliseconds} ms)");
#endif

#if DEBUG
                var loadStartTime = System.Diagnostics.Stopwatch.StartNew();
#endif
                // 清除旧的背景
                EditorCanvas.Background = new SolidColorBrush(Colors.Black);
                var oldMediaElements = EditorCanvas.Children.OfType<MediaElement>().ToList();
                foreach (var old in oldMediaElements)
                {
                    old.Stop();
                    old.Close();
                    EditorCanvas.Children.Remove(old);
                }

                // 创建 MediaElement
                var mediaElement = new MediaElement
                {
                    Source = new Uri(videoPath, UriKind.Absolute),
                    LoadedBehavior = MediaState.Manual,
                    UnloadedBehavior = MediaState.Manual,
                    Stretch = Stretch.UniformToFill,  // 🔧 改为 UniformToFill，填充整个画布
                    Width = EditorCanvas.Width,       // 🔧 明确设置宽度
                    Height = EditorCanvas.Height,     // 🔧 明确设置高度
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    VerticalAlignment = System.Windows.VerticalAlignment.Top,
                    Volume = 0.0,  // 默认静音
                    ScrubbingEnabled = true,
                    // 🚀 启用 GPU 硬件加速缓存
                    CacheMode = new BitmapCache
                    {
                        EnableClearType = false,  // 视频不需要ClearType
                        RenderAtScale = 1.0,      // 1080p适配，减少GPU内存占用
                        SnapsToDevicePixels = true
                    }
                };
                
                // 🚀 设置 GPU 渲染优化
                RenderOptions.SetBitmapScalingMode(mediaElement, BitmapScalingMode.LowQuality);  // 优先性能而非质量
                RenderOptions.SetCachingHint(mediaElement, CachingHint.Cache);  // 强制启用缓存
                
#if DEBUG
                //var cache = mediaElement.CacheMode as BitmapCache;
                //System.Diagnostics.Debug.WriteLine($"🚀 [视频GPU加速] BitmapCache 已启用: RenderAtScale={cache?.RenderAtScale ?? 0}");
                //System.Diagnostics.Debug.WriteLine($"🚀 [视频GPU加速] CachingHint: {RenderOptions.GetCachingHint(mediaElement)}");
                //System.Diagnostics.Debug.WriteLine($"🚀 [视频GPU加速] BitmapScalingMode: {RenderOptions.GetBitmapScalingMode(mediaElement)}");
#endif

                // 设置循环播放
                UpdateVideoLoopBehavior(mediaElement, true);

                // 添加到 Canvas（设置位置为左上角）
                Canvas.SetLeft(mediaElement, 0);
                Canvas.SetTop(mediaElement, 0);
                Canvas.SetZIndex(mediaElement, -1);  // 🔧 设置为最底层，确保文本在上方
                EditorCanvas.Children.Insert(0, mediaElement);

                // 自动播放
                mediaElement.Play();

#if DEBUG
                loadStartTime.Stop();
                //System.Diagnostics.Debug.WriteLine($"🎬 [视频导入] 视频加载到编辑器完成 (耗时: {loadStartTime.ElapsedMilliseconds} ms)");
#endif

                // 更新投影
                if (_projectionManager.IsProjectionActive && !_isProjectionLocked)
                {
#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"🔄 [视频导入] 开始更新投影...");
                    var projStartTime = System.Diagnostics.Stopwatch.StartNew();
#endif
                    await Task.Delay(100); // 等待视频加载
                    await Dispatcher.InvokeAsync(() =>
                    {
                        UpdateProjectionFromCanvas();
                    }, System.Windows.Threading.DispatcherPriority.Render);
#if DEBUG
                    projStartTime.Stop();
                    //System.Diagnostics.Debug.WriteLine($"✅ [视频导入] 投影更新完成 (耗时: {projStartTime.ElapsedMilliseconds} ms)");
#endif
                }

                ShowStatus($"已设置视频背景: {System.IO.Path.GetFileName(videoPath)}");

#if DEBUG
                totalTime.Stop();
                //System.Diagnostics.Debug.WriteLine($"✅ [视频导入] 已设置视频背景");
                //System.Diagnostics.Debug.WriteLine($"   - 循环播放: 开启");
                //System.Diagnostics.Debug.WriteLine($"   - 音量: 0% (静音)");
                //System.Diagnostics.Debug.WriteLine($"   - 自动播放: 是");
                //System.Diagnostics.Debug.WriteLine($"⏱️ [视频导入] 总耗时: {totalTime.ElapsedMilliseconds} ms");
                //System.Diagnostics.Debug.WriteLine($"📥 [视频导入] ===== 导入完成 =====\n");
#endif
            }
            catch (Exception
#if DEBUG
            ex
#endif
            )
            {
#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"❌ [视频导入] 失败: {ex.Message}");
                WpfMessageBox.Show($"设置视频背景失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
#else
                WpfMessageBox.Show("设置视频背景失败，请检查视频文件是否有效", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
#endif
            }
        }

        /// <summary>
        /// 导入背景图片（原有方法，保持兼容）
        /// </summary>
        private async void BtnLoadBackgroundImage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTextProject == null || _currentSlide == null)
                return;

            var dialog = new WpfOpenFileDialog
            {
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif",
                Title = "选择背景图"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // 🆕 使用 ImageBrush 设置 Canvas.Background
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(dialog.FileName);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    EditorCanvas.Background = new ImageBrush(bitmap)
                    {
                        Stretch = Stretch.Fill
                    };
                    
                    // 🔧 保存背景图路径到当前幻灯片
                    var slideToUpdate = await _dbContext.Slides.FindAsync(_currentSlide.Id);
                    if (slideToUpdate != null)
                    {
                        slideToUpdate.BackgroundImagePath = dialog.FileName;
                        slideToUpdate.BackgroundColor = null; // 清除背景色
                        slideToUpdate.ModifiedTime = DateTime.Now;
                        await _dbContext.SaveChangesAsync();
                        
                        // 更新本地缓存
                        _currentSlide.BackgroundImagePath = dialog.FileName;
                        _currentSlide.BackgroundColor = null;
                        
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"✅ [背景图] 已保存到数据库: SlideId={slideToUpdate.Id}");
                        //#endif
                    }
                    
                    // 更新项目的背景图片路径（兼容旧数据）
                    await _textProjectManager.UpdateBackgroundImageAsync(_currentTextProject.Id, dialog.FileName);

                    // 🔧 如果投影已开启且未锁定，更新投影
                    if (_projectionManager != null && _projectionManager.IsProjectionActive && !_isProjectionLocked)
                    {
                        UpdateProjectionFromCanvas();
                    }

                    MarkContentAsModified();
                    
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"✅ [背景图] 导入完成");
                    //#endif
                }
                catch (Exception ex)
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"❌ [背景图] 导入失败: {ex.Message}");
                    //System.Diagnostics.Debug.WriteLine($"   堆栈: {ex.StackTrace}");
                    //#endif
                    
                    WpfMessageBox.Show($"加载背景图失败: {ex.Message}", "错误", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 选择背景颜色
        /// </summary>
        private async void BtnSelectBackgroundColor_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTextProject == null || _currentSlide == null)
                return;

            // 创建颜色选择对话框
            var colorDialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                Color = System.Drawing.Color.White
            };

            // 如果当前幻灯片有背景色，设置为初始颜色
            if (!string.IsNullOrEmpty(_currentSlide.BackgroundColor))
            {
                try
                {
                    var wpfColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_currentSlide.BackgroundColor);
                    colorDialog.Color = System.Drawing.Color.FromArgb(wpfColor.A, wpfColor.R, wpfColor.G, wpfColor.B);
                }
                catch { }
            }

            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                
                try
                {
                    // 转换为WPF颜色
                    var wpfColor = System.Windows.Media.Color.FromArgb(
                        colorDialog.Color.A,
                        colorDialog.Color.R,
                        colorDialog.Color.G,
                        colorDialog.Color.B
                    );

                    // 转换为十六进制字符串
                    var hexColor = $"#{wpfColor.R:X2}{wpfColor.G:X2}{wpfColor.B:X2}";

                    //System.Diagnostics.Debug.WriteLine($"🎨 准备设置背景色: {hexColor}");
                    //System.Diagnostics.Debug.WriteLine($"   EditorCanvas: {EditorCanvas?.Name ?? "null"}");
                    
                    // 设置Canvas背景色
                    EditorCanvas.Background = new SolidColorBrush(wpfColor);
                    
                    //System.Diagnostics.Debug.WriteLine($"   EditorCanvas.Background 已设置: {EditorCanvas.Background}");
                    
                    // 🔧 背景色设置后，Canvas.Background 会被直接覆盖为纯色，无需额外清除
                    
                    // 🔧 保存背景色到当前幻灯片
                    var slideToUpdate = await _dbContext.Slides.FindAsync(_currentSlide.Id);
                    if (slideToUpdate != null)
                    {
                        slideToUpdate.BackgroundColor = hexColor;
                        slideToUpdate.BackgroundImagePath = null; // 清除背景图片
                        slideToUpdate.ModifiedTime = DateTime.Now;
                        await _dbContext.SaveChangesAsync();
                        
                        // 更新本地缓存
                        _currentSlide.BackgroundColor = hexColor;
                        _currentSlide.BackgroundImagePath = null;
                        
                        //System.Diagnostics.Debug.WriteLine($"✅ 背景色已保存到幻灯片: {hexColor}");
                    }
                    
                    // 清除项目的背景图片路径（兼容旧数据）
                    await _textProjectManager.UpdateBackgroundImageAsync(_currentTextProject.Id, null);
                    
                    //System.Diagnostics.Debug.WriteLine($"✅ 背景色设置成功: {hexColor}");
                    MarkContentAsModified();
                }
                catch (Exception ex)
                {
                    //System.Diagnostics.Debug.WriteLine($"❌ 设置背景色失败: {ex.Message}");
                    WpfMessageBox.Show($"设置背景色失败: {ex.Message}", "错误", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 清除背景
        /// </summary>
        private async void BtnClearBackground_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTextProject == null || _currentSlide == null)
                return;

            try
            {
                // 重置Canvas背景为白色
                EditorCanvas.Background = new SolidColorBrush(Colors.White);
                
                // 🔧 保存白色背景到当前幻灯片
                var slideToUpdate = await _dbContext.Slides.FindAsync(_currentSlide.Id);
                if (slideToUpdate != null)
                {
                    slideToUpdate.BackgroundColor = "#FFFFFF";
                    slideToUpdate.BackgroundImagePath = null;
                    slideToUpdate.ModifiedTime = DateTime.Now;
                    await _dbContext.SaveChangesAsync();
                    
                    // 更新本地缓存
                    _currentSlide.BackgroundColor = "#FFFFFF";
                    _currentSlide.BackgroundImagePath = null;
                    
                    //System.Diagnostics.Debug.WriteLine("✅ 背景已清除并保存到幻灯片");
                }
                
                // 清除项目的背景图片路径（兼容旧数据）
                await _textProjectManager.UpdateBackgroundImageAsync(_currentTextProject.Id, null);
                
                //System.Diagnostics.Debug.WriteLine("✅ 背景已清除");
                MarkContentAsModified();
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 清除背景失败: {ex.Message}");
                WpfMessageBox.Show($"清除背景失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 水平对称按钮
        /// </summary>
        private async void BtnSymmetricH_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null)
            {
                WpfMessageBox.Show("请先选中一个文本框！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                double centerX = EditorCanvas.Width / 2;
                double mirrorX = centerX + (centerX - _selectedTextBox.Data.X - _selectedTextBox.Data.Width);

                // 克隆元素
                var mirrorElement = _textProjectManager.CloneElement(_selectedTextBox.Data);
                mirrorElement.X = mirrorX;
                mirrorElement.IsSymmetricBool = true;
                mirrorElement.SymmetricPairId = _selectedTextBox.Data.Id;
                mirrorElement.SymmetricType = "Horizontal";

                // 保存到数据库
                await _textProjectManager.AddElementAsync(mirrorElement);

                // 添加到画布
                var mirrorBox = new DraggableTextBox(mirrorElement);
                AddTextBoxToCanvas(mirrorBox);

                // 建立联动
                _selectedTextBox.PositionChanged += (s, pos) =>
                {
                    double newMirrorX = centerX + (centerX - pos.X - _selectedTextBox.Data.Width);
                    Canvas.SetLeft(mirrorBox, newMirrorX);
                    mirrorBox.Data.X = newMirrorX;
                };

                //System.Diagnostics.Debug.WriteLine($"✅ 创建水平对称元素成功");
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 创建对称元素失败: {ex.Message}");
                WpfMessageBox.Show($"创建对称元素失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 垂直对称按钮
        /// </summary>
        private async void BtnSymmetricV_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null)
            {
                WpfMessageBox.Show("请先选中一个文本框！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                double centerY = EditorCanvas.Height / 2;
                double mirrorY = centerY + (centerY - _selectedTextBox.Data.Y - _selectedTextBox.Data.Height);

                // 克隆元素
                var mirrorElement = _textProjectManager.CloneElement(_selectedTextBox.Data);
                mirrorElement.Y = mirrorY;
                mirrorElement.IsSymmetricBool = true;
                mirrorElement.SymmetricPairId = _selectedTextBox.Data.Id;
                mirrorElement.SymmetricType = "Vertical";

                // 保存到数据库
                await _textProjectManager.AddElementAsync(mirrorElement);

                // 添加到画布
                var mirrorBox = new DraggableTextBox(mirrorElement);
                AddTextBoxToCanvas(mirrorBox);

                // 建立联动
                _selectedTextBox.PositionChanged += (s, pos) =>
                {
                    double newMirrorY = centerY + (centerY - pos.Y - _selectedTextBox.Data.Height);
                    Canvas.SetTop(mirrorBox, newMirrorY);
                    mirrorBox.Data.Y = newMirrorY;
                };

                //System.Diagnostics.Debug.WriteLine($"✅ 创建垂直对称元素成功");
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 创建对称元素失败: {ex.Message}");
                WpfMessageBox.Show($"创建对称元素失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 保存项目按钮
        /// </summary>
        private async void BtnSaveTextProject_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTextProject == null)
                return;

            try
            {
//#if DEBUG
//                //System.Diagnostics.Debug.WriteLine($"💾 [文字保存] 开始保存项目: {_currentTextProject.Name}");
//                //System.Diagnostics.Debug.WriteLine($"💾 [文字保存] 文本框数量: {_textBoxes.Count}");
//                //System.Diagnostics.Debug.WriteLine($"💾 [文字保存] 投影状态: {(_projectionManager.IsProjectionActive ? "已开启" : "未开启")}");
//
//                // 打印每个文本框的样式信息
//                foreach (var tb in _textBoxes)
//                {
//                    //System.Diagnostics.Debug.WriteLine($"  📦 文本框 ID={tb.Data.Id}: 边框={tb.Data.BorderColor}/{tb.Data.BorderWidth}px/透明度{tb.Data.BorderOpacity}%, 背景={tb.Data.BackgroundColor}/透明度{tb.Data.BackgroundOpacity}%, 加粗={tb.Data.IsBold}, 斜体={tb.Data.IsItalic}");
//                }
//#endif

                // 批量更新所有元素
                await _textProjectManager.UpdateElementsAsync(_textBoxes.Select(tb => tb.Data));
//#if DEBUG
//                //System.Diagnostics.Debug.WriteLine($"💾 [文字保存] 已更新元素到数据库");
//#endif

                // 🔧 同步 FlowDocument 到 RichTextSpans 表（支持局部样式持久化）
//#if DEBUG
//                //System.Diagnostics.Debug.WriteLine($"💾 [文字保存] 开始同步 FlowDocument 到 RichTextSpans");
//#endif
                foreach (var tb in _textBoxes)
                {
                    var richTextSpans = tb.ExtractRichTextSpansFromFlowDocument();
                    if (richTextSpans != null && richTextSpans.Count > 0)
                    {
//#if DEBUG
//                        //System.Diagnostics.Debug.WriteLine($"💾 [文字保存] 文本框 ID={tb.Data.Id} 提取了 {richTextSpans.Count} 个片段，准备保存到数据库");
//#endif
                        await _textProjectManager.SaveRichTextSpansAsync(tb.Data.Id, richTextSpans);
//#if DEBUG
//                        //System.Diagnostics.Debug.WriteLine($"💾 [文字保存] 文本框 ID={tb.Data.Id} 已保存到数据库");
//#endif
                    }
                    else
                    {
                        // 如果没有富文本片段，清除旧的片段（用户可能删除了所有局部样式）
                        await _textProjectManager.DeleteRichTextSpansByElementIdAsync(tb.Data.Id);
//#if DEBUG
//                        //System.Diagnostics.Debug.WriteLine($"💾 [文字保存] 文本框 ID={tb.Data.Id} 清除了富文本片段（无局部样式）");
//#endif
                    }
                }
//#if DEBUG
//                //System.Diagnostics.Debug.WriteLine($"💾 [文字保存] FlowDocument 同步完成");
//#endif

                // 🆕 保存分割区域配置（单画面/分割模式的图片）
                await SaveSplitConfigAsync();

                // 🆕 生成当前幻灯片的缩略图
                if (_currentSlide != null)
                {
                    var thumbnailPath = SaveSlideThumbnail(_currentSlide.Id);
                    if (!string.IsNullOrEmpty(thumbnailPath))
                    {
                        _currentSlide.ThumbnailPath = thumbnailPath;
                    }
                }

                // 🆕 保存成功后，恢复按钮为白色
                BtnSaveTextProject.Background = new SolidColorBrush(Colors.White);

                // 🆕 刷新幻灯片列表，更新缩略图显示
                RefreshSlideList();

                // 🔧 如果投影开启且未锁定，自动更新投影
                if (_projectionManager.IsProjectionActive && !_isProjectionLocked)
                {
//#if DEBUG
//                    //System.Diagnostics.Debug.WriteLine($"🔄 [文字保存] 投影已开启，准备自动更新投影...");
//#endif
                    UpdateProjectionFromCanvas();
//#if DEBUG
//                    //System.Diagnostics.Debug.WriteLine($"✅ [文字保存] 已调用 UpdateProjectionFromCanvas");
//#endif
                }
                else
                {
//#if DEBUG
//                    //System.Diagnostics.Debug.WriteLine($"⚠️ [文字保存] 投影未开启或已锁定，跳过投影更新 (IsProjectionActive={_projectionManager.IsProjectionActive}, IsLocked={_isProjectionLocked})");
//#endif
                }

//#if DEBUG
//                //System.Diagnostics.Debug.WriteLine($"✅ [文字保存] 保存项目成功: {_currentTextProject.Name}");
//#endif
            }
            catch (Exception ex)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"❌ [文字保存] 保存项目失败: {ex.Message}");
                //#endif
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"❌ [文字保存] 堆栈: {ex.StackTrace}");
                //#endif
                WpfMessageBox.Show($"保存项目失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 🆕 更新投影按钮（核心功能）
        /// </summary>
        private void BtnUpdateProjection_Click(object sender, RoutedEventArgs e)
        {
            UpdateProjectionFromCanvas();
        }

        #endregion

    }
}
