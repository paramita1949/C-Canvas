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
    /// MainWindow TextEditor Canvas Events
    /// </summary>
    public partial class MainWindow
    {
        #region 画布事件

        /// <summary>
        /// 画布点击（处理编辑区域内的空白点击）
        /// </summary>
        private void EditorCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            //System.Diagnostics.Debug.WriteLine($"🖱️ [EditorCanvas_MouseDown] 开始处理");
            //System.Diagnostics.Debug.WriteLine($"   - OriginalSource: {e.OriginalSource?.GetType().Name}");

            // 这个事件只有在EditorCanvas区域内点击时才会触发
            // 所以只要不是点击在文本框内，就取消选中
            bool clickedOnTextBox = false;

            // 检查是否点击在任意文本框内
            foreach (var textBox in _textBoxes)
            {
                var position = e.GetPosition(textBox);
                //System.Diagnostics.Debug.WriteLine($"   - 检查文本框 {textBox.Name}，位置: ({position.X:F1}, {position.Y:F1})");

                if (position.X >= 0 && position.Y >= 0 &&
                    position.X <= textBox.ActualWidth &&
                    position.Y <= textBox.ActualHeight)
                {
                    clickedOnTextBox = true;
                    //System.Diagnostics.Debug.WriteLine($"   - ✅ 点击在文本框内，保持选中状态");
                    break;
                }
            }

            // 如果点击在编辑区域内但没有点击在文本框内，则取消所有文本框选中状态
            if (!clickedOnTextBox)
            {
                //System.Diagnostics.Debug.WriteLine($"   - ❌ 点击在编辑区域空白位置，取消所有文本框选中状态");
                DeselectAllTextBoxes(true); // 关闭侧边面板（点击空白区域时关闭）
            }

            //System.Diagnostics.Debug.WriteLine($"🖱️ [EditorCanvas_MouseDown] 处理完成");
        }
        
        /// <summary>
        /// 画布键盘事件（处理DEL快捷键）
        /// </summary>
        private async void EditorCanvas_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"🎹 [EditorCanvas_KeyDown] 按键: {e.Key}");
            //System.Diagnostics.Debug.WriteLine($"   IsInSplitMode: {IsInSplitMode()}");
            //System.Diagnostics.Debug.WriteLine($"   _selectedRegionIndex: {_selectedRegionIndex}");
            //System.Diagnostics.Debug.WriteLine($"   _regionImages.Count: {_regionImages.Count}");
            //System.Diagnostics.Debug.WriteLine($"   包含选中区域图片: {_regionImages.ContainsKey(_selectedRegionIndex)}");
            //#endif
            
            // DEL键：只清除选中区域的图片（仅在分割模式下且有图片时）
            if (e.Key == System.Windows.Input.Key.Delete)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"🎹 [DEL键] 检测到 Delete 键");
                //#endif
                
                if (IsInSplitMode())
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"✅ [DEL键] 在分割模式下");
                    //#endif
                    
                    if (_selectedRegionIndex >= 0)
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"✅ [DEL键] 有选中区域: {_selectedRegionIndex}");
                        //#endif
                        
                        if (_regionImages.ContainsKey(_selectedRegionIndex))
                        {
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"✅ [DEL键] 区域有图片，执行清空");
                            //#endif
                            
                            await ClearSelectedRegionImage();
                            e.Handled = true;
                        }
                        //#if DEBUG
                        //else
                        //{
                        //    //System.Diagnostics.Debug.WriteLine($"⚠️ [DEL键] 区域没有图片");
                        //}
                        //#endif
                    }
                    //#if DEBUG
                    //else
                    //{
                    //    //System.Diagnostics.Debug.WriteLine($"⚠️ [DEL键] 没有选中区域");
                    //}
                    //#endif
                }
                //#if DEBUG
                //else
                //{
                //    //System.Diagnostics.Debug.WriteLine($"⚠️ [DEL键] 不在分割模式");
                //}
                //#endif
            }
        }

        #endregion

    }
}