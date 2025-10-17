using System;
using System.IO;
using System.Windows;
using SkiaSharp;
using ImageColorChanger.Core;
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 图片保存管理器
    /// 负责处理图片保存功能（支持原图和效果图）
    /// </summary>
    public class ImageSaveManager
    {
        private readonly ImageProcessor _imageProcessor;

        /// <summary>
        /// 构造函数
        /// </summary>
        public ImageSaveManager(ImageProcessor imageProcessor)
        {
            _imageProcessor = imageProcessor ?? throw new ArgumentNullException(nameof(imageProcessor));
        }

        /// <summary>
        /// 保存当前显示的图片（根据效果状态）
        /// </summary>
        /// <param name="currentImagePath">当前图片路径（用于建议文件名）</param>
        /// <returns>保存是否成功</returns>
        public bool SaveEffectImage(string currentImagePath = null)
        {
            if (_imageProcessor.CurrentImage == null)
            {
                MessageBox.Show("请先打开一张图片！", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            try
            {
                // 确定效果类型
                string effectName = _imageProcessor.IsInverted ? "变色" : "原图";

                // 准备保存对话框
                var saveDialog = new SaveFileDialog
                {
                    Title = $"另存{effectName}图",
                    Filter = "PNG 图片|*.png|JPEG 图片|*.jpg;*.jpeg|所有文件|*.*",
                    DefaultExt = ".png",
                    FilterIndex = 1
                };

                // 设置建议的文件名和初始目录
                if (!string.IsNullOrEmpty(currentImagePath) && File.Exists(currentImagePath))
                {
                    var fileInfo = new FileInfo(currentImagePath);
                    saveDialog.FileName = fileInfo.Name;
                    saveDialog.InitialDirectory = fileInfo.DirectoryName;
                }
                else
                {
                    saveDialog.FileName = $"{effectName}图片.png";
                    saveDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                }

                // 显示对话框
                if (saveDialog.ShowDialog() != true)
                {
                    return false; // 用户取消
                }

                var savePath = saveDialog.FileName;

                // 根据效果状态准备要保存的图片
                SKBitmap imageToSave;
                
                if (_imageProcessor.IsInverted)
                {
                    // 应用变色效果
                    imageToSave = _imageProcessor.ApplyYellowTextEffectForSave();
                }
                else
                {
                    imageToSave = _imageProcessor.CurrentImage.Copy();
                }

                if (imageToSave == null)
                {
                    MessageBox.Show("准备保存图片失败！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                // 根据扩展名保存
                var extension = Path.GetExtension(savePath).ToLower();
                
                bool saveSuccess = false;
                using (var stream = File.OpenWrite(savePath))
                {
                    if (extension == ".jpg" || extension == ".jpeg")
                    {
                        saveSuccess = imageToSave.Encode(stream, SKEncodedImageFormat.Jpeg, 95);
                    }
                    else if (extension == ".png")
                    {
                        saveSuccess = imageToSave.Encode(stream, SKEncodedImageFormat.Png, 100);
                    }
                    else
                    {
                        // 默认保存为PNG
                        saveSuccess = imageToSave.Encode(stream, SKEncodedImageFormat.Png, 100);
                    }
                }

                // 释放临时图片
                if (_imageProcessor.IsInverted)
                {
                    imageToSave.Dispose();
                }
                
                // 静默保存，不显示成功提示（与Python版本一致）
                return saveSuccess;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存图片失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// 快速导出原图（不弹对话框，直接保存到指定路径）
        /// </summary>
        public bool QuickSave(string targetPath)
        {
            if (_imageProcessor.CurrentImage == null)
            {
                return false;
            }

            try
            {
                var extension = Path.GetExtension(targetPath).ToLower();
                
                SKBitmap imageToSave;
                
                if (_imageProcessor.IsInverted)
                {
                    imageToSave = _imageProcessor.ApplyYellowTextEffectForSave();
                }
                else
                {
                    imageToSave = _imageProcessor.CurrentImage.Copy();
                }

                if (imageToSave == null)
                    return false;

                bool saveSuccess = false;
                using (var stream = File.OpenWrite(targetPath))
                {
                    if (extension == ".jpg" || extension == ".jpeg")
                    {
                        saveSuccess = imageToSave.Encode(stream, SKEncodedImageFormat.Jpeg, 95);
                    }
                    else
                    {
                        saveSuccess = imageToSave.Encode(stream, SKEncodedImageFormat.Png, 100);
                    }
                }

                if (_imageProcessor.IsInverted)
                {
                    imageToSave.Dispose();
                }
                
                return saveSuccess;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
