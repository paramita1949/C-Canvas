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
using ImageColorChanger.Services.TextEditor.Models;
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
    /// MainWindow TextEditor Helper Methods
    /// </summary>
    public partial class MainWindow
    {
        #region 辅助方法


        /// <summary>
        /// 将文本框添加到画布
        /// </summary>
        private void AddTextBoxToCanvas(DraggableTextBox textBox)
        {
            _textBoxes.Add(textBox);
            EditorCanvas.Children.Add(textBox);

            // 监听选中事件
            textBox.SelectionChanged += (s, isSelected) =>
            {
                _textBoxEditSessionService?.SetSelected(textBox.Data.Id, isSelected);

                if (isSelected)
                {
                    // 取消其他文本框的选中状态
                    foreach (var tb in _textBoxes)
                    {
                        if (tb != textBox && tb.IsSelected)
                        {
                            tb.SetSelected(false);
                        }
                    }
                    _selectedTextBox = textBox;

                    // 更新工具栏状态
                    UpdateToolbarFromSelection();
                    
                    // 显示浮动工具栏
                    ShowTextBoxFloatingToolbar(textBox);
                }
                else
                {
                    // 取消选中时隐藏浮动工具栏
                    if (BibleToolbar != null)
                    {
                        BibleToolbar.IsOpen = false;
                    }
                }
            };

            textBox.EditModeChanged += (s, isEditing) =>
            {
                _textBoxEditSessionService?.SetEditing(textBox.Data.Id, isEditing);
            };

            // 监听内容变化，保存按钮变绿色
            textBox.ContentChanged += (s, content) =>
            {
                MarkContentAsModified();
                //System.Diagnostics.Debug.WriteLine($"文本内容改变: {content}");
            };
            
            // 监听位置变化，显示辅助线并保存
            textBox.PositionChanged += (s, pos) =>
            {
                UpdateAlignmentGuides(textBox);
                MarkContentAsModified();
            };
            
            // 监听拖动结束，隐藏辅助线
            textBox.DragEnded += (s, e) =>
            {
                HideAlignmentGuides();
            };
            
            // 监听尺寸变化，保存按钮变绿色
            textBox.SizeChanged += (s, size) =>
            {
                MarkContentAsModified();
            };
            
            // 监听删除请求（右键菜单或DEL键）
            textBox.RequestDelete += async (s, e) =>
            {
                await DeleteTextBoxAsync(textBox);
            };

            // 监听复制请求（右键菜单 - 复制到缓冲区）
            textBox.RequestCopy += async (s, e) =>
            {
                await CopyTextBoxToClipboardAsync(textBox);
            };

            // 监听粘贴请求（右键菜单 - 从缓冲区粘贴）
            textBox.RequestPaste += async (s, e) =>
            {
                await PasteTextBoxFromClipboardAsync(textBox);
            };

            //  监听文本选择改变事件（更新工具栏按钮状态）
            textBox.TextSelectionChanged += (s, e) =>
            {
                if (_selectedTextBox == textBox)
                {
                    UpdateToolbarButtonStatesFromSelection();
                }
            };
        }

        private List<TextBoxSnapshot> CaptureTextBoxSnapshotsForSave(IEnumerable<DraggableTextBox> sourceTextBoxes = null)
        {
            var textBoxes = (sourceTextBoxes ?? _textBoxes).Where(tb => tb != null).ToList();
            var snapshots = new List<TextBoxSnapshot>(textBoxes.Count);
            foreach (var textBox in textBoxes)
            {
                snapshots.Add(textBox.CaptureSnapshotForSave());
            }

            return snapshots;
        }

        private async Task PersistTextElementsAsync(IEnumerable<DraggableTextBox> sourceTextBoxes = null)
        {
            if (_textElementPersistenceService == null)
            {
                throw new InvalidOperationException("文本持久化服务未初始化");
            }

            var snapshots = CaptureTextBoxSnapshotsForSave(sourceTextBoxes);
            await _textElementPersistenceService.SaveAsync(snapshots);
        }

        /// <summary>
        /// 标记内容已修改（保存按钮变绿）
        /// </summary>
        private void MarkContentAsModified()
        {
            if (BtnSaveTextProject.Background is SolidColorBrush brush && brush.Color == Colors.LightGreen)
                return; // 已经是绿色，不重复设置

            BtnSaveTextProject.Background = new SolidColorBrush(Colors.LightGreen);
            //System.Diagnostics.Debug.WriteLine("内容已修改，保存按钮变绿");
        }

        /// <summary>
        ///  根据选中文字的实际样式更新工具栏按钮状态
        /// </summary>
        private void UpdateToolbarButtonStatesFromSelection()
        {
            if (_selectedTextBox == null) return;

            // 更新加粗按钮状态（使用选中文字的实际样式）
            UpdateBoldButtonState(_selectedTextBox.IsSelectionBold());

            // 更新斜体按钮状态（使用选中文字的实际样式）
            UpdateItalicButtonState(_selectedTextBox.IsSelectionItalic());

            // 更新下划线按钮状态（使用选中文字的实际样式）
            UpdateUnderlineButtonState(_selectedTextBox.IsSelectionUnderline());
        }

        /// <summary>
        /// 根据选中的文本框更新工具栏状态
        /// </summary>
        private void UpdateToolbarFromSelection()
        {
            if (_selectedTextBox == null) return;

            // 更新字体选择器
            var fontFamily = _selectedTextBox.Data.FontFamily;
            // //System.Diagnostics.Debug.WriteLine($"同步字体选择器: {fontFamily}");
            
            for (int i = 0; i < FontFamilySelector.Items.Count; i++)
            {
                var item = FontFamilySelector.Items[i] as ComboBoxItem;
                if (item?.Tag is FontItemData fontData)
                {
                    // 匹配字体：可能是完整URI，也可能是简单的字体族名称
                    var fontSource = fontData.FontFamily.Source;
                    
                    // 情况1：完全匹配（新格式：完整URI）
                    if (fontSource == fontFamily)
                    {
                        FontFamilySelector.SelectedIndex = i;
                        //System.Diagnostics.Debug.WriteLine($" 找到匹配字体（完整URI）: {fontData.Config.Name}");
                        break;
                    }
                    
                    // 情况2：旧数据格式匹配（只有字体族名称）
                    if (fontData.Config.Family == fontFamily)
                    {
                        FontFamilySelector.SelectedIndex = i;
                        //System.Diagnostics.Debug.WriteLine($" 找到匹配字体（族名称）: {fontData.Config.Name}");
                        
                        // 自动修复：更新文本框的字体为完整URI
                        _selectedTextBox.Data.FontFamily = fontSource;
                        //System.Diagnostics.Debug.WriteLine($"自动修复字体URI: {fontSource}");
                        break;
                    }
                }
            }

            // 更新字号选择框
            FontSizeSelector.Text = ((int)Math.Round(_selectedTextBox.Data.FontSize)).ToString();

            // 保持用户最后一次设置的颜色
            if (string.IsNullOrEmpty(_currentTextColor))
            {
                _currentTextColor = _selectedTextBox.Data.FontColor;
            }

            // 更新加粗按钮状态
            UpdateBoldButtonState(_selectedTextBox.Data.IsBoldBool);

            // 更新下划线按钮状态
            UpdateUnderlineButtonState(_selectedTextBox.Data.IsUnderlineBool);

            // 更新斜体按钮状态
            UpdateItalicButtonState(_selectedTextBox.Data.IsItalicBool);
        }
        
        /// <summary>
        /// 更新加粗按钮状态
        /// </summary>
        private void UpdateBoldButtonState(bool isBold)
        {
            var themeBrush = System.Windows.Application.Current?.Resources["BrushGlobalIcon"] as SolidColorBrush;
            var activeBrush = themeBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243));

            if (isBold)
            {
                // 加粗状态：按钮背景使用当前主题色
                BtnBold.Background = activeBrush;
                BtnBold.Foreground = new SolidColorBrush(Colors.White);
            }
            else
            {
                // 非加粗状态：恢复默认样式
                BtnBold.Background = new SolidColorBrush(Colors.White);
                BtnBold.Foreground = activeBrush;
            }
        }

        /// <summary>
        /// 更新下划线按钮状态
        /// </summary>
        private void UpdateUnderlineButtonState(bool isUnderline)
        {
            if (BtnFloatingUnderline == null) return;
            var themeBrush = System.Windows.Application.Current?.Resources["BrushGlobalIcon"] as SolidColorBrush;
            var activeBrush = themeBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243));

            if (isUnderline)
            {
                // 下划线状态：按钮背景使用当前主题色
                BtnFloatingUnderline.Background = activeBrush;
                BtnFloatingUnderline.Foreground = new SolidColorBrush(Colors.White);
            }
            else
            {
                // 非下划线状态：恢复默认样式（透明背景）
                BtnFloatingUnderline.Background = new SolidColorBrush(Colors.Transparent);
                BtnFloatingUnderline.Foreground = activeBrush;
            }
        }

        /// <summary>
        /// 更新斜体按钮状态
        /// </summary>
        private void UpdateItalicButtonState(bool isItalic)
        {
            if (BtnFloatingItalic == null) return;
            var themeBrush = System.Windows.Application.Current?.Resources["BrushGlobalIcon"] as SolidColorBrush;
            var activeBrush = themeBrush ?? new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243));

            if (isItalic)
            {
                // 斜体状态：按钮背景使用当前主题色
                BtnFloatingItalic.Background = activeBrush;
                BtnFloatingItalic.Foreground = new SolidColorBrush(Colors.White);
            }
            else
            {
                // 非斜体状态：恢复默认样式（透明背景）
                BtnFloatingItalic.Background = new SolidColorBrush(Colors.Transparent);
                BtnFloatingItalic.Foreground = activeBrush;
            }
        }
        
        /// <summary>
        /// 查找可视化树中的子元素
        /// </summary>
        private T FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (parent == null) return null;

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                if (child is T typedChild && (string.IsNullOrEmpty(name) || typedChild.Name == name))
                {
                    return typedChild;
                }
                
                var result = FindVisualChild<T>(child, name);
                if (result != null)
                    return result;
            }
            
            return null;
        }

        /// <summary>
        /// 生成Canvas渲染缓存键（基于所有区域图片路径、文本框内容、背景色和背景图）
        /// </summary>
        private string GenerateCanvasCacheKey()
        {
            // 图片路径部分
            var imagePart = string.Join("|", _regionImagePaths.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"));

            // 文本框内容部分（包括内容、位置、尺寸、样式等所有影响渲染的属性）
            var textPart = string.Join("|", _textBoxes.Select(tb =>
                $"{tb.Data.Content}_{tb.Data.X}_{tb.Data.Y}_{tb.Data.Width}_{tb.Data.Height}" +
                $"_{tb.Data.FontSize}_{tb.Data.FontFamily}_{tb.Data.FontColor}_{tb.Data.IsBold}_{tb.Data.IsItalic}_{tb.Data.IsUnderline}" +
                $"_{tb.Data.TextAlign}_{tb.Data.ZIndex}" +
                $"_{tb.Data.BorderColor}_{tb.Data.BorderWidth}_{tb.Data.BorderRadius}_{tb.Data.BorderOpacity}" +
                $"_{tb.Data.BackgroundColor}_{tb.Data.BackgroundRadius}_{tb.Data.BackgroundOpacity}" +
                $"_{tb.Data.LineSpacing}_{tb.Data.LetterSpacing}"));

            // 背景色和背景图部分（确保背景变化时缓存失效）
            var bgColor = _currentSlide?.BackgroundColor ?? "";
            var bgImage = _currentSlide?.BackgroundImagePath ?? "";

            return $"{imagePart}#{textPart}#{_currentSlide?.SplitMode}#{_splitStretchMode}#{bgColor}#{bgImage}";
        }
        
        /// <summary>
        /// 清除Canvas渲染缓存（在Canvas内容发生变化时调用）
        /// </summary>
        private void ClearCanvasRenderCache()
        {
            _lastCanvasRenderCache?.Dispose();
            _lastCanvasRenderCache = null;
            _lastCanvasCacheKey = "";
            
            #if DEBUG
            //System.Diagnostics.Debug.WriteLine($" [缓存] Canvas渲染缓存已清除");
            #endif
        }
        
        /// <summary>
        /// 从Canvas更新投影（核心投影功能）
        /// 优化：添加缓存机制和节流控制
        ///  新增：支持视频背景的 VisualBrush 镜像投影
        /// 新增：添加淡入淡出过渡动画
        /// </summary>
        private void UpdateProjectionFromCanvas()
        {
#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"[更新投影] ===== 开始更新投影 =====");
            //System.Diagnostics.Debug.WriteLine($"[更新投影] 投影状态: {(_projectionManager.IsProjectionActive ? "已开启" : "未开启")}");
            //System.Diagnostics.Debug.WriteLine($"[更新投影] 文本框数量: {_textBoxes.Count}");
            //System.Diagnostics.Debug.WriteLine($" [更新投影] 视频背景: {(_currentSlide?.VideoBackgroundEnabled == true ? "已启用" : "未启用")}");
#endif

            if (!_projectionManager.IsProjectionActive)
            {
#if DEBUG
                //System.Diagnostics.Debug.WriteLine(" [更新投影] 投影未开启，无法更新投影内容");
#endif
                WpfMessageBox.Show("请先开启投影！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 投影屏幕淡入淡出动画
            FadeOutAndUpdateProjection();
        }

        /// <summary>
        /// 投影屏幕淡出并更新（根据动画设置决定是否使用动画）
        /// </summary>
        private void FadeOutAndUpdateProjection()
        {
            if (!_projectionManager.IsProjectionActive)
                return;

            // 如果动画未启用，直接更新（无动画）
            if (!_projectionAnimationEnabled)
            {
                UpdateProjectionContent();
                return;
            }

            // 获取投影窗口的容器（用于动画）
            var projectionContainer = _projectionManager.GetProjectionContainer();
            if (projectionContainer == null)
            {
                // 如果无法获取容器，直接更新（无动画）
                UpdateProjectionContent();
                return;
            }

            // 淡入淡出动画
            DoubleAnimation fadeOutAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = _projectionAnimationOpacity,
                Duration = TimeSpan.FromMilliseconds(_projectionAnimationDuration / 2),  // 淡出时间占一半
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            // 淡出完成后更新内容并淡入
            fadeOutAnimation.Completed += (s, e) =>
            {
                // 更新投影内容
                UpdateProjectionContent();
                
                // 淡入新内容
                FadeInProjection(projectionContainer);
            };

            // 开始淡出动画
            projectionContainer.BeginAnimation(UIElement.OpacityProperty, fadeOutAnimation);
        }

        /// <summary>
        /// 更新投影内容（实际更新逻辑）
        /// </summary>
        private void UpdateProjectionContent()
        {
            //  检查是否有视频背景
            bool hasVideoBackground = _currentSlide?.VideoBackgroundEnabled == true 
                                     && !string.IsNullOrEmpty(_currentSlide?.BackgroundImagePath);
            
            if (_isProjectionLocked)
            {
                //  锁定模式：使用独立的 MediaElement
                if (hasVideoBackground)
                {
                    // 主屏幕有视频：更新投影的独立 MediaElement
                    var (projWidth, projHeight) = _projectionManager?.GetCurrentProjectionPhysicalSize() ?? (1920, 1080);
                    var textLayer = ComposeCanvasWithSkia(projWidth, projHeight, transparentBackground: true);
                    _projectionManager.UpdateProjectionWithLockedVideo(
                        _currentSlide.BackgroundImagePath,
                        _currentSlide.VideoLoopEnabled,
                        textLayer);
                }
                else
                {
                    // 主屏幕没有视频：清理投影的独立 MediaElement，使用静态背景模式
                    _projectionManager.ClearLockedVideo();
                    UpdateProjectionWithStaticBackground();
                }
            }
            else
            {
                //  未锁定模式：使用 VisualBrush（镜像主屏幕）
                if (hasVideoBackground)
                {
                    //  视频背景模式：使用 VisualBrush 镜像投影
                    UpdateProjectionWithVideoBackground();
                }
                else
                {
                    // 普通模式：使用 SkiaSharp 渲染投影
                    UpdateProjectionWithStaticBackground();
                }
            }
        }

        /// <summary>
        /// 投影屏幕淡入动画
        /// </summary>
        private void FadeInProjection(UIElement projectionContainer)
        {
            if (projectionContainer == null)
                return;

            // 如果动画未启用，直接显示（无动画）
            if (!_projectionAnimationEnabled)
            {
                projectionContainer.Opacity = 1.0;
                // 清除之前的动画
                projectionContainer.BeginAnimation(UIElement.OpacityProperty, null);
                return;
            }

            // 设置初始透明度为设置的透明度值（淡入效果）
            projectionContainer.Opacity = _projectionAnimationOpacity;

            // 创建淡入动画
            DoubleAnimation fadeInAnimation = new DoubleAnimation
            {
                From = _projectionAnimationOpacity,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(_projectionAnimationDuration / 2),  // 淡入时间占一半
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            // 开始动画
            projectionContainer.BeginAnimation(UIElement.OpacityProperty, fadeInAnimation);
        }

        /// <summary>
        ///  使用 VisualBrush 更新视频背景投影
        /// </summary>
        private void UpdateProjectionWithVideoBackground()
        {
#if DEBUG
            var startTime = System.Diagnostics.Stopwatch.StartNew();
            //System.Diagnostics.Debug.WriteLine($" [视频投影] ===== 开始更新视频投影 =====");
            //System.Diagnostics.Debug.WriteLine($" [视频投影] 使用 VisualBrush 镜像模式");
#endif

            // 查找编辑器 Canvas 中的 MediaElement
            var editorMedia = EditorCanvas.Children.OfType<MediaElement>().FirstOrDefault();
            if (editorMedia == null)
            {
#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [视频投影] 未找到编辑器中的 MediaElement，回退到静态模式");
#endif
                UpdateProjectionWithStaticBackground();
                return;
            }

#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"[视频投影] 找到 MediaElement: {editorMedia.Source}");
            //System.Diagnostics.Debug.WriteLine($"[视频投影] MediaElement 状态: Position={editorMedia.Position}, NaturalDuration={editorMedia.NaturalDuration}");
#endif

            // 创建 VisualBrush 镜像
            var visualBrush = new VisualBrush(editorMedia)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };
            
            // 设置 VisualBrush 的缓存模式为 BitmapCache 以提高性能
            // 注意：BitmapCache 可能导致视频不更新，所以只在需要时启用
            // visualBrush.Visual.CacheMode = new BitmapCache();

#if DEBUG
            var brushTime = startTime.ElapsedMilliseconds;
            //System.Diagnostics.Debug.WriteLine($" [视频投影] 已创建 VisualBrush (耗时: {brushTime} ms)");
            //System.Diagnostics.Debug.WriteLine($"[视频投影] VisualBrush.Visual: {visualBrush.Visual?.GetType().Name}");
            //System.Diagnostics.Debug.WriteLine($"[视频投影] MediaElement.IsLoaded: {editorMedia.IsLoaded}, IsVisible: {editorMedia.IsVisible}");
#endif

            //  获取投影窗口尺寸
            var (projWidth, projHeight) = _projectionManager?.GetCurrentProjectionPhysicalSize() ?? (1920, 1080);

#if DEBUG
            //System.Diagnostics.Debug.WriteLine($" [视频投影] 投影窗口尺寸: {projWidth}x{projHeight}");
#endif

            // 渲染文本层（透明背景）
#if DEBUG
            var textLayerStartTime = System.Diagnostics.Stopwatch.StartNew();
#endif
            var textLayer = ComposeCanvasWithSkia(projWidth, projHeight, transparentBackground: true);
#if DEBUG
            textLayerStartTime.Stop();
            //System.Diagnostics.Debug.WriteLine($"[视频投影] 文本层渲染完成 (耗时: {textLayerStartTime.ElapsedMilliseconds} ms)");
#endif

            // 更新投影（传递 VisualBrush 和文本层）
#if DEBUG
            var updateStartTime = System.Diagnostics.Stopwatch.StartNew();
#endif
            _projectionManager.UpdateProjectionWithVideo(visualBrush, textLayer);
#if DEBUG
            updateStartTime.Stop();
            //System.Diagnostics.Debug.WriteLine($"[视频投影] ProjectionManager.UpdateProjectionWithVideo 完成 (耗时: {updateStartTime.ElapsedMilliseconds} ms)");
#endif

#if DEBUG
            startTime.Stop();
            //System.Diagnostics.Debug.WriteLine($" [视频投影] 投影已更新");
            //System.Diagnostics.Debug.WriteLine($"⏱ [视频投影] 总耗时: {startTime.ElapsedMilliseconds} ms");
            //System.Diagnostics.Debug.WriteLine($" [视频投影] ===== 更新完成 =====\n");
#endif
        }

        /// <summary>
        /// 使用 SkiaSharp 更新静态背景投影（原有逻辑）
        /// </summary>
        private void UpdateProjectionWithStaticBackground()
        {
#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"[静态投影] 使用 SkiaSharp 渲染模式");
#endif

            // 渲染节流 - 避免过于频繁的更新
            var now = DateTime.Now;
            if ((now - _lastCanvasUpdateTime).TotalMilliseconds < CanvasUpdateThrottleMs)
            {
#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [静态投影] 节流跳过 (距上次 {(now - _lastCanvasUpdateTime).TotalMilliseconds:F0}ms)");
#endif
                return;
            }
            _lastCanvasUpdateTime = now;
            
            // 缓存检查 - 如果Canvas内容没变，直接复用上次的渲染结果
            string cacheKey = GenerateCanvasCacheKey();
#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"[静态投影-缓存] 缓存键相同: {cacheKey == _lastCanvasCacheKey}, 缓存存在: {_lastCanvasRenderCache != null}");
#endif
            if (cacheKey == _lastCanvasCacheKey && _lastCanvasRenderCache != null)
            {
#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [静态投影] 缓存命中，直接复用旧渲染结果");
#endif
                _projectionManager.UpdateProjectionText(_lastCanvasRenderCache);
                return;
            }

#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"[静态投影] 缓存未命中，开始完整渲染");
#endif

            // 保存辅助线的可见性状态
            var guidesVisibility = AlignmentGuidesCanvas.Visibility;
            
            try
            {
                // 渲染前：隐藏辅助线，避免被渲染到投影中
                AlignmentGuidesCanvas.Visibility = Visibility.Collapsed;
                
                // 渲染前：隐藏分割线和边框，避免被渲染到投影中
                HideSplitLinesForProjection();
                
                // 渲染前：隐藏所有文本框的装饰元素（边框、拖拽手柄等）
                foreach (var textBox in _textBoxes)
                {
                    textBox.HideDecorations();
                }
                
                // 1. 渲染EditorCanvasContainer（只包含Canvas和背景图，不包含辅助线）
                if (EditorCanvasContainer == null)
                {
                    return;
                }
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[Canvas信息] 尺寸: {EditorCanvas.ActualWidth}×{EditorCanvas.ActualHeight}");
                //System.Diagnostics.Debug.WriteLine($"[Canvas信息] 子元素数量: {EditorCanvas.Children.Count}");
                //System.Diagnostics.Debug.WriteLine($"[Canvas信息] 区域图片: {_regionImages.Count}");
                //System.Diagnostics.Debug.WriteLine($"[Canvas信息] 文本框: {_textBoxes.Count}");
                //#endif
                
                // 新方案：直接用SkiaSharp合成Canvas，完全跳过RenderTargetBitmap！
                // 2. 直接按投影物理像素分辨率合成Canvas内容（最高质量，避免二次缩放）
                //#if DEBUG
                //var composeSw = System.Diagnostics.Stopwatch.StartNew();
                //#endif
                
                // 使用物理像素分辨率（而非WPF单位），获得最高质量
                var (projWidth, projHeight) = _projectionManager?.GetCurrentProjectionPhysicalSize() ?? (1920, 1080);
                var finalImage = ComposeCanvasWithSkia(projWidth, projHeight);
                
                //#if DEBUG
                //composeSw.Stop();
                //System.Diagnostics.Debug.WriteLine($"⏱ [性能] ComposeCanvasWithSkia (物理像素分辨率): {composeSw.ElapsedMilliseconds}ms ({finalImage.Width}×{finalImage.Height})");
                //#endif

                // 4. 更新投影（使用专用的文字投影方法，语义清晰）
                //#if DEBUG
                //var updateSw = System.Diagnostics.Stopwatch.StartNew();
                //#endif
                
                _projectionManager.UpdateProjectionText(finalImage);
                
                //#if DEBUG
                //updateSw.Stop();
                //System.Diagnostics.Debug.WriteLine($"⏱ [性能] UpdateProjectionText: {updateSw.ElapsedMilliseconds}ms");
                //#endif

                // 保存渲染结果到缓存
                _lastCanvasRenderCache?.Dispose(); // 释放旧缓存
                _lastCanvasRenderCache = finalImage;
                _lastCanvasCacheKey = cacheKey;

#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [静态投影] 渲染完成");
#endif
            }
            catch (Exception ex)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [更新投影] 更新投影失败: {ex.Message}");
                //#endif
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [更新投影] 堆栈: {ex.StackTrace}");
                //#endif
                WpfMessageBox.Show($"更新投影失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // 渲染后：恢复所有文本框的装饰元素
                foreach (var textBox in _textBoxes)
                {
                    textBox.RestoreDecorations();
                }
                
                // 确保恢复辅助线的可见性（无论成功还是失败）
                AlignmentGuidesCanvas.Visibility = guidesVisibility;
                //System.Diagnostics.Debug.WriteLine($"[更新投影] 已恢复辅助线状态");
                
                // 恢复分割线和边框显示
                RestoreSplitLinesAfterProjection();
                
                //System.Diagnostics.Debug.WriteLine($"[更新投影] ===== 更新投影结束 =====");
            }
        }

        /// <summary>
        /// 使用SkiaSharp直接合成Canvas内容（跳过WPF的RenderTargetBitmap）
        /// 核心优化：直接访问Image控件的Source，避免WPF渲染管道
        ///  新增：支持透明背景（用于视频背景的文本层）
        /// </summary>
        /// <param name="targetWidth">目标宽度（0表示使用Canvas实际宽度）</param>
        /// <param name="targetHeight">目标高度（0表示使用Canvas实际高度）</param>
        /// <param name="transparentBackground">是否使用透明背景（true=透明，false=使用幻灯片背景色）</param>
        private SKBitmap ComposeCanvasWithSkia(int targetWidth = 0, int targetHeight = 0, bool transparentBackground = false)
        {
            // 编辑器画布的实际尺寸（用于计算缩放比例）
            double canvasWidth = EditorCanvas.ActualWidth;
            double canvasHeight = EditorCanvas.ActualHeight;
            
            // 如果没有指定目标尺寸，使用Canvas实际尺寸
            if (targetWidth <= 0) targetWidth = (int)canvasWidth;
            if (targetHeight <= 0) targetHeight = (int)canvasHeight;
            
            // 计算缩放比例
            double scaleX = targetWidth / canvasWidth;
            double scaleY = targetHeight / canvasHeight;

            // 保存缩放比例到字段
            _currentCanvasScaleX = scaleX;
            _currentCanvasScaleY = scaleY;

//#if DEBUG
//            //System.Diagnostics.Debug.WriteLine($" [画布缩放] 原始={canvasWidth}×{canvasHeight}, 目标={targetWidth}×{targetHeight}, 缩放={scaleX:F2}×{scaleY:F2}");
//#endif

            // 优先使用GPU表面，如果GPU不可用则降级到CPU
            SKBitmap bitmap = null;
            SKCanvas canvas = null;
            SKSurface surface = null;
            
            try
            {
                // 尝试创建GPU表面
                var gpuContext = _gpuContext;
                if (gpuContext.IsGpuAvailable && gpuContext.GetContext() != null)
                {
                    var grContext = gpuContext.GetContext();
                    var info = new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
                    surface = SKSurface.Create(grContext, false, info);
                    
                    if (surface != null)
                    {
                        canvas = surface.Canvas;
                        // GPU表面创建成功，使用GPU渲染
                    }
                }
            }
            catch
            {
                // GPU表面创建失败，降级到CPU
            }
            
            // 如果GPU表面创建失败，使用CPU表面
            if (canvas == null)
            {
                bitmap = new SKBitmap(targetWidth, targetHeight);
                canvas = new SKCanvas(bitmap);
            }
            
            try
            {
                // 根据参数选择背景色
                SKColor backgroundColor = SKColors.Transparent; // 默认透明
                
                if (!transparentBackground)
                {
                    // 使用幻灯片设置的背景色
                    backgroundColor = SKColors.Black; // 默认黑色
                    if (_currentSlide != null && !string.IsNullOrEmpty(_currentSlide.BackgroundColor))
                    {
                        try
                        {
                            // 解析十六进制颜色（如 #FFFFFF）
                            string hexColor = _currentSlide.BackgroundColor.TrimStart('#');
                            if (hexColor.Length == 6)
                            {
                                byte r = Convert.ToByte(hexColor.Substring(0, 2), 16);
                                byte g = Convert.ToByte(hexColor.Substring(2, 2), 16);
                                byte b = Convert.ToByte(hexColor.Substring(4, 2), 16);
                                backgroundColor = new SKColor(r, g, b);

                                //#if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"  [Compose] 背景色: {_currentSlide.BackgroundColor} -> RGB({r},{g},{b})");
                                //#endif
                            }
                        }
                        catch
                        {
                            // 解析失败，使用默认黑色
                        }
                    }
                }

                canvas.Clear(backgroundColor);

                // 应用缩放变换（X 和 Y 方向可以不同，铺满整个投影屏幕）
                // 注意：文本框使用 WPF 原生渲染，已经是最终视觉效果，缩放不会影响行间距
                canvas.Scale((float)scaleX, (float)scaleY);
                
                //#if DEBUG
                //createSw.Stop();
                //System.Diagnostics.Debug.WriteLine($"  [Compose] 创建画布: {createSw.ElapsedMilliseconds}ms");
                //#endif
                
                // 绘制背景图（如果有）
                if (_currentSlide != null && !string.IsNullOrEmpty(_currentSlide.BackgroundImagePath) &&
                    System.IO.File.Exists(_currentSlide.BackgroundImagePath))
                {
                    try
                    {
                        //#if DEBUG
                        //var bgSw = System.Diagnostics.Stopwatch.StartNew();
                        //#endif
                        
                        // 加载背景图
                        var bgBitmap = SKBitmap.Decode(_currentSlide.BackgroundImagePath);
                        if (bgBitmap != null)
                        {
                            // 绘制背景图，铺满整个画布（使用原始尺寸，canvas.Scale 会自动缩放）
                            var destRect = new SKRect(0, 0, (float)canvasWidth, (float)canvasHeight);
                            // 使用 DrawImage 替代 DrawBitmap，支持 SKSamplingOptions
                            using var image = SKImage.FromBitmap(bgBitmap);
                            var paint = new SKPaint
                            {
                                IsAntialias = true
                            };
                            var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                            // 使用 DrawImage(SKImage, SKRect, SKSamplingOptions, SKPaint) 重载
                            canvas.DrawImage(image, destRect, sampling, paint);
                            paint.Dispose();
                            bgBitmap.Dispose();
                            
                            //#if DEBUG
                            //bgSw.Stop();
                            //System.Diagnostics.Debug.WriteLine($"  [Compose] 背景图绘制: {_currentSlide.BackgroundImagePath}, 耗时: {bgSw.ElapsedMilliseconds}ms");
                            //#endif
                        }
                    }
                    catch
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"  [Compose] 背景图加载失败: {ex.Message}");
                        //#endif
                    }
                }
                
                // 绘制所有区域图片
                foreach (var kvp in _regionImages)
                {
                    var imageControl = kvp.Value;
                    int regionIndex = kvp.Key;
                    
                    //#if DEBUG
                    //var imgSw = System.Diagnostics.Stopwatch.StartNew();
                    //#endif
                    
                    // 关键修复：获取Image控件的位置和边框的尺寸
                    // Image控件的ActualWidth/Height在Uniform模式下会小于设置的Width/Height
                    // 应该使用边框的尺寸作为控件区域，才能正确计算居中位置
                    double left = Canvas.GetLeft(imageControl);
                    double top = Canvas.GetTop(imageControl);
                    double width = _splitRegionBorders[regionIndex].Width;  // 使用边框宽度，不是Image的ActualWidth
                    double height = _splitRegionBorders[regionIndex].Height; // 使用边框高度，不是Image的ActualHeight
                    
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"[Compose] 区域 {regionIndex} - Image控件位置: ({left}, {top}), 尺寸: {width}×{height}, Stretch: {imageControl.Stretch}");
                    //System.Diagnostics.Debug.WriteLine($"    边框信息: 位置=({Canvas.GetLeft(_splitRegionBorders[regionIndex])}, {Canvas.GetTop(_splitRegionBorders[regionIndex])}), 尺寸={_splitRegionBorders[regionIndex].Width}×{_splitRegionBorders[regionIndex].Height}");
                    //#endif
                    
                    SKBitmap skBitmap = null;
                    
                    // 优先从原始文件加载高质量图片
                    if (_regionImagePaths.ContainsKey(regionIndex) && 
                        System.IO.File.Exists(_regionImagePaths[regionIndex]))
                    {
                        try
                        {
                            string imagePath = _regionImagePaths[regionIndex];
                            skBitmap = SKBitmap.Decode(imagePath);
                            
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"[Compose] 区域 {regionIndex} - 原始图片尺寸: {skBitmap.Width}×{skBitmap.Height}");
                            //#endif
                            
                            // 检查是否需要应用变色效果
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"[Compose] 区域 {regionIndex} - 检查变色状态");
                            //System.Diagnostics.Debug.WriteLine($"   _regionImageColorEffects.ContainsKey: {_regionImageColorEffects.ContainsKey(regionIndex)}");
                            //if (_regionImageColorEffects.ContainsKey(regionIndex))
                            //{
                            //    //System.Diagnostics.Debug.WriteLine($"   需要变色: {_regionImageColorEffects[regionIndex]}");
                            //}
                            //#endif
                            
                            if (_regionImageColorEffects.ContainsKey(regionIndex) && 
                                _regionImageColorEffects[regionIndex])
                            {
                                //#if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"[Compose] 区域 {regionIndex} - 开始应用变色效果到投影");
                                //#endif
                                
                                // 应用变色效果
                                _imageProcessor.ApplyYellowTextEffect(skBitmap);
                                
                                //#if DEBUG
                                //System.Diagnostics.Debug.WriteLine($" [Compose] 区域 {regionIndex} - 变色效果已应用");
                                //#endif
                            }
                            else
                            {
                                //#if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"⚪ [Compose] 区域 {regionIndex} - 无需变色效果");
                                //#endif
                            }
                            
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"  [Compose] 处理图片 {regionIndex}: 从原始文件加载 {skBitmap.Width}×{skBitmap.Height}, 位置: ({left}, {top}), 显示: {width}×{height}");
                            //#endif
                        }
                        catch
                        {
                            // 加载失败，回退到BitmapSource
                            skBitmap = null;
                        }
                    }
                    
                    // 回退方案：从WPF控件的BitmapSource转换
                    if (skBitmap == null && imageControl?.Source is BitmapSource bitmapSource)
                    {
                        skBitmap = ConvertBitmapSourceToSKBitmap(bitmapSource);
                        
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"  [Compose] 处理图片 {regionIndex}: 从BitmapSource转换 {bitmapSource.PixelWidth}×{bitmapSource.PixelHeight}, 位置: ({left}, {top}), 显示: {width}×{height}");
                        //#endif
                    }
                    
                    if (skBitmap != null)
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"  [Compose] 加载耗时: {imgSw.ElapsedMilliseconds}ms");
                        //imgSw.Restart();
                        //#endif
                        
                        // 关键修复：根据Image控件的Stretch属性计算实际绘制区域
                        SKRect destRect;
                        if (imageControl.Stretch == System.Windows.Media.Stretch.Uniform)
                        {
                            // Uniform模式：保持比例，居中显示
                            double imageAspect = (double)skBitmap.Width / skBitmap.Height;
                            double controlAspect = width / height;
                            
                            double drawWidth, drawHeight;
                            double drawLeft, drawTop;
                            
                            double aspectDiff = Math.Abs(imageAspect - controlAspect);
                            if (aspectDiff < 0.001)
                            {
                                // 宽高比几乎相等：填满整个控件区域
                                drawWidth = width;
                                drawHeight = height;
                                drawLeft = left;
                                drawTop = top;
                            }
                            else if (imageAspect > controlAspect)
                            {
                                // 图片更宽（更扁），以宽度为准，垂直居中
                                drawWidth = width;
                                drawHeight = width / imageAspect;
                                drawLeft = left;
                                drawTop = top + (height - drawHeight) / 2; // 垂直居中
                            }
                            else
                            {
                                // 图片更高（更瘦），以高度为准，水平居中
                                drawHeight = height;
                                drawWidth = height * imageAspect;
                                drawLeft = left + (width - drawWidth) / 2; // 水平居中
                                drawTop = top + (height - drawHeight) / 2; // 垂直也居中！
                            }
                            
                            destRect = new SKRect((float)drawLeft, (float)drawTop,
                                                   (float)(drawLeft + drawWidth), (float)(drawTop + drawHeight));

                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"[Compose] 区域 {regionIndex} - Uniform模式计算:");
                            //System.Diagnostics.Debug.WriteLine($"    图片宽高比: {imageAspect:F3}, 控件宽高比: {controlAspect:F3}");
                            //System.Diagnostics.Debug.WriteLine($"    绘制位置: ({drawLeft:F1}, {drawTop:F1}), 绘制尺寸: {drawWidth:F1}×{drawHeight:F1}");
                            //System.Diagnostics.Debug.WriteLine($"    destRect: Left={destRect.Left:F1}, Top={destRect.Top:F1}, Right={destRect.Right:F1}, Bottom={destRect.Bottom:F1}");
                            //#endif
                        }
                        else
                        {
                            // Fill模式：拉伸填满整个控件区域
                            destRect = new SKRect((float)left, (float)top,
                                                   (float)(left + width), (float)(top + height));
                            
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"[Compose] 区域 {regionIndex} - Fill模式: 直接填满控件区域");
                            //#endif
                        }
                        
                        // 使用高质量过滤模式，确保投影质量
                        // 使用 DrawImage 替代 DrawBitmap，支持 SKSamplingOptions
                        using var image = SKImage.FromBitmap(skBitmap);
                        var paint = new SKPaint
                        {
                            IsAntialias = true
                        };
                        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                        // 使用 DrawImage(SKImage, SKRect, SKSamplingOptions, SKPaint) 重载
                        canvas.DrawImage(image, destRect, sampling, paint);
                        paint.Dispose();
                        
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"  [Compose] 绘制耗时: {imgSw.ElapsedMilliseconds}ms");
                        //#endif
                        
                        skBitmap.Dispose();
                    }
                }
                
                // 绘制所有文本框
                foreach (var textBox in _textBoxes)
                {
                    //#if DEBUG
                    //var textSw = System.Diagnostics.Stopwatch.StartNew();
                    //System.Diagnostics.Debug.WriteLine($"  [Compose] 文本框位置: ({textBox.Data.X}, {textBox.Data.Y}), 尺寸: {textBox.Data.Width}×{textBox.Data.Height}");
                    //#endif
                    
                    DrawTextBoxToCanvas(canvas, textBox);
                    
                    //#if DEBUG
                    //textSw.Stop();
                    //System.Diagnostics.Debug.WriteLine($"  [Compose] 绘制文本框: {textSw.ElapsedMilliseconds}ms");
                    //#endif
                }
                
                // 绘制分割线（如果有分割模式）
                if (_currentSlide != null && _currentSlide.SplitMode >= 0)
                {
                    DrawSplitLinesToCanvas(canvas, (Database.Models.Enums.ViewSplitMode)_currentSlide.SplitMode, canvasWidth, canvasHeight);
                }
                
                // 如果是GPU表面，需要刷新并获取快照
                if (surface != null)
                {
                    canvas.Flush();
                    var snapshot = surface.Snapshot();
                    bitmap = SKBitmap.FromImage(snapshot);
                }
            }
            finally
            {
                // 清理资源
                if (surface != null)
                {
                    surface.Dispose();
                }
                if (canvas != null && bitmap != null && surface == null)
                {
                    // 只有CPU表面才需要释放canvas（GPU表面的canvas会随surface一起释放）
                    canvas.Dispose();
                }
            }
            
            return bitmap;
        }
        
        /// <summary>
        /// 在SkiaSharp画布上绘制分割线和角标（匹配投影样式：细实线）
        /// </summary>
        private void DrawSplitLinesToCanvas(SKCanvas canvas, Database.Models.Enums.ViewSplitMode mode, double canvasWidth, double canvasHeight)
        {
            // 分割线画笔（使用统一常量，投影屏幕使用细实线）
            var linePaint = new SKPaint
            {
                Color = new SKColor(SPLIT_LINE_COLOR_R, SPLIT_LINE_COLOR_G, SPLIT_LINE_COLOR_B), // 使用统一常量（红色）
                StrokeWidth = (float)SPLIT_LINE_THICKNESS_PROJECTION, // 投影屏幕使用细线
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
                // 投影屏幕使用实线（不使用虚线）
            };
            
            // 角标背景画笔（半透明橙色）
            var labelBgPaint = new SKPaint
            {
                Color = new SKColor(255, 102, 0, 200), // ARGB(200, 255, 102, 0)
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            
            // 角标字体（白色粗体）
            // 使用统一常量，与主屏幕保持一致
            using var labelFont = new SKFont
            {
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
                Size = (float)REGION_LABEL_FONT_SIZE,
                Subpixel = true
            };
            
            // 角标文字画笔（白色）
            var labelTextPaint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true
            };
            
            switch (mode)
            {
                case Database.Models.Enums.ViewSplitMode.Single:
                    // 单画面：不绘制分割线和角标
                    break;
                    
                case Database.Models.Enums.ViewSplitMode.Horizontal:
                    // 左右分割：绘制竖线
                    canvas.DrawLine((float)(canvasWidth / 2), 0, (float)(canvasWidth / 2), (float)canvasHeight, linePaint);
                    // 角标（只显示已加载图片的区域）
                    if (_regionImages.ContainsKey(0)) DrawLabel(canvas, "1", 0, 0, labelBgPaint, labelFont, labelTextPaint);
                    if (_regionImages.ContainsKey(1)) DrawLabel(canvas, "2", (float)(canvasWidth / 2), 0, labelBgPaint, labelFont, labelTextPaint);
                    break;
                    
                case Database.Models.Enums.ViewSplitMode.Vertical:
                    // 上下分割：绘制横线
                    canvas.DrawLine(0, (float)(canvasHeight / 2), (float)canvasWidth, (float)(canvasHeight / 2), linePaint);
                    // 角标（只显示已加载图片的区域）
                    if (_regionImages.ContainsKey(0)) DrawLabel(canvas, "1", 0, 0, labelBgPaint, labelFont, labelTextPaint);
                    if (_regionImages.ContainsKey(1)) DrawLabel(canvas, "2", 0, (float)(canvasHeight / 2), labelBgPaint, labelFont, labelTextPaint);
                    break;
                    
                case Database.Models.Enums.ViewSplitMode.Quad:
                    // 四宫格：绘制十字线
                    canvas.DrawLine((float)(canvasWidth / 2), 0, (float)(canvasWidth / 2), (float)canvasHeight, linePaint);
                    canvas.DrawLine(0, (float)(canvasHeight / 2), (float)canvasWidth, (float)(canvasHeight / 2), linePaint);
                    // 角标（只显示已加载图片的区域）
                    if (_regionImages.ContainsKey(0)) DrawLabel(canvas, "1", 0, 0, labelBgPaint, labelFont, labelTextPaint);
                    if (_regionImages.ContainsKey(1)) DrawLabel(canvas, "2", (float)(canvasWidth / 2), 0, labelBgPaint, labelFont, labelTextPaint);
                    if (_regionImages.ContainsKey(2)) DrawLabel(canvas, "3", 0, (float)(canvasHeight / 2), labelBgPaint, labelFont, labelTextPaint);
                    if (_regionImages.ContainsKey(3)) DrawLabel(canvas, "4", (float)(canvasWidth / 2), (float)(canvasHeight / 2), labelBgPaint, labelFont, labelTextPaint);
                    break;
                    
                case Database.Models.Enums.ViewSplitMode.TripleSplit:
                    // 三分割：左边上下分割，右边整个
                    canvas.DrawLine((float)(canvasWidth / 2), 0, (float)(canvasWidth / 2), (float)canvasHeight, linePaint);
                    canvas.DrawLine(0, (float)(canvasHeight / 2), (float)(canvasWidth / 2), (float)(canvasHeight / 2), linePaint);
                    // 角标（只显示已加载图片的区域）
                    if (_regionImages.ContainsKey(0)) DrawLabel(canvas, "1", 0, 0, labelBgPaint, labelFont, labelTextPaint);
                    if (_regionImages.ContainsKey(1)) DrawLabel(canvas, "2", 0, (float)(canvasHeight / 2), labelBgPaint, labelFont, labelTextPaint);
                    if (_regionImages.ContainsKey(2)) DrawLabel(canvas, "3", (float)(canvasWidth / 2), 0, labelBgPaint, labelFont, labelTextPaint);
                    break;
            }
            
            linePaint.Dispose();
            labelBgPaint.Dispose();
            labelTextPaint.Dispose();
        }
        
        /// <summary>
        /// 绘制角标（带圆角背景的数字标签）
        /// </summary>
        private void DrawLabel(SKCanvas canvas, string text, float x, float y, SKPaint bgPaint, SKFont font, SKPaint textPaint)
        {
            // 测量文本尺寸
            float textWidth = font.MeasureText(text);
            var fontMetrics = font.Metrics;
            float textHeight = fontMetrics.Descent - fontMetrics.Ascent;
            
            // 使用统一常量，与主屏幕保持一致
            float paddingX = (float)REGION_LABEL_PADDING_X;  // 左右padding
            float paddingY = (float)REGION_LABEL_PADDING_Y;  // 上下padding
            float labelWidth = textWidth + paddingX * 2;
            float labelHeight = textHeight + paddingY * 2;
            
            // 绘制圆角矩形背景（右下圆角）
            var path = new SKPath();
            var rect = new SKRect(x, y, x + labelWidth, y + labelHeight);
            float cornerRadius = (float)REGION_LABEL_CORNER_RADIUS;
            
            // 创建右下圆角的路径
            path.MoveTo(rect.Left, rect.Top);
            path.LineTo(rect.Right, rect.Top);
            path.LineTo(rect.Right, rect.Bottom - cornerRadius);
            path.ArcTo(new SKRect(rect.Right - cornerRadius, rect.Bottom - cornerRadius, rect.Right, rect.Bottom), 0, 90, false);
            path.LineTo(rect.Left, rect.Bottom);
            path.Close();
            
            canvas.DrawPath(path, bgPaint);
            path.Dispose();
            
            // 绘制文本（居中）
            float textX = x + paddingX;
            float textY = y + labelHeight - paddingY - fontMetrics.Descent; // 垂直居中
            canvas.DrawText(text, textX, textY, SKTextAlign.Left, font, textPaint);
        }
        
        /// <summary>
        /// 将文本框绘制到SkiaSharp画布上
        ///  使用 WPF 原生 RenderTargetBitmap 渲染 RichTextBox，确保行间距与主屏幕完全一致
        /// </summary>
        private void DrawTextBoxToCanvas(SKCanvas canvas, DraggableTextBox textBox)
        {
            var data = textBox.Data;

            // 获取文本框在Canvas上的实际位置（而不是Data中的值）
            double actualLeft = Canvas.GetLeft(textBox);
            double actualTop = Canvas.GetTop(textBox);
            double actualWidth = textBox.ActualWidth;
            double actualHeight = textBox.ActualHeight;

            // 处理NaN的情况
            if (double.IsNaN(actualLeft)) actualLeft = data.X;
            if (double.IsNaN(actualTop)) actualTop = data.Y;
            if (actualWidth <= 0) actualWidth = data.Width;
            if (actualHeight <= 0) actualHeight = data.Height;

            //  使用 WPF 原生渲染 RichTextBox（保证行间距与主屏幕完全一致）
            try
            {
                int width = (int)Math.Ceiling(actualWidth);
                int height = (int)Math.Ceiling(actualHeight);

                if (width > 0 && height > 0)
                {
//#if DEBUG
//                    //System.Diagnostics.Debug.WriteLine($"[投影渲染参数-WPF] 文本框ID={data.Id}");
//                    //System.Diagnostics.Debug.WriteLine($"  字体大小: {data.FontSize}");
//                    //System.Diagnostics.Debug.WriteLine($"  行间距: {data.LineSpacing}");
//                    //System.Diagnostics.Debug.WriteLine($"  文本框尺寸: {width}×{height}");
//                    //System.Diagnostics.Debug.WriteLine($"  实际位置: ({actualLeft}, {actualTop})");
//                    //System.Diagnostics.Debug.WriteLine($"  缩放比例: {_currentCanvasScaleX:F2}×{_currentCanvasScaleY:F2}");
//#endif

                    //  使用 WPF 原生方法渲染 RichTextBox，传入缩放比例以获得高清效果
                    var wpfBitmap = textBox.GetRenderedBitmap(_currentCanvasScaleX, _currentCanvasScaleY);

                    if (wpfBitmap != null)
                    {
                        // 转换 WPF BitmapSource 到 SkiaSharp SKBitmap
                        var skBitmap = ConvertBitmapSourceToSKBitmap(wpfBitmap);

                        if (skBitmap != null)
                        {
                            // 绘制到Canvas（使用原始位置和尺寸，canvas.Scale 会自动处理缩放）
                            var destRect = new SKRect(
                                (float)actualLeft,
                                (float)actualTop,
                                (float)(actualLeft + actualWidth),
                                (float)(actualTop + actualHeight));

                            // 使用高质量过滤模式，确保投影质量
                            // 使用 DrawImage 替代 DrawBitmap，支持 SKSamplingOptions
                            using var image = SKImage.FromBitmap(skBitmap);
                            var paint = new SKPaint
                            {
                                IsAntialias = true
                            };
                            var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                            // 使用 DrawImage(SKImage, SKRect, SKSamplingOptions, SKPaint) 重载
                            canvas.DrawImage(image, destRect, sampling, paint);
                            paint.Dispose();
                            skBitmap.Dispose();

//#if DEBUG
//                            //System.Diagnostics.Debug.WriteLine($" [文本绘制-WPF] 位置: ({actualLeft}, {actualTop}), 尺寸: {width}×{height}");
//#endif
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [文本绘制-WPF] 失败: {ex.Message}");
                //#else
                _ = ex;
                //#endif
            }
        }
        
        /// <summary>
        /// 直接用SkiaSharp绘制文字（支持加粗、对齐等样式）
        /// </summary>
        private void DrawTextDirectly(SKCanvas canvas, TextElement data, float x, float y, float width, float height)
        {
            // 解析颜色
            SKColor textColor = SKColors.White;
            try
            {
                string hexColor = data.FontColor.TrimStart('#');
                if (hexColor.Length == 6)
                {
                    byte r = Convert.ToByte(hexColor.Substring(0, 2), 16);
                    byte g = Convert.ToByte(hexColor.Substring(2, 2), 16);
                    byte b = Convert.ToByte(hexColor.Substring(4, 2), 16);
                    textColor = new SKColor(r, g, b);
                }
            }
            catch { }
            
            // 创建字体（支持PAK资源、文件路径和系统字体）
            SKTypeface typeface = null;
            try
            {
                // 字体路径格式：./CCanvas_Fonts/江西拙楷.ttf#江西拙楷
                string fontPath = data.FontFamily;
                
                // 如果是文件路径格式（包含#号分隔符），提取文件路径部分
                if (fontPath.Contains("#"))
                {
                    fontPath = fontPath.Split('#')[0];
                }
                
                // 检查是否是相对路径（从PAK加载）
                if (fontPath.StartsWith("./") || fontPath.StartsWith(".\\"))
                {
                    // 从PAK资源包加载字体
                    // 提取文件名（例如从 ./CCanvas_Fonts/江西拙楷.ttf 提取 江西拙楷.ttf）
                    string fileName = System.IO.Path.GetFileName(fontPath);
                    
                    #if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"  [字体] 尝试从PAK加载: 原始路径='{fontPath}', 文件名='{fileName}'");
                    #endif
                    
                    // 在PAK中搜索匹配的字体文件
                    string actualPakPath = null;
                    var allResources = _pakManager.GetAllResourcePaths();
                    foreach (var resourcePath in allResources)
                    {
                        if (System.IO.Path.GetFileName(resourcePath) == fileName && 
                            resourcePath.StartsWith("Fonts/"))
                        {
                            actualPakPath = resourcePath;
                            break;
                        }
                    }
                    
                    if (actualPakPath != null)
                    {
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"  [字体] 在PAK中找到: {actualPakPath}");
                        #endif
                        
                        var fontData = _pakManager.GetResource(actualPakPath);
                        
                        if (fontData != null)
                        {
                            #if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"  [字体] PAK数据获取成功: {fontData.Length} bytes");
                            #endif
                            
                            typeface = SKTypeface.FromData(SKData.CreateCopy(fontData));
                            
                            if (typeface != null)
                            {
                                #if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"  [字体] 从PAK加载成功: {actualPakPath}");
                                #endif
                            }
                            else
                            {
                                #if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"  [字体] SKTypeface创建失败，数据可能不是有效字体");
                                #endif
                            }
                        }
                    }
                    else
                    {
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"  [字体] PAK中未找到字体，尝试文件系统");
                        #endif
                        
                        // PAK中没有，尝试从文件系统加载
                        if (System.IO.File.Exists(fontPath))
                        {
                            typeface = SKTypeface.FromFile(fontPath);
                            
                            #if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"  [字体] 从文件加载: {fontPath}");
                            #endif
                        }
                        else
                        {
                            #if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"  [字体] 文件也不存在: {fontPath}");
                            #endif
                        }
                    }
                }
                else if (System.IO.Path.IsPathRooted(fontPath))
                {
                    // 绝对路径，从文件加载
                    if (System.IO.File.Exists(fontPath))
                    {
                        typeface = SKTypeface.FromFile(fontPath);
                        
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"  [字体] 从文件加载: {fontPath}");
                        #endif
                    }
                }
                
                // 如果字体加载失败，使用系统字体
                if (typeface == null)
                {
                    var fontStyle = data.IsBoldBool ? SKFontStyle.Bold : SKFontStyle.Normal;
                    typeface = SKTypeface.FromFamilyName(fontPath, fontStyle);
                    
                    #if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"  [字体] 使用系统字体: {fontPath}");
                    #endif
                }
            }
            catch (Exception)
            {
                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($"  [字体] 加载失败: {ex.Message}，使用默认字体");
                #endif
                // 加载失败，使用默认字体
                var fontStyle = data.IsBoldBool ? SKFontStyle.Bold : SKFontStyle.Normal;
                typeface = SKTypeface.FromFamilyName("Arial", fontStyle);
            }
            
            // 创建字体
            using var font = new SKFont
            {
                Typeface = typeface,
                Size = (float)data.FontSize,
                Subpixel = true
            };
            
            // 创建画笔
            using var paint = new SKPaint
            {
                Color = textColor,
                IsAntialias = true
            };
            
            // 处理文本对齐
            SKTextAlign textAlign = data.TextAlign switch
            {
                "Center" => SKTextAlign.Center,
                "Right" => SKTextAlign.Right,
                _ => SKTextAlign.Left
            };
            
            // 计算文本位置
            float textX = x;
            if (data.TextAlign == "Center")
            {
                textX = x + width / 2;
            }
            else if (data.TextAlign == "Right")
            {
                textX = x + width;
            }
            
            // 绘制文本（支持多行）
            string[] lines = data.Content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            float lineHeight = font.Spacing;
            
            // 正确计算第一行基线位置
            // 使用 FontMetrics 获取字体度量信息
            var fontMetrics = font.Metrics;
            float firstLineBaseline = y - fontMetrics.Ascent; // Ascent是负值，表示基线到顶部的距离
            float currentY = firstLineBaseline;
            
            #if DEBUG
            //System.Diagnostics.Debug.WriteLine($"  [文本绘制] 位置: ({textX}, {currentY}), 字号: {font.Size}, 行高: {lineHeight}, 对齐: {textAlign}");
            //System.Diagnostics.Debug.WriteLine($"  [文本绘制] 区域: x={x}, y={y}, w={width}, h={height}");
            //System.Diagnostics.Debug.WriteLine($"  [字体度量] Ascent: {fontMetrics.Ascent}, Descent: {fontMetrics.Descent}, Leading: {fontMetrics.Leading}");
            #endif
            
            foreach (string line in lines)
            {
                canvas.DrawText(line, textX, currentY, textAlign, font, paint);
                currentY += lineHeight;
            }
            
            typeface.Dispose();
        }
        
        #endregion

    }
}



