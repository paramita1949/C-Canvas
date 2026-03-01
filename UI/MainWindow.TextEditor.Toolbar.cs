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
using ImageColorChanger.UI.Modules;
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
        private TextEditorMenuController _textEditorMenuController;

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
                // 固定位置创建新文本框,新文本在上层(ZIndex最大)
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
                
                //  创建文本框时始终使用微软雅黑，不应用当前选择的字体
                // 字体应用必须框选文字才能应用
                string defaultFontFamily = "Microsoft YaHei UI";
                
                // 创建新元素 (关联到当前幻灯片)
                var newElement = new TextElement
                {
                    SlideId = _currentSlide.Id,  // 关联到幻灯片
                    X = newX,
                    Y = newY,
                    Width = newWidth,
                    Height = newHeight,
                    Content = "双击编辑",
                    FontSize = 60,  // 默认字号60
                    FontFamily = defaultFontFamily,  //  始终使用微软雅黑
                    FontColor = "#FFFFFF",  // 默认白色字体
                    ZIndex = maxZIndex + 1  // 新文本在最上层
                };

                // 保存到数据库
                await _textProjectService.AddElementAsync(newElement);

                // 添加到画布
                var textBox = new DraggableTextBox(newElement);
                AddTextBoxToCanvas(textBox);
                
                // 新建文本框：自动进入编辑模式，全选占位符文本
                textBox.Focus();
                textBox.EnterEditModeForNew();
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($" 添加文本框失败: {ex.Message}");
                WpfMessageBox.Show($"添加文本框失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 复制文本框到内部剪贴板（支持跨幻灯片粘贴）
        /// </summary>
        private Task CopyTextBoxToClipboardAsync(DraggableTextBox sourceTextBox)
        {
            if (sourceTextBox == null)
                return Task.CompletedTask;

            try
            {
                // 先同步文本内容，确保复制的是最新状态
                sourceTextBox.SyncTextFromRichTextBox();

                var sourceElement = sourceTextBox.Data;
                _textBoxClipboardElement = _textProjectService.CloneElement(sourceElement);
                _textBoxClipboardSpans = CloneRichTextSpans(sourceElement.RichTextSpans);
                _textBoxPasteOffsetStep = 1;

                ShowToast("已复制文本框");
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"复制文本框失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 从内部剪贴板粘贴文本框（可跨幻灯片）
        /// </summary>
        private async Task PasteTextBoxFromClipboardAsync(DraggableTextBox anchorTextBox = null)
        {
            if (_currentSlide == null)
                return;

            if (_textBoxClipboardElement == null)
            {
                ShowToast("没有可粘贴的文本框");
                return;
            }

            try
            {
                int maxZIndex = _textBoxes.Count > 0 ? _textBoxes.Max(tb => tb.Data.ZIndex) : 0;
                var newElement = _textProjectService.CloneElement(_textBoxClipboardElement);

                // 位置策略：优先锚点文本框，否则当前选中文本框，再否则默认位置
                double baseX = anchorTextBox?.Data.X ?? _selectedTextBox?.Data.X ?? 100;
                double baseY = anchorTextBox?.Data.Y ?? _selectedTextBox?.Data.Y ?? 100;
                double step = 20 * _textBoxPasteOffsetStep;

                newElement.SlideId = _currentSlide.Id;
                newElement.ProjectId = null;
                newElement.X = baseX + step;
                newElement.Y = baseY + step;
                newElement.ZIndex = maxZIndex + 1;

                await _textProjectService.AddElementAsync(newElement);

                if (_textBoxClipboardSpans != null && _textBoxClipboardSpans.Count > 0)
                {
                    var spansToSave = _textBoxClipboardSpans
                        .OrderBy(s => s.SpanOrder)
                        .Select((span, index) => new Database.Models.RichTextSpan
                        {
                            TextElementId = newElement.Id,
                            SpanOrder = index,
                            Text = span.Text,
                            FontFamily = span.FontFamily,
                            FontSize = span.FontSize,
                            FontColor = span.FontColor,
                            IsBold = span.IsBold,
                            IsItalic = span.IsItalic,
                            IsUnderline = span.IsUnderline,
                            BorderColor = span.BorderColor,
                            BorderWidth = span.BorderWidth,
                            BorderRadius = span.BorderRadius,
                            BorderOpacity = span.BorderOpacity,
                            BackgroundColor = span.BackgroundColor,
                            BackgroundRadius = span.BackgroundRadius,
                            BackgroundOpacity = span.BackgroundOpacity,
                            ShadowColor = span.ShadowColor,
                            ShadowOffsetX = span.ShadowOffsetX,
                            ShadowOffsetY = span.ShadowOffsetY,
                            ShadowBlur = span.ShadowBlur,
                            ShadowOpacity = span.ShadowOpacity,
                            ParagraphIndex = span.ParagraphIndex,
                            RunIndex = span.RunIndex,
                            FormatVersion = span.FormatVersion
                        })
                        .ToList();

                    await _richTextSpanRepository.SaveForTextElementAsync(newElement.Id, spansToSave);
                    newElement.RichTextSpans = spansToSave;
                }

                var textBox = new DraggableTextBox(newElement);
                AddTextBoxToCanvas(textBox);
                textBox.SetSelected(true);
                _selectedTextBox = textBox;
                ShowTextBoxFloatingToolbar(textBox);
                MarkContentAsModified();

                _textBoxPasteOffsetStep++;
                ShowToast("已粘贴文本框");
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"粘贴文本框失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<Database.Models.RichTextSpan> CloneRichTextSpans(ICollection<Database.Models.RichTextSpan> sourceSpans)
        {
            if (sourceSpans == null || sourceSpans.Count == 0)
                return new List<Database.Models.RichTextSpan>();

            return sourceSpans
                .OrderBy(s => s.SpanOrder)
                .Select((span, index) => new Database.Models.RichTextSpan
                {
                    SpanOrder = index,
                    Text = span.Text,
                    FontFamily = span.FontFamily,
                    FontSize = span.FontSize,
                    FontColor = span.FontColor,
                    IsBold = span.IsBold,
                    IsItalic = span.IsItalic,
                    IsUnderline = span.IsUnderline,
                    BorderColor = span.BorderColor,
                    BorderWidth = span.BorderWidth,
                    BorderRadius = span.BorderRadius,
                    BorderOpacity = span.BorderOpacity,
                    BackgroundColor = span.BackgroundColor,
                    BackgroundRadius = span.BackgroundRadius,
                    BackgroundOpacity = span.BackgroundOpacity,
                    ShadowColor = span.ShadowColor,
                    ShadowOffsetX = span.ShadowOffsetX,
                    ShadowOffsetY = span.ShadowOffsetY,
                    ShadowBlur = span.ShadowBlur,
                    ShadowOpacity = span.ShadowOpacity,
                    ParagraphIndex = span.ParagraphIndex,
                    RunIndex = span.RunIndex,
                    FormatVersion = span.FormatVersion
                })
                .ToList();
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
                await _textProjectService.DeleteElementAsync(textBox.Data.Id);

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
//                        //System.Diagnostics.Debug.WriteLine($" [删除文本框] 圣经工具栏已隐藏");
//#endif
                    }
                }

                // 标记已修改
                MarkContentAsModified();

                //System.Diagnostics.Debug.WriteLine($" 删除文本框成功");
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($" 删除文本框失败: {ex.Message}");
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

            EnsureTextEditorMenuController();
            _textEditorMenuController.ShowBackgroundImportMenu(
                BtnBackgroundImage,
                (Style)FindResource("NoBorderContextMenuStyle"),
                ImportSingleImageAsSlide,
                ImportMultipleImagesAsSlidesAsync,
                ImportVideoAsSlideAsync);
        }

        /// <summary>
        /// 背景颜色按钮点击（直接选择颜色）
        /// </summary>
        private void BtnBackgroundColor_Click(object sender, RoutedEventArgs e)
        {
            BtnSelectBackgroundColor_Click(sender, e);
        }

        /// <summary>
        /// 分割图片显示模式按钮点击（弹出可选模式菜单）
        /// </summary>
        private void BtnSplitStretchMode_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTextProject == null || _currentSlide == null)
                return;

            if (_regionImages.Count == 0)
                return;

            var anchor = sender as FrameworkElement ?? BtnSplitStretchMode;
            if (anchor == null)
                return;

            var modeMenu = new ContextMenu
            {
                PlacementTarget = anchor,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                Style = (Style)FindResource("NoBorderContextMenuStyle")
            };

            foreach (var mode in GetAlternativeSplitImageDisplayModes(_splitImageDisplayMode))
            {
                var item = new MenuItem
                {
                    Header = GetSplitImageDisplayModeLabel(mode),
                    Tag = mode
                };

                item.Click += async (_, __) =>
                {
                    await ApplySelectedSplitImageDisplayModeAsync(mode);
                };

                modeMenu.Items.Add(item);
            }

            if (modeMenu.Items.Count == 0)
            {
                return;
            }

            anchor.ContextMenu = modeMenu;
            modeMenu.IsOpen = true;
        }
        
        /// <summary>
        /// 更新显示模式按钮显示
        /// </summary>
        private void UpdateStretchModeButton()
        {
            SetSplitStretchButtonContent(_splitImageDisplayMode);
        }

        private static IReadOnlyList<SplitImageDisplayMode> GetAlternativeSplitImageDisplayModes(SplitImageDisplayMode currentMode)
        {
            var allModes = new[]
            {
                SplitImageDisplayMode.FitCenter,
                SplitImageDisplayMode.Fill,
                SplitImageDisplayMode.FitTop
            };

            return allModes.Where(mode => mode != currentMode).ToArray();
        }

        private static string GetSplitImageDisplayModeLabel(SplitImageDisplayMode mode)
        {
            return mode switch
            {
                SplitImageDisplayMode.Fill => "拉伸",
                SplitImageDisplayMode.FitTop => "置顶",
                _ => "适中"
            };
        }

        private async Task ApplySelectedSplitImageDisplayModeAsync(SplitImageDisplayMode mode)
        {
            if (_splitImageDisplayMode == mode)
                return;

            _splitImageDisplayMode = mode;
            ApplySplitImageDisplayModeToAllRegions();
            UpdateStretchModeButton();

            await SaveSplitStretchModeAsync();

            // 作为全局偏好长期保存（下次分割图沿用）
            SaveSettings();

            if (!_isProjectionLocked && _projectionManager?.IsProjectionActive == true)
            {
                UpdateProjectionFromCanvas();
            }
        }

        private void ApplySplitImageDisplayModeToAllRegions()
        {
            foreach (var kvp in _regionImages)
            {
                if (kvp.Key >= 0 && kvp.Key < _splitRegionBorders.Count)
                {
                    ApplySplitImageDisplayModeToRegion(kvp.Value, kvp.Key);
                }
            }
        }

        private void ApplySplitImageDisplayModeToRegion(System.Windows.Controls.Image imageControl, int regionIndex)
        {
            if (imageControl == null || regionIndex < 0 || regionIndex >= _splitRegionBorders.Count)
                return;

            var border = _splitRegionBorders[regionIndex];
            double regionLeft = Canvas.GetLeft(border);
            double regionTop = Canvas.GetTop(border);
            double regionWidth = border.Width;
            double regionHeight = border.Height;

            // 保持控件区域和分割区域一致，置顶模式通过向上平移去掉 Uniform 的上方留白。
            imageControl.Width = regionWidth;
            imageControl.Height = regionHeight;
            Canvas.SetLeft(imageControl, regionLeft);

            if (_splitImageDisplayMode == SplitImageDisplayMode.Fill)
            {
                imageControl.Stretch = System.Windows.Media.Stretch.Fill;
                Canvas.SetTop(imageControl, regionTop);
                return;
            }

            imageControl.Stretch = System.Windows.Media.Stretch.Uniform;
            if (_splitImageDisplayMode == SplitImageDisplayMode.FitCenter)
            {
                Canvas.SetTop(imageControl, regionTop);
                return;
            }

            // FitTop：对于“横图”向上偏移一半留白，达到置顶效果。
            double topOffset = CalculateUniformVerticalCenterOffset(imageControl.Source, regionWidth, regionHeight);
            Canvas.SetTop(imageControl, regionTop - topOffset);
        }

        private static double CalculateUniformVerticalCenterOffset(ImageSource source, double regionWidth, double regionHeight)
        {
            if (source is not BitmapSource bitmap || regionWidth <= 0 || regionHeight <= 0 || bitmap.PixelHeight <= 0)
            {
                return 0;
            }

            double imageAspect = (double)bitmap.PixelWidth / bitmap.PixelHeight;
            double regionAspect = regionWidth / regionHeight;

            if (imageAspect <= regionAspect)
            {
                // 竖图或接近区域比例时，Uniform按高度撑满，无垂直留白。
                return 0;
            }

            // 横图以宽度为准，产生上下留白。
            double drawHeight = regionWidth / imageAspect;
            return Math.Max(0, (regionHeight - drawHeight) / 2.0);
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
                var slideToUpdate = await _textProjectService.GetSlideByIdAsync(_currentSlide.Id);
                if (slideToUpdate != null)
                {
                    slideToUpdate.SplitStretchMode = _splitImageDisplayMode;
                    slideToUpdate.ModifiedTime = DateTime.Now;
                    await _textProjectService.UpdateSlideAsync(slideToUpdate);
                    
                    // 更新本地缓存
                    _currentSlide.SplitStretchMode = _splitImageDisplayMode;
                    
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"[SaveSplitStretchMode] 已保存显示模式: {_splitImageDisplayMode}");
                    //#endif
                }
            }
            catch
            {
                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [SaveSplitStretchMode] 失败");
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

            EnsureTextEditorMenuController();
            _textEditorMenuController.ShowSplitModeMenu(
                sender as UIElement ?? BtnSplitView,
                (Style)FindResource("NoBorderContextMenuStyle"),
                _currentSlide.SplitMode,
                SetSplitMode);
        }

        private void EnsureTextEditorMenuController()
        {
            _textEditorMenuController ??= new TextEditorMenuController();
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
                var slideToUpdate = await _textProjectService.GetSlideByIdAsync(_currentSlide.Id);
                if (slideToUpdate != null)
                {
                    slideToUpdate.SplitMode = (int)mode;
                    slideToUpdate.ModifiedTime = DateTime.Now;
                    
                    // 切换分割模式时，清空分割区域数据
                    slideToUpdate.SplitRegionsData = null;
                    
                    await _textProjectService.UpdateSlideAsync(slideToUpdate);

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
//                //System.Diagnostics.Debug.WriteLine($" [SetSplitMode] 失败: {ex.Message}");
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
                    // 单画面模式：创建一个占满整个画布的区域（不显示边框和标签）
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
        /// 创建区域边框
        /// </summary>
        private void CreateRegionBorder(int regionIndex, double x, double y, double width, double height)
        {
            // 判断是否是单画面模式
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
            
            // 只在非单画面模式下显示序列号标签
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
            
            // 清除序列号标签
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
            //System.Diagnostics.Debug.WriteLine($"[SelectRegion] 选中区域: {regionIndex}");
            //#endif
            
            _selectedRegionIndex = regionIndex;
            
            // 设置画布焦点，使其能接收键盘事件
            EditorCanvas.Focus();
            
            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"[SelectRegion] 已设置画布焦点，IsFocused: {EditorCanvas.IsFocused}");
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
                // 检查图片是否来自原图标记或变色标记的文件夹
                bool shouldUseStretch = false;
                bool shouldApplyColorEffect = false;
                try
                {
                    var mediaFile = await _textProjectService.GetMediaFileByPathAsync(imagePath);
                    
                    #if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"[LoadImageToSplitRegion] 检查图片: {System.IO.Path.GetFileName(imagePath)}");
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

                        shouldApplyColorEffect = DatabaseManagerService.HasFolderAutoColorEffect(mediaFile.FolderId.Value);

                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"   原图标记: {shouldUseStretch}");
                        //System.Diagnostics.Debug.WriteLine($"   变色标记: {shouldApplyColorEffect}");
                        //if (shouldUseStretch)
                        //{
                        //    //System.Diagnostics.Debug.WriteLine($"[LoadImageToSplitRegion] 检测到原图标记文件夹，自动使用拉伸模式");
                        //}
                        //if (shouldApplyColorEffect)
                        //{
                        //    //System.Diagnostics.Debug.WriteLine($"[LoadImageToSplitRegion] 检测到变色标记文件夹，自动应用变色效果");
                        //}
                        #endif
                    }
                    
                    #if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"   未找到MediaFile或FolderId为空");
                    #endif
                }
                catch
                {
                    #if DEBUG
                    //System.Diagnostics.Debug.WriteLine($" [LoadImageToSplitRegion] 检查标记失败");
                    #endif
                }
                
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
                
                // 使用优化的图片加载（GPU加速 + 缓存）
                var bitmapSource = await Task.Run<BitmapSource>(() =>
                {
                    // 如果需要应用变色效果，使用 SkiaSharp 加载并处理
                    if (shouldApplyColorEffect)
                    {
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"[LoadImageToSplitRegion] 开始应用变色效果...");
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
                                //System.Diagnostics.Debug.WriteLine($" [LoadImageToSplitRegion] 变色效果应用成功");
                                #endif
                                
                                return result;
                            }
                            else
                            {
                                #if DEBUG
                                //System.Diagnostics.Debug.WriteLine($" [LoadImageToSplitRegion] SKBitmap加载失败");
                                #endif
                            }
                        }
                        catch
                        {
                            #if DEBUG
                            //System.Diagnostics.Debug.WriteLine($" [LoadImageToSplitRegion] 应用变色效果失败");
                            #endif
                        }
                    }
                    else
                    {
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"[LoadImageToSplitRegion] 正常加载（无变色效果）");
                        #endif
                    }

                    // 正常加载（无变色效果）
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(imagePath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad; // 立即加载到内存
                    bitmap.EndInit();
                    bitmap.Freeze(); // 冻结到GPU显存，跨线程共享
                    return bitmap;
                });
                
                // 创建 Image 控件（显示模式由统一方法应用）
                var imageControl = new System.Windows.Controls.Image
                {
                    Source = bitmapSource,
                    Width = width,
                    Height = height,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    Tag = $"RegionImage_{_selectedRegionIndex}",
                    CacheMode = new BitmapCache // 启用GPU缓存，减少重复渲染
                    {
                        RenderAtScale = CalculateOptimalRenderScale()  // 动态计算渲染质量：自适应1080p/2K/4K投影屏
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
                //System.Diagnostics.Debug.WriteLine($"[LoadImageToSplitRegion] 保存变色状态: 区域{_selectedRegionIndex}, 需要变色={shouldApplyColorEffect}");
                //System.Diagnostics.Debug.WriteLine($"   当前所有区域变色状态: {string.Join(", ", _regionImageColorEffects.Select(kv => $"区域{kv.Key}={kv.Value}"))}");
                //#endif
                
                // 更新边框样式（有图片的区域显示黄色）
                border.Stroke = new SolidColorBrush(WpfColor.FromRgb(255, 215, 0)); // 金色
                
                // 应用当前全局显示模式，并同步按钮显示
                ApplySplitImageDisplayModeToRegion(imageControl, _selectedRegionIndex);
                UpdateStretchModeButton();
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [LoadImageToSplitRegion] 图片已加载到区域 {_selectedRegionIndex}");
                //#endif
                
                // 保存分割配置到数据库
                await SaveSplitConfigAsync();
                
                MarkContentAsModified();
                
                // 自动切换到下一个区域（无论是否已有图片）
                AutoSelectNextRegion();
            }
            catch
            {
                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [LoadImageToSplitRegion] 失败");
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
            //System.Diagnostics.Debug.WriteLine($" [AutoSelectNextRegion] 自动切换到区域 {nextIndex}");
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
            //System.Diagnostics.Debug.WriteLine($" [ClearSelectedRegionImage] 开始清空区域图片");
            //System.Diagnostics.Debug.WriteLine($"   _selectedRegionIndex: {_selectedRegionIndex}");
            //System.Diagnostics.Debug.WriteLine($"   包含图片: {_regionImages.ContainsKey(_selectedRegionIndex)}");
            //#endif
            
            if (_selectedRegionIndex < 0 || !_regionImages.ContainsKey(_selectedRegionIndex))
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [ClearSelectedRegionImage] 条件不满足，退出");
                //#endif
                return;
            }
                
            try
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [ClearSelectedRegionImage] 开始移除图片控件");
                //#endif
                
                // 移除图片控件
                var imageControl = _regionImages[_selectedRegionIndex];
                EditorCanvas.Children.Remove(imageControl);
                _regionImages.Remove(_selectedRegionIndex);
                _regionImagePaths.Remove(_selectedRegionIndex);
                _regionImageColorEffects.Remove(_selectedRegionIndex); // 同时清除变色效果记录
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [ClearSelectedRegionImage] 图片控件已移除");
                //#endif
                
                // 保持边框选中状态（绿色），不改变分割状态
                // 边框和分割线保持不变，只是清空了图片内容
                
                // 保存到数据库
                await SaveSplitConfigAsync();
                MarkContentAsModified();
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [ClearSelectedRegionImage] 已保存到数据库");
                //#endif
                
                ShowStatus($"已清空区域 {_selectedRegionIndex + 1} 的图片");
            }
            catch (Exception ex)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [ClearSelectedRegionImage] 失败: {ex.Message}");
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
                
                ShowStatus($"已清空所有区域的图片");
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
                Header = " 清空当前区域",
                Height = 36
            };
            clearCurrentItem.Click += async (s, e) => await ClearSelectedRegionImage();
            contextMenu.Items.Add(clearCurrentItem);
            
            // 选项2：清空所有区域
            var clearAllItem = new MenuItem
            {
                Header = " 清空所有区域",
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

                // 未变化时跳过数据库写入，减少切换链路开销
                if (string.Equals(_currentSlide.SplitRegionsData, json, StringComparison.Ordinal))
                {
                    return;
                }
                 
                // 更新数据库
                var slideToUpdate = await _textProjectService.GetSlideByIdAsync(_currentSlide.Id);
                if (slideToUpdate != null)
                {
                    slideToUpdate.SplitRegionsData = json;
                    slideToUpdate.ModifiedTime = DateTime.Now;
                    await _textProjectService.UpdateSlideAsync(slideToUpdate);
                    
                    // 更新本地缓存
                    _currentSlide.SplitRegionsData = json;
                    
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"[SaveSplitConfig] 已保存 {regionDataList.Count} 个区域配置");
                    //#endif
                }
            }
            catch
            {
                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [SaveSplitConfig] 失败");
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
                        
                        // 改为投影样式：细实线（使用统一常量）
                        child.StrokeThickness = SPLIT_LINE_THICKNESS_PROJECTION;
                        child.StrokeDashArray = null; // 实线
                    }
                }
                
                // 隐藏所有区域边框
                foreach (var border in _splitRegionBorders)
                {
                    border.Visibility = Visibility.Collapsed;
                }
                
                // 隐藏未加载图片的区域的序号标签
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
                
                //System.Diagnostics.Debug.WriteLine($"[投影] 已调整分割线为细线，隐藏边框和空白区域标签");
            }
            catch
            {
                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [HideSplitLinesForProjection] 失败");
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
                
                // 恢复所有区域序号标签（包括未加载图片的）
                var labels = EditorCanvas.Children.OfType<System.Windows.Controls.Border>()
                    .Where(b => b.Tag != null && b.Tag.ToString().StartsWith("RegionLabel_"))
                    .ToList();
                
                foreach (var label in labels)
                {
                    label.Visibility = Visibility.Visible;
                }
                
                //System.Diagnostics.Debug.WriteLine($"[投影] 已恢复分割线、边框和标签");
            }
            catch
            {
                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [RestoreSplitLinesAfterProjection] 失败");
                #endif
            }
        }
        
        /// <summary>
        /// 恢复分割配置
        /// </summary>
        private async Task RestoreSplitConfigAsync(Slide slide)
        {
            try
            {
                // 恢复显示模式
                _splitImageDisplayMode = slide.SplitStretchMode;
                UpdateStretchModeButton();
                
                // 检查是否有分割模式（-1 表示无分割模式）
                if (slide.SplitMode < 0)
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($" [RestoreSplitConfig] 无分割模式，清空分割区域");
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
                    //System.Diagnostics.Debug.WriteLine($" [RestoreSplitConfig] 分割模式={splitMode}，但无区域数据");
                    //#endif
                    return;
                }
                
                // 反序列化区域数据
                var regionDataList = JsonSerializer.Deserialize<List<Database.Models.DTOs.SplitRegionData>>(slide.SplitRegionsData);
                if (regionDataList == null || regionDataList.Count == 0)
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($" [RestoreSplitConfig] 反序列化失败或数据为空");
                    //#endif
                    return;
                }
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [RestoreSplitConfig] 开始恢复 {regionDataList.Count} 个区域");
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
                        //System.Diagnostics.Debug.WriteLine($" [RestoreSplitConfig] 区域 {regionData.RegionIndex} 图片不存在: {regionData.ImagePath}");
                        //#endif
                        continue;
                    }
                    
                    if (regionData.RegionIndex >= _splitRegionBorders.Count)
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($" [RestoreSplitConfig] 区域索引超出范围: {regionData.RegionIndex}");
                        //#endif
                        continue;
                    }
                    
                    // 检查图片是否来自原图标记或变色标记的文件夹
                    bool shouldUseStretch = false;
                    bool shouldApplyColorEffect = false;
                    try
                    {
                        var mediaFile = await _textProjectService.GetMediaFileByPathAsync(regionData.ImagePath);
                        
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"[RestoreSplitConfig] 区域 {regionData.RegionIndex} 检查图片: {System.IO.Path.GetFileName(regionData.ImagePath)}");
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

                            // 检查文件夹是否有变色标记
                            shouldApplyColorEffect = DatabaseManagerService.HasFolderAutoColorEffect(mediaFile.FolderId.Value);

                            #if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"   原图标记: {shouldUseStretch}");
                            //System.Diagnostics.Debug.WriteLine($"   变色标记: {shouldApplyColorEffect}");
                            //if (shouldUseStretch)
                            //{
                            //    //System.Diagnostics.Debug.WriteLine($"[RestoreSplitConfig] 区域 {regionData.RegionIndex} 来自原图标记文件夹，使用拉伸模式");
                            //}
                            //if (shouldApplyColorEffect)
                            //{
                            //    //System.Diagnostics.Debug.WriteLine($"[RestoreSplitConfig] 区域 {regionData.RegionIndex} 来自变色标记文件夹，应用变色效果");
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
                        //System.Diagnostics.Debug.WriteLine($" [RestoreSplitConfig] 检查标记失败");
                        #endif
                    }
                    
                    // 获取区域边框信息
                    var border = _splitRegionBorders[regionData.RegionIndex];
                    double x = Canvas.GetLeft(border);
                    double y = Canvas.GetTop(border);
                    double width = border.Width;
                    double height = border.Height;
                    
                    // 使用优化的图片加载（GPU加速 + 缓存）
                    BitmapSource bitmap;

                    // 如果需要应用变色效果，使用 SkiaSharp 加载并处理
                    if (shouldApplyColorEffect)
                    {
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"[RestoreSplitConfig] 区域 {regionData.RegionIndex} 开始应用变色效果...");
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
                                //System.Diagnostics.Debug.WriteLine($" [RestoreSplitConfig] 区域 {regionData.RegionIndex} 变色效果应用成功");
                                #endif
                            }
                            else
                            {
                                #if DEBUG
                                //System.Diagnostics.Debug.WriteLine($" [RestoreSplitConfig] SKBitmap加载失败");
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
                            //System.Diagnostics.Debug.WriteLine($" [RestoreSplitConfig] 应用变色效果失败");
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
                        //System.Diagnostics.Debug.WriteLine($"[RestoreSplitConfig] 区域 {regionData.RegionIndex} 正常加载（无变色效果）");
                        #endif
                        
                        // 正常加载（无变色效果）
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(regionData.ImagePath);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        bmp.Freeze(); // 冻结到GPU显存
                        bitmap = bmp;
                    }
                    
                    // 创建 Image 控件（显示模式由统一方法应用）
                    var imageControl = new System.Windows.Controls.Image
                    {
                        Source = bitmap,
                        Width = width,
                        Height = height,
                        Stretch = System.Windows.Media.Stretch.Uniform,
                        Tag = $"RegionImage_{regionData.RegionIndex}",
                        CacheMode = new BitmapCache // 启用GPU缓存
                        {
                            RenderAtScale = CalculateOptimalRenderScale()  // 动态计算渲染质量：自适应1080p/2K/4K投影屏
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
                    ApplySplitImageDisplayModeToRegion(imageControl, regionData.RegionIndex);
                    
                    // 更新边框样式（有图片的区域显示金色）
                    border.Stroke = new SolidColorBrush(WpfColor.FromRgb(255, 215, 0));
                    
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($" [RestoreSplitConfig] 已恢复区域 {regionData.RegionIndex}: {System.IO.Path.GetFileName(regionData.ImagePath)}");
                    //#endif
                }
                
                // 最终同步按钮显示
                UpdateStretchModeButton();
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [RestoreSplitConfig] 分割配置恢复完成");
                //#endif
            }
            catch
            {
                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [RestoreSplitConfig] 失败");
                #endif
            }
        }

        #endregion

    }
}
