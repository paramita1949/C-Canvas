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
    /// MainWindow TextEditor Misc (Floating Toolbar, Font, Video Background, Aspect Ratio)
    /// </summary>
    public partial class MainWindow
    {
        #region 浮动工具栏

        /// <summary>
        /// 显示文本框浮动工具栏（圣经工具栏）
        /// </summary>
        private void ShowTextBoxFloatingToolbar(DraggableTextBox textBox)
        {
            if (textBox == null)
                return;

            try
            {
                // 显示圣经工具栏（悬浮在画布上方固定位置）
                if (BibleToolbar != null)
                {
                    BibleToolbar.IsOpen = true;
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($" [圣经工具栏] 已显示");
                    //#endif
                }
            }
            catch (Exception ex)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [浮动工具栏] 显示失败: {ex.Message}");
                //#else
                _ = ex;  // 防止未使用变量警告
                //#endif
            }
        }

        #endregion

        #region 字体名称处理

        /// <summary>
        /// 清理字体名称：从 WPF 格式转换为纯字体名称
        /// WPF 格式：./CCanvas_Fonts/思源宋体-Regular.ttf#思源宋体
        /// 纯字体名：思源宋体
        /// </summary>
        private string CleanFontFamilyName(string fontFamily)
        {
            if (string.IsNullOrEmpty(fontFamily))
                return "Microsoft YaHei UI";
            
            // 检查是否是 WPF 格式 (包含 # 符号)
            if (fontFamily.Contains("#"))
            {
                // 提取 # 后面的字体名称
                int hashIndex = fontFamily.IndexOf('#');
                return fontFamily.Substring(hashIndex + 1);
            }
            
            return fontFamily;
        }

        #endregion

        #region  视频背景控制功能

        /// <summary>
        /// 更新 MediaElement 的循环播放行为
        /// </summary>
        private void UpdateVideoLoopBehavior(MediaElement mediaElement, bool loop)
        {
            if (mediaElement == null)
                return;

            // 先移除旧的事件处理器
            mediaElement.MediaEnded -= OnVideoMediaEnded;

            // 如果启用循环，添加新的事件处理器
            if (loop)
            {
                mediaElement.MediaEnded += OnVideoMediaEnded;
            }

#if DEBUG
            //System.Diagnostics.Debug.WriteLine($" [UpdateVideoLoopBehavior] 循环播放: {loop}");
#endif
        }

        /// <summary>
        /// 视频播放结束事件处理（循环播放）
        /// </summary>
        private void OnVideoMediaEnded(object sender, RoutedEventArgs e)
        {
            if (sender is MediaElement mediaElement)
            {
#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [OnVideoMediaEnded] 视频循环播放，当前位置: {mediaElement.Position}");
#endif
                mediaElement.Position = TimeSpan.Zero;
                mediaElement.Play();
                
#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [OnVideoMediaEnded] 已重置并重新播放");
#endif
            }
        }

#if DEBUG
        // 帧率监控变量
        private System.Diagnostics.Stopwatch _frameMonitorStopwatch = System.Diagnostics.Stopwatch.StartNew();
        private int _frameCount = 0;
        private DateTime _lastFrameReport = DateTime.Now;
