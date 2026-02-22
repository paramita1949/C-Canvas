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
    /// MainWindow TextEditor Slides (Management, Drag-Drop)
    /// </summary>
    public partial class MainWindow
    {
        #region 幻灯片管理

        /// <summary>
        /// 当前选中的幻灯片
        /// </summary>
        private Slide _currentSlide;
        private bool _isRevertingSlideSelection;

        private bool CanSwitchSlideWhileProjecting(bool showToast = true)
        {
            return true;
        }

        /// <summary>
        /// 判断文件是否为视频格式
        /// </summary>
        private bool IsVideoFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            var extension = System.IO.Path.GetExtension(filePath).ToLower();
            var videoExtensions = new[] { ".mp4", ".avi", ".wmv", ".mov", ".mkv", ".flv", ".webm", ".m4v" };
            return videoExtensions.Contains(extension);
        }

        /// <summary>
        /// 加载视频背景
        /// </summary>
        private void LoadVideoBackground(Slide slide)
        {
            try
            {
#if DEBUG
                var startTime = System.Diagnostics.Stopwatch.StartNew();
                //System.Diagnostics.Debug.WriteLine($" [视频加载] ===== 开始加载视频背景 =====");
                //System.Diagnostics.Debug.WriteLine($" [视频加载] 文件: {System.IO.Path.GetFileName(slide.BackgroundImagePath)}");
                //System.Diagnostics.Debug.WriteLine($" [视频加载] 投影状态: {(_projectionManager.IsProjectionActive ? "已开启" : "未开启")}");
#endif

                // 清除旧的背景
                EditorCanvas.Background = new SolidColorBrush(Colors.Black);
                var oldMediaElements = EditorCanvas.Children.OfType<MediaElement>().ToList();
                
#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [视频加载] 清除旧的 MediaElement 数量: {oldMediaElements.Count}");
#endif
                
                foreach (var old in oldMediaElements)
                {
                    old.Stop();
                    old.Close();
                    EditorCanvas.Children.Remove(old);
                }

                // 创建新的 MediaElement
                var mediaElement = new MediaElement
                {
                    Source = new Uri(slide.BackgroundImagePath, UriKind.Absolute),
                    LoadedBehavior = MediaState.Manual,
                    UnloadedBehavior = MediaState.Manual,
                    Stretch = Stretch.UniformToFill,  // 改为 UniformToFill，填充整个画布
                    Width = EditorCanvas.Width,       // 明确设置宽度
                    Height = EditorCanvas.Height,     // 明确设置高度
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    VerticalAlignment = System.Windows.VerticalAlignment.Top,
                    Volume = slide.VideoVolume,  // 使用数据库中的音量（默认0）
                    ScrubbingEnabled = true,
                    // 启用 GPU 硬件加速缓存
                    CacheMode = new BitmapCache
                    {
                        EnableClearType = false,  // 视频不需要ClearType
                        RenderAtScale = 1.0,      // 1080p适配，减少GPU内存占用
                        SnapsToDevicePixels = true
                    }
                };
                
                // 设置 GPU 渲染优化
                RenderOptions.SetBitmapScalingMode(mediaElement, BitmapScalingMode.LowQuality);  // 优先性能而非质量
                RenderOptions.SetCachingHint(mediaElement, CachingHint.Cache);  // 强制启用缓存
                
#if DEBUG
                //var cache = mediaElement.CacheMode as BitmapCache;
                //System.Diagnostics.Debug.WriteLine($"[视频GPU加速] BitmapCache 已启用: RenderAtScale={cache?.RenderAtScale ?? 0}");
                //System.Diagnostics.Debug.WriteLine($"[视频GPU加速] CachingHint: {RenderOptions.GetCachingHint(mediaElement)}");
                //System.Diagnostics.Debug.WriteLine($"[视频GPU加速] BitmapScalingMode: {RenderOptions.GetBitmapScalingMode(mediaElement)}");
#endif

                // 设置循环播放（根据数据库设置）
                UpdateVideoLoopBehavior(mediaElement, slide.VideoLoopEnabled);

                // 添加到 Canvas（设置位置为左上角）
                Canvas.SetLeft(mediaElement, 0);
                Canvas.SetTop(mediaElement, 0);
                Canvas.SetZIndex(mediaElement, -1);  // 设置为最底层，确保文本在上方
                EditorCanvas.Children.Insert(0, mediaElement);

#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [视频加载] MediaElement 已添加到 EditorCanvas (索引 0)");
#endif

                // 自动播放
                mediaElement.Play();

#if DEBUG
                startTime.Stop();
                //System.Diagnostics.Debug.WriteLine($" [视频加载] 加载成功: {System.IO.Path.GetFileName(slide.BackgroundImagePath)}");
                //System.Diagnostics.Debug.WriteLine($"   - 循环播放: {slide.VideoLoopEnabled}");
                //System.Diagnostics.Debug.WriteLine($"   - 音量: {slide.VideoVolume * 100}%");
                //System.Diagnostics.Debug.WriteLine($"⏱ [视频加载] 耗时: {startTime.ElapsedMilliseconds} ms");
                //System.Diagnostics.Debug.WriteLine($" [视频加载] ===== 加载完成 =====\n");
#endif
            }
            catch (Exception
#if DEBUG
                ex
#endif
            )
            {
#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [视频背景] 加载失败: {ex.Message}");
                _ = ex;
#endif
                // 失败时使用纯色背景
                EditorCanvas.Background = !string.IsNullOrEmpty(slide.BackgroundColor)
                    ? (SolidColorBrush)new BrushConverter().ConvertFrom(slide.BackgroundColor)
                    : new SolidColorBrush(Colors.Black);
            }
        }

        /// <summary>
        /// 幻灯片列表选择改变事件
        ///  切换幻灯片前先保存当前文本，防止文本丢失
        /// </summary>
        private void SlideListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isRevertingSlideSelection)
            {
                return;
            }

            if (SlideListBox.SelectedItem is Slide selectedSlide)
            {
                if (_currentSlide != null && selectedSlide.Id != _currentSlide.Id && !CanSwitchSlideWhileProjecting())
                {
                    _isRevertingSlideSelection = true;
                    try
                    {
                        SlideListBox.SelectedItem = _currentSlide;
                    }
                    finally
                    {
                        _isRevertingSlideSelection = false;
                    }
                    return;
                }

                //  切换幻灯片前，先保存当前编辑的文本（确保换行符等格式不丢失）
                // 先创建集合的副本，避免在遍历时集合被修改（LoadSlide 会清空 _textBoxes）
                var textBoxesCopy = _textBoxes.ToList();
                
                // 先同步所有文本框的内容到 Data.Content
                foreach (var textBox in textBoxesCopy)
                {
                    if (textBox.IsInEditMode)
                    {
                        // 如果文本框正在编辑，先退出编辑模式（会触发 SyncTextFromRichTextBox）
                        textBox.ExitEditMode();
                    }
                    else
                    {
                        // 如果不在编辑模式，手动同步一次（确保 Data.Content 是最新的）
                        textBox.SyncTextFromRichTextBox();
                    }
                }
                
                // 先提取 RichTextSpans（在 LoadSlide 之前，此时 UI 元素仍然有效）
                var richTextSpansDict = new Dictionary<int, List<Database.Models.RichTextSpan>>();
                foreach (var tb in textBoxesCopy)
                {
                    try
                    {
                        if (tb != null && tb.Data != null)
                        {
                            var richTextSpans = tb.ExtractRichTextSpansFromFlowDocument();
                            if (richTextSpans != null && richTextSpans.Count > 0)
                            {
                                richTextSpansDict[tb.Data.Id] = richTextSpans;
                            }
                        }
                    }
                    catch (Exception
#if DEBUG
                        ex
#endif
                    )
                    {
                        // 如果提取失败，记录但不阻止切换幻灯片
#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($" [切换幻灯片] 提取 RichTextSpans 失败 (ID={tb?.Data?.Id}): {ex.Message}");
                        _ = ex;
#endif
                    }
                }
                
                // 在切换前预解析相邻视频（确保切换时已准备好）
                if (_isProjectionLocked)
                {
                    _ = PreparseAdjacentVideosBeforeSwitchAsync(selectedSlide);
                }
                
                // 直接加载新幻灯片
                LoadSlide(selectedSlide);
                
                // 保存到数据库（在 UI 线程中异步执行，避免阻塞UI）
                // 使用 #pragma 抑制警告，因为这里是有意不等待的（fire-and-forget）
                //  注意：必须在 UI 线程中执行，因为 DbContext 不是线程安全的
#pragma warning disable CS4014
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        // 使用副本集合，避免集合被修改
                        // 注意：此时 LoadSlide 已经执行，_textBoxes 已被清空，但 textBoxesCopy 仍然有效
                        await _textProjectManager.UpdateElementsAsync(textBoxesCopy.Select(tb => tb.Data));
                        
                        // 同步 FlowDocument 到 RichTextSpans（使用之前提取的数据）
                        foreach (var kvp in richTextSpansDict)
                        {
                            await _textProjectManager.SaveRichTextSpansAsync(kvp.Key, kvp.Value);
                        }
#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($" [切换幻灯片] 文本已保存到数据库");
#endif
                    }
                    catch (ObjectDisposedException)
                    {
                        //  如果 DbContext 已被释放，忽略错误（程序可能正在关闭）
#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($" [切换幻灯片] DbContext 已被释放，跳过保存");
#endif
                    }
                    catch (InvalidOperationException)
                    {
                        //  如果 DbContext 操作失败（可能是线程安全问题），忽略错误
#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($" [切换幻灯片] DbContext 操作失败，跳过保存");
#endif
                    }
                    catch
                    {
#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($" [切换幻灯片] 保存文本失败");
#endif
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
#pragma warning restore CS4014
            }
        }

        /// <summary>
        /// 幻灯片列表键盘事件（处理DEL删除）
        /// </summary>
        private void SlideListBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // DEL键删除幻灯片
            if (e.Key == System.Windows.Input.Key.Delete)
            {
                if (SlideListBox.SelectedItem != null)
                {
                    BtnDeleteSlide_Click(sender, new RoutedEventArgs());
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// 幻灯片列表右键点击事件
        /// </summary>
        private void SlideListBox_RightClick(object sender, MouseButtonEventArgs e)
        {
            // 创建右键菜单
            var contextMenu = new ContextMenu();
            
            // 应用自定义样式
            contextMenu.Style = (Style)this.FindResource("NoBorderContextMenuStyle");

            // 新建幻灯片
            var addItem = new MenuItem 
            { 
                Header = "新建",
                FontSize = 14
            };
            addItem.Click += BtnAddSlide_Click;
            contextMenu.Items.Add(addItem);

            // 复制幻灯片
            var copyItem = new MenuItem 
            { 
                Header = "复制",
                FontSize = 14,
                IsEnabled = SlideListBox.SelectedItem != null
            };
            copyItem.Click += BtnCopySlide_Click;
            contextMenu.Items.Add(copyItem);

            // 删除幻灯片
            var deleteItem = new MenuItem 
            { 
                Header = "删除",
                FontSize = 14,
                IsEnabled = SlideListBox.SelectedItem != null
            };
            deleteItem.Click += BtnDeleteSlide_Click;
            contextMenu.Items.Add(deleteItem);


            contextMenu.PlacementTarget = sender as UIElement;
            contextMenu.IsOpen = true;
        }

        #region 幻灯片拖动排序

        private Slide _draggingSlide = null;
        private System.Windows.Point _slideDragStartPoint;

        /// <summary>
        /// 幻灯片列表鼠标按下事件（开始拖动）
        /// </summary>
        private void SlideListBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _slideDragStartPoint = e.GetPosition(null);
            
            // 获取点击的幻灯片
            var item = FindVisualAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
            if (item != null)
            {
                _draggingSlide = item.DataContext as Slide;
            }
        }

        /// <summary>
        /// 幻灯片列表鼠标移动事件（执行拖动）
        /// </summary>
        private void SlideListBox_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggingSlide != null)
            {
                System.Windows.Point currentPosition = e.GetPosition(null);
                Vector diff = _slideDragStartPoint - currentPosition;

                // 检查是否移动了足够的距离来开始拖动
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    // 开始拖放操作
                    System.Windows.DataObject dragData = new System.Windows.DataObject("Slide", _draggingSlide);
                    DragDrop.DoDragDrop(SlideListBox, dragData, System.Windows.DragDropEffects.Move);
                    _draggingSlide = null;
                }
            }
        }

        /// <summary>
        /// 幻灯片列表拖放over事件
        /// </summary>
        private void SlideListBox_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent("Slide"))
            {
                e.Effects = System.Windows.DragDropEffects.Move;
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.None;
            }
            e.Handled = true;
        }

        /// <summary>
        /// 幻灯片列表放下事件（完成排序）
        /// </summary>
        private void SlideListBox_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent("Slide"))
            {
                Slide sourceSlide = e.Data.GetData("Slide") as Slide;
                
                // 获取放下位置的幻灯片
                var targetItem = FindVisualAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
                if (targetItem != null)
                {
                    Slide targetSlide = targetItem.DataContext as Slide;
                    if (targetSlide != null && sourceSlide != targetSlide)
                    {
                        // 执行排序
                        ReorderSlides(sourceSlide, targetSlide);
                    }
                }
            }
        }

        /// <summary>
        /// 重新排序幻灯片
        /// </summary>
        private async void ReorderSlides(Slide sourceSlide, Slide targetSlide)
        {
            try
            {
                // 通过ID查找幻灯片，避免对象引用不一致的问题
                var slides = await _textProjectManager.GetSlidesByProjectAsync(_currentTextProject.Id);

                // 通过ID查找索引，而不是使用对象引用
                int sourceIndex = slides.FindIndex(s => s.Id == sourceSlide.Id);
                int targetIndex = slides.FindIndex(s => s.Id == targetSlide.Id);

                if (sourceIndex == -1 || targetIndex == -1)
                {
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($" [ReorderSlides] 无法找到幻灯片: sourceIndex={sourceIndex}, targetIndex={targetIndex}");
                    #endif
                    return;
                }

                // 移除源幻灯片
                var sourceSlideEntity = slides[sourceIndex];
                slides.RemoveAt(sourceIndex);
                
                // 插入到目标位置
                slides.Insert(targetIndex, sourceSlideEntity);

                // 更新所有幻灯片的SortOrder
                for (int i = 0; i < slides.Count; i++)
                {
                    slides[i].SortOrder = i;
                }

                await _textProjectManager.UpdateSlideSortOrdersAsync(slides);

                #if DEBUG
                System.Diagnostics.Debug.WriteLine($" [ReorderSlides] 排序已保存: 从位置{sourceIndex}移动到位置{targetIndex}");
                #endif

                // 刷新列表
                LoadSlideList();

                // 保持选中当前幻灯片（通过ID查找）
                var updatedSourceSlide = slides.FirstOrDefault(s => s.Id == sourceSlide.Id);
                if (updatedSourceSlide != null)
                {
                    SlideListBox.SelectedItem = updatedSourceSlide;
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($" [ReorderSlides] 排序失败: {ex.Message}");
                #endif
                WpfMessageBox.Show($"排序失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 查找可视树祖先元素（用于幻灯片拖动）
        /// </summary>
        private T FindVisualAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            do
            {
                if (current is T)
                {
                    return (T)current;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            while (current != null);
            return null;
        }

        #endregion

        /// <summary>
        /// 文本编辑器面板鼠标点击事件（只在点击编辑区域空白处时取消编辑框选中状态）
        /// </summary>
        private void TextEditorPanel_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // 只在文本编辑器可见时处理
            if (TextEditorPanel.Visibility != Visibility.Visible)
                return;

            //System.Diagnostics.Debug.WriteLine($"[TextEditorPanel_PreviewMouseDown] 开始处理");
            //System.Diagnostics.Debug.WriteLine($"   - OriginalSource: {e.OriginalSource?.GetType().Name}");

            // 检查点击是否在 DraggableTextBox 内部
            var clickedElement = e.OriginalSource as DependencyObject;
            while (clickedElement != null)
            {
                if (clickedElement is DraggableTextBox)
                {
                    //System.Diagnostics.Debug.WriteLine($"   -  点击在文本框内，不处理");
                    // 点击在文本框内部，不处理（让文本框自己处理）
                    return;
                }

                // 检查是否点击在工具栏、面板等控件上
                if (clickedElement is FrameworkElement fe)
                {
                    if (fe.Name?.Contains("Toolbar") == true ||
                        fe.Name?.Contains("Panel") == true ||
                        fe.Name?.Contains("Popup") == true ||
                        fe.Name?.Contains("Button") == true ||
                        fe.Name?.Contains("Border") == true)
                    {
                        //System.Diagnostics.Debug.WriteLine($"   -  点击在工具栏/按钮上: {fe.Name}，不处理");
                        return;
                    }
                }

                // 修复：只对 Visual 或 Visual3D 使用 VisualTreeHelper.GetParent
                // 对于非 Visual 元素（如 Run、Paragraph 等文档元素），使用 LogicalTreeHelper
                if (clickedElement is Visual || clickedElement is System.Windows.Media.Media3D.Visual3D)
                {
                    clickedElement = VisualTreeHelper.GetParent(clickedElement);
                }
                else
                {
                    clickedElement = LogicalTreeHelper.GetParent(clickedElement);
                }
            }

            // 只有当点击在EditorCanvas内的空白区域时才处理
            // 这个判断通过EditorCanvas_MouseDown事件来处理，这里不再重复处理
            //System.Diagnostics.Debug.WriteLine($"   -  点击在其他区域，交由EditorCanvas_MouseDown处理");
        }

        /// <summary>
        /// 文本编辑器面板键盘事件（处理PageUp/PageDown切换幻灯片）
        /// </summary>
        private void TextEditorPanel_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // 只在文本编辑器可见时处理
            if (TextEditorPanel.Visibility != Visibility.Visible)
                return;

            // PageUp: 切换到上一张幻灯片
            if (e.Key == System.Windows.Input.Key.PageUp)
            {
                NavigateToPreviousSlide();
                e.Handled = true; // 阻止事件冒泡，避免触发全局热键
                //System.Diagnostics.Debug.WriteLine(" 文本编辑器: PageUp 切换幻灯片");
            }
            // PageDown: 切换到下一张幻灯片
            else if (e.Key == System.Windows.Input.Key.PageDown)
            {
                NavigateToNextSlide();
                e.Handled = true; // 阻止事件冒泡，避免触发全局热键
                //System.Diagnostics.Debug.WriteLine(" 文本编辑器: PageDown 切换幻灯片");
            }
        }

        /// <summary>
        /// 切换到上一张幻灯片（循环：第一张时跳到最后一张）
        /// </summary>
        internal void NavigateToPreviousSlide()
        {
            if (!CanSwitchSlideWhileProjecting())
                return;

            if (SlideListBox.Items.Count == 0)
                return;

            int currentIndex = SlideListBox.SelectedIndex;
            if (currentIndex > 0)
            {
                // 不是第一张，切换到上一张
                SlideListBox.SelectedIndex = currentIndex - 1;
                //System.Diagnostics.Debug.WriteLine($"⬆ 切换到上一张幻灯片: Index={currentIndex - 1}");
            }
            else
            {
                // 第一张，循环到最后一张
                SlideListBox.SelectedIndex = SlideListBox.Items.Count - 1;
                //System.Diagnostics.Debug.WriteLine($" 循环到最后一张幻灯片: Index={SlideListBox.Items.Count - 1}");
            }

            // 自动滚动到当前幻灯片
            ScrollToCurrentSlide();
        }

        /// <summary>
        /// 切换到下一张幻灯片（循环：最后一张时回到第一张）
        /// </summary>
        internal void NavigateToNextSlide()
        {
            if (!CanSwitchSlideWhileProjecting())
                return;

            if (SlideListBox.Items.Count == 0)
                return;

            int currentIndex = SlideListBox.SelectedIndex;
            if (currentIndex < SlideListBox.Items.Count - 1)
            {
                // 不是最后一张，切换到下一张
                SlideListBox.SelectedIndex = currentIndex + 1;
                //System.Diagnostics.Debug.WriteLine($"⬇ 切换到下一张幻灯片: Index={currentIndex + 1}");
            }
            else
            {
                // 最后一张，循环回到第一张
                SlideListBox.SelectedIndex = 0;
                //System.Diagnostics.Debug.WriteLine($" 循环回到第一张幻灯片: Index=0");
            }

            // 自动滚动到当前幻灯片
            ScrollToCurrentSlide();
        }

        /// <summary>
        /// 滚动到当前选中的幻灯片
        /// </summary>
        private void ScrollToCurrentSlide()
        {
            if (SlideListBox.SelectedItem != null)
            {
                SlideListBox.ScrollIntoView(SlideListBox.SelectedItem);
            }
        }

        /// <summary>
        /// 幻灯片列表鼠标滚动事件（支持在幻灯片区域滚动）
        /// </summary>
        private void SlideScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            // 获取 ScrollViewer
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer == null)
                return;

            // 滚动幻灯片列表
            if (e.Delta > 0)
            {
                // 向上滚动
                scrollViewer.LineUp();
                scrollViewer.LineUp();
                scrollViewer.LineUp();
            }
            else
            {
                // 向下滚动
                scrollViewer.LineDown();
                scrollViewer.LineDown();
                scrollViewer.LineDown();
            }

            e.Handled = true;
        }

        /// <summary>
        /// 加载幻灯片内容到编辑器
        /// </summary>
        private void LoadSlide(Slide slide)
        {
            try
            {
                // [缩略图调试] 输出幻灯片信息和缩略图路径
                //System.Diagnostics.Debug.WriteLine($"\n========== [缩略图调试] ==========");
                //System.Diagnostics.Debug.WriteLine($" 幻灯片ID: {slide.Id}");
                //System.Diagnostics.Debug.WriteLine($" 幻灯片标题: {slide.Title}");
                //System.Diagnostics.Debug.WriteLine($" 排序顺序: {slide.SortOrder}");
                //System.Diagnostics.Debug.WriteLine($" 缩略图路径: {slide.ThumbnailPath ?? "无"}");
                //System.Diagnostics.Debug.WriteLine($" 主图路径: {slide.BackgroundImagePath ?? "无"}");
                //
                //if (!string.IsNullOrEmpty(slide.ThumbnailPath))
                //{
                //    //System.Diagnostics.Debug.WriteLine($" 缩略图文件名: {System.IO.Path.GetFileName(slide.ThumbnailPath)}");
                //    //System.Diagnostics.Debug.WriteLine($" 缩略图存在: {System.IO.File.Exists(slide.ThumbnailPath)}");
                //}
                //
                //if (!string.IsNullOrEmpty(slide.BackgroundImagePath))
                //{
                //    //System.Diagnostics.Debug.WriteLine($" 主图文件名: {System.IO.Path.GetFileName(slide.BackgroundImagePath)}");
                //    //System.Diagnostics.Debug.WriteLine($" 主图存在: {System.IO.File.Exists(slide.BackgroundImagePath)}");
                //}
                //System.Diagnostics.Debug.WriteLine($"====================================\n");

                _currentSlide = slide;

                // 清空画布
                ClearEditorCanvas();

                //  加载背景（支持视频背景）
                if (!string.IsNullOrEmpty(slide.BackgroundImagePath) &&
                    System.IO.File.Exists(slide.BackgroundImagePath))
                {
                    // 判断是视频还是图片
                    bool isVideo = slide.VideoBackgroundEnabled && IsVideoFile(slide.BackgroundImagePath);

#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"[LoadSlide] 背景路径: {System.IO.Path.GetFileName(slide.BackgroundImagePath)}");
                    //System.Diagnostics.Debug.WriteLine($"[LoadSlide] VideoBackgroundEnabled: {slide.VideoBackgroundEnabled}");
                    //System.Diagnostics.Debug.WriteLine($"[LoadSlide] IsVideoFile: {IsVideoFile(slide.BackgroundImagePath)}");
                    //System.Diagnostics.Debug.WriteLine($"[LoadSlide] isVideo: {isVideo}");
                    //System.Diagnostics.Debug.WriteLine($"[LoadSlide] _isProjectionLocked: {_isProjectionLocked}");
                    //System.Diagnostics.Debug.WriteLine($"[LoadSlide] IsProjectionActive: {_projectionManager?.IsProjectionActive ?? false}");
#endif

                    if (isVideo)
                    {
                        //  视频背景：主屏幕正常加载视频
                        //  锁定模式下，投影使用独立的 MediaElement，主屏幕也正常显示视频
                        // 这样主屏幕可以正常预览，投影独立播放
#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($" [LoadSlide] 加载视频背景");
                        //System.Diagnostics.Debug.WriteLine($"   - 视频路径: {slide.BackgroundImagePath}");
                        //System.Diagnostics.Debug.WriteLine($"   - 循环播放: {slide.VideoLoopEnabled}");
                        //System.Diagnostics.Debug.WriteLine($"   - 锁定状态: {_isProjectionLocked}");
#endif
                        LoadVideoBackground(slide);
                    }
                    else
                    {
                        // 图片背景
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(slide.BackgroundImagePath);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                        bitmap.EndInit();
                        bitmap.Freeze();

                        EditorCanvas.Background = new ImageBrush(bitmap)
                        {
                            Stretch = Stretch.Fill
                        };
                    }
                }
                else
                {
                    // 无背景图：设置Canvas背景色
                    if (!string.IsNullOrEmpty(slide.BackgroundColor))
                    {
                        EditorCanvas.Background = (SolidColorBrush)new BrushConverter().ConvertFrom(slide.BackgroundColor);
                    }
                    else
                    {
                        EditorCanvas.Background = new SolidColorBrush(Colors.White);
                    }
                }

                // 加载文本元素（包含富文本片段）
                var elements = _textProjectManager.GetElementsBySlideWithRichTextAsync(slide.Id).GetAwaiter().GetResult();

//#if DEBUG
//                int totalSpans = elements.Sum(e => e.RichTextSpans?.Count ?? 0);
//                //System.Diagnostics.Debug.WriteLine($"[加载幻灯片] ID={slide.Id}, Title={slide.Title}, Elements={elements.Count}, RichTextSpans={totalSpans}");
//#endif

                foreach (var element in elements)
                {
                    var textBox = new DraggableTextBox(element);

                    // 应用字体
                    var fontFamilyToApply = FindFontFamilyByName(element.FontFamily);
                    if (fontFamilyToApply != null)
                    {
                        textBox.ApplyFontFamily(fontFamilyToApply);
                    }

                    AddTextBoxToCanvas(textBox);
                }

                //System.Diagnostics.Debug.WriteLine($" 加载幻灯片成功: ID={slide.Id}, Title={slide.Title}, Elements={elements.Count}");
                
                // 恢复分割配置
                RestoreSplitConfig(slide);
                
                // 加载完成后，如果投影已开启且未锁定，自动更新投影
                if (_projectionManager.IsProjectionActive && !_isProjectionLocked)
                {
                    // 延迟一点点，确保UI渲染完成
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdateProjectionFromCanvas();
                        //System.Diagnostics.Debug.WriteLine(" 幻灯片加载后已自动更新投影");
                    }), System.Windows.Threading.DispatcherPriority.Render);
                }

                // 预解析下一个/上一个视频（异步，不阻塞）
                _ = PreparseAdjacentVideosAsync(slide);
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($" 加载幻灯片失败: {ex.Message}");
                WpfMessageBox.Show($"加载幻灯片失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        ///  已禁用：切换前预解析相邻视频（简化为跟主屏幕WPF一样的逻辑）
        /// </summary>
        private async System.Threading.Tasks.Task PreparseAdjacentVideosBeforeSwitchAsync(Slide targetSlide)
        {
            //  简化：去掉预解析，直接播放更简单稳定
            await System.Threading.Tasks.Task.CompletedTask;
            return;
        }

        /// <summary>
        ///  已禁用：预解析相邻幻灯片的视频（简化为跟主屏幕WPF一样的逻辑）
        /// </summary>
        private async System.Threading.Tasks.Task PreparseAdjacentVideosAsync(Slide currentSlide)
        {
            //  简化：去掉预解析，直接播放更简单稳定
            await System.Threading.Tasks.Task.CompletedTask;
            return;
        }


        /// <summary>
        /// 新建幻灯片按钮点击事件
        /// </summary>
        private async void BtnAddSlide_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTextProject == null)
                return;

            try
            {
                // 获取当前幻灯片总数（用于生成标题序号）
                var slideCount = await _textProjectManager.GetSlideCountAsync(_currentTextProject.Id);
                
                // 获取当前最大排序号（用于SortOrder）
                int maxOrder = await _textProjectManager.GetMaxSlideSortOrderAsync(_currentTextProject.Id);

                // 创建新幻灯片（标题序号 = 总数 + 1）
                var newSlide = new Slide
                {
                    ProjectId = _currentTextProject.Id,
                    Title = $"幻灯片 {slideCount + 1}",
                    SortOrder = maxOrder + 1,
                    BackgroundColor = "#000000",  // 默认黑色背景
                    SplitMode = -1,  // 默认无分割模式
                    SplitStretchMode = false  // 默认适中模式
                };

                await _textProjectManager.AddSlideAsync(newSlide);

                // 刷新幻灯片列表
                LoadSlideList();

                // 选中新建的幻灯片
                SlideListBox.SelectedItem = newSlide;

                //System.Diagnostics.Debug.WriteLine($" 新建幻灯片成功: ID={newSlide.Id}, Title={newSlide.Title}");
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($" 新建幻灯片失败: {ex.Message}");
                WpfMessageBox.Show($"新建幻灯片失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 复制幻灯片按钮点击事件
        /// </summary>
        private async void BtnCopySlide_Click(object sender, RoutedEventArgs e)
        {
            if (SlideListBox.SelectedItem is not Slide sourceSlide)
            {
                WpfMessageBox.Show("请先选择要复制的幻灯片", "提示", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // 加载源幻灯片的所有元素（包含富文本片段）
                var sourceElements = await _textProjectManager.GetElementsBySlideWithRichTextAsync(sourceSlide.Id);

                // 计算新的排序位置（在源幻灯片后面）
                int newSortOrder = sourceSlide.SortOrder + 1;
                
                // 将后面的幻灯片排序顺序都+1
                var slidesToUpdate = (await _textProjectManager.GetSlidesByProjectAsync(_currentTextProject.Id))
                    .Where(s => s.SortOrder >= newSortOrder)
                    .ToList();
                
                foreach (var slide in slidesToUpdate)
                {
                    slide.SortOrder++;
                }
                await _textProjectManager.UpdateSlideSortOrdersAsync(slidesToUpdate);

                // 创建新幻灯片（复制所有属性）
                var newSlide = new Slide
                {
                    ProjectId = _currentTextProject.Id,
                    Title = $"{sourceSlide.Title} (副本)",
                    SortOrder = newSortOrder,
                    BackgroundColor = sourceSlide.BackgroundColor,
                    BackgroundImagePath = sourceSlide.BackgroundImagePath,
                    SplitMode = sourceSlide.SplitMode,  // 复制分割模式
                    SplitStretchMode = sourceSlide.SplitStretchMode,  // 复制拉伸模式
                    SplitRegionsData = sourceSlide.SplitRegionsData  // 复制区域数据
                };

                await _textProjectManager.AddSlideAsync(newSlide);

                // 复制所有文本元素（使用 CloneElement 确保复制所有样式配置）
                foreach (var sourceElement in sourceElements)
                {
                    //  使用 CloneElement 方法复制所有样式属性
                    var newElement = _textProjectManager.CloneElement(sourceElement);
                    newElement.SlideId = newSlide.Id;  // 设置新的幻灯片ID
                    
                    await _textProjectManager.AddElementAsync(newElement);

                    // 复制富文本片段（如果有）
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
                    }
                }

                // 先加载新幻灯片内容并生成缩略图,再刷新列表(避免闪烁)
                await Dispatcher.InvokeAsync(async () =>
                {
                    // 临时选中新幻灯片(不触发SelectionChanged)
                    var previousIndex = SlideListBox.SelectedIndex;
                    SlideListBox.SelectionChanged -= SlideListBox_SelectionChanged;
                    
                    // 手动加载新幻灯片内容
                    LoadSlide(newSlide);
                    
                    // 等待UI完全渲染
                    await Task.Delay(150);
                    
                    // 生成新幻灯片的缩略图
                    var thumbnailPath = SaveSlideThumbnail(newSlide.Id);
                    if (!string.IsNullOrEmpty(thumbnailPath))
                    {
                        newSlide.ThumbnailPath = thumbnailPath;
                    }
                    
                    // 恢复事件监听
                    SlideListBox.SelectionChanged += SlideListBox_SelectionChanged;
                    
                    // 刷新幻灯片列表(此时缩略图已生成)
                    LoadSlideList();
                    
                    // 选中新幻灯片
                    SlideListBox.SelectedItem = newSlide;
                    
                }, System.Windows.Threading.DispatcherPriority.Loaded);

                //System.Diagnostics.Debug.WriteLine($" 复制幻灯片成功: 原ID={sourceSlide.Id}, 新ID={newSlide.Id}");
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($" 复制幻灯片失败: {ex.Message}");
                WpfMessageBox.Show($"复制幻灯片失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 删除幻灯片按钮点击事件（直接删除,不弹窗确认）
        /// </summary>
        private async void BtnDeleteSlide_Click(object sender, RoutedEventArgs e)
        {
            if (SlideListBox.SelectedItem is not Slide selectedSlide)
                return;

            // 保存要删除的幻灯片ID和当前选中索引
            int slideIdToDelete = selectedSlide.Id;
            int currentSelectedIndex = SlideListBox.SelectedIndex;

            try
            {
                //  从数据库重新加载实体，确保实体是从当前 DbContext 加载的
                // 这样可以避免乐观并发异常
                var slideToDelete = await _textProjectManager.GetSlideByIdAsync(slideIdToDelete);
                if (slideToDelete == null)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($" 幻灯片不存在: ID={slideIdToDelete}");
#endif
                    // 刷新列表，可能已经被删除了
                    LoadSlideList();
                    return;
                }

                // 临时禁用 SelectionChanged 事件，避免删除后刷新列表时触发保存操作
                SlideListBox.SelectionChanged -= SlideListBox_SelectionChanged;

                try
                {
                    // 如果当前正在显示要删除的幻灯片，先切换到其他幻灯片
                    // 这样可以避免在删除时尝试保存已删除的文本元素
                    var allSlides = await _textProjectManager.GetSlidesByProjectAsync(_currentTextProject.Id);
                    
                    var slideToSwitchTo = allSlides.FirstOrDefault(s => s.Id != slideIdToDelete);
                    if (slideToSwitchTo != null)
                    {
                        // 先切换到其他幻灯片（不触发 SelectionChanged，因为已禁用）
                        LoadSlide(slideToSwitchTo);
                    }
                    else
                    {
                        // 如果没有其他幻灯片，清空画布
                        EditorCanvas.Children.Clear();
                        _textBoxes.Clear();
                        _selectedTextBox = null;
                    }

                    // 删除幻灯片（级联删除会自动删除关联的 TextElements 和 RichTextSpans）
                    await _textProjectManager.DeleteSlideAsync(slideIdToDelete);

                    // 刷新幻灯片列表（此时 SelectionChanged 已被禁用，不会触发保存）
                    LoadSlideList();

                    // 选中合适的幻灯片
                    if (SlideListBox.Items.Count > 0)
                    {
                        int targetIndex = currentSelectedIndex < SlideListBox.Items.Count 
                            ? currentSelectedIndex 
                            : SlideListBox.Items.Count - 1;
                        SlideListBox.SelectedIndex = targetIndex;
                        
                        // 手动加载选中的幻灯片（因为 SelectionChanged 被禁用了）
                        if (SlideListBox.SelectedItem is Slide targetSlide)
                        {
                            LoadSlide(targetSlide);
                        }
                    }

#if DEBUG
                    System.Diagnostics.Debug.WriteLine($" 删除幻灯片成功: ID={slideIdToDelete}, Title={selectedSlide.Title}");
#endif
                }
                finally
                {
                    // 恢复 SelectionChanged 事件
                    SlideListBox.SelectionChanged += SlideListBox_SelectionChanged;
                }
            }
            catch (Exception ex)
            {
                // 如果出错，也要恢复事件监听
                SlideListBox.SelectionChanged += SlideListBox_SelectionChanged;

#if DEBUG
                System.Diagnostics.Debug.WriteLine($" 删除幻灯片失败: {ex.Message}");
#endif
                WpfMessageBox.Show($"删除幻灯片失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 加载幻灯片列表
        /// </summary>
        private void LoadSlideList()
        {
            if (_currentTextProject == null)
                return;

            var slides = _textProjectManager.GetSlidesByProjectWithElementsAsync(_currentTextProject.Id).GetAwaiter().GetResult();

            // 加载缩略图路径
            var thumbnailDir = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                "Thumbnails");
            
            foreach (var slide in slides)
            {
                var thumbnailPath = System.IO.Path.Combine(thumbnailDir, $"slide_{slide.Id}.png");
                if (System.IO.File.Exists(thumbnailPath))
                {
                    slide.ThumbnailPath = thumbnailPath;
                }
            }

            // 保存当前选中的索引
            int previousSelectedIndex = SlideListBox.SelectedIndex;
            
            SlideListBox.ItemsSource = slides;

            // 如果有幻灯片，恢复选中或默认选中第一个
            if (slides.Any())
            {
                // 如果之前有选中项，恢复选中；否则默认选中第一个
                int targetIndex = previousSelectedIndex >= 0 && previousSelectedIndex < slides.Count 
                    ? previousSelectedIndex 
                    : 0;
                
                // 先清空选中项，然后再设置，强制触发SelectionChanged事件
                SlideListBox.SelectedIndex = -1;
                SlideListBox.SelectedIndex = targetIndex;
                
                //System.Diagnostics.Debug.WriteLine($" 强制选中幻灯片: Index={targetIndex}");
            }

            //System.Diagnostics.Debug.WriteLine($" 加载幻灯片列表: Count={slides.Count}");
        }

        /// <summary>
        /// 刷新幻灯片列表（保持当前选中项）
        /// </summary>
        private void RefreshSlideList()
        {
            if (_currentTextProject == null)
                return;

            var currentSelectedSlide = SlideListBox.SelectedItem as Slide;
            var currentSelectedId = currentSelectedSlide?.Id;
            
            // 临时禁用SelectionChanged事件，避免重新加载当前幻灯片
            SlideListBox.SelectionChanged -= SlideListBox_SelectionChanged;
            
            try
            {
                // 先清空ItemsSource，强制UI重新绑定
                SlideListBox.ItemsSource = null;
                
                // 重新加载列表
                LoadSlideList();
                
                // 尝试恢复选中项（不会触发SelectionChanged）
                if (currentSelectedId.HasValue)
                {
                    var updatedSlide = (SlideListBox.ItemsSource as List<Slide>)?.FirstOrDefault(s => s.Id == currentSelectedId.Value);
                    if (updatedSlide != null)
                    {
                        SlideListBox.SelectedItem = updatedSlide;
                    }
                }
                
                //System.Diagnostics.Debug.WriteLine($" 刷新幻灯片列表完成（未重新加载幻灯片内容）");
            }
            finally
            {
                // 恢复SelectionChanged事件
                SlideListBox.SelectionChanged += SlideListBox_SelectionChanged;
            }
        }

        /// <summary>
        /// 生成当前画布的缩略图
        /// </summary>
        private BitmapSource GenerateThumbnail()
        {
            try
            {
                // 获取画布的父Grid（包含背景图）
                var canvasParent = EditorCanvas.Parent as Grid;
                if (canvasParent == null)
                    return null;

                // 保存缩略图前：隐藏所有文本框的装饰元素（边框、拖拽手柄等）
                foreach (var textBox in _textBoxes)
                {
                    textBox.HideDecorations();
                }

                // 强制更新布局，确保隐藏效果生效
                canvasParent.UpdateLayout();

                // 获取实际尺寸
                int width = (int)canvasParent.ActualWidth;
                int height = (int)canvasParent.ActualHeight;
                
                // 如果尺寸无效，使用默认值
                if (width <= 0) width = 1600;
                if (height <= 0) height = 900;

                // 创建渲染目标
                var renderBitmap = new RenderTargetBitmap(
                    width, height,
                    96, 96,
                    PixelFormats.Pbgra32);

                // 渲染画布
                renderBitmap.Render(canvasParent);

                // 缩放到缩略图大小
                var thumbnail = new TransformedBitmap(renderBitmap, new ScaleTransform(0.1, 0.1));

                // 保存缩略图后：恢复所有文本框的装饰元素
                foreach (var textBox in _textBoxes)
                {
                    textBox.RestoreDecorations();
                }

                return thumbnail;
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($" 生成缩略图失败: {ex.Message}");
                
                // 异常时也要恢复装饰元素
                foreach (var textBox in _textBoxes)
                {
                    textBox.RestoreDecorations();
                }
                
                return null;
            }
        }

        /// <summary>
        /// 保存当前幻灯片的缩略图到临时文件
        /// </summary>
        private string SaveSlideThumbnail(int slideId)
        {
            try
            {
                var thumbnail = GenerateThumbnail();
                if (thumbnail == null)
                    return null;

                // 创建缩略图目录
                var thumbnailDir = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                    "Thumbnails");
                
                if (!System.IO.Directory.Exists(thumbnailDir))
                    System.IO.Directory.CreateDirectory(thumbnailDir);

                // 保存缩略图
                var thumbnailPath = System.IO.Path.Combine(thumbnailDir, $"slide_{slideId}.png");
                
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(thumbnail));
                
                using (var fileStream = new FileStream(thumbnailPath, FileMode.Create))
                {
                    encoder.Save(fileStream);
                }

                //System.Diagnostics.Debug.WriteLine($" 缩略图已保存: {thumbnailPath}");
                return thumbnailPath;
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($" 保存缩略图失败: {ex.Message}");
                return null;
            }
        }

        #endregion

    }
}



