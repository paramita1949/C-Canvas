using System;
using System.Windows;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 顶部按钮与视图状态
    /// </summary>
    public partial class MainWindow
    {
        private void BtnSync_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnSync.IsEnabled = false;
                BtnSync.Background = new SolidColorBrush(Colors.LightGreen);

                var (added, removed, updated) = ImportManagerService.SyncAllFolders();

                ReloadProjectsPreservingTreeState();
                LoadSearchScopes();

                ShowStatus($"同步完成: 新增 {added}, 删除 {removed}");
            }
            catch (Exception ex)
            {
                ShowStatus($"同步失败: {ex.Message}");
            }
            finally
            {
                BtnSync.IsEnabled = true;
                BtnSync.Background = Brushes.Transparent;
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            ResetView();
        }

        private void BtnOriginal_Click(object sender, RoutedEventArgs e)
        {
            ToggleOriginalMode();
        }

        private void BtnZoomReset_Click(object sender, RoutedEventArgs e)
        {
            ResetZoom();
            ShowStatus("已重置缩放比例");
        }

        /// <summary>
        /// 切换原图模式
        /// </summary>
        private void ToggleOriginalMode()
        {
            _originalMode = !_originalMode;
            _imageProcessor.OriginalMode = _originalMode;

            if (_originalMode)
            {
                BtnOriginal.Background = new SolidColorBrush(Color.FromRgb(144, 238, 144));
                ShowStatus("已启用原图模式");

                // 启用原图模式时恢复独立持久化的原图滚轮缩放值。
                _currentZoom = _originalModeZoomRatio;
                SetZoom(_currentZoom);

                if (_currentImageId > 0)
                {
                    _ = _originalManager.FindSimilarImages(_currentImageId);
                }
            }
            else
            {
                BtnOriginal.Background = Brushes.Transparent;
                ShowStatus("已关闭原图模式");
            }

            _imageProcessor.UpdateImage();
            UpdateProjection();
        }

        /// <summary>
        /// 重置视图状态以进入文本编辑器
        /// </summary>
        private void ResetViewStateForTextEditor()
        {
            if (_originalMode)
            {
                _originalMode = false;
                _imageProcessor.OriginalMode = false;
                BtnOriginal.Background = Brushes.Transparent;
            }

            if (Math.Abs(_imageProcessor.ZoomRatio - 1.0) > 0.001)
            {
                _imageProcessor.ZoomRatio = 1.0;
            }

            if (_isColorEffectEnabled)
            {
                _isColorEffectEnabled = false;
                BtnColorEffect.Background = Brushes.Transparent;
            }

            ClearImageDisplay();
            UpdateFloatingCompositePlayButton();
        }
    }
}