#endif

        /// <summary>
        /// 监控 VisualBrush 刷新率（调试用）
        /// </summary>
        private void MonitorVisualBrushFrameRate()
        {
#if DEBUG
            _frameCount++;
            
            // 每秒报告一次帧率
            if ((DateTime.Now - _lastFrameReport).TotalSeconds >= 1.0)
            {
                double fps = _frameCount / _frameMonitorStopwatch.Elapsed.TotalSeconds;
                //System.Diagnostics.Debug.WriteLine($"[VisualBrush FPS] 当前帧率: {fps:F1} FPS");
                
                // 重置计数器
                _frameCount = 0;
                _frameMonitorStopwatch.Restart();
                _lastFrameReport = DateTime.Now;
            }
#endif
        }

        /// <summary>
        /// 保存视频背景设置到数据库
        /// </summary>
        private async Task SaveVideoBackgroundSettingsAsync()
        {
            if (_currentSlide == null)
                return;
                
            try
            {
                var slideToUpdate = await _textProjectService.GetSlideByIdAsync(_currentSlide.Id);
                if (slideToUpdate != null)
                {
                    slideToUpdate.BackgroundImagePath = _currentSlide.BackgroundImagePath;
                    slideToUpdate.VideoBackgroundEnabled = _currentSlide.VideoBackgroundEnabled;
                    slideToUpdate.VideoLoopEnabled = _currentSlide.VideoLoopEnabled;
                    slideToUpdate.VideoVolume = _currentSlide.VideoVolume;
                    slideToUpdate.ModifiedTime = DateTime.Now;
                    await _textProjectService.UpdateSlideAsync(slideToUpdate);
                    
                    // 更新本地缓存
                    _currentSlide.BackgroundImagePath = slideToUpdate.BackgroundImagePath;
                    _currentSlide.VideoBackgroundEnabled = slideToUpdate.VideoBackgroundEnabled;
                    _currentSlide.VideoLoopEnabled = slideToUpdate.VideoLoopEnabled;
                    _currentSlide.VideoVolume = slideToUpdate.VideoVolume;
                    
                    #if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"[SaveVideoBackgroundSettings] 已保存视频背景设置: SlideId={slideToUpdate.Id}");
                    #endif
                }
            }
            catch (Exception
#if DEBUG
                ex
#endif
            )
            {
#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [SaveVideoBackgroundSettings] 失败: {ex.Message}");
                _ = ex;
#endif
            }
        }

        #endregion

        #region 画布比例切换

        /// <summary>
        /// 画布比例切换按钮点击事件
        /// </summary>
        private void BtnCanvasAspectRatio_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 切换比例
                string currentRatio = _configManager.CanvasAspectRatio;
                string newRatio = currentRatio == "16:9" ? "4:3" : "16:9";

                // 保存到配置
                _configManager.CanvasAspectRatio = newRatio;

                // 应用新比例
                ApplyCanvasAspectRatio(newRatio);

                // 更新按钮文本（显示中文描述）
                if (BtnCanvasAspectRatioInPanel != null)
                {
                    BtnCanvasAspectRatioInPanel.Content = newRatio == "16:9" ? "宽屏幕" : "窄屏幕";
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($" [画布比例切换] 失败: {ex.Message}");
#else
                _ = ex;
#endif
            }
        }

        /// <summary>
        /// 应用画布比例设置
        /// </summary>
        private void ApplyCanvasAspectRatio(string ratio)
        {
            try
            {
                int width, height;

                if (ratio == "16:9")
                {
                    // 16:9 比例：1600×900
                    width = 1600;
                    height = 900;
                }
                else // 4:3
                {
                    // 4:3 比例：960×720（更小以更适合编辑）
                    width = 960;
                    height = 720;
                }

                // 更新画布尺寸
                if (EditorCanvasContainer != null)
                {
                    EditorCanvasContainer.Width = width;
                    EditorCanvasContainer.Height = height;
                }

                if (EditorCanvas != null)
                {
                    EditorCanvas.Width = width;
                    EditorCanvas.Height = height;
                }

                // 更新对齐线的长度
                if (VerticalAlignLine != null)
                {
                    VerticalAlignLine.Y2 = height;
                }

                if (HorizontalAlignLine != null)
                {
                    HorizontalAlignLine.X2 = width;
                }

                // 如果有当前幻灯片，更新分割布局
                if (_currentSlide != null && _currentSlide.SplitMode >= 0)
                {
                    UpdateSplitLayout((Database.Models.Enums.ViewSplitMode)_currentSlide.SplitMode);
                }

                // 更新视频背景尺寸（如果存在）
                UpdateVideoBackgroundSize(width, height);

                // 更新浮动工具栏位置（根据画布高度调整）
                UpdateBibleToolbarPosition(height);

                // 强制更新布局
                EditorCanvas?.UpdateLayout();
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($" [应用画布比例] 失败: {ex.Message}");
#else
                _ = ex;
#endif
            }
        }

        /// <summary>
        /// 初始化画布比例（在编辑器初始化时调用）
        /// </summary>
        private void InitializeCanvasAspectRatio()
        {
            try
            {
                // 从配置加载比例
                string ratio = _configManager.CanvasAspectRatio;

                // 应用比例
                ApplyCanvasAspectRatio(ratio);

                // 更新按钮文本（显示中文描述）
                if (BtnCanvasAspectRatioInPanel != null)
                {
                    BtnCanvasAspectRatioInPanel.Content = ratio == "16:9" ? "宽屏幕" : "窄屏幕";
                }

                // 初始化工具栏位置
                int height = ratio == "16:9" ? 900 : 720;
                UpdateBibleToolbarPosition(height);
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($" [初始化画布比例] 失败: {ex.Message}");
#else
                _ = ex;
#endif
            }
        }

        /// <summary>
        /// 更新视频背景尺寸（比例切换时调用）
        /// </summary>
        private void UpdateVideoBackgroundSize(double width, double height)
        {
            try
            {
                if (EditorCanvas == null)
                    return;

                // 查找 Canvas 中的 MediaElement
                var mediaElements = EditorCanvas.Children.OfType<MediaElement>().ToList();
                foreach (var mediaElement in mediaElements)
                {
                    mediaElement.Width = width;
                    mediaElement.Height = height;
                }

#if DEBUG
                if (mediaElements.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[视频尺寸] 已更新 {mediaElements.Count} 个视频背景尺寸: {width}×{height}");
                }
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($" [更新视频尺寸] 失败: {ex.Message}");
#else
                _ = ex;
#endif
            }
        }

        /// <summary>
        /// 更新浮动工具栏位置（根据画布高度调整）
        /// </summary>
        private void UpdateBibleToolbarPosition(double canvasHeight)
        {
            try
            {
                if (BibleToolbar != null)
                {
                    // 工具栏位置 = -(画布高度/2 + 45)
                    // 这样工具栏始终悬浮在画布上方中央，距离画布顶部约45px（按钮放大后）
                    double offset = -(canvasHeight / 2 + 45);
                    BibleToolbar.VerticalOffset = offset;

#if DEBUG
                    // System.Diagnostics.Debug.WriteLine($"[工具栏位置] 画布高度={canvasHeight}, VerticalOffset={offset:F0}");
#endif
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                // System.Diagnostics.Debug.WriteLine($" [更新工具栏位置] 失败: {ex.Message}");
                _ = ex;
#else
                _ = ex;
#endif
            }
        }

        #endregion

    }
}
