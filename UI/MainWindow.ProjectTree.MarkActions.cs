using System;
using System.Windows;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;

using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 项目树：原图/变色标记动作
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// 标记文件夹自动变色
        /// </summary>
        private void MarkFolderColorEffect(ProjectTreeItem item)
        {
            try
            {
                var dbManager = DatabaseManagerService;
                dbManager.MarkFolderAutoColorEffect(item.Id);
                ReloadProjectsPreservingTreeState();

                bool shouldApplyEffect = false;

                if (_currentImageId > 0 && _imageProcessor.CurrentImage != null)
                {
                    var currentMediaFile = dbManager.GetMediaFileById(_currentImageId);
                    if (currentMediaFile != null && currentMediaFile.FolderId.HasValue)
                    {
                        shouldApplyEffect = currentMediaFile.FolderId.Value == item.Id;
                    }
                }

                if (shouldApplyEffect)
                {
                    _isColorEffectEnabled = true;
                    _imageProcessor.IsInverted = true;
                    BtnColorEffect.Background = new SolidColorBrush(Color.FromRgb(255, 215, 0));
                    _currentFolderId = item.Id;
                    _imageProcessor.UpdateImage();
                    UpdateProjection();
                    ShowStatus($"已标记文件夹 [{item.Name}] 自动变色（当前图片已应用变色效果）");
                }
                else
                {
                    ShowStatus($"已标记文件夹 [{item.Name}] 自动变色（点击图片时将自动应用）");
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 取消文件夹变色标记
        /// </summary>
        private void UnmarkFolderColorEffect(ProjectTreeItem item)
        {
            try
            {
                var dbManager = DatabaseManagerService;
                dbManager.UnmarkFolderAutoColorEffect(item.Id);
                ReloadProjectsPreservingTreeState();

                bool shouldRemoveEffect = false;

                if (_currentImageId > 0 && _imageProcessor.CurrentImage != null)
                {
                    var currentMediaFile = dbManager.GetMediaFileById(_currentImageId);
                    if (currentMediaFile != null && currentMediaFile.FolderId.HasValue)
                    {
                        shouldRemoveEffect = currentMediaFile.FolderId.Value == item.Id;
                    }
                }

                if (shouldRemoveEffect)
                {
                    _isColorEffectEnabled = false;
                    _imageProcessor.IsInverted = false;
                    BtnColorEffect.Background = Brushes.Transparent;
                    _imageProcessor.UpdateImage();
                    UpdateProjection();
                    ShowStatus($"已取消文件夹 [{item.Name}] 的变色标记（当前图片已恢复正常）");
                }
                else
                {
                    ShowStatus($"已取消文件夹 [{item.Name}] 的变色标记");
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 标记文件夹为原图
        /// </summary>
        private void MarkFolderAsOriginal(ProjectTreeItem item, MarkType markType)
        {
            bool success = _originalManager.AddOriginalMark(ItemType.Folder, item.Id, markType);

            if (success)
            {
                string modeText = markType == MarkType.Loop ? "循环" : "顺序";
                ShowStatus($"已标记文件夹为原图({modeText}): {item.Name}");
                ReloadProjectsPreservingTreeState();
            }
            else
            {
                ShowStatus($"标记文件夹失败: {item.Name}");
            }
        }

        /// <summary>
        /// 取消文件夹原图标记
        /// </summary>
        private void UnmarkOriginalFolder(ProjectTreeItem item)
        {
            bool success = _originalManager.RemoveOriginalMark(ItemType.Folder, item.Id);

            if (success)
            {
                ShowStatus($"已取消文件夹原图标记: {item.Name}");
                ReloadProjectsPreservingTreeState();
            }
            else
            {
                ShowStatus($"取消文件夹标记失败: {item.Name}");
            }
        }

        /// <summary>
        /// 标记为原图
        /// </summary>
        private void MarkAsOriginal(ProjectTreeItem item, MarkType markType)
        {
            bool success = _originalManager.AddOriginalMark(ItemType.Image, item.Id, markType);

            if (!success)
            {
                ShowStatus($"标记失败: {item.Name}");
                return;
            }

            string modeText = markType == MarkType.Loop ? "循环" : "顺序";
            ShowStatus($"已标记为原图({modeText}): {item.Name}");
            ReloadProjectsPreservingTreeState();

            if (_currentImageId == item.Id && !_originalMode)
            {
                _originalMode = true;
                _imageProcessor.OriginalMode = true;
                BtnOriginal.Background = new SolidColorBrush(Color.FromRgb(144, 238, 144));
                _originalManager.FindSimilarImages(_currentImageId);
                _imageProcessor.UpdateImage();
                UpdateProjection();
                ShowStatus("已自动启用原图模式");
            }
        }

        /// <summary>
        /// 取消原图标记
        /// </summary>
        private void UnmarkOriginal(ProjectTreeItem item)
        {
            bool success = _originalManager.RemoveOriginalMark(ItemType.Image, item.Id);

            if (!success)
            {
                ShowStatus($"取消标记失败: {item.Name}");
                return;
            }

            ShowStatus($"已取消原图标记: {item.Name}");
            ReloadProjectsPreservingTreeState();

            if (_currentImageId == item.Id && _originalMode)
            {
                _originalMode = false;
                _imageProcessor.OriginalMode = false;
                BtnOriginal.Background = Brushes.Transparent;
                _imageProcessor.UpdateImage();
                UpdateProjection();
                ShowStatus("已自动关闭原图模式");
            }
        }
    }
}


