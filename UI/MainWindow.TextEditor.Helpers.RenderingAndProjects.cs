using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageColorChanger.Core;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Managers;
using ImageColorChanger.UI.Controls;
using SkiaSharp;
using WpfMessageBox = System.Windows.MessageBox;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow TextEditor Rendering And Project Operations
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// 将WPF BitmapSource转换为SKBitmap
        /// </summary>
        private SKBitmap ConvertBitmapSourceToSKBitmap(BitmapSource source)
        {
            int width = source.PixelWidth;
            int height = source.PixelHeight;
            
            var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            source.CopyPixels(pixels, stride, 0);
            
            unsafe
            {
                fixed (byte* ptr = pixels)
                {
                    var dest = bitmap.GetPixels();
                    Buffer.MemoryCopy(ptr, dest.ToPointer(), pixels.Length, pixels.Length);
                }
            }
            
            return bitmap;
        }
        
        /// <summary>
        /// 将UI元素渲染为位图（旧方法，已被ComposeCanvasWithSkia替代）
        /// 优化策略：先渲染到Canvas原始尺寸（快），后续用GPU缩放到投影分辨率（快）
        /// </summary>
        private RenderTargetBitmap RenderCanvasToBitmap(UIElement element)
        {
            // 获取元素的实际尺寸
            double width = 0;
            double height = 0;
            
            if (element is FrameworkElement frameworkElement)
            {
                width = frameworkElement.ActualWidth > 0 ? frameworkElement.ActualWidth : frameworkElement.Width;
                height = frameworkElement.ActualHeight > 0 ? frameworkElement.ActualHeight : frameworkElement.Height;
            }
            
            // 新策略：渲染到Canvas原始尺寸，避免DrawingVisual缩放带来的性能损失
            // 后续会用GPU快速缩放到投影分辨率
            int renderWidth = (int)Math.Ceiling(width);
            int renderHeight = (int)Math.Ceiling(height);
            
            // 确保元素已完成布局
            element.Measure(new System.Windows.Size(width, height));
            element.Arrange(new Rect(new System.Windows.Size(width, height)));
            element.UpdateLayout();
            
            // 渲染到Canvas原始尺寸，96 DPI
            var renderBitmap = new RenderTargetBitmap(
                renderWidth,
                renderHeight,
                96, 96,
                PixelFormats.Pbgra32);

            renderBitmap.Render(element);
            return renderBitmap;
        }

        /// <summary>
        /// 将WPF位图转换为SkiaSharp格式
        /// </summary>
        private SKBitmap ConvertBitmapToSkia(BitmapSource bitmap)
        {
            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;

            // 创建SkiaSharp图片
            var image = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

            // 从WPF位图读取像素
            int stride = width * 4; // BGRA32 = 4 bytes per pixel
            byte[] pixels = new byte[height * stride];
            bitmap.CopyPixels(pixels, stride, 0);

            // 直接复制像素数据（WPF和SkiaSharp都使用BGRA格式）
            unsafe
            {
                fixed (byte* src = pixels)
                {
                    var dst = image.GetPixels();
                    Buffer.MemoryCopy(src, dst.ToPointer(), pixels.Length, pixels.Length);
                }
            }

            return image;
        }

        /// <summary>
        /// 将图像缩放到投影屏幕尺寸，拉伸填满整个屏幕
        /// 优化：使用GPU加速缩放，性能提升10倍
        /// </summary>
        private SKBitmap ScaleImageForProjection(SKBitmap sourceImage, int targetWidth, int targetHeight)
        {
            #if DEBUG
            //System.Diagnostics.Debug.WriteLine($"[GPU缩放] 输入: {sourceImage.Width}×{sourceImage.Height}, 输出: {targetWidth}×{targetHeight}");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            #endif

            // 使用GPU加速缩放（与普通图片投影保持一致）
            var scaled = _gpuContext.ScaleImageGpu(
                sourceImage, 
                targetWidth, 
                targetHeight);
            
            #if DEBUG
            sw.Stop();
            //System.Diagnostics.Debug.WriteLine($"[GPU缩放] GPUContext.ScaleImageGpu 实际耗时: {sw.ElapsedMilliseconds}ms");
            #endif

            return scaled;
        }

        /// <summary>
        /// 计算最佳的BitmapCache渲染缩放比例，以适应投影屏幕分辨率
        /// </summary>
        /// <returns>渲染缩放比例（1.0-4.0）</returns>
        private double CalculateOptimalRenderScale()
        {
            try
            {
                // 获取投影屏幕分辨率
                var (projWidth, projHeight) = _projectionManager?.GetCurrentProjectionSize() ?? (1920, 1080);

                // 使用实际画布尺寸（而非硬编码）
                double canvasWidth = EditorCanvas?.ActualWidth ?? 1600.0;
                double canvasHeight = EditorCanvas?.ActualHeight ?? 900.0;

                // 计算宽度和高度的缩放比例
                double scaleX = projWidth / canvasWidth;
                double scaleY = projHeight / canvasHeight;

                // 使用较大的缩放比例，确保投影时质量充足
                double scale = Math.Max(scaleX, scaleY);

                // 限制范围：1.0-4.0（避免过大导致内存问题）
                scale = Math.Max(1.0, Math.Min(4.0, scale));

                #if DEBUG
                // //System.Diagnostics.Debug.WriteLine($"[RenderScale] 投影屏={projWidth}×{projHeight}, 画布={canvasWidth}×{canvasHeight}, 缩放={scale:F2}");
                #endif

                return scale;
            }
            catch
            {
                // 异常时返回默认值2.0（适合1080p投影）
                return 2.0;
            }
        }

        /// <summary>
        /// 重命名文本项目 - 进入内联编辑模式
        /// </summary>
        private void RenameTextProjectAsync(ProjectTreeItem item)
        {
            if (item == null || item.Type != TreeItemType.TextProject)
            {
                //System.Diagnostics.Debug.WriteLine($" 无法重命名: item null 或类型不匹配");
                return;
            }

            //System.Diagnostics.Debug.WriteLine($"进入编辑模式: ID={item.Id}, Name={item.Name}");
            
            // 保存原始名称
            item.OriginalName = item.Name;
            
            // 进入编辑模式
            item.IsEditing = true;
            
            //System.Diagnostics.Debug.WriteLine($" IsEditing 已设置为 true, OriginalName={item.OriginalName}");
        }

        /// <summary>
        /// 完成内联重命名
        /// </summary>
        private async Task CompleteRenameAsync(ProjectTreeItem item, string newName)
        {
            try
            {
                //System.Diagnostics.Debug.WriteLine($"完成重命名: OriginalName={item.OriginalName}, CurrentName={item.Name}, NewName={newName}");
                
                // 如果取消或输入为空，恢复原始名称
                if (string.IsNullOrWhiteSpace(newName))
                {
                    //System.Diagnostics.Debug.WriteLine($" 名称为空，恢复原始名称");
                    item.Name = item.OriginalName;
                    item.IsEditing = false;
                    return;
                }

                // 如果名称未改变，直接返回
                if (newName.Trim() == item.OriginalName)
                {
                    //System.Diagnostics.Debug.WriteLine($" 名称未改变，取消编辑");
                    item.IsEditing = false;
                    return;
                }

                //System.Diagnostics.Debug.WriteLine($" 开始保存项目: ID={item.Id}, {item.OriginalName} -> {newName.Trim()}");
                
                // 加载并更新项目
                var project = await _textProjectService.LoadProjectAsync(item.Id);
                if (project != null)
                {
                    //System.Diagnostics.Debug.WriteLine($" 项目加载成功，更新名称");
                    
                    project.Name = newName.Trim();
                    await _textProjectService.SaveProjectAsync(project);
                    
                    // 更新树节点（Name 已经通过绑定更新了，只需更新 OriginalName）
                    item.OriginalName = newName.Trim();
                    item.IsEditing = false;
                    
                    ShowStatus($"项目已重命名: {newName}");
                    //System.Diagnostics.Debug.WriteLine($" 项目已重命名: ID={item.Id}, NewName={newName}");
                }
                else
                {
                    //System.Diagnostics.Debug.WriteLine($" 项目加载失败: ID={item.Id}，恢复原始名称");
                    item.Name = item.OriginalName;
                    item.IsEditing = false;
                }
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($" 重命名项目失败: {ex.Message}");
                //System.Diagnostics.Debug.WriteLine($" 堆栈跟踪: {ex.StackTrace}");
                
                // 恢复原始名称
                item.Name = item.OriginalName;
                item.IsEditing = false;
                
                WpfMessageBox.Show($"重命名项目失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 导出文本项目
        /// </summary>
        private async Task ExportTextProjectAsync(ProjectTreeItem item)
        {
            try
            {
                var slideExportManager = _mainWindowServices.GetRequired<SlideExportManager>();
                if (slideExportManager != null)
                {
                    bool success = await slideExportManager.ExportProjectAsync(item.Id);
                    if (success)
                    {
                        ShowStatus($"已导出项目: {item.Name}");
                    }
                    else if (!string.IsNullOrWhiteSpace(slideExportManager.LastError))
                    {
                        ShowStatus($"{slideExportManager.LastError}");
                    }
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"导出项目失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 复制文本项目（包含全部幻灯片与文本元素）
        /// </summary>
        private async Task CopyTextProjectAsync(ProjectTreeItem item)
        {
            if (item == null || item.Id <= 0)
            {
                return;
            }

            try
            {
                var sourceProject = await _textProjectService.LoadProjectAsync(item.Id);
                if (sourceProject == null)
                {
                    ShowStatus("复制项目失败：源项目不存在");
                    return;
                }

                string targetName = await GenerateCopyProjectNameAsync(sourceProject.Name);
                var newProject = await _textProjectService.CreateProjectAsync(
                    targetName,
                    sourceProject.CanvasWidth,
                    sourceProject.CanvasHeight);

                newProject.BackgroundImagePath = sourceProject.BackgroundImagePath;
                newProject.ModifiedTime = DateTime.Now;
                await _textProjectService.SaveProjectAsync(newProject);

                var sourceSlides = (await _textProjectService.GetSlidesByProjectAsync(sourceProject.Id))
                    .OrderBy(s => s.SortOrder)
                    .ThenBy(s => s.Id)
                    .ToList();

                var slideIdMap = new Dictionary<int, int>();
                foreach (var sourceSlide in sourceSlides)
                {
                    var clonedSlide = new Slide
                    {
                        ProjectId = newProject.Id,
                        Title = sourceSlide.Title,
                        SortOrder = sourceSlide.SortOrder,
                        BackgroundColor = sourceSlide.BackgroundColor,
                        BackgroundImagePath = sourceSlide.BackgroundImagePath,
                        BackgroundGradientEnabled = sourceSlide.BackgroundGradientEnabled,
                        BackgroundGradientStartColor = sourceSlide.BackgroundGradientStartColor,
                        BackgroundGradientEndColor = sourceSlide.BackgroundGradientEndColor,
                        BackgroundGradientDirection = sourceSlide.BackgroundGradientDirection,
                        BackgroundOpacity = sourceSlide.BackgroundOpacity,
                        SplitMode = sourceSlide.SplitMode,
                        SplitStretchMode = sourceSlide.SplitStretchMode,
                        SplitRegionsData = sourceSlide.SplitRegionsData,
                        VideoBackgroundEnabled = sourceSlide.VideoBackgroundEnabled,
                        VideoLoopEnabled = sourceSlide.VideoLoopEnabled,
                        VideoVolume = sourceSlide.VideoVolume,
                        OutputMode = sourceSlide.OutputMode
                    };

                    var savedSlide = await _textProjectService.AddSlideAsync(clonedSlide);
                    slideIdMap[sourceSlide.Id] = savedSlide.Id;
                }

                foreach (var sourceSlide in sourceSlides)
                {
                    if (!slideIdMap.TryGetValue(sourceSlide.Id, out var newSlideId))
                    {
                        continue;
                    }

                    var sourceElements = await _textElementRepository.GetBySlideWithRichTextAsync(sourceSlide.Id);
                    foreach (var sourceElement in sourceElements)
                    {
                        var newElement = _textProjectService.CloneElement(sourceElement);
                        newElement.ProjectId = newProject.Id;
                        newElement.SlideId = newSlideId;

                        var savedElement = await _textProjectService.AddElementAsync(newElement);

                        if (sourceElement.RichTextSpans != null && sourceElement.RichTextSpans.Count > 0)
                        {
                            var newSpans = CloneRichTextSpans(sourceElement.RichTextSpans);
                            foreach (var span in newSpans)
                            {
                                span.TextElementId = savedElement.Id;
                            }

                            await _richTextSpanRepository.SaveForTextElementAsync(savedElement.Id, newSpans);
                        }
                    }
                }

                ReloadProjectsPreservingTreeState(TreeItemType.TextProject, newProject.Id);
                await LoadTextProjectAsync(newProject.Id);
                ShowStatus($"已复制项目: {targetName}");
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"复制项目失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        internal async Task<bool> TryCopySelectedTextProjectFromTreeAsync()
        {
            if (ProjectTree?.SelectedItem is not ProjectTreeItem selectedItem)
            {
                return false;
            }

            if (selectedItem.Type != TreeItemType.TextProject)
            {
                return false;
            }

            await CopyTextProjectAsync(selectedItem);
            return true;
        }

        private async Task<string> GenerateCopyProjectNameAsync(string sourceName)
        {
            string baseName = string.IsNullOrWhiteSpace(sourceName) ? "项目" : sourceName.Trim();
            string candidate = $"{baseName} (副本)";

            var existingNames = (await _textProjectService.GetAllProjectsAsync())
                .Select(p => p.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }

            int index = 2;
            while (existingNames.Contains($"{baseName} (副本{index})"))
            {
                index++;
            }

            return $"{baseName} (副本{index})";
        }

        /// <summary>
        /// 导出所有项目
        /// </summary>
        private async Task ExportAllProjectsAsync()
        {
            try
            {
                var slideExportManager = _mainWindowServices.GetRequired<SlideExportManager>();
                if (slideExportManager != null)
                {
                    bool success = await slideExportManager.ExportAllProjectsAsync();
                    if (success)
                    {
                        ShowStatus($"已导出所有项目");
                    }
                    else if (!string.IsNullOrWhiteSpace(slideExportManager.LastError))
                    {
                        ShowStatus($"{slideExportManager.LastError}");
                    }
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"导出所有项目失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 删除文本项目
        /// </summary>
        private async Task DeleteTextProjectAsync(ProjectTreeItem item)
        {
            try
            {
                var result = WpfMessageBox.Show(
                    $"确定要删除项目 '{item.Name}' 吗？\n所有文本元素和背景都将被删除。",
                    "确认删除",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );

                if (result == MessageBoxResult.Yes)
                {
                    await _textProjectService.DeleteProjectAsync(item.Id);

                    // 如果删除的是当前项目，关闭编辑器
                    if (_currentTextProject != null && _currentTextProject.Id == item.Id)
                    {
                        CloseTextEditor();
                    }

                    // 刷新项目树并保留展开状态
                    ReloadProjectsPreservingTreeState();

                    ShowStatus($"已删除项目: {item.Name}");
                    //System.Diagnostics.Debug.WriteLine($" 已删除项目: ID={item.Id}, Name={item.Name}");
                }
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($" 删除项目失败: {ex.Message}");
                WpfMessageBox.Show($"删除项目失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 更新辅助线显示
        /// </summary>
        private void UpdateAlignmentGuides(DraggableTextBox movingBox)
        {
            if (movingBox == null) return;

            double centerX = EditorCanvas.Width / 2;
            double centerY = EditorCanvas.Height / 2;
            
            double boxCenterX = movingBox.Data.X + movingBox.Data.Width / 2;
            double boxCenterY = movingBox.Data.Y + movingBox.Data.Height / 2;

            bool showVerticalCenter = false;
            bool showHorizontalCenter = false;
            bool showVerticalAlign = false;
            bool showHorizontalAlign = false;
            
            double alignX = 0;
            double alignY = 0;

            // 检查是否接近画布中心
            if (Math.Abs(boxCenterX - centerX) < SNAP_THRESHOLD)
            {
                showVerticalCenter = true;
            }
            
            if (Math.Abs(boxCenterY - centerY) < SNAP_THRESHOLD)
            {
                showHorizontalCenter = true;
            }

            // 检查是否与其他文本框对齐
            foreach (var otherBox in _textBoxes)
            {
                if (otherBox == movingBox) continue;

                double otherCenterX = otherBox.Data.X + otherBox.Data.Width / 2;
                double otherCenterY = otherBox.Data.Y + otherBox.Data.Height / 2;

                // 垂直对齐（左、中、右）
                if (Math.Abs(movingBox.Data.X - otherBox.Data.X) < SNAP_THRESHOLD) // 左对齐
                {
                    showVerticalAlign = true;
                    alignX = otherBox.Data.X;
                }
                else if (Math.Abs(boxCenterX - otherCenterX) < SNAP_THRESHOLD) // 中心对齐
                {
                    showVerticalAlign = true;
                    alignX = otherCenterX;
                }
                else if (Math.Abs(movingBox.Data.X + movingBox.Data.Width - otherBox.Data.X - otherBox.Data.Width) < SNAP_THRESHOLD) // 右对齐
                {
                    showVerticalAlign = true;
                    alignX = otherBox.Data.X + otherBox.Data.Width;
                }

                // 水平对齐（上、中、下）
                if (Math.Abs(movingBox.Data.Y - otherBox.Data.Y) < SNAP_THRESHOLD) // 上对齐
                {
                    showHorizontalAlign = true;
                    alignY = otherBox.Data.Y;
                }
                else if (Math.Abs(boxCenterY - otherCenterY) < SNAP_THRESHOLD) // 中心对齐
                {
                    showHorizontalAlign = true;
                    alignY = otherCenterY;
                }
                else if (Math.Abs(movingBox.Data.Y + movingBox.Data.Height - otherBox.Data.Y - otherBox.Data.Height) < SNAP_THRESHOLD) // 下对齐
                {
                    showHorizontalAlign = true;
                    alignY = otherBox.Data.Y + otherBox.Data.Height;
                }
            }

            // 更新辅助线显示
            VerticalCenterLine.Visibility = showVerticalCenter ? Visibility.Visible : Visibility.Collapsed;
            HorizontalCenterLine.Visibility = showHorizontalCenter ? Visibility.Visible : Visibility.Collapsed;
            
            if (showVerticalAlign)
            {
                VerticalAlignLine.X1 = alignX;
                VerticalAlignLine.X2 = alignX;
                VerticalAlignLine.Visibility = Visibility.Visible;
            }
            else
            {
                VerticalAlignLine.Visibility = Visibility.Collapsed;
            }
            
            if (showHorizontalAlign)
            {
                HorizontalAlignLine.Y1 = alignY;
                HorizontalAlignLine.Y2 = alignY;
                HorizontalAlignLine.Visibility = Visibility.Visible;
            }
            else
            {
                HorizontalAlignLine.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 隐藏所有辅助线
        /// </summary>
        private void HideAlignmentGuides()
        {
            VerticalCenterLine.Visibility = Visibility.Collapsed;
            HorizontalCenterLine.Visibility = Visibility.Collapsed;
            VerticalAlignLine.Visibility = Visibility.Collapsed;
            HorizontalAlignLine.Visibility = Visibility.Collapsed;
        }

    }
}
