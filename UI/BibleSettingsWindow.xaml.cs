using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ImageColorChanger.Core;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfMessageBox = System.Windows.MessageBox;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// 圣经设置窗口
    /// </summary>
    public partial class BibleSettingsWindow : Window
    {
        private readonly ConfigManager _configManager;
        private System.Windows.Forms.ColorDialog _colorDialog;
        private Dictionary<string, CustomFont> _fontMap = new Dictionary<string, CustomFont>(); // 字体名称到字体信息的映射
        private bool _isLoading = false; // 标记是否正在加载设置，避免触发保存
        private bool _isSelectingColor = false; // 标记是否正在选择颜色，避免窗口自动关闭
        private Action _onSettingsChanged; // 设置改变时的回调

        public BibleSettingsWindow(ConfigManager configManager, Action onSettingsChanged = null)
        {
            _isLoading = true; // 在 InitializeComponent 之前设置，防止初始化时触发保存
            
            InitializeComponent();
            _configManager = configManager;
            _onSettingsChanged = onSettingsChanged;
            _colorDialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true
            };

            LoadFontFamilies();
            LoadSettings();
            
            // 窗口关闭时标记为已保存
            this.Closing += (s, e) => { DialogResult = true; };
        }

        /// <summary>
        /// 窗口失去焦点时自动关闭（选择颜色时除外）
        /// </summary>
        private void Window_Deactivated(object sender, EventArgs e)
        {
            // 如果正在选择颜色，不要关闭窗口
            if (_isSelectingColor)
            {
                #if DEBUG
                Debug.WriteLine($"[圣经设置] 窗口失去焦点，但正在选择颜色，不关闭");
                #endif
                return;
            }
            
            this.Close();
        }

        /// <summary>
        /// 标题栏拖动事件
        /// </summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
                
                // 保存窗口位置
                _configManager.BibleSettingsWindowLeft = this.Left;
                _configManager.BibleSettingsWindowTop = this.Top;
                
                #if DEBUG
                Debug.WriteLine($"[圣经设置] 窗口拖动后位置: Left={this.Left}, Top={this.Top}");
                Debug.WriteLine($"[圣经设置] 位置已保存到配置");
                #endif
            }
        }

        /// <summary>
        /// 加载字体列表（使用程序自带字体配置）
        /// </summary>
        private void LoadFontFamilies()
        {
            try
            {
                // 使用ResourceLoader加载字体配置
                var json = ResourceLoader.LoadTextFile("Fonts/fonts-simplified.json");
                
                if (string.IsNullOrEmpty(json))
                {
                    #if DEBUG
                    Debug.WriteLine($"[圣经设置] 未找到 fonts-simplified.json，使用系统默认字体");
                    #endif
                    LoadDefaultFonts();
                    return;
                }

                // 反序列化配置文件
                var config = JsonSerializer.Deserialize<FontConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (config == null || config.FontCategories == null || config.FontCategories.Count == 0)
                {
                    #if DEBUG
                    Debug.WriteLine($"[圣经设置] fonts-simplified.json 配置为空，使用系统默认字体");
                    #endif
                    LoadDefaultFonts();
                    return;
                }

                // 清空字体选择器
                CmbFontFamily.Items.Clear();
                _fontMap.Clear();

                int totalFonts = 0;

                // 按分类加载字体
                foreach (var category in config.FontCategories)
                {
                    // 添加分类标题（不可选）
                    var categoryHeader = new System.Windows.Controls.ComboBoxItem
                    {
                        Content = $"━━ {category.Name} ━━",
                        IsEnabled = false,
                        FontWeight = System.Windows.FontWeights.Bold,
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x8C, 0x00)) // 橙色
                    };
                    CmbFontFamily.Items.Add(categoryHeader);

                    // 添加该分类下的字体
                    foreach (var font in category.Fonts)
                    {
                        var item = new System.Windows.Controls.ComboBoxItem
                        {
                            Content = font.Name,
                            Tag = font // 保存字体信息
                        };
                        CmbFontFamily.Items.Add(item);
                        _fontMap[font.Name] = font;
                        totalFonts++;
                    }
                }

                #if DEBUG
                Debug.WriteLine($"[圣经设置] 加载了 {totalFonts} 个字体");
                #endif
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"[圣经设置] 加载字体失败: {ex.Message}");
                #endif
                LoadDefaultFonts();
            }
        }

        /// <summary>
        /// 加载默认系统字体
        /// </summary>
        private void LoadDefaultFonts()
        {
            CmbFontFamily.Items.Clear();
            _fontMap.Clear();

            var defaultFonts = new[]
            {
                new { Name = "微软雅黑", Family = "Microsoft YaHei" },
                new { Name = "宋体", Family = "SimSun" },
                new { Name = "黑体", Family = "SimHei" },
                new { Name = "楷体", Family = "KaiTi" }
            };

            foreach (var font in defaultFonts)
            {
                var customFont = new CustomFont
                {
                    Name = font.Name,
                    Family = font.Family,
                    File = "system"
                };
                
                var item = new System.Windows.Controls.ComboBoxItem
                {
                    Content = font.Name,
                    Tag = customFont
                };
                CmbFontFamily.Items.Add(item);
                _fontMap[font.Name] = customFont;
            }
        }

        /// <summary>
        /// 加载当前设置
        /// </summary>
        private void LoadSettings()
        {
            _isLoading = true; // 开始加载，禁止保存
            try
            {
                #if DEBUG
                Debug.WriteLine($"[圣经设置] 开始加载设置");
                Debug.WriteLine($"[圣经设置] 译本: {_configManager.BibleVersion}");
                Debug.WriteLine($"[圣经设置] 字体: {_configManager.BibleFontFamily}");
                Debug.WriteLine($"[圣经设置] 字号: {_configManager.BibleFontSize}");
                Debug.WriteLine($"[圣经设置] 行距: {_configManager.BibleLineHeight}");
                Debug.WriteLine($"[圣经设置] 边距: {_configManager.BibleMargin}");
                #endif

                // 译本
                if (CmbBibleVersion != null)
                {
                    CmbBibleVersion.Text = _configManager.BibleVersion ?? "和合本";
                }

                // 字体（根据Family查找对应的字体名称）
                if (CmbFontFamily != null && !string.IsNullOrEmpty(_configManager.BibleFontFamily))
                {
                    string fontName = FindFontNameByFamily(_configManager.BibleFontFamily);
                    SelectComboBoxItemByContent(CmbFontFamily, fontName);
                }

                // 字号（统一使用经文字号）
                if (CmbFontSize != null && _configManager.BibleFontSize > 0)
                {
                    SelectComboBoxItem(CmbFontSize, _configManager.BibleFontSize.ToString("0"));
                }

                // 边距
                if (CmbMargin != null)
                {
                    SelectComboBoxItem(CmbMargin, _configManager.BibleMargin.ToString("0"));
                }

                // 节间距
                if (CmbVerseSpacing != null)
                {
                    SelectComboBoxItem(CmbVerseSpacing, _configManager.BibleVerseSpacing.ToString("0"));
                }

                // 更新颜色预览
                if (BorderBackgroundColor != null && !string.IsNullOrEmpty(_configManager.BibleBackgroundColor))
                {
                    UpdateColorPreview(BorderBackgroundColor, _configManager.BibleBackgroundColor);
                }
                if (BorderTextColor != null && !string.IsNullOrEmpty(_configManager.BibleTextColor))
                {
                    UpdateColorPreview(BorderTextColor, _configManager.BibleTextColor);
                }
                if (BorderTitleColor != null && !string.IsNullOrEmpty(_configManager.BibleTitleColor))
                {
                    UpdateColorPreview(BorderTitleColor, _configManager.BibleTitleColor);
                }
                if (BorderVerseNumberColor != null && !string.IsNullOrEmpty(_configManager.BibleVerseNumberColor))
                {
                    UpdateColorPreview(BorderVerseNumberColor, _configManager.BibleVerseNumberColor);
                }

                #if DEBUG
                Debug.WriteLine("[圣经设置] 已加载当前设置");
                #endif
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"[圣经设置] 加载设置失败: {ex.Message}");
                Debug.WriteLine($"[圣经设置] 堆栈: {ex.StackTrace}");
                #endif
            }
            finally
            {
                _isLoading = false; // 加载完成，允许保存
            }
        }

        /// <summary>
        /// 根据FontFamily查找字体名称
        /// </summary>
        private string FindFontNameByFamily(string family)
        {
            // 查找匹配的字体
            var font = _fontMap.Values.FirstOrDefault(f => 
                f.Family.Equals(family, StringComparison.OrdinalIgnoreCase) ||
                f.Name.Equals(family, StringComparison.OrdinalIgnoreCase));
            
            if (font != null)
                return font.Name;
            
            // 默认返回微软雅黑
            return _fontMap.ContainsKey("微软雅黑") ? "微软雅黑" : _fontMap.Keys.FirstOrDefault() ?? "微软雅黑";
        }

        /// <summary>
        /// 选中ComboBox中的项（按内容）
        /// </summary>
        private void SelectComboBoxItemByContent(System.Windows.Controls.ComboBox comboBox, string content)
        {
            foreach (var item in comboBox.Items)
            {
                if (item is System.Windows.Controls.ComboBoxItem comboItem && 
                    comboItem.Content?.ToString() == content)
                {
                    comboItem.IsSelected = true;
                    return;
                }
            }
        }

        /// <summary>
        /// 选中ComboBox中的项（数值）
        /// </summary>
        private void SelectComboBoxItem(System.Windows.Controls.ComboBox comboBox, string value)
        {
            foreach (System.Windows.Controls.ComboBoxItem item in comboBox.Items)
            {
                if (item.Content.ToString() == value)
                {
                    item.IsSelected = true;
                    return;
                }
            }
        }


        /// <summary>
        /// 更新单个颜色预览
        /// </summary>
        private void UpdateColorPreview(System.Windows.Controls.Border border, string colorHex)
        {
            try
            {
                if (string.IsNullOrEmpty(colorHex))
                    return;

                // 支持 #RGB, #RRGGBB, #AARRGGBB 格式
                if (colorHex.StartsWith("#") && (colorHex.Length == 4 || colorHex.Length == 7 || colorHex.Length == 9))
                {
                    var color = (WpfColor)WpfColorConverter.ConvertFromString(colorHex);
                    border.Background = new System.Windows.Media.SolidColorBrush(color);
                }
            }
            catch (Exception)
            {
                // 颜色格式错误，忽略
            }
        }

        /// <summary>
        /// 颜色按钮点击事件
        /// </summary>
        private void ColorButton_Click(object sender, MouseButtonEventArgs e)
        {
            #if DEBUG
            Debug.WriteLine($"[圣经设置] 颜色块点击事件触发");
            #endif
            
            if (sender is not System.Windows.Controls.Border border)
            {
                #if DEBUG
                Debug.WriteLine($"[圣经设置] sender 不是 Border，类型: {sender?.GetType().Name}");
                #endif
                return;
            }

            #if DEBUG
            Debug.WriteLine($"[圣经设置] Border 名称: {border.Name}, 当前颜色: {border.Background}");
            #endif

            // 获取当前颜色
            WpfColor currentColor;
            try
            {
                var brush = border.Background as System.Windows.Media.SolidColorBrush;
                currentColor = brush?.Color ?? System.Windows.Media.Colors.White;
                _colorDialog.Color = System.Drawing.Color.FromArgb(
                    currentColor.A, currentColor.R, currentColor.G, currentColor.B);
                
                #if DEBUG
                Debug.WriteLine($"[圣经设置] 准备打开颜色对话框，当前颜色: #{currentColor.R:X2}{currentColor.G:X2}{currentColor.B:X2}");
                #endif
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"[圣经设置] 获取当前颜色失败: {ex.Message}");
                #endif
                _colorDialog.Color = System.Drawing.Color.White;
            }

            // 打开颜色选择器（需要设置 Owner 为当前 WPF 窗口）
            #if DEBUG
            Debug.WriteLine($"[圣经设置] 准备打开颜色对话框");
            #endif
            
            // 设置标志，防止窗口在选择颜色时自动关闭
            _isSelectingColor = true;
            
            System.Windows.Forms.DialogResult result;
            try
            {
                // 获取 WPF 窗口句柄并设置为 Owner
                var helper = new WindowInteropHelper(this);
                var hwnd = helper.Handle;
                
                if (hwnd == IntPtr.Zero)
                {
                    helper.EnsureHandle();
                    hwnd = helper.Handle;
                }
                
                var owner = new System.Windows.Forms.NativeWindow();
                owner.AssignHandle(hwnd);
                
                #if DEBUG
                Debug.WriteLine($"[圣经设置] 开始调用 ColorDialog.ShowDialog(owner)");
                #endif
                
                result = _colorDialog.ShowDialog(owner);
                
                owner.ReleaseHandle();
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"[圣经设置] ShowDialog() 异常: {ex.Message}");
                #endif
                // 如果设置 Owner 失败，尝试不带 Owner 调用
                result = _colorDialog.ShowDialog();
            }
            finally
            {
                // 恢复标志
                _isSelectingColor = false;
            }
            
            #if DEBUG
            Debug.WriteLine($"[圣经设置] ShowDialog() 返回: {result}");
            #endif
            
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                var color = _colorDialog.Color;
                var wpfColor = WpfColor.FromArgb(color.A, color.R, color.G, color.B);
                border.Background = new System.Windows.Media.SolidColorBrush(wpfColor);

                #if DEBUG
                Debug.WriteLine($"[圣经设置] 选择颜色: #{color.R:X2}{color.G:X2}{color.B:X2}");
                #endif

                // 实时保存颜色设置
                SaveSettings();
            }
        }

        /// <summary>
        /// 设置改变事件（实时保存）
        /// </summary>
        private void Setting_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isLoading) return; // 加载过程中不保存
            SaveSettings();
        }


        /// <summary>
        /// 保存设置（实时保存）
        /// </summary>
        private void SaveSettings()
        {
            try
            {
                #if DEBUG
                Debug.WriteLine("[圣经设置] 开始保存设置...");
                #endif

                // 保存译本
                if (CmbBibleVersion != null && !string.IsNullOrEmpty(CmbBibleVersion.Text))
                {
                    _configManager.BibleVersion = CmbBibleVersion.Text;
                    #if DEBUG
                    Debug.WriteLine($"[圣经设置] 保存译本: {CmbBibleVersion.Text}");
                    #endif
                }

                // 保存字体（保存FontFamily）
                if (CmbFontFamily != null && CmbFontFamily.SelectedItem is System.Windows.Controls.ComboBoxItem selectedFontItem &&
                    selectedFontItem.Tag is CustomFont selectedFont)
                {
                    _configManager.BibleFontFamily = selectedFont.Family;
                    #if DEBUG
                    Debug.WriteLine($"[圣经设置] 保存字体: {selectedFont.Family}");
                    #endif
                }

                // 保存字号（经文和节号）
                if (CmbFontSize != null && CmbFontSize.SelectedItem is System.Windows.Controls.ComboBoxItem fontSizeItem)
                {
                    double fontSize = double.Parse(fontSizeItem.Content.ToString());
                    _configManager.BibleFontSize = fontSize; // 经文字号
                    _configManager.BibleTitleFontSize = fontSize * 1.333; // 标题 = 字号 × 1.333
                    _configManager.BibleVerseNumberFontSize = fontSize; // 节号 = 经文字号

                    #if DEBUG
                    Debug.WriteLine($"[圣经设置] 保存字号: 经文={fontSize}, 标题={fontSize * 1.333}, 节号={fontSize}");
                    #endif
                }

                // 保存边距
                if (CmbMargin != null && CmbMargin.SelectedItem is System.Windows.Controls.ComboBoxItem marginItem)
                {
                    _configManager.BibleMargin = double.Parse(marginItem.Content.ToString());
                    #if DEBUG
                    Debug.WriteLine($"[圣经设置] 保存边距: {marginItem.Content}");
                    #endif
                }

                // 保存节间距
                if (CmbVerseSpacing != null && CmbVerseSpacing.SelectedItem is System.Windows.Controls.ComboBoxItem spacingItem)
                {
                    _configManager.BibleVerseSpacing = double.Parse(spacingItem.Content.ToString());
                    #if DEBUG
                    Debug.WriteLine($"[圣经设置] 保存节间距: {spacingItem.Content}");
                    #endif
                }

                // 保存颜色
                #if DEBUG
                Debug.WriteLine($"[圣经设置] 开始保存颜色...");
                Debug.WriteLine($"[圣经设置] BorderBackgroundColor is null: {BorderBackgroundColor == null}");
                if (BorderBackgroundColor != null)
                    Debug.WriteLine($"[圣经设置] BorderBackgroundColor.Background is null: {BorderBackgroundColor.Background == null}");
                #endif

                if (BorderBackgroundColor != null && BorderBackgroundColor.Background != null)
                {
                    _configManager.BibleBackgroundColor = ColorToHex(BorderBackgroundColor.Background);
                    #if DEBUG
                    Debug.WriteLine($"[圣经设置] 保存背景色: {_configManager.BibleBackgroundColor}");
                    #endif
                }
                
                if (BorderTextColor != null && BorderTextColor.Background != null)
                {
                    _configManager.BibleTextColor = ColorToHex(BorderTextColor.Background);
                    #if DEBUG
                    Debug.WriteLine($"[圣经设置] 保存经文色: {_configManager.BibleTextColor}");
                    #endif
                }
                
                if (BorderTitleColor != null && BorderTitleColor.Background != null)
                {
                    _configManager.BibleTitleColor = ColorToHex(BorderTitleColor.Background);
                    #if DEBUG
                    Debug.WriteLine($"[圣经设置] 保存标题色: {_configManager.BibleTitleColor}");
                    #endif
                }
                
                if (BorderVerseNumberColor != null && BorderVerseNumberColor.Background != null)
                {
                    _configManager.BibleVerseNumberColor = ColorToHex(BorderVerseNumberColor.Background);
                    #if DEBUG
                    Debug.WriteLine($"[圣经设置] 保存节号色: {_configManager.BibleVerseNumberColor}");
                    #endif
                }

                #if DEBUG
                Debug.WriteLine("[圣经设置] 设置已实时保存");
                #endif

                // 通知主窗口设置已改变，立即应用
                _onSettingsChanged?.Invoke();
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"[圣经设置] ❌ 保存设置失败: {ex.Message}");
                Debug.WriteLine($"[圣经设置] 错误类型: {ex.GetType().Name}");
                Debug.WriteLine($"[圣经设置] 错误堆栈: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Debug.WriteLine($"[圣经设置] 内部异常: {ex.InnerException.Message}");
                }
                #endif
            }
        }

        /// <summary>
        /// 将Brush转换为十六进制颜色字符串
        /// </summary>
        private string ColorToHex(System.Windows.Media.Brush brush)
        {
            if (brush is System.Windows.Media.SolidColorBrush solidBrush)
            {
                var color = solidBrush.Color;
                return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            }
            return "#FFFFFF";
        }

        /// <summary>
        /// ComboBox 鼠标滚轮事件处理（鼠标悬停即可滚动切换）
        /// </summary>
        private void ComboBox_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not System.Windows.Controls.ComboBox comboBox)
                return;

            // 如果ComboBox是打开状态，使用默认行为（滚动下拉列表）
            if (comboBox.IsDropDownOpen)
                return;

            int currentIndex = comboBox.SelectedIndex;
            int itemCount = comboBox.Items.Count;

            if (itemCount == 0)
                return;

            // 向上滚动（Delta > 0）：选择上一项
            // 向下滚动（Delta < 0）：选择下一项
            if (e.Delta > 0)
            {
                // 向上滚动
                if (currentIndex > 0)
                {
                    comboBox.SelectedIndex = currentIndex - 1;
                }
            }
            else if (e.Delta < 0)
            {
                // 向下滚动
                if (currentIndex < itemCount - 1)
                {
                    comboBox.SelectedIndex = currentIndex + 1;
                }
            }

            // 标记事件已处理，防止滚动传递到父控件
            e.Handled = true;

            #if DEBUG
            Debug.WriteLine($"[圣经设置] {comboBox.Name} 滚轮切换: 索引 {currentIndex} -> {comboBox.SelectedIndex}");
            #endif
        }
    }
}

