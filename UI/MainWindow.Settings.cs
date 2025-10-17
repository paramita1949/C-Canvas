using System;
using System.Windows;
using SkiaSharp;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        #region 设置管理

        /// <summary>
        /// 加载用户设置 - 从 config.json
        /// </summary>
        private void LoadSettings()
        {
            try
            {
                // 从 ConfigManager 加载原图显示模式
                _originalDisplayMode = _configManager.OriginalDisplayMode;
                _imageProcessor.OriginalDisplayModeValue = _originalDisplayMode;
                
                // 加载缩放比例
                _currentZoom = _configManager.ZoomRatio;
                
                // 加载目标颜色
                _currentTargetColor = new SKColor(
                    _configManager.TargetColorR,
                    _configManager.TargetColorG,
                    _configManager.TargetColorB
                );
                _currentTargetColorName = _configManager.TargetColorName ?? "淡黄";
                
                // 加载导航栏宽度
                if (NavigationPanelColumn != null)
                {
                    NavigationPanelColumn.Width = new GridLength(_configManager.NavigationPanelWidth);
                }
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 加载设置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 保存用户设置 - 到 config.json
        /// </summary>
        private void SaveSettings()
        {
            try
            {
                // 保存原图显示模式到 ConfigManager
                _configManager.OriginalDisplayMode = _originalDisplayMode;
                
                // 保存缩放比例
                _configManager.ZoomRatio = _currentZoom;
                
                // 使用 ConfigManager 的统一方法保存目标颜色
                _configManager.SetCurrentColor(_currentTargetColor.Red, _currentTargetColor.Green, _currentTargetColor.Blue, _currentTargetColorName);
                
                // System.Diagnostics.Debug.WriteLine($"✅ 已保存设置到 config.json (颜色: {_currentTargetColorName})");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 保存设置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置文件夹字号
        /// </summary>
        private void SetFolderFontSize(double size)
        {
            _configManager.FolderFontSize = size;
            OnPropertyChanged(nameof(FolderFontSize));
            ShowStatus($"✅ 文件夹字号已设置为: {size}");
        }

        /// <summary>
        /// 设置文件字号
        /// </summary>
        private void SetFileFontSize(double size)
        {
            _configManager.FileFontSize = size;
            OnPropertyChanged(nameof(FileFontSize));
            ShowStatus($"✅ 文件字号已设置为: {size}");
        }

        /// <summary>
        /// 设置文件夹标签字号（搜索结果显示）
        /// </summary>
        private void SetFolderTagFontSize(double size)
        {
            _configManager.FolderTagFontSize = size;
            OnPropertyChanged(nameof(FolderTagFontSize));
            ShowStatus($"✅ 文件夹标签字号已设置为: {size}");
        }

        #endregion
    }
}

