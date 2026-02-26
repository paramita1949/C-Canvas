using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Managers;
using ImageColorChanger.Services.TextEditor.Application.Models;
using ImageColorChanger.UI.Controls;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow TextEditor Toolbar Actions
    /// </summary>
    public partial class MainWindow
    {
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
                var dialog = new WpfOpenFileDialog
                {
                    Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif",
                    Title = "选择图片（可多选）",
                    Multiselect = true
                };

                if (dialog.ShowDialog() != true || dialog.FileNames == null || dialog.FileNames.Length == 0)
                    return;

                var fileNames = dialog.FileNames;
                var sortManager = new SortManager();
                var sortedFiles = fileNames
                    .Select(f => new { Path = f, SortKey = sortManager.GetSortKey(System.IO.Path.GetFileName(f)) })
                    .OrderBy(x => x.SortKey.prefixNumber)
                    .ThenBy(x => x.SortKey.pinyinPart)
                    .ThenBy(x => x.SortKey.suffixNumber)
                    .Select(x => x.Path)
                    .ToArray();

                ShowStatus($"正在导入 {sortedFiles.Length} 张图片...");

                int currentOrder = await _textProjectService.GetMaxSlideSortOrderAsync(_currentTextProject.Id);
                var slideCount = await _textProjectService.GetSlideCountAsync(_currentTextProject.Id);

                var newSlides = new List<Slide>();
                for (int i = 0; i < sortedFiles.Length; i++)
                {
                    var imagePath = sortedFiles[i];
                    var newSlide = new Slide
                    {
                        ProjectId = _currentTextProject.Id,
                        Title = $"幻灯片 {slideCount + i + 1}",
                        SortOrder = currentOrder + i + 1,
                        BackgroundImagePath = imagePath,
                        BackgroundColor = null,
                        SplitMode = -1,
                        SplitStretchMode = _splitStretchMode,
                        CreatedTime = DateTime.Now,
                        ModifiedTime = DateTime.Now
                    };

                    newSlides.Add(newSlide);
                }

                await _textProjectService.AddSlidesAsync(newSlides);

                SlideListBox.SelectionChanged -= SlideListBox_SelectionChanged;
                foreach (var slide in newSlides)
                {
                    await LoadSlide(slide);
                    await Task.Delay(150);

                    var thumbnailPath = SaveSlideThumbnail(slide.Id);
                    if (!string.IsNullOrEmpty(thumbnailPath))
                    {
                        slide.ThumbnailPath = thumbnailPath;
                        await _textProjectService.UpdateSlideThumbnailAsync(slide.Id, thumbnailPath);
                    }
                }
                SlideListBox.SelectionChanged += SlideListBox_SelectionChanged;

                await LoadSlideList();
                ShowStatus($"成功导入 {sortedFiles.Length} 张图片");

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

            var dialog = new WpfOpenFileDialog
            {
                Filter = "视频文件|*.mp4;*.avi;*.wmv;*.mov;*.mkv",
                Title = "选择视频背景"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                string videoPath = dialog.FileName;
                _currentSlide.BackgroundImagePath = videoPath;
                _currentSlide.VideoBackgroundEnabled = true;
                _currentSlide.VideoLoopEnabled = true;
                _currentSlide.VideoVolume = 0.0;

                await SaveVideoBackgroundSettingsAsync();

                EditorCanvas.Background = new SolidColorBrush(Colors.Black);
                var oldMediaElements = EditorCanvas.Children.OfType<MediaElement>().ToList();
                foreach (var old in oldMediaElements)
                {
                    old.Stop();
                    old.Close();
                    EditorCanvas.Children.Remove(old);
                }

                var mediaElement = new MediaElement
                {
                    Source = new Uri(videoPath, UriKind.Absolute),
                    LoadedBehavior = MediaState.Manual,
                    UnloadedBehavior = MediaState.Manual,
                    Stretch = Stretch.UniformToFill,
                    Width = EditorCanvas.Width,
                    Height = EditorCanvas.Height,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Volume = 0.0,
                    ScrubbingEnabled = true,
                    CacheMode = new BitmapCache
                    {
                        EnableClearType = false,
                        RenderAtScale = 1.0,
                        SnapsToDevicePixels = true
                    }
                };

                RenderOptions.SetBitmapScalingMode(mediaElement, BitmapScalingMode.LowQuality);
                RenderOptions.SetCachingHint(mediaElement, CachingHint.Cache);
                UpdateVideoLoopBehavior(mediaElement, true);

                Canvas.SetLeft(mediaElement, 0);
                Canvas.SetTop(mediaElement, 0);
                Canvas.SetZIndex(mediaElement, -1);
                EditorCanvas.Children.Insert(0, mediaElement);
                mediaElement.Play();

                if (_projectionManager.IsProjectionActive && !_isProjectionLocked)
                {
                    await Task.Delay(100);
                    await Dispatcher.InvokeAsync(UpdateProjectionFromCanvas, System.Windows.Threading.DispatcherPriority.Render);
                }

                ShowStatus($"已设置视频背景: {System.IO.Path.GetFileName(videoPath)}");
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"设置视频背景失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(dialog.FileName);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    EditorCanvas.Background = new ImageBrush(bitmap) { Stretch = Stretch.Fill };

                    var slideToUpdate = await _textProjectService.GetSlideByIdAsync(_currentSlide.Id);
                    if (slideToUpdate != null)
                    {
                        slideToUpdate.BackgroundImagePath = dialog.FileName;
                        slideToUpdate.BackgroundColor = null;
                        slideToUpdate.ModifiedTime = DateTime.Now;
                        await _textProjectService.UpdateSlideAsync(slideToUpdate);

                        _currentSlide.BackgroundImagePath = dialog.FileName;
                        _currentSlide.BackgroundColor = null;
                    }

                    await _textProjectService.UpdateBackgroundImageAsync(_currentTextProject.Id, dialog.FileName);
                    if (_projectionManager != null && _projectionManager.IsProjectionActive && !_isProjectionLocked)
                    {
                        UpdateProjectionFromCanvas();
                    }

                    MarkContentAsModified();
                }
                catch (Exception ex)
                {
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

            var colorDialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                Color = System.Drawing.Color.White
            };

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
                    var wpfColor = System.Windows.Media.Color.FromArgb(
                        colorDialog.Color.A,
                        colorDialog.Color.R,
                        colorDialog.Color.G,
                        colorDialog.Color.B
                    );

                    var hexColor = $"#{wpfColor.R:X2}{wpfColor.G:X2}{wpfColor.B:X2}";
                    EditorCanvas.Background = new SolidColorBrush(wpfColor);

                    var slideToUpdate = await _textProjectService.GetSlideByIdAsync(_currentSlide.Id);
                    if (slideToUpdate != null)
                    {
                        slideToUpdate.BackgroundColor = hexColor;
                        slideToUpdate.BackgroundImagePath = null;
                        slideToUpdate.ModifiedTime = DateTime.Now;
                        await _textProjectService.UpdateSlideAsync(slideToUpdate);

                        _currentSlide.BackgroundColor = hexColor;
                        _currentSlide.BackgroundImagePath = null;
                    }

                    await _textProjectService.UpdateBackgroundImageAsync(_currentTextProject.Id, null);
                    MarkContentAsModified();
                }
                catch (Exception ex)
                {
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
                EditorCanvas.Background = new SolidColorBrush(Colors.White);
                var slideToUpdate = await _textProjectService.GetSlideByIdAsync(_currentSlide.Id);
                if (slideToUpdate != null)
                {
                    slideToUpdate.BackgroundColor = "#FFFFFF";
                    slideToUpdate.BackgroundImagePath = null;
                    slideToUpdate.ModifiedTime = DateTime.Now;
                    await _textProjectService.UpdateSlideAsync(slideToUpdate);

                    _currentSlide.BackgroundColor = "#FFFFFF";
                    _currentSlide.BackgroundImagePath = null;
                }

                await _textProjectService.UpdateBackgroundImageAsync(_currentTextProject.Id, null);
                MarkContentAsModified();
            }
            catch (Exception ex)
            {
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

                var mirrorElement = _textProjectService.CloneElement(_selectedTextBox.Data);
                mirrorElement.X = mirrorX;
                mirrorElement.IsSymmetricBool = true;
                mirrorElement.SymmetricPairId = _selectedTextBox.Data.Id;
                mirrorElement.SymmetricType = "Horizontal";

                await _textProjectService.AddElementAsync(mirrorElement);

                var mirrorBox = new DraggableTextBox(mirrorElement);
                AddTextBoxToCanvas(mirrorBox);

                _selectedTextBox.PositionChanged += (s, pos) =>
                {
                    double newMirrorX = centerX + (centerX - pos.X - _selectedTextBox.Data.Width);
                    Canvas.SetLeft(mirrorBox, newMirrorX);
                    mirrorBox.Data.X = newMirrorX;
                };
            }
            catch (Exception ex)
            {
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

                var mirrorElement = _textProjectService.CloneElement(_selectedTextBox.Data);
                mirrorElement.Y = mirrorY;
                mirrorElement.IsSymmetricBool = true;
                mirrorElement.SymmetricPairId = _selectedTextBox.Data.Id;
                mirrorElement.SymmetricType = "Vertical";

                await _textProjectService.AddElementAsync(mirrorElement);

                var mirrorBox = new DraggableTextBox(mirrorElement);
                AddTextBoxToCanvas(mirrorBox);

                _selectedTextBox.PositionChanged += (s, pos) =>
                {
                    double newMirrorY = centerY + (centerY - pos.Y - _selectedTextBox.Data.Height);
                    Canvas.SetTop(mirrorBox, newMirrorY);
                    mirrorBox.Data.Y = newMirrorY;
                };
            }
            catch (Exception ex)
            {
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
                var saveResult = await SaveTextEditorStateAsync(
                    SaveTrigger.Manual,
                    _textBoxes,
                    persistAdditionalState: true,
                    saveThumbnail: true);
                if (!saveResult.Succeeded)
                {
                    throw saveResult.Exception ?? new InvalidOperationException("未知保存失败。");
                }

                BtnSaveTextProject.Background = new SolidColorBrush(Colors.White);
                await RefreshSlideList();

                if (_projectionManager.IsProjectionActive && !_isProjectionLocked)
                {
                    UpdateProjectionFromCanvas();
                }
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"保存项目失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 更新投影按钮（核心功能）
        /// </summary>
        private void BtnUpdateProjection_Click(object sender, RoutedEventArgs e)
        {
            UpdateProjectionFromCanvas();
        }
    }
}
