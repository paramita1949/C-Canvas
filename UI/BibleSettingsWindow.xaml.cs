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
using ImageColorChanger.Services.Interfaces;
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
        private readonly IBibleService _bibleService;
        // 暂时保留 Windows Forms ColorDialog（未来可替换为 WPF 颜色选择器）
        private System.Windows.Forms.ColorDialog _colorDialog;
        private Dictionary<string, CustomFont> _fontMap = new Dictionary<string, CustomFont>(); // 字体名称到字体信息的映射
        private bool _isLoading = false; // 标记是否正在加载设置，避免触发保存
        private bool _isSelectingColor = false; // 标记是否正在选择颜色，避免窗口自动关闭
        private Action _onSettingsChanged; // 设置改变时的回调（需要重新加载经文，如译本切换）
        private Action _onStyleChanged; // 样式改变时的回调（只需刷新样式，如颜色、字体等）

        /// <summary>
        /// 是否正在选择颜色（供外部查询，避免在选择颜色时关闭窗口）
        /// </summary>
        public bool IsSelectingColor => _isSelectingColor;

        public BibleSettingsWindow(ConfigManager configManager, IBibleService bibleService, Action onSettingsChanged = null, Action onStyleChanged = null)
        {
            _isLoading = true; // 在 InitializeComponent 之前设置，防止初始化时触发保存
            
            InitializeComponent();
            _configManager = configManager;
            _bibleService = bibleService;
            _onSettingsChanged = onSettingsChanged;
            _onStyleChanged = onStyleChanged;
            // 暂时保留 Windows Forms ColorDialog（未来可替换为 WPF 颜色选择器）
            _colorDialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                AnyColor = true
            };

            LoadFontFamilies();
            LoadSettings();
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
                
                //#if DEBUG
                //Debug.WriteLine($"[圣经设置] 窗口拖动后位置: Left={this.Left}, Top={this.Top}");
                //Debug.WriteLine($"[圣经设置] 位置已保存到配置");
                //#endif
            }
        }

        /// <summary>
        /// 关闭按钮点击事件
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        /// <summary>
        /// 加载字体列表（使用程序自带字体配置）
        /// </summary>
        private void LoadFontFamilies()
        {
            try
            {
                // 使用FontService统一加载字体（使用完整版配置）
                if (!FontService.Instance.Initialize())
                {
                    //#if DEBUG
                    //Debug.WriteLine($"[圣经设置] FontService初始化失败，使用系统默认字体");
                    //#endif
                    LoadDefaultFonts();
                    return;
                }

                // 使用FontService填充字体选择器（简化版，不加载字体对象）
                _fontMap = FontService.Instance.PopulateComboBoxSimple(
                    CmbFontFamily,
                    showCategoryHeaders: true
                );

                if (_fontMap.Count == 0)
                {
                    //#if DEBUG
                    //Debug.WriteLine($"[圣经设置] 未加载到任何字体，使用系统默认字体");
                    //#endif
                    LoadDefaultFonts();
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"[圣经设置] 加载字体失败: {ex.Message}");
                #else
                _ = ex;  // 防止未使用变量警告
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
                //#if DEBUG
                //Debug.WriteLine($"[圣经设置] 开始加载设置");
                //Debug.WriteLine($"[圣经设置] 译本: {_configManager.BibleVersion}");
                //Debug.WriteLine($"[圣经设置] 字体: {_configManager.BibleFontFamily}");
                //Debug.WriteLine($"[圣经设置] 字号: {_configManager.BibleFontSize}");
                //Debug.WriteLine($"[圣经设置] 行距: {_configManager.BibleLineHeight}");
                //Debug.WriteLine($"[圣经设置] 边距: {_configManager.BibleMargin}");
                //#endif

                // 译本（根据配置的数据库文件名选择对应的译本）
                if (CmbBibleVersion != null)
                {
                    var dbFileName = _configManager.BibleDatabaseFileName ?? "bible.db";
                    
                    // 根据数据库文件名找到对应的ComboBoxItem并选中
                    foreach (System.Windows.Controls.ComboBoxItem item in CmbBibleVersion.Items)
                    {
                        if (item.Tag?.ToString() == dbFileName)
                        {
                            item.IsSelected = true;
                            break;
                        }
                    }
                }

                // 字体（根据Family查找对应的字体名称）
                if (CmbFontFamily != null && !string.IsNullOrEmpty(_configManager.BibleFontFamily))
                {
                    string fontName = FindFontNameByFamily(_configManager.BibleFontFamily);
                    if (!string.IsNullOrEmpty(fontName))
                    {
                        // 找到了字体名称，选择对应的项
                        SelectComboBoxItemByContent(CmbFontFamily, fontName);
                    }
                    else
                    {
                        // 如果找不到字体名称，尝试直接使用family名称查找ComboBox项
                        // 因为等线等系统字体可能直接用family名称显示
                        bool found = SelectComboBoxItemByContent(CmbFontFamily, _configManager.BibleFontFamily);
                        
                        // 如果还是找不到，尝试查找字体名称（等线的显示名称）
                        if (!found)
                        {
                            // 等线的显示名称可能是"等线"
                            if (_configManager.BibleFontFamily.Equals("DengXian", StringComparison.OrdinalIgnoreCase))
                            {
                                SelectComboBoxItemByContent(CmbFontFamily, "等线");
                            }
                        }
                    }
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

                // 节间距（实际值 ÷ 10 = 显示值）
                if (CmbVerseSpacing != null)
                {
                    double displayValue = _configManager.BibleVerseSpacing / 10;
                    SelectComboBoxItem(CmbVerseSpacing, displayValue.ToString("0"));
                }

                // 搜索结果字号（悬浮）
                if (CmbSearchFloatingFontSize != null && _configManager.BibleSearchFloatingFontSize > 0)
                {
                    SelectComboBoxItem(CmbSearchFloatingFontSize, _configManager.BibleSearchFloatingFontSize.ToString("0"));
                }

                // 搜索结果字号（内嵌）
                if (CmbSearchEmbeddedFontSize != null && _configManager.BibleSearchEmbeddedFontSize > 0)
                {
                    SelectComboBoxItem(CmbSearchEmbeddedFontSize, _configManager.BibleSearchEmbeddedFontSize.ToString("0"));
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
                if (BorderHighlightColor != null && !string.IsNullOrEmpty(_configManager.BibleHighlightColor))
                {
                    UpdateColorPreview(BorderHighlightColor, _configManager.BibleHighlightColor);
                }

                // 保存投影记录复选框
                if (ChkSaveBibleHistory != null)
                {
                    ChkSaveBibleHistory.IsChecked = _configManager.SaveBibleHistory;
                }

                //#if DEBUG
                //Debug.WriteLine("[圣经设置] 已加载当前设置");
                //#endif
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"[圣经设置] 加载设置失败: {ex.Message}");
                Debug.WriteLine($"[圣经设置] 堆栈: {ex.StackTrace}");
                #else
                _ = ex;  // 防止未使用变量警告
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
            if (string.IsNullOrEmpty(family))
                return null;
            
            // 查找匹配的字体
            var font = _fontMap.Values.FirstOrDefault(f => 
                f.Family.Equals(family, StringComparison.OrdinalIgnoreCase) ||
                f.Name.Equals(family, StringComparison.OrdinalIgnoreCase));
            
            if (font != null)
                return font.Name;
            
            // 如果找不到，尝试直接使用family作为系统字体，不强制返回第一个字体
            // 这样即使字体服务中没有配置，也能使用系统字体
            return null; // 返回null，让调用者决定如何处理
        }

        /// <summary>
        /// 选中ComboBox中的项（按内容）
        /// </summary>
        /// <returns>是否找到并选中了项</returns>
        private bool SelectComboBoxItemByContent(System.Windows.Controls.ComboBox comboBox, string content)
        {
            if (comboBox == null || string.IsNullOrEmpty(content))
                return false;
                
            foreach (var item in comboBox.Items)
            {
                if (item is System.Windows.Controls.ComboBoxItem comboItem && 
                    comboItem.Content?.ToString() == content)
                {
                    comboItem.IsSelected = true;
                    return true;
                }
            }
            return false;
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
        /// 从 ComboBox 读取数值（优先 SelectedItem，其次 Text）
        /// </summary>
        private static bool TryGetComboBoxNumericValue(System.Windows.Controls.ComboBox comboBox, out double value)
        {
            value = 0;
            if (comboBox == null)
            {
                return false;
            }

            if (comboBox.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem &&
                double.TryParse(selectedItem.Content?.ToString(), out value))
            {
                return true;
            }

            return double.TryParse(comboBox.Text, out value);
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
            //#if DEBUG
            //Debug.WriteLine($"[圣经设置] 颜色块点击事件触发");
            //#endif
            
            if (sender is not System.Windows.Controls.Border border)
            {
                //#if DEBUG
                //Debug.WriteLine($"[圣经设置] sender 不是 Border，类型: {sender?.GetType().Name}");
                //#endif
                return;
            }

            //#if DEBUG
            //Debug.WriteLine($"[圣经设置] Border 名称: {border.Name}, 当前颜色: {border.Background}");
            //#endif

            // 设置 ColorDialog 的初始颜色
            try
            {
                var brush = border.Background as System.Windows.Media.SolidColorBrush;
                var currentColor = brush?.Color ?? System.Windows.Media.Colors.White;
                _colorDialog.Color = System.Drawing.Color.FromArgb(
                    currentColor.A, currentColor.R, currentColor.G, currentColor.B);
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"[圣经设置] 设置初始颜色失败: {ex.Message}");
                #else
                _ = ex;
                #endif
                _colorDialog.Color = System.Drawing.Color.White;
            }

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
                
                result = _colorDialog.ShowDialog(owner);
                
                owner.ReleaseHandle();
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"[圣经设置] ShowDialog() 异常: {ex.Message}");
                #else
                _ = ex;
                #endif
                result = _colorDialog.ShowDialog();
            }
            finally
            {
                _isSelectingColor = false;
            }
            
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                var color = _colorDialog.Color;
                var wpfColor = WpfColor.FromArgb(color.A, color.R, color.G, color.B);
                border.Background = new System.Windows.Media.SolidColorBrush(wpfColor);
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
                //#if DEBUG
                //Debug.WriteLine("[圣经设置] 开始保存设置...");
                //#endif

                bool versionChanged = false; // 标记是否发生译本切换
                
                // 保存译本和数据库文件名
                if (CmbBibleVersion != null && CmbBibleVersion.SelectedItem is System.Windows.Controls.ComboBoxItem selectedVersionItem)
                {
                    var versionName = selectedVersionItem.Content?.ToString() ?? "和合本";
                    var dbFileName = selectedVersionItem.Tag?.ToString() ?? "bible.db";
                    
                    // 检查是否发生了译本切换
                    versionChanged = _configManager.BibleDatabaseFileName != dbFileName;
                    
                    _configManager.BibleVersion = versionName;
                    _configManager.BibleDatabaseFileName = dbFileName;
                    
                    // 如果译本切换了，通知 BibleService 更新数据库路径
                    if (versionChanged)
                    {
                        _bibleService?.UpdateDatabasePath();
                        
                        #if DEBUG
                        Debug.WriteLine($"[圣经设置] 译本已切换: {versionName}, 数据库: {dbFileName}");
                        #endif
                    }
                    else
                    {
                        //#if DEBUG
                        //Debug.WriteLine($"[圣经设置] 保存译本: {versionName}, 数据库: {dbFileName}");
                        //#endif
                    }
                }

                // 保存字体（保存FontFamily）
                if (CmbFontFamily != null && CmbFontFamily.SelectedItem is System.Windows.Controls.ComboBoxItem selectedFontItem &&
                    selectedFontItem.Tag is CustomFont selectedFont)
                {
                    _configManager.BibleFontFamily = selectedFont.Family;
                    //#if DEBUG
                    //Debug.WriteLine($"[圣经设置] 保存字体: {selectedFont.Family}");
                    //#endif
                }

                // 保存字号（经文和节号）
                if (CmbFontSize != null && CmbFontSize.SelectedItem is System.Windows.Controls.ComboBoxItem fontSizeItem)
                {
                    double fontSize = double.Parse(fontSizeItem.Content.ToString());
                    _configManager.BibleFontSize = fontSize; // 经文字号
                    _configManager.BibleTitleFontSize = fontSize * 1.333; // 标题 = 字号 × 1.333
                    _configManager.BibleVerseNumberFontSize = fontSize; // 节号 = 经文字号

                    //#if DEBUG
                    //Debug.WriteLine($"[圣经设置] 保存字号: 经文={fontSize}, 标题={fontSize * 1.333}, 节号={fontSize}");
                    //#endif
                }

                // 保存边距
                if (CmbMargin != null && CmbMargin.SelectedItem is System.Windows.Controls.ComboBoxItem marginItem)
                {
                    _configManager.BibleMargin = double.Parse(marginItem.Content.ToString());
                    //#if DEBUG
                    //Debug.WriteLine($"[圣经设置] 保存边距: {marginItem.Content}");
                    //#endif
                }

                // 保存节间距（显示值 × 10）
                if (CmbVerseSpacing != null && CmbVerseSpacing.SelectedItem is System.Windows.Controls.ComboBoxItem spacingItem)
                {
                    double displayValue = double.Parse(spacingItem.Content.ToString());
                    _configManager.BibleVerseSpacing = displayValue * 10; // 实际值 = 显示值 × 10
                    //#if DEBUG
                    //Debug.WriteLine($"[圣经设置] 保存节间距: 显示值={displayValue}, 实际值={displayValue * 10}");
                    //#endif
                }

                // 保存搜索结果字号（悬浮）
                if (TryGetComboBoxNumericValue(CmbSearchFloatingFontSize, out var floatingFontSize))
                {
                    _configManager.BibleSearchFloatingFontSize = floatingFontSize;
                }

                // 保存搜索结果字号（内嵌）
                if (TryGetComboBoxNumericValue(CmbSearchEmbeddedFontSize, out var embeddedFontSize))
                {
                    _configManager.BibleSearchEmbeddedFontSize = embeddedFontSize;
                }

                // 保存颜色
                //#if DEBUG
                //Debug.WriteLine($"[圣经设置] 开始保存颜色...");
                //Debug.WriteLine($"[圣经设置] BorderBackgroundColor is null: {BorderBackgroundColor == null}");
                //if (BorderBackgroundColor != null)
                //    Debug.WriteLine($"[圣经设置] BorderBackgroundColor.Background is null: {BorderBackgroundColor.Background == null}");
                //#endif

                if (BorderBackgroundColor != null && BorderBackgroundColor.Background != null)
                {
                    _configManager.BibleBackgroundColor = ColorToHex(BorderBackgroundColor.Background);
                    //#if DEBUG
                    //Debug.WriteLine($"[圣经设置] 保存背景色: {_configManager.BibleBackgroundColor}");
                    //#endif
                }
                
                if (BorderTextColor != null && BorderTextColor.Background != null)
                {
                    _configManager.BibleTextColor = ColorToHex(BorderTextColor.Background);
                    //#if DEBUG
                    //Debug.WriteLine($"[圣经设置] 保存经文色: {_configManager.BibleTextColor}");
                    //#endif
                }
                
                if (BorderTitleColor != null && BorderTitleColor.Background != null)
                {
                    _configManager.BibleTitleColor = ColorToHex(BorderTitleColor.Background);
                    //#if DEBUG
                    //Debug.WriteLine($"[圣经设置] 保存标题色: {_configManager.BibleTitleColor}");
                    //#endif
                }
                
                if (BorderVerseNumberColor != null && BorderVerseNumberColor.Background != null)
                {
                    _configManager.BibleVerseNumberColor = ColorToHex(BorderVerseNumberColor.Background);
                    //#if DEBUG
                    //Debug.WriteLine($"[圣经设置] 保存节号色: {_configManager.BibleVerseNumberColor}");
                    //#endif
                }
                
                if (BorderHighlightColor != null && BorderHighlightColor.Background != null)
                {
                    _configManager.BibleHighlightColor = ColorToHex(BorderHighlightColor.Background);
                    //#if DEBUG
                    //Debug.WriteLine($"[圣经设置] 保存选中色: {_configManager.BibleHighlightColor}");
                    //#endif
                }

                //#if DEBUG
                //Debug.WriteLine("[圣经设置] 设置已实时保存");
                //#endif

                // 根据改变类型调用不同的回调
                if (versionChanged)
                {
                    // 译本切换：需要重新加载经文
                    _onSettingsChanged?.Invoke();
                    
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine("[圣经设置] 译本切换，触发完整重新加载");
                    #endif
                }
                else
                {
                    // 样式改变（颜色、字体、字号等）：只刷新样式
                    _onStyleChanged?.Invoke();
                    
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine("[圣经设置] 样式改变，仅刷新样式");
                    //#endif
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"[圣经设置]  保存设置失败: {ex.Message}");
                Debug.WriteLine($"[圣经设置] 错误类型: {ex.GetType().Name}");
                Debug.WriteLine($"[圣经设置] 错误堆栈: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Debug.WriteLine($"[圣经设置] 内部异常: {ex.InnerException.Message}");
                }
                #else
                _ = ex;  // 防止未使用变量警告
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

            //#if DEBUG
            //Debug.WriteLine($"[圣经设置] {comboBox.Name} 滚轮切换: 索引 {currentIndex} -> {comboBox.SelectedIndex}");
            //#endif
        }

        /// <summary>
        /// 保存历史记录复选框状态改变事件
        /// </summary>
        private void SaveHistory_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return; // 加载过程中不保存
            
            if (ChkSaveBibleHistory != null)
            {
                _configManager.SaveBibleHistory = ChkSaveBibleHistory.IsChecked == true;
                
                //#if DEBUG
                //Debug.WriteLine($"[圣经设置] 保存投影记录: {_configManager.SaveBibleHistory}");
                //#endif
            }
        }
    }
}



