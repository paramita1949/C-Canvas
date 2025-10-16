using System;
using System.IO;
using System.Windows;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
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
                Image<Rgba32> imageToSave;
                
                if (_imageProcessor.IsInverted)
                {
                    // 应用变色效果
                    imageToSave = _imageProcessor.ApplyYellowTextEffectForSave();
                }
                else
                {
                    imageToSave = _imageProcessor.CurrentImage.Clone();
                }

                // 根据扩展名保存
                var extension = Path.GetExtension(savePath).ToLower();
                
                if (extension == ".jpg" || extension == ".jpeg")
                {
                    imageToSave.Save(savePath, new JpegEncoder { Quality = 95 });
                }
                else if (extension == ".png")
                {
                    imageToSave.Save(savePath, new PngEncoder());
                }
                else
                {
                    // 默认保存为PNG
                    imageToSave.Save(savePath, new PngEncoder());
                }

                // 释放临时图片
                imageToSave.Dispose();

                //System.Diagnostics.Debug.WriteLine($"✅ 图片已保存: {savePath}");
                
                // 静默保存，不显示成功提示（与Python版本一致）
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存图片失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                //System.Diagnostics.Debug.WriteLine($"保存图片失败: {ex}");
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
                
                Image<Rgba32> imageToSave;
                
                if (_imageProcessor.IsInverted)
                {
                    imageToSave = _imageProcessor.ApplyYellowTextEffectForSave();
                }
                else
                {
                    imageToSave = _imageProcessor.CurrentImage.Clone();
                }

                if (extension == ".jpg" || extension == ".jpeg")
                {
                    imageToSave.Save(targetPath, new JpegEncoder { Quality = 95 });
                }
                else
                {
                    imageToSave.Save(targetPath, new PngEncoder());
                }

                imageToSave.Dispose();
                
                return true;
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"快速保存失败: {ex}");
                return false;
            }
        }
    }
}

