using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ImageColorChanger.Core;
using ImageColorChanger.Database.Models;
using static ImageColorChanger.Core.Constants;
using SkiaSharp;
using WpfColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfMessageBox = System.Windows.MessageBox;
using WpfSize = System.Windows.Size;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 的歌词编辑功能分部类
    /// </summary>
    public partial class MainWindow
    {
        // ============================================
        // 字段
        // ============================================
        
        private bool _isLyricsMode = false; // 是否处于歌词模式
        private LyricsProject _currentLyricsProject = null; // 当前歌词项目
        private System.Windows.Threading.DispatcherTimer _lyricsAutoSaveTimer; // 自动保存计时器

        // ============================================
        // 公共属性
        // ============================================
        
        /// <summary>
        /// 是否处于歌词模式（供ProjectionManager访问）
        /// </summary>
        public bool IsInLyricsMode => _isLyricsMode;

        // ============================================
        // 进入/退出歌词模式
        // ============================================

        // 浮动歌词按钮已删除，通过右键菜单进入歌词模式

        /// <summary>
        /// 退出按钮点击事件
        /// </summary>
        private void BtnCloseLyricsEditor_Click(object sender, RoutedEventArgs e)
        {
            ExitLyricsMode();
        }

        /// <summary>
        /// 进入歌词编辑模式
        /// </summary>
        private void EnterLyricsMode()
        {
//#if DEBUG
//            Debug.WriteLine("[歌词] 进入歌词模式");
//#endif

            // 隐藏其他显示区域
            ImageScrollViewer.Visibility = Visibility.Collapsed;
            VideoContainer.Visibility = Visibility.Collapsed;
            TextEditorPanel.Visibility = Visibility.Collapsed;

            // 显示歌词编辑面板
            LyricsEditorPanel.Visibility = Visibility.Visible;

            // 加载或创建歌词项目
            LoadOrCreateLyricsProject();

            // 聚焦到文本框
            Dispatcher.InvokeAsync(() =>
            {
                LyricsTextBox.Focus();
            }, System.Windows.Threading.DispatcherPriority.Loaded);

            // 🔧 隐藏合成播放按钮面板（歌词模式不需要）
            CompositePlaybackPanel.Visibility = Visibility.Collapsed;

            // 启动自动保存计时器（每30秒保存一次）
            StartAutoSaveTimer();

            // 🔧 如果投影已开启，先清空图片投影状态，再投影歌词
//#if DEBUG
//            Debug.WriteLine($"[歌词] 检查投影状态 - _projectionManager: {_projectionManager != null}, IsProjecting: {_projectionManager?.IsProjecting}");
//#endif

            if (_projectionManager != null && _projectionManager.IsProjecting)
            {
//#if DEBUG
//                Debug.WriteLine("[歌词] 投影已开启，先清空图片状态");
//#endif
                // 清空投影的图片状态（歌词模式不使用图片）
                _projectionManager.ClearImageState();
                
//#if DEBUG
//                Debug.WriteLine("[歌词] 准备渲染歌词");
//#endif
                RenderLyricsToProjection();
//#if DEBUG
//                Debug.WriteLine("[歌词] 进入模式时自动投影完成");
//#endif
            }
            else
            {
//#if DEBUG
//                Debug.WriteLine("[歌词] 投影未开启，跳过投影");
//#endif
            }

            _isLyricsMode = true;
        }

        /// <summary>
        /// 退出歌词编辑模式
        /// </summary>
        private void ExitLyricsMode()
        {
//#if DEBUG
//            Debug.WriteLine("[歌词] 退出歌词模式");
//#endif

            // 停止自动保存计时器
            StopAutoSaveTimer();

            // 保存当前内容
            SaveLyricsProject();

            // 隐藏歌词编辑面板
            LyricsEditorPanel.Visibility = Visibility.Collapsed;

            // 显示图片浏览区域
            ImageScrollViewer.Visibility = Visibility.Visible;

            // 🔧 先设置标志为false，再恢复合成播放按钮的显示状态
            _isLyricsMode = false;

            // 🔧 恢复合成播放按钮的显示状态
            UpdateFloatingCompositePlayButton();

            // 🔧 如果投影已开启，恢复图片投影（刷新当前图片）
            if (_projectionManager != null && _projectionManager.IsProjecting)
            {
//#if DEBUG
//                Debug.WriteLine("[歌词] 退出歌词模式，恢复图片投影");
//#endif
                UpdateProjection();
            }
        }

        // ============================================
        // 字号调整（鼠标滚轮）
        // ============================================

        /// <summary>
        /// 字号显示区域 - 鼠标滚轮调整字号
        /// </summary>
        private void LyricsFontSizeDisplay_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double currentSize = LyricsTextBox.FontSize;
            
            if (e.Delta > 0)
            {
                // 向上滚动 - 增大字号
                if (currentSize < 200)
                {
                    LyricsTextBox.FontSize = Math.Min(200, currentSize + 4);
                    LyricsFontSizeDisplay.Text = LyricsTextBox.FontSize.ToString("0");

//#if DEBUG
//                    Debug.WriteLine($"[歌词] 滚轮调整字号到 {LyricsTextBox.FontSize}");
//#endif
                }
            }
            else
            {
                // 向下滚动 - 减小字号
                if (currentSize > 20)
                {
                    LyricsTextBox.FontSize = Math.Max(20, currentSize - 4);
                    LyricsFontSizeDisplay.Text = LyricsTextBox.FontSize.ToString("0");

//#if DEBUG
//                    Debug.WriteLine($"[歌词] 滚轮调整字号到 {LyricsTextBox.FontSize}");
//#endif
                }
            }
            
            e.Handled = true;

            // 字号改变后，如果投影已开启，自动更新投影
            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                RenderLyricsToProjection();
            }
        }

        // ============================================
        // 文字颜色
        // ============================================

        /// <summary>
        /// 颜色按钮点击 - 打开颜色选择器
        /// </summary>
        private void BtnLyricsTextColor_Click(object sender, RoutedEventArgs e)
        {
            OpenLyricsCustomColorPicker();
        }

        /// <summary>
        /// 打开自定义颜色选择器
        /// </summary>
        private void OpenLyricsCustomColorPicker()
        {
            var colorDialog = new System.Windows.Forms.ColorDialog();

            // 设置默认颜色为当前颜色
            var currentColor = (LyricsTextBox.Foreground as System.Windows.Media.SolidColorBrush)?.Color 
                ?? HexToColor(_configManager.DefaultLyricsColor);
            colorDialog.Color = System.Drawing.Color.FromArgb(
                currentColor.A, currentColor.R, currentColor.G, currentColor.B);

            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var color = colorDialog.Color;
                SetLyricsColor(color.R, color.G, color.B);
                ShowStatus($"✨ 全局歌词颜色已更新");

//#if DEBUG
//                Debug.WriteLine($"[歌词-全局] 自定义颜色: #{color.R:X2}{color.G:X2}{color.B:X2}");
//#endif
            }
        }

        /// <summary>
        /// 设置歌词颜色（全局设置，应用到所有歌词）
        /// </summary>
        private void SetLyricsColor(byte r, byte g, byte b)
        {
            // 转换为十六进制格式
            string hexColor = $"#{r:X2}{g:X2}{b:X2}";

            // 更新全局默认颜色配置（保存到config.json）
            _configManager.DefaultLyricsColor = hexColor;

//#if DEBUG
//            Debug.WriteLine($"[歌词-全局] 颜色更改为 {hexColor}");
//#endif

            // 更新当前UI显示
            var brush = new System.Windows.Media.SolidColorBrush(WpfColor.FromRgb(r, g, b));
            LyricsTextBox.Foreground = brush;

            // 颜色改变后，如果投影已开启，自动更新投影
            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                RenderLyricsToProjection();
            }
        }

        // ============================================
        // 对齐方式
        // ============================================

        /// <summary>
        /// 左对齐
        /// </summary>
        private void BtnLyricsAlignLeft_Click(object sender, RoutedEventArgs e)
        {
            LyricsTextBox.TextAlignment = TextAlignment.Left;
            UpdateAlignmentButtonsState(TextAlignment.Left);

//#if DEBUG
//            Debug.WriteLine("[歌词] 切换到左对齐");
//#endif

            // 对齐方式改变后，如果投影已开启，自动更新投影
            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                RenderLyricsToProjection();
            }
        }

        /// <summary>
        /// 居中对齐
        /// </summary>
        private void BtnLyricsAlignCenter_Click(object sender, RoutedEventArgs e)
        {
            LyricsTextBox.TextAlignment = TextAlignment.Center;
            UpdateAlignmentButtonsState(TextAlignment.Center);

//#if DEBUG
//            Debug.WriteLine("[歌词] 切换到居中对齐");
//#endif

            // 对齐方式改变后，如果投影已开启，自动更新投影
            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                RenderLyricsToProjection();
            }
        }

        /// <summary>
        /// 右对齐
        /// </summary>
        private void BtnLyricsAlignRight_Click(object sender, RoutedEventArgs e)
        {
            LyricsTextBox.TextAlignment = TextAlignment.Right;
            UpdateAlignmentButtonsState(TextAlignment.Right);

//#if DEBUG
//            Debug.WriteLine("[歌词] 切换到右对齐");
//#endif

            // 对齐方式改变后，如果投影已开启，自动更新投影
            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
                RenderLyricsToProjection();
            }
        }

        /// <summary>
        /// 更新对齐按钮的视觉状态
        /// </summary>
        private void UpdateAlignmentButtonsState(TextAlignment alignment)
        {
            // 🔧 重新设计的视觉反馈：使用深色背景+橙色高亮
            var normalBrush = new System.Windows.Media.SolidColorBrush(WpfColor.FromRgb(44, 44, 44)); // 深灰色
            var normalBorder = new System.Windows.Media.SolidColorBrush(WpfColor.FromRgb(68, 68, 68)); // 边框灰色
            var highlightBrush = new System.Windows.Media.SolidColorBrush(WpfColor.FromRgb(255, 152, 0)); // 橙色高亮
            var highlightBorder = new System.Windows.Media.SolidColorBrush(WpfColor.FromRgb(255, 183, 77)); // 亮橙色边框

            // 重置所有按钮
            BtnLyricsAlignLeft.Background = normalBrush;
            BtnLyricsAlignLeft.BorderBrush = normalBorder;
            BtnLyricsAlignCenter.Background = normalBrush;
            BtnLyricsAlignCenter.BorderBrush = normalBorder;
            BtnLyricsAlignRight.Background = normalBrush;
            BtnLyricsAlignRight.BorderBrush = normalBorder;

            // 高亮选中的按钮
            switch (alignment)
            {
                case TextAlignment.Left:
                    BtnLyricsAlignLeft.Background = highlightBrush;
                    BtnLyricsAlignLeft.BorderBrush = highlightBorder;
                    break;
                case TextAlignment.Center:
                    BtnLyricsAlignCenter.Background = highlightBrush;
                    BtnLyricsAlignCenter.BorderBrush = highlightBorder;
                    break;
                case TextAlignment.Right:
                    BtnLyricsAlignRight.Background = highlightBrush;
                    BtnLyricsAlignRight.BorderBrush = highlightBorder;
                    break;
            }
        }

        // ============================================
        // 清空和投影
        // ============================================

        /// <summary>
        /// 清空内容
        /// </summary>
        private void BtnLyricsClear_Click(object sender, RoutedEventArgs e)
        {
            var result = WpfMessageBox.Show(
                "确定要清空所有歌词内容吗？",
                "确认清空",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                LyricsTextBox.Text = "";
                LyricsTextBox.Focus();

//#if DEBUG
//                Debug.WriteLine("[歌词] 清空内容");
//#endif
            }
        }


        // ============================================
        // 事件处理
        // ============================================

        /// <summary>
        /// 文本内容改变事件
        /// </summary>
        private void LyricsTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 内容改变时重置自动保存计时器
            if (_lyricsAutoSaveTimer != null && _lyricsAutoSaveTimer.IsEnabled)
            {
                _lyricsAutoSaveTimer.Stop();
                _lyricsAutoSaveTimer.Start();
            }

            // 如果投影已开启，自动更新投影
