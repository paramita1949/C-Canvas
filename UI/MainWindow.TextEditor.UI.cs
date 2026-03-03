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
    /// MainWindow TextEditor UI (TreeEdit, Projection Lock, Floating Toolbar, Font, Video, Ratio)
    /// </summary>
    public partial class MainWindow
    {
        #region 树节点内联编辑事件

        /// <summary>
        /// 编辑框加载时自动聚焦并定位光标到末尾
        /// </summary>
        private void TreeItemEditBox_Loaded(object sender, RoutedEventArgs e)
        {
            // //System.Diagnostics.Debug.WriteLine($"TreeItemEditBox_Loaded 触发");
            
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                // //System.Diagnostics.Debug.WriteLine($"TextBox 实例: Text={textBox.Text}, Visibility={textBox.Visibility}");
                
                if (textBox.DataContext is ProjectTreeItem item)
                {
                    // //System.Diagnostics.Debug.WriteLine($"DataContext: Name={item.Name}, IsEditing={item.IsEditing}");
                    
                    // 只在编辑模式时才聚焦
                    if (!item.IsEditing)
                    {
                        // //System.Diagnostics.Debug.WriteLine($" IsEditing=false，跳过聚焦");
                        return;
                    }
                    
                    //System.Diagnostics.Debug.WriteLine($"编辑框加载: Text={textBox.Text}, IsEditing={item.IsEditing}");
                    
                    // 延迟聚焦，确保UI完全加载
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (item.IsEditing) // 再次检查，防止在延迟期间状态改变
                        {
                            bool focused = textBox.Focus();
                            textBox.CaretIndex = textBox.Text.Length; // 光标定位到末尾
                            //System.Diagnostics.Debug.WriteLine($" 编辑框已聚焦: Success={focused}, 光标位置: {textBox.CaretIndex}");
                        }
                        else
                        {
                            //System.Diagnostics.Debug.WriteLine($" 延迟检查时 IsEditing=false");
                        }
                    }), System.Windows.Threading.DispatcherPriority.Input);
                }
                else
                {
                    //System.Diagnostics.Debug.WriteLine($" DataContext 不是 ProjectTreeItem: {textBox.DataContext?.GetType().Name}");
                }
            }
            else
            {
                //System.Diagnostics.Debug.WriteLine($" sender 不是 TextBox: {sender?.GetType().Name}");
            }
        }

        /// <summary>
        /// 编辑框按键处理（Enter 保存，Esc 取消）
        /// </summary>
        private async void TreeItemEditBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox textBox && 
                textBox.DataContext is ProjectTreeItem item)
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    // 回车保存
                    await CompleteRenameAsync(item, textBox.Text);
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.Escape)
                {
                    // Esc 取消
                    item.IsEditing = false;
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// 编辑框失去焦点时保存
        /// </summary>
        private async void TreeItemEditBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox textBox && 
                textBox.DataContext is ProjectTreeItem item)
            {
                await CompleteRenameAsync(item, textBox.Text);
            }
        }

        /// <summary>
        /// 编辑框可见性改变时处理
        /// </summary>
        private void TreeItemEditBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox textBox && 
                textBox.DataContext is ProjectTreeItem item)
            {
                bool isVisible = (bool)e.NewValue;
                //System.Diagnostics.Debug.WriteLine($"编辑框可见性改变: IsVisible={isVisible}, IsEditing={item.IsEditing}, Name={item.Name}");
                
                // 当变为可见且处于编辑模式时，聚焦并定位光标
                if (isVisible && item.IsEditing)
                {
                    //System.Diagnostics.Debug.WriteLine($"编辑框变为可见，准备聚焦");
                    
                    // 延迟聚焦，确保控件完全渲染
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (item.IsEditing && textBox.IsVisible)
                        {
                            bool focused = textBox.Focus();
                            textBox.CaretIndex = textBox.Text.Length;
                            //System.Diagnostics.Debug.WriteLine($" 编辑框已聚焦: Success={focused}, CaretIndex={textBox.CaretIndex}, IsFocused={textBox.IsFocused}");
                        }
                    }), System.Windows.Threading.DispatcherPriority.Input);
                }
            }
        }

        /// <summary>
        /// 根据字体族名称查找FontFamily对象
        /// </summary>
        private System.Windows.Media.FontFamily FindFontFamilyByName(string fontFamilyName)
        {
            if (string.IsNullOrEmpty(fontFamilyName))
                return null;

            // 遍历ComboBox中的所有字体项
            foreach (var item in FontFamilySelector.Items)
            {
                if (item is ComboBoxItem comboItem && comboItem.Tag is FontItemData fontData)
                {
                    // 匹配字体族名称
                    if (fontData.Config.Family == fontFamilyName || 
                        fontData.Config.Name == fontFamilyName ||
                        fontFamilyName.Contains(fontData.Config.Family))
                    {
                        return fontData.FontFamily;
                    }
                }
            }

            // 如果没找到，返回null（将使用系统默认字体）
            //System.Diagnostics.Debug.WriteLine($" 未找到字体: {fontFamilyName}，将使用默认字体");
            return null;
        }

        /// <summary>
        /// 退出文本编辑器按钮点击事件
        /// </summary>
        private async void BtnCloseTextEditor_Click(object sender, RoutedEventArgs e)
        {
            // 检查是否有未保存的更改
            if (_currentTextProject != null && BtnSaveTextProject.Background is SolidColorBrush brush && 
                brush.Color == Colors.LightGreen)
            {
                var result = WpfMessageBox.Show(
                    "当前项目有未保存的更改，是否保存？", 
                    "提示", 
                    MessageBoxButton.YesNoCancel, 
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var saveResult = await SaveTextEditorStateAsync(
                        Services.TextEditor.Application.Models.SaveTrigger.AutoExit,
                        _textBoxes,
                        persistAdditionalState: true,
                        saveThumbnail: true);
                    if (!saveResult.Succeeded)
                    {
                        WpfMessageBox.Show(
                            $"保存失败，已取消退出：{saveResult.Exception?.Message}",
                            "保存失败",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    BtnSaveTextProject.Background = new SolidColorBrush(Colors.White);
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    // 取消退出
                    return;
                }
                // No: 不保存，直接退出
            }

            // 关闭文本编辑器
            CloseTextEditor();
            
            //System.Diagnostics.Debug.WriteLine("状态:  已退出文本编辑器，返回图片/视频浏览模式");
        }

        #endregion

        #region 投影锁定功能

        /// <summary>
        /// 投影锁定状态（true=锁定，切换幻灯片不自动更新投影；false=未锁定，自动更新）
        /// </summary>
        private bool _isProjectionLocked = false;

        /// <summary>
        /// 锁定投影按钮点击事件
        /// </summary>
        private void BtnLockProjection_Click(object sender, RoutedEventArgs e)
        {
            // 切换锁定状态
            _isProjectionLocked = !_isProjectionLocked;

            // 更新按钮显示
            if (_isProjectionLocked)
            {
                // 锁定状态：设置橙色，Tag标记锁定（样式会根据Tag禁用悬停效果）
                SetLockProjectionButtonContent(true);
                BtnLockProjection.ToolTip = "投影已锁定：切换幻灯片不会自动更新投影，点击解锁";
                BtnLockProjection.Tag = "Locked";
                BtnLockProjection.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 165, 0));
                BtnLockProjection.Foreground = new SolidColorBrush(Colors.White);
                
                //  锁定模式：如果当前有视频背景，切换到独立 MediaElement 模式
#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [BtnLockProjection] ===== 锁定投影 =====");
                //System.Diagnostics.Debug.WriteLine($" [BtnLockProjection] VideoBackgroundEnabled: {_currentSlide?.VideoBackgroundEnabled ?? false}");
                //System.Diagnostics.Debug.WriteLine($" [BtnLockProjection] BackgroundImagePath: {_currentSlide?.BackgroundImagePath ?? "null"}");
                //System.Diagnostics.Debug.WriteLine($" [BtnLockProjection] IsVideoFile: {(_currentSlide != null && !string.IsNullOrEmpty(_currentSlide.BackgroundImagePath) ? IsVideoFile(_currentSlide.BackgroundImagePath) : false)}");
                //System.Diagnostics.Debug.WriteLine($" [BtnLockProjection] IsProjectionActive: {_projectionManager?.IsProjectionActive ?? false}");
#endif
                if (_currentSlide?.VideoBackgroundEnabled == true && 
                    !string.IsNullOrEmpty(_currentSlide?.BackgroundImagePath) &&
                    IsVideoFile(_currentSlide.BackgroundImagePath) &&
                    _projectionManager.IsProjectionActive)
                {
#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($" [BtnLockProjection] 切换到独立 MediaElement 模式");
                    //System.Diagnostics.Debug.WriteLine($" [BtnLockProjection] 视频路径: {_currentSlide.BackgroundImagePath}");
                    //System.Diagnostics.Debug.WriteLine($" [BtnLockProjection] 循环播放: {_currentSlide.VideoLoopEnabled}");
#endif
                    var (projWidth, projHeight) = _projectionManager?.GetCurrentProjectionPhysicalSize() ?? (1920, 1080);
                    var textLayer = ComposeCanvasWithSkia(
                        projWidth,
                        projHeight,
                        transparentBackground: true,
                        hideNoticeComponents: _hideNoticeOnProjection);
                    _projectionManager.UpdateProjectionWithLockedVideo(
                        _currentSlide.BackgroundImagePath,
                        _currentSlide.VideoLoopEnabled,
                        textLayer);
                }
#if DEBUG
                //else
                //{
                //    System.Diagnostics.Debug.WriteLine($" [BtnLockProjection] 条件不满足，未切换到独立 MediaElement 模式");
                //    System.Diagnostics.Debug.WriteLine($"   - VideoBackgroundEnabled: {_currentSlide?.VideoBackgroundEnabled ?? false}");
                //    System.Diagnostics.Debug.WriteLine($"   - BackgroundImagePath为空: {string.IsNullOrEmpty(_currentSlide?.BackgroundImagePath)}");
                //    System.Diagnostics.Debug.WriteLine($"   - IsVideoFile: {(_currentSlide != null && !string.IsNullOrEmpty(_currentSlide.BackgroundImagePath) ? IsVideoFile(_currentSlide.BackgroundImagePath) : false)}");
                //    System.Diagnostics.Debug.WriteLine($"   - IsProjectionActive: {_projectionManager?.IsProjectionActive ?? false}");
                //}
#endif
            }
            else
            {
                // 未锁定状态：恢复默认蓝色
                SetLockProjectionButtonContent(false);
                BtnLockProjection.ToolTip = "投影未锁定：切换幻灯片自动更新投影，点击锁定";
                BtnLockProjection.Tag = null;
                BtnLockProjection.ClearValue(System.Windows.Controls.Button.BackgroundProperty);
                BtnLockProjection.ClearValue(System.Windows.Controls.Button.ForegroundProperty);
                
                //  解锁模式：清理独立 MediaElement，切换回 VisualBrush 模式
                _projectionManager.ClearLockedVideo();
                
                // 如果当前有视频背景，重新使用 VisualBrush 模式更新投影
                if (_currentSlide?.VideoBackgroundEnabled == true && 
                    !string.IsNullOrEmpty(_currentSlide?.BackgroundImagePath) &&
                    IsVideoFile(_currentSlide.BackgroundImagePath) &&
                    _projectionManager.IsProjectionActive)
                {
                    UpdateProjectionWithVideoBackground();
                }
            }
        }

        #endregion

    }
}


