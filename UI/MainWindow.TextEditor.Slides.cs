using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
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
using ImageColorChanger.Services.TextEditor.Application.Models;
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
        private bool _isSwitchingSlide;
        private bool _isReorderingSlides;
        private bool _isDeletingSlide;
        private bool _isNormalizingSlideSortOrder;

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
                EditorCanvas.Background = BuildSlideBackgroundBrush(slide);
            }
        }

        /// <summary>
        /// 幻灯片列表选择改变事件
        ///  切换幻灯片前先保存当前文本，防止文本丢失
        /// </summary>
        private async void SlideListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isRevertingSlideSelection || _isSwitchingSlide)
            {
                return;
            }

            if (SlideListBox.SelectedItem is not Slide selectedSlide)
            {
                return;
            }

            _isSwitchingSlide = true;
            try
            {
                if (_currentSlide != null && selectedSlide.Id != _currentSlide.Id)
                {
                    if (!CanSwitchSlideWhileProjecting())
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

                    var previousSlide = _currentSlide;
                    var textBoxesCopy = _textBoxes.ToList();

                    bool saveSucceeded = false;
                    Exception lastSaveException = null;

                    bool hasPendingChanges = BtnSaveTextProject.Background is SolidColorBrush saveBrush
                                             && saveBrush.Color == Colors.LightGreen;

                    if (!hasPendingChanges)
                    {
                        saveSucceeded = true;
                    }
                    else
                    {
                        var saveResult = await SaveTextEditorStateAsync(
                            SaveTrigger.SlideSwitch,
                            textBoxesCopy,
                            persistAdditionalState: true,
                            saveThumbnail: false);
                        saveSucceeded = saveResult.Succeeded;
                        lastSaveException = saveResult.Exception;

                        if (!saveSucceeded)
                        {
                            var retryResult = WpfMessageBox.Show(
                                $"切换幻灯片前保存失败：{lastSaveException?.Message}\n是否重试？",
                                "保存失败",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning);

                            if (retryResult == MessageBoxResult.Yes)
                            {
                                saveResult = await SaveTextEditorStateAsync(
                                    SaveTrigger.SlideSwitch,
                                    textBoxesCopy,
                                    persistAdditionalState: true,
                                    saveThumbnail: false);
                                saveSucceeded = saveResult.Succeeded;
                                lastSaveException = saveResult.Exception;
                            }
                        }
                    }

                    if (!saveSucceeded)
                    {
                        WpfMessageBox.Show(
                            $"保存失败，未切换幻灯片：{lastSaveException?.Message}",
                            "错误",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);

                        _isRevertingSlideSelection = true;
                        try
                        {
                            SlideListBox.SelectedItem = previousSlide;
                        }
                        finally
                        {
                            _isRevertingSlideSelection = false;
                        }

                        return;
                    }
                }

                // 在切换前预解析相邻视频（确保切换时已准备好）
                if (_isProjectionLocked)
                {
                    _ = PreparseAdjacentVideosBeforeSwitchAsync(selectedSlide);
                }

                // 直接加载新幻灯片
                await LoadSlide(selectedSlide);
            }
            finally
            {
                _isSwitchingSlide = false;
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
        private AdornerLayer _slideInsertAdornerLayer;
        private SlideInsertAdorner _slideInsertAdorner;
        private DropInsertPosition _pendingDropPosition = DropInsertPosition.Before;

        private enum DropInsertPosition
        {
            Before,
            After
        }

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
                    try
                    {
                        // 开始拖放操作
                        System.Windows.DataObject dragData = new System.Windows.DataObject("Slide", _draggingSlide);
                        DragDrop.DoDragDrop(SlideListBox, dragData, System.Windows.DragDropEffects.Move);
                    }
                    finally
                    {
                        ClearSlideDragVisuals();
                        _draggingSlide = null;
                    }
                }
            }
        }

        /// <summary>
        /// 幻灯片列表拖放over事件
        /// </summary>
        private void SlideListBox_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent("Slide") && e.Data.GetData("Slide") is Slide sourceSlide)
            {
                e.Effects = System.Windows.DragDropEffects.Move;

                var targetItem = FindVisualAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
                if (targetItem?.DataContext is Slide targetSlide)
                {
                    if (targetSlide.Id == sourceSlide.Id)
                    {
                        HideSlideInsertIndicator();
                        e.Handled = true;
                        return;
                    }

                    var mouseInItem = e.GetPosition(targetItem);
                    _pendingDropPosition = mouseInItem.Y >= targetItem.ActualHeight * 0.5
                        ? DropInsertPosition.After
                        : DropInsertPosition.Before;
                    ShowSlideInsertIndicator(targetItem, _pendingDropPosition);
                }
                else
                {
                    HideSlideInsertIndicator();
                }
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.None;
                HideSlideInsertIndicator();
            }
            e.Handled = true;
        }

        /// <summary>
        /// 幻灯片列表放下事件（完成排序）
        /// </summary>
        private async void SlideListBox_Drop(object sender, System.Windows.DragEventArgs e)
        {
            try
            {
                if (!e.Data.GetDataPresent("Slide"))
                {
                    return;
                }

                if (e.Data.GetData("Slide") is not Slide sourceSlide)
                {
                    return;
                }

                // 获取放下位置的幻灯片
                var targetItem = FindVisualAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
                Slide targetSlide = targetItem?.DataContext as Slide;
                if (targetSlide == null)
                {
                    targetSlide = SlideListBox.Items.Cast<object>().LastOrDefault() as Slide;
                    _pendingDropPosition = DropInsertPosition.After;
                }

                if (targetSlide == null || targetSlide.Id == sourceSlide.Id)
                {
                    return;
                }

                var beforePositions = CaptureVisibleSlideItemPositions();
                bool reordered = await ReorderSlides(sourceSlide, targetSlide, _pendingDropPosition == DropInsertPosition.After);
                if (reordered)
                {
                    AnimateSlideListReorder(beforePositions);
                }
            }
            finally
            {
                ClearSlideDragVisuals();
            }
        }

        /// <summary>
        /// 重新排序幻灯片
        /// </summary>
        private async Task<bool> ReorderSlides(Slide sourceSlide, Slide targetSlide, bool insertAfterTarget)
        {
            if (_isReorderingSlides || _currentTextProject == null || sourceSlide == null || targetSlide == null)
            {
                return false;
            }

            _isReorderingSlides = true;
            try
            {
                // 通过ID查找幻灯片，避免对象引用不一致的问题
                var slides = await _textProjectService.GetSlidesByProjectAsync(_currentTextProject.Id);

                // 通过ID查找索引，而不是使用对象引用
                int sourceIndex = slides.FindIndex(s => s.Id == sourceSlide.Id);
                int targetIndex = slides.FindIndex(s => s.Id == targetSlide.Id);

                if (sourceIndex == -1 || targetIndex == -1)
                {
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($" [ReorderSlides] 无法找到幻灯片: sourceIndex={sourceIndex}, targetIndex={targetIndex}");
                    #endif
                    return false;
                }

                if (insertAfterTarget)
                {
                    targetIndex++;
                }

                // 移除源幻灯片
                var sourceSlideEntity = slides[sourceIndex];
                slides.RemoveAt(sourceIndex);

                if (sourceIndex < targetIndex)
                {
                    targetIndex--;
                }

                targetIndex = Math.Clamp(targetIndex, 0, slides.Count);
                if (targetIndex == sourceIndex)
                {
                    return false;
                }

                // 插入到目标位置
                slides.Insert(targetIndex, sourceSlideEntity);

                // 更新所有幻灯片的SortOrder
                for (int i = 0; i < slides.Count; i++)
                {
                    slides[i].SortOrder = i + 1;
                }

                await _textProjectService.UpdateSlideSortOrdersAsync(slides);

                // #if DEBUG
                // System.Diagnostics.Debug.WriteLine($" [ReorderSlides] 排序已保存: 从位置{sourceIndex}移动到位置{targetIndex}");
                // #endif

                // 刷新列表
                await LoadSlideList();

                // 保持选中当前幻灯片（通过ID查找）
                var updatedSourceSlide = slides.FirstOrDefault(s => s.Id == sourceSlide.Id);
                if (updatedSourceSlide != null)
                {
                    SlideListBox.SelectedItem = updatedSourceSlide;
                }

                return true;
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($" [ReorderSlides] 排序失败: {ex.Message}");
                #endif
                WpfMessageBox.Show($"排序失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            finally
            {
                _isReorderingSlides = false;
            }
        }

        /// <summary>
        /// 幻灯片列表拖拽离开事件
        /// </summary>
        private void SlideListBox_DragLeave(object sender, System.Windows.DragEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject originalSource)
            {
                ClearSlideDragVisuals();
                return;
            }

            var visualHit = SlideListBox.InputHitTest(e.GetPosition(SlideListBox)) as DependencyObject;
            if (visualHit == null || FindVisualAncestor<ListBoxItem>(visualHit) == null)
            {
                HideSlideInsertIndicator();
            }

            if (!IsDescendantOf(originalSource, SlideListBox))
            {
                ClearSlideDragVisuals();
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

        private bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
        {
            while (child != null)
            {
                if (ReferenceEquals(child, ancestor))
                {
                    return true;
                }

                child = child switch
                {
                    Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(child),
                    _ => LogicalTreeHelper.GetParent(child)
                };
            }

            return false;
        }

        private void ShowSlideInsertIndicator(ListBoxItem targetItem, DropInsertPosition position)
        {
            if (targetItem == null)
            {
                return;
            }

            _slideInsertAdornerLayer ??= AdornerLayer.GetAdornerLayer(targetItem);
            if (_slideInsertAdornerLayer == null)
            {
                return;
            }

            if (_slideInsertAdorner == null || !ReferenceEquals(_slideInsertAdorner.AdornedElement, targetItem))
            {
                HideSlideInsertIndicator();
                _slideInsertAdorner = new SlideInsertAdorner(targetItem);
                _slideInsertAdornerLayer = AdornerLayer.GetAdornerLayer(targetItem);
                _slideInsertAdornerLayer?.Add(_slideInsertAdorner);
            }

            _slideInsertAdorner.SetPosition(position == DropInsertPosition.After);
        }

        private void HideSlideInsertIndicator()
        {
            if (_slideInsertAdornerLayer != null && _slideInsertAdorner != null)
            {
                _slideInsertAdornerLayer.Remove(_slideInsertAdorner);
            }

            _slideInsertAdorner = null;
            _slideInsertAdornerLayer = null;
        }

        private void ClearSlideDragVisuals()
        {
            HideSlideInsertIndicator();
        }

        private Dictionary<int, double> CaptureVisibleSlideItemPositions()
        {
            var positions = new Dictionary<int, double>();
            if (SlideListBox == null || SlideListBox.Items.Count == 0)
            {
                return positions;
            }

            for (int i = 0; i < SlideListBox.Items.Count; i++)
            {
                if (SlideListBox.Items[i] is not Slide slide)
                {
                    continue;
                }

                if (SlideListBox.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem item)
                {
                    continue;
                }

                var transform = item.TransformToAncestor(SlideListBox);
                var point = transform.Transform(new System.Windows.Point(0, 0));
                positions[slide.Id] = point.Y;
            }

            return positions;
        }

        private void AnimateSlideListReorder(Dictionary<int, double> oldPositions)
        {
            if (oldPositions == null || oldPositions.Count == 0 || SlideListBox == null)
            {
                return;
            }

            SlideListBox.UpdateLayout();

            for (int i = 0; i < SlideListBox.Items.Count; i++)
            {
                if (SlideListBox.Items[i] is not Slide slide || !oldPositions.TryGetValue(slide.Id, out var oldY))
                {
                    continue;
                }

                if (SlideListBox.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem item)
                {
                    continue;
                }

                var newY = item.TransformToAncestor(SlideListBox).Transform(new System.Windows.Point(0, 0)).Y;
                var delta = oldY - newY;
                if (Math.Abs(delta) < 0.5)
                {
                    continue;
                }

                if (item.RenderTransform is not TranslateTransform translate)
                {
                    translate = new TranslateTransform();
                    item.RenderTransform = translate;
                }

                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.Y = delta;

                var animation = new DoubleAnimation
                {
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(160),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                animation.Completed += (_, _) =>
                {
                    translate.BeginAnimation(TranslateTransform.YProperty, null);
                    translate.Y = 0;
                };

                translate.BeginAnimation(TranslateTransform.YProperty, animation);
            }
        }

        private sealed class SlideInsertAdorner : Adorner
        {
            private readonly System.Windows.Media.Pen _linePen;
            private bool _drawAfter;

            public SlideInsertAdorner(UIElement adornedElement) : base(adornedElement)
            {
                IsHitTestVisible = false;
                _linePen = new System.Windows.Media.Pen(
                    new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF9800")),
                    4);
                _linePen.Freeze();
            }

            public void SetPosition(bool drawAfter)
            {
                if (_drawAfter == drawAfter)
                {
                    return;
                }

                _drawAfter = drawAfter;
                InvalidateVisual();
            }

            protected override void OnRender(DrawingContext drawingContext)
            {
                base.OnRender(drawingContext);

                var width = AdornedElement.RenderSize.Width;
                var y = _drawAfter ? AdornedElement.RenderSize.Height : 0;
                drawingContext.DrawLine(_linePen, new System.Windows.Point(0, y), new System.Windows.Point(width, y));
            }
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

            // 统一逻辑：弹窗经文模式也复用同一套悬浮工具栏显示/隐藏链路。
            // 当工具栏已打开且当前不是文本框编辑态，点击弹窗以外区域即隐藏。
            if (BibleToolbar?.IsOpen == true && _selectedTextBox == null && _isBiblePopupOverlayVisible)
            {
                var originalSource = e.OriginalSource as DependencyObject;
                bool inPopupOverlay =
                    (MainBiblePopupOverlayImage != null && IsDescendantOf(originalSource, MainBiblePopupOverlayImage)) ||
                    (MainBiblePopupOverlayCloseButton != null && IsDescendantOf(originalSource, MainBiblePopupOverlayCloseButton));

                var popupChild = BibleToolbar.Child as DependencyObject;
                bool inToolbarPopup = popupChild != null && IsDescendantOf(originalSource, popupChild);

                bool inAnySidePanelPopup = IsClickInsideAnyTextEditorSidePopup(originalSource);

                if (!inPopupOverlay && !inToolbarPopup && !inAnySidePanelPopup)
                {
                    HideBibleFloatingToolbar();
                }
            }

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

        private bool IsClickInsideAnyTextEditorSidePopup(DependencyObject originalSource)
        {
            bool IsInPopup(Popup popup)
            {
                if (popup == null || !popup.IsOpen)
                {
                    return false;
                }

                var child = popup.Child as DependencyObject;
                return child != null && IsDescendantOf(originalSource, child);
            }

            return IsInPopup(BorderSettingsPopup) ||
                   IsInPopup(BackgroundSettingsPopup) ||
                   IsInPopup(TextColorSettingsPopup) ||
                   IsInPopup(ShadowSettingsPopup) ||
                   IsInPopup(SpacingSettingsPopup) ||
                   IsInPopup(AnimationSettingsPopup);
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
        private async Task LoadSlide(Slide slide)
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
                    EditorCanvas.Background = BuildSlideBackgroundBrush(slide);
                }

                // 加载文本元素（包含富文本片段）
                var elements = await _textElementRepository.GetBySlideWithRichTextAsync(slide.Id);

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

                EnsureNoticeAnimationLoopState();

                //System.Diagnostics.Debug.WriteLine($" 加载幻灯片成功: ID={slide.Id}, Title={slide.Title}, Elements={elements.Count}");
                
                // 恢复分割配置
                await RestoreSplitConfigAsync(slide);
                
                // 加载完成后，如果投影已开启且未锁定，自动更新投影
                if (_projectionManager.IsProjectionActive && !_isProjectionLocked)
                {
                    // 延迟一点点，确保UI渲染完成
                    _ = Dispatcher.BeginInvoke(new Action(() =>
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
                var slideCount = await _textProjectService.GetSlideCountAsync(_currentTextProject.Id);
                
                // 获取当前最大排序号（用于SortOrder）
                int maxOrder = await _textProjectService.GetMaxSlideSortOrderAsync(_currentTextProject.Id);

                // 创建新幻灯片（标题序号 = 总数 + 1）
                var newSlide = new Slide
                {
                    ProjectId = _currentTextProject.Id,
                    Title = $"幻灯片 {slideCount + 1}",
                    SortOrder = maxOrder + 1,
                    BackgroundColor = GetCurrentSlideThemeBackgroundColorHex(),
                    SplitMode = -1,  // 默认无分割模式
                    SplitStretchMode = _splitImageDisplayMode  // 使用当前分割显示偏好
                };

                await _textProjectService.AddSlideAsync(newSlide);

                // 刷新幻灯片列表
                await LoadSlideList();

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
                // 先保存当前编辑态，避免从数据库读取到旧内容（会导致多行文本在副本中被拼接）。
                bool hasPendingChanges = BtnSaveTextProject.Background is SolidColorBrush saveBrush
                                         && saveBrush.Color == Colors.LightGreen;
                if (hasPendingChanges)
                {
                    var saveResult = await SaveTextEditorStateAsync(
                        SaveTrigger.SlideSwitch,
                        _textBoxes.ToList(),
                        persistAdditionalState: true,
                        saveThumbnail: false);

                    if (!saveResult.Succeeded)
                    {
                        WpfMessageBox.Show(
                            $"复制前保存失败：{saveResult.Exception?.Message}",
                            "错误",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        return;
                    }
                }

                // 加载源幻灯片的所有元素（包含富文本片段）
                var sourceElements = await _textElementRepository.GetBySlideWithRichTextAsync(sourceSlide.Id);

                // 计算新的排序位置（在源幻灯片后面）
                int newSortOrder = sourceSlide.SortOrder + 1;
                
                // 将后面的幻灯片排序顺序都+1
                var slidesToUpdate = (await _textProjectService.GetSlidesByProjectAsync(_currentTextProject.Id))
                    .Where(s => s.SortOrder >= newSortOrder)
                    .ToList();
                
                foreach (var slide in slidesToUpdate)
                {
                    slide.SortOrder++;
                }
                await _textProjectService.UpdateSlideSortOrdersAsync(slidesToUpdate);

                // 创建新幻灯片（复制所有属性）
                var newSlide = new Slide
                {
                    ProjectId = _currentTextProject.Id,
                    Title = $"{sourceSlide.Title} (副本)",
                    SortOrder = newSortOrder,
                    BackgroundColor = sourceSlide.BackgroundColor,
                    BackgroundImagePath = sourceSlide.BackgroundImagePath,
                    SplitMode = sourceSlide.SplitMode,  // 复制分割模式
                    SplitStretchMode = sourceSlide.SplitStretchMode,  // 复制显示模式
                    SplitRegionsData = sourceSlide.SplitRegionsData,  // 复制区域数据
                    OutputMode = sourceSlide.OutputMode // 复制输出模式（普通/透明）
                };

                await _textProjectService.AddSlideAsync(newSlide);

                // 复制所有文本元素（使用 CloneElement 确保复制所有样式配置）
                foreach (var sourceElement in sourceElements)
                {
                    //  使用 CloneElement 方法复制所有样式属性
                    var newElement = _textProjectService.CloneElement(sourceElement);
                    newElement.SlideId = newSlide.Id;  // 设置新的幻灯片ID
                    
                    await _textProjectService.AddElementAsync(newElement);

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
                                ShadowOpacity = sourceSpan.ShadowOpacity,
                                ParagraphIndex = sourceSpan.ParagraphIndex,
                                RunIndex = sourceSpan.RunIndex,
                                FormatVersion = sourceSpan.FormatVersion
                            };
                            newSpans.Add(newSpan);
                        }

                        // 批量保存富文本片段
                        await _richTextSpanRepository.SaveForTextElementAsync(newElement.Id, newSpans);
                    }
                }

                // 先加载新幻灯片内容并生成缩略图，再刷新列表（避免闪烁）
                SlideListBox.SelectionChanged -= SlideListBox_SelectionChanged;
                try
                {
                    await LoadSlide(newSlide);
                    await Task.Delay(150);

                    var thumbnailPath = SaveSlideThumbnail(newSlide.Id);
                    if (!string.IsNullOrEmpty(thumbnailPath))
                    {
                        newSlide.ThumbnailPath = thumbnailPath;
                    }

                    await LoadSlideList();
                    SlideListBox.SelectedItem = newSlide;
                }
                finally
                {
                    SlideListBox.SelectionChanged += SlideListBox_SelectionChanged;
                }

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
            if (_isDeletingSlide)
                return;

            if (SlideListBox.SelectedItem is not Slide selectedSlide)
                return;

            _isDeletingSlide = true;

            // 保存要删除的幻灯片ID和当前选中索引
            int slideIdToDelete = selectedSlide.Id;
            int currentSelectedIndex = SlideListBox.SelectedIndex;

            try
            {
                //  从数据库重新加载实体，确保实体是从当前 DbContext 加载的
                // 这样可以避免乐观并发异常
                var slideToDelete = await _textProjectService.GetSlideByIdAsync(slideIdToDelete);
                if (slideToDelete == null)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($" 幻灯片不存在: ID={slideIdToDelete}");
#endif
                    // 刷新列表，可能已经被删除了
                    await LoadSlideList();
                    return;
                }

                // 临时禁用 SelectionChanged 事件，避免删除后刷新列表时触发保存操作
                SlideListBox.SelectionChanged -= SlideListBox_SelectionChanged;

                try
                {
                    // 如果当前正在显示要删除的幻灯片，先切换到其他幻灯片
                    // 这样可以避免在删除时尝试保存已删除的文本元素
                    var allSlides = await _textProjectService.GetSlidesByProjectAsync(_currentTextProject.Id);
                    
                    var slideToSwitchTo = allSlides.FirstOrDefault(s => s.Id != slideIdToDelete);
                    if (slideToSwitchTo != null)
                    {
                        // 先切换到其他幻灯片（不触发 SelectionChanged，因为已禁用）
                        await LoadSlide(slideToSwitchTo);
                    }
                    else
                    {
                        // 如果没有其他幻灯片，清空画布
                        EditorCanvas.Children.Clear();
                        _textBoxes.Clear();
                        _selectedTextBox = null;
                    }

                    // 删除幻灯片（级联删除会自动删除关联的 TextElements 和 RichTextSpans）
                    await _textProjectService.DeleteSlideAsync(slideIdToDelete);

                    // 刷新幻灯片列表（此时 SelectionChanged 已被禁用，不会触发保存）
                    await LoadSlideList();

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
                            await LoadSlide(targetSlide);
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
#if DEBUG
                System.Diagnostics.Debug.WriteLine($" 删除幻灯片失败: {ex.Message}");
#endif
                WpfMessageBox.Show($"删除幻灯片失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isDeletingSlide = false;
            }
        }

        /// <summary>
        /// 文本编辑器面板滚轮事件：经文弹窗显示时用于滚动经文并同步投影。
        /// </summary>
        private void TextEditorPanel_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            _ = sender;
            if (HandleBiblePopupOverlayMouseWheel(e))
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// 经文弹窗预览层滚轮：优先用于经文滚动并同步投影。
        /// </summary>
        private void MainBiblePopupOverlayImage_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            _ = sender;
            if (HandleBiblePopupOverlayMouseWheel(e))
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// 经文弹窗预览层点击：呼出悬浮工具栏，便于直接调整动画参数。
        /// </summary>
        private void MainBiblePopupOverlayImage_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _ = sender;
            if (TryOpenBibleToolbarForPopupOverlay())
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// 加载幻灯片列表
        /// </summary>
        private async Task LoadSlideList()
        {
            if (_currentTextProject == null)
                return;

            await NormalizeSlideSortOrdersIfNeededAsync(_currentTextProject.Id);
            var slides = await _textProjectService.GetSlidesByProjectWithElementsAsync(_currentTextProject.Id);

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
        /// 只在排序异常时修复 SortOrder（重复/非连续/小于1），避免历史脏数据影响拖拽与删除。
        /// </summary>
        private async Task NormalizeSlideSortOrdersIfNeededAsync(int projectId)
        {
            if (_isNormalizingSlideSortOrder || projectId <= 0)
            {
                return;
            }

            _isNormalizingSlideSortOrder = true;
            try
            {
                var slides = await _textProjectService.GetSlidesByProjectAsync(projectId);
                if (slides == null || slides.Count <= 1)
                {
                    return;
                }

                bool needsNormalize = false;
                for (int i = 0; i < slides.Count; i++)
                {
                    int expectedOrder = i + 1;
                    if (slides[i].SortOrder != expectedOrder)
                    {
                        needsNormalize = true;
                        break;
                    }
                }

                if (!needsNormalize)
                {
                    return;
                }

                for (int i = 0; i < slides.Count; i++)
                {
                    slides[i].SortOrder = i + 1;
                }

                await _textProjectService.UpdateSlideSortOrdersAsync(slides);
            }
            catch
            {
                // 自愈失败不阻断主流程，后续由正常操作继续兜底。
            }
            finally
            {
                _isNormalizingSlideSortOrder = false;
            }
        }

        /// <summary>
        /// 刷新幻灯片列表（保持当前选中项）
        /// </summary>
        private async Task RefreshSlideList()
        {
            if (_currentTextProject == null)
                return;

            var currentSelectedSlide = SlideListBox.SelectedItem as Slide;
            var currentSelectedId = currentSelectedSlide?.Id;

            // 通过保护位屏蔽转发链路中的 SelectionChanged，避免保存后触发 LoadSlide 造成视觉跳变。
            _isRevertingSlideSelection = true;

            try
            {
                // 先清空ItemsSource，强制UI重新绑定
                SlideListBox.ItemsSource = null;
                
                // 重新加载列表
                await LoadSlideList();
                
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
                _isRevertingSlideSelection = false;
            }
        }

        /// <summary>
        /// 生成当前画布的缩略图
        /// </summary>
        private BitmapSource GenerateThumbnail()
        {
            var canvasParent = EditorCanvas.Parent as Grid;
            return _textEditorThumbnailService?.GenerateThumbnail(canvasParent, _textBoxes);
        }

        /// <summary>
        /// 保存当前幻灯片的缩略图到临时文件
        /// </summary>
        private string SaveSlideThumbnail(int slideId)
        {
            try
            {
                var canvasParent = EditorCanvas.Parent as Grid;
                var thumbnailDir = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                    "Thumbnails");

                return _textEditorThumbnailService?.SaveSlideThumbnail(
                    slideId,
                    canvasParent,
                    _textBoxes,
                    thumbnailDir);
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