//#if DEBUG
//            Debug.WriteLine($"[歌词] TextChanged - _isLyricsMode: {_isLyricsMode}, _projectionManager: {_projectionManager != null}, IsProjecting: {_projectionManager?.IsProjecting}");
//#endif
            
            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
//#if DEBUG
//                Debug.WriteLine("[歌词] 文字改变，触发投影更新");
//#endif
                RenderLyricsToProjection();
            }
        }

        /// <summary>
        /// 键盘事件处理
        /// </summary>
        private void LyricsTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Ctrl+S 保存
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SaveLyricsProject();
                ShowToast("歌词已保存");
                e.Handled = true;
            }
        }

        /// <summary>
        /// 鼠标滚轮事件（用于滚动）
        /// </summary>
        private void LyricsTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // 在全图模式下，让滚轮事件冒泡到ScrollViewer
            // 不需要特殊处理
        }

        /// <summary>
        /// 歌词滚动事件 - 同步到投影
        /// </summary>
        private void LyricsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // 如果投影已开启且在歌词模式，同步滚动位置
            if (_isLyricsMode && _projectionManager != null && _projectionManager.IsProjecting)
            {
//#if DEBUG
//                Debug.WriteLine($"[歌词] 滚动位置改变: {e.VerticalOffset:F2}");
//#endif
                // 🔧 同步投影滚动位置（传入歌词ScrollViewer）
                _projectionManager.SyncLyricsScroll(LyricsScrollViewer);
            }
        }

        /// <summary>
        /// 歌词区域右键菜单
        /// </summary>
        private void LyricsScrollViewer_RightClick(object sender, MouseButtonEventArgs e)
        {
            // 创建右键菜单
            var contextMenu = new ContextMenu();
            
            // 应用自定义样式
            contextMenu.Style = (Style)this.FindResource("NoBorderContextMenuStyle");
            
            // 颜色菜单（第一位）
            var colorMenuItem = new MenuItem 
            { 
                Header = "颜色",
                Height = 36
            };

            // 获取当前颜色
            var currentColor = (LyricsTextBox.Foreground as System.Windows.Media.SolidColorBrush)?.Color 
                ?? System.Windows.Media.Colors.White;

            // 预设颜色
            var builtInPresets = new List<Core.ColorPreset>
            {
                new Core.ColorPreset { Name = "纯黄", R = 255, G = 255, B = 0 },
                new Core.ColorPreset { Name = "秋麒麟", R = 218, G = 165, B = 32 },
                new Core.ColorPreset { Name = "纯白", R = 255, G = 255, B = 255 }
            };

            foreach (var preset in builtInPresets)
            {
                var colorItem = new MenuItem
                {
                    Header = preset.Name,
                    IsCheckable = true,
                    IsChecked = currentColor.R == preset.R && 
                               currentColor.G == preset.G && 
                               currentColor.B == preset.B,
                    Height = 36
                };

                var currentPreset = preset;
                colorItem.Click += (s, args) =>
                {
                    SetLyricsColor(currentPreset.R, currentPreset.G, currentPreset.B);
                    ShowStatus($"✨ 歌词颜色: {currentPreset.Name}");
                };

                colorMenuItem.Items.Add(colorItem);
            }

            // 添加分隔线
            colorMenuItem.Items.Add(new Separator());

            // 自定义颜色
            var customColorItem = new MenuItem 
            { 
                Header = "自定义颜色...",
                Height = 36
            };
            customColorItem.Click += (s, args) => OpenLyricsCustomColorPicker();
            colorMenuItem.Items.Add(customColorItem);

            contextMenu.Items.Add(colorMenuItem);
            
            // 退出歌词模式选项
            var exitLyricsItem = new MenuItem 
            { 
                Header = "退出歌词",
                Height = 36
            };
            exitLyricsItem.Click += (s, args) => ExitLyricsMode();
            contextMenu.Items.Add(exitLyricsItem);
            
            // 显示菜单
            contextMenu.PlacementTarget = LyricsScrollViewer;
            contextMenu.IsOpen = true;
            
            e.Handled = true;
        }

        // ============================================
        // 数据管理
        // ============================================

        /// <summary>
        /// 加载或创建歌词项目
        /// </summary>
        private void LoadOrCreateLyricsProject()
        {
            try
            {
                // 获取当前图片ID（从主窗口）
                int currentImageId = _currentImageId;
                
//#if DEBUG
//                Debug.WriteLine($"[歌词-加载] 当前图片ID: {currentImageId}");
//#endif
                
                if (currentImageId == 0)
                {
//#if DEBUG
//                    Debug.WriteLine("[歌词] 当前无图片，无法加载歌词");
//#endif
                    // 创建临时项目（不关联图片）
                    CreateTempLyricsProject();
                    return;
                }

                // 🔧 强制刷新数据库上下文（确保查询到最新数据）
                _dbContext.ChangeTracker.Clear();
                
//#if DEBUG
//                Debug.WriteLine($"[歌词-加载] 开始查询，条件：ImageId == {currentImageId}");
//                // 显示数据库中所有歌词项目
//                var allProjects = _dbContext.LyricsProjects.ToList();
//                Debug.WriteLine($"[歌词-加载] 数据库中共有 {allProjects.Count} 个歌词项目：");
//                foreach (var proj in allProjects)
//                {
//                    Debug.WriteLine($"  - ID: {proj.Id}, 名称: {proj.Name}, 关联图片ID: {proj.ImageId}, 内容长度: {(proj.Content ?? "").Length}");
//                }
//#endif
                
                // 尝试加载当前图片对应的歌词项目
                _currentLyricsProject = _dbContext.LyricsProjects
                    .FirstOrDefault(p => p.ImageId == currentImageId);
                    
//#if DEBUG
//                Debug.WriteLine($"[歌词-加载] 查询结果: {(_currentLyricsProject != null ? $"找到 - {_currentLyricsProject.Name}" : "未找到，将创建新项目")}");
//#endif

                if (_currentLyricsProject != null)
                {
                    // 加载现有项目
//#if DEBUG
//                    Debug.WriteLine($"[歌词-加载] 项目ID: {_currentLyricsProject.Id}, 名称: {_currentLyricsProject.Name}");
//                    Debug.WriteLine($"[歌词-加载] 关联图片ID: {_currentLyricsProject.ImageId}");
//                    Debug.WriteLine($"[歌词-加载] 内容长度: {(_currentLyricsProject.Content ?? "").Length}");
//                    Debug.WriteLine($"[歌词-加载] 内容完整: {_currentLyricsProject.Content ?? "(空)"}");
//#endif

                    // 🔧 自动升级旧项目：对齐方式
                    if (_currentLyricsProject.TextAlign == "Left")
                    {
                        _currentLyricsProject.TextAlign = "Center";
                        _dbContext.SaveChanges();
//#if DEBUG
//                        Debug.WriteLine($"[歌词-升级] 对齐从左对齐更新为居中");
//#endif
                    }

                    LyricsTextBox.Text = _currentLyricsProject.Content ?? "";
                    LyricsTextBox.FontSize = _currentLyricsProject.FontSize;
                    LyricsFontSizeDisplay.Text = _currentLyricsProject.FontSize.ToString("0");

                    // 始终使用全局默认颜色（不从数据库读取）
                    var textColor = new System.Windows.Media.SolidColorBrush(HexToColor(_configManager.DefaultLyricsColor));
                    LyricsTextBox.Foreground = textColor;
//#if DEBUG
//                    Debug.WriteLine($"[歌词-颜色] 使用全局默认颜色: {_configManager.DefaultLyricsColor}");
//#endif

                    // 恢复对齐方式
                    var alignment = (TextAlignment)Enum.Parse(typeof(TextAlignment), _currentLyricsProject.TextAlign);
                    LyricsTextBox.TextAlignment = alignment;
                    UpdateAlignmentButtonsState(alignment);

//#if DEBUG
//                    Debug.WriteLine($"[歌词] 加载项目完成: {_currentLyricsProject.Name}");
//                    Debug.WriteLine($"[歌词] TextBox当前文本长度: {LyricsTextBox.Text.Length}");
//#endif
                }
                else
                {
                    // 获取当前图片文件名（用于项目命名）
                    var currentImagePath = _imageProcessor?.CurrentImagePath ?? "";
                    var imageName = string.IsNullOrEmpty(currentImagePath) 
                        ? "未命名" 
                        : System.IO.Path.GetFileNameWithoutExtension(currentImagePath);

                    // 创建新项目（关联到当前图片）
                    _currentLyricsProject = new LyricsProject
                    {
                        Name = $"歌词_{imageName}",
                        ImageId = currentImageId,
                        CreatedTime = DateTime.Now,
                        FontSize = 48,
                        TextAlign = "Center"
                    };

                    _dbContext.LyricsProjects.Add(_currentLyricsProject);
                    _dbContext.SaveChanges();
                    
                    // 🔧 清空TextBox内容（新项目没有歌词）
                    LyricsTextBox.Text = "";
                    LyricsTextBox.FontSize = 48;
                    LyricsFontSizeDisplay.Text = "48";
                    LyricsTextBox.Foreground = new System.Windows.Media.SolidColorBrush(HexToColor(_configManager.DefaultLyricsColor));
                    LyricsTextBox.TextAlignment = TextAlignment.Center;

                    // 初始化对齐按钮状态
                    UpdateAlignmentButtonsState(TextAlignment.Center);

//#if DEBUG
//                    Debug.WriteLine($"[歌词] 创建新项目: {_currentLyricsProject.Name}, 关联图片ID: {currentImageId}");
//                    Debug.WriteLine($"[歌词] TextBox已清空");
//#endif
                }
            }
            catch (Exception)
            {
//#if DEBUG
//                Debug.WriteLine($"[歌词] 加载项目出错: {ex.Message}");
//#endif
                CreateTempLyricsProject();
            }
        }

        /// <summary>
        /// 创建临时歌词项目（不关联图片）
        /// </summary>
        private void CreateTempLyricsProject()
        {
            _currentLyricsProject = new LyricsProject
            {
                Name = $"歌词_临时_{DateTime.Now:yyyyMMdd_HHmmss}",
                ImageId = null,
                CreatedTime = DateTime.Now,
                FontSize = 48,
                TextAlign = "Center"
            };
            
            // 初始化对齐按钮状态
            UpdateAlignmentButtonsState(TextAlignment.Center);
        }

        /// <summary>
        /// 保存歌词项目
        /// </summary>
        internal void SaveLyricsProject()
        {
            if (_currentLyricsProject == null)
                return;

            try
            {
                // 更新内容（不保存颜色，使用全局配置）
                _currentLyricsProject.Content = LyricsTextBox.Text;
                _currentLyricsProject.FontSize = LyricsTextBox.FontSize;
                _currentLyricsProject.TextAlign = LyricsTextBox.TextAlignment.ToString();
                _currentLyricsProject.ModifiedTime = DateTime.Now;

                // 保存到数据库
                _dbContext.SaveChanges();

//#if DEBUG
//                Debug.WriteLine($"[歌词] 保存成功: {_currentLyricsProject.Name}");
//#endif
            }
            catch (Exception ex)
            {
//#if DEBUG
//                Debug.WriteLine($"[歌词] 保存出错: {ex.Message}");
//#endif

                WpfMessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 颜色转十六进制字符串
        /// </summary>
        private string ColorToHex(System.Windows.Media.Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        /// <summary>
        /// 十六进制字符串转颜色
        /// </summary>
        private System.Windows.Media.Color HexToColor(string hex)
        {
            if (string.IsNullOrEmpty(hex))
                return System.Windows.Media.Colors.White;

            hex = hex.Replace("#", "");
            if (hex.Length == 6)
            {
                return System.Windows.Media.Color.FromRgb(
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16));
            }

            return System.Windows.Media.Colors.White;
        }

        /// <summary>
        /// 渲染歌词到投影（使用SkiaSharp）
        /// </summary>
        private void RenderLyricsToProjection()
        {
            try
            {
                // 🔧 获取投影屏幕的实际尺寸
                var (screenWidth, screenHeight) = _projectionManager.GetProjectionScreenSize();

                // ========================================
                // ✅ 恢复使用 WPF TextBlock 渲染（确保与主屏 TextBox 行高完全一致）
                // ========================================
                
                // 创建一个与投影窗口同尺寸的Canvas
                var canvas = new Canvas
                {
                    Width = screenWidth,
                    Height = screenHeight,
                    Background = WpfBrushes.Black
                };

                double actualHeight = screenHeight;

                // 🔧 使用 WPF TextBlock，与主屏幕 TextBox 共享同一渲染引擎
                var textBlock = new TextBlock
                {
                    Text = LyricsTextBox.Text,
                    FontFamily = new WpfFontFamily("Microsoft YaHei UI"),
                    FontSize = LyricsTextBox.FontSize,
                    Foreground = LyricsTextBox.Foreground,
                    TextAlignment = LyricsTextBox.TextAlignment,
                    TextWrapping = TextWrapping.Wrap,
                    Width = screenWidth,
                    Padding = new Thickness(60, 40, 60, 40), // 与主屏幕ScrollViewer的Padding一致
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left
                };

                // 🔧 测量TextBlock的实际高度
                textBlock.Measure(new WpfSize(screenWidth, double.PositiveInfinity));
                double textBlockHeight = textBlock.DesiredSize.Height;

                // 如果内容超过屏幕高度，调整Canvas高度
                if (textBlockHeight > screenHeight)
                {
                    actualHeight = textBlockHeight;
                    canvas.Height = actualHeight;
                }

                Canvas.SetLeft(textBlock, 0);
                Canvas.SetTop(textBlock, 0);
                canvas.Children.Add(textBlock);

                // 渲染到图片（固定使用96 DPI）
                canvas.Measure(new WpfSize(screenWidth, actualHeight));
                canvas.Arrange(new Rect(0, 0, screenWidth, actualHeight));
                canvas.UpdateLayout();

                // 🔧 使用96 DPI渲染，确保像素对齐
                var renderBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)screenWidth, (int)Math.Ceiling(actualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                renderBitmap.Render(canvas);
                renderBitmap.Freeze();

                // 转换为SKBitmap并更新投影
                var skBitmap = ConvertToSKBitmap(renderBitmap);
                
                if (skBitmap != null)
                {
                    _projectionManager?.UpdateProjectionText(skBitmap);
                    skBitmap.Dispose();
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [歌词渲染-WPF] 失败: {ex.Message}");
#else
                _ = ex;
#endif
                WpfMessageBox.Show($"投影失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ============================================
        // 自动保存
        // ============================================

        /// <summary>
        /// 启动自动保存计时器
        /// </summary>
        private void StartAutoSaveTimer()
        {
            if (_lyricsAutoSaveTimer == null)
            {
                _lyricsAutoSaveTimer = new System.Windows.Threading.DispatcherTimer();
                _lyricsAutoSaveTimer.Interval = TimeSpan.FromSeconds(30); // 每30秒保存一次
                _lyricsAutoSaveTimer.Tick += (s, e) =>
                {
                    SaveLyricsProject();
//#if DEBUG
//                    Debug.WriteLine("[歌词] 自动保存");
//#endif
                };
            }

            _lyricsAutoSaveTimer.Start();

//#if DEBUG
//            Debug.WriteLine("[歌词] 自动保存计时器已启动");
//#endif
        }

        /// <summary>
        /// 停止自动保存计时器
        /// </summary>
        private void StopAutoSaveTimer()
        {
            if (_lyricsAutoSaveTimer != null && _lyricsAutoSaveTimer.IsEnabled)
            {
                _lyricsAutoSaveTimer.Stop();

//#if DEBUG
//                Debug.WriteLine("[歌词] 自动保存计时器已停止");
//#endif
            }
        }

        // ============================================
        // 公共方法（供主窗口调用）
        // ============================================

        /// <summary>
        /// 当图片切换时调用（供主窗口调用）
        /// 如果在歌词模式，自动切换到新图片的歌词
        /// </summary>
        public void OnImageChanged()
        {
            if (!_isLyricsMode)
                return;

//#if DEBUG
//            Debug.WriteLine("[歌词] 检测到图片切换，重新加载对应歌词");
//#endif

            // 保存当前歌词
            SaveLyricsProject();

            // 加载新图片的歌词
            LoadOrCreateLyricsProject();

            // 如果投影已开启，更新投影
            if (_projectionManager != null && _projectionManager.IsProjecting)
            {
//#if DEBUG
//                Debug.WriteLine("[歌词] 图片切换，自动更新歌词投影");
//#endif
                RenderLyricsToProjection();
            }
        }

        /// <summary>
        /// 图片切换时的回调（在歌词模式下调用）
        /// 保存当前歌词，加载新图片的歌词，更新投影
        /// </summary>
        public void OnImageChangedInLyricsMode()
        {
//#if DEBUG
//            Debug.WriteLine("[歌词] 检测到图片切换，准备切换歌词");
//#endif

            // 1. 保存当前歌词项目
            SaveLyricsProject();
            
            // 2. 加载新图片的歌词
            LoadOrCreateLyricsProject();
            
            // 3. 如果投影已开启，更新投影
            if (_projectionManager != null && _projectionManager.IsProjecting)
            {
//#if DEBUG
//                Debug.WriteLine("[歌词] 投影已开启，渲染新图片的歌词");
//#endif
                RenderLyricsToProjection();
            }

//#if DEBUG
//            Debug.WriteLine($"[歌词] 已切换到新图片的歌词: {_currentLyricsProject?.Name}");
//#endif
        }

        /// <summary>
        /// 投影状态改变时的回调（供主窗口调用）
        /// 当投影开启时，如果在歌词模式，自动投影歌词
        /// </summary>
        public void OnProjectionStateChanged(bool isProjecting)
        {
//#if DEBUG
//            Debug.WriteLine($"[歌词] 投影状态改变 - IsProjecting: {isProjecting}, _isLyricsMode: {_isLyricsMode}");
//#endif

            if (isProjecting && _isLyricsMode)
            {
//#if DEBUG
//                Debug.WriteLine("[歌词] 投影开启且在歌词模式，触发投影");
//#endif
                // 🔧 立即清空图片状态（防止自动刷新显示图片）
                _projectionManager.ClearImageState();
                
                // 延迟500ms确保投影窗口完全初始化，并且在其他自动刷新之后执行
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(2)
                };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
//#if DEBUG
//                    Debug.WriteLine("[歌词] 延迟后开始投影歌词");
//#endif
                    RenderLyricsToProjection();
                };
                timer.Start();
            }
        }

        // 浮动歌词按钮已删除

        /// <summary>
        /// 将WPF BitmapSource转换为SKBitmap
        /// </summary>
        private SkiaSharp.SKBitmap ConvertToSKBitmap(System.Windows.Media.Imaging.BitmapSource bitmapSource)
        {
            try
            {
                int width = bitmapSource.PixelWidth;
                int height = bitmapSource.PixelHeight;
                
                // 转换为Bgra32格式
                var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap();
                converted.BeginInit();
                converted.Source = bitmapSource;
                converted.DestinationFormat = System.Windows.Media.PixelFormats.Bgra32;
                converted.EndInit();

                // 获取像素数据
                int stride = width * 4;
                byte[] pixels = new byte[stride * height];
                converted.CopyPixels(pixels, stride, 0);

                // 创建SKBitmap
                var skBitmap = new SkiaSharp.SKBitmap(width, height, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul);
                
                // 复制像素数据
                IntPtr pixelsPtr = skBitmap.GetPixels();
                System.Runtime.InteropServices.Marshal.Copy(pixels, 0, pixelsPtr, pixels.Length);

                return skBitmap;
            }
            catch (Exception)
            {
//#if DEBUG
//                Debug.WriteLine($"[歌词] BitmapSource转SKBitmap出错: {ex.Message}");
//#endif
                return null;
            }
        }
    }
}

