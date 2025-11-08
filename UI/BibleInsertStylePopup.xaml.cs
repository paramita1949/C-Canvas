using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SkiaSharp;
using ImageColorChanger.Core;
using ImageColorChanger.Services;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// 圣经经文插入样式设置 Popup（重构版 - 只管配置）
    /// </summary>
    public partial class BibleInsertStylePopup : Popup
    {
        private BibleTextInsertConfig _config;
        private readonly Database.DatabaseManager _dbManager;
        private Dictionary<string, string> _fontDisplayMap; // 字体显示名（中文）-> FontFamily（英文）
        
        /// <summary>
        /// 构造函数
        /// </summary>
        public BibleInsertStylePopup(Database.DatabaseManager dbManager)
        {
            InitializeComponent();
            _dbManager = dbManager ?? throw new ArgumentNullException(nameof(dbManager));
            LoadConfig();
            InitializeUI();
            
            // 监听 Opened 事件，设置 Popup 窗口不置顶
            this.Opened += BibleInsertStylePopup_Opened;
            
            //#if DEBUG
            //Debug.WriteLine($"✅ [BibleInsertStylePopup] 初始化完成");
            //#endif
        }
        
        /// <summary>
        /// Popup 打开时，设置其底层窗口不置顶
        /// </summary>
        private void BibleInsertStylePopup_Opened(object sender, EventArgs e)
        {
            try
            {
                // 获取 Popup 的 Child（Border）
                if (this.Child is FrameworkElement child)
                {
                    // 获取 Popup 的底层窗口
                    var window = Window.GetWindow(child);
                    if (window != null)
                    {
                        // 设置窗口不置顶
                        window.Topmost = false;
                        
                        //#if DEBUG
                        //Debug.WriteLine($"✅ [BibleInsertStylePopup] 已设置 Popup 窗口不置顶");
                        //#endif
                    }
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"⚠️ [BibleInsertStylePopup] 设置窗口属性失败: {ex.Message}");
                #else
                _ = ex;  // 防止未使用变量警告
                #endif
            }
        }
        
        /// <summary>
        /// 加载配置（从数据库，字体大小为显示值）
        /// </summary>
        private void LoadConfig()
        {
            _config = new BibleTextInsertConfig();
            
            // 从数据库加载配置
            _config.Style = (BibleTextInsertStyle)int.Parse(_dbManager.GetBibleInsertConfigValue("style", "0"));
            _config.FontFamily = _dbManager.GetBibleInsertConfigValue("font_family", "Microsoft YaHei");
            
            _config.TitleStyle.ColorHex = _dbManager.GetBibleInsertConfigValue("title_color", "#FF0000");
            _config.TitleStyle.FontSize = float.Parse(_dbManager.GetBibleInsertConfigValue("title_size", "20"));
            _config.TitleStyle.IsBold = _dbManager.GetBibleInsertConfigValue("title_bold", "1") == "1";
            
            _config.VerseStyle.ColorHex = _dbManager.GetBibleInsertConfigValue("verse_color", "#D2691E");
            _config.VerseStyle.FontSize = float.Parse(_dbManager.GetBibleInsertConfigValue("verse_size", "15"));
            _config.VerseStyle.IsBold = _dbManager.GetBibleInsertConfigValue("verse_bold", "0") == "1";
            _config.VerseStyle.VerseSpacing = float.Parse(_dbManager.GetBibleInsertConfigValue("verse_spacing", "10"));
            
            _config.AutoHideNavigationAfterInsert = _dbManager.GetBibleInsertConfigValue("auto_hide_navigation", "1") == "1";
            
            //#if DEBUG
            //Debug.WriteLine($"📝 [BibleInsertStylePopup] 从数据库加载配置");
            //Debug.WriteLine($"   字体: {_config.FontFamily}");
            //Debug.WriteLine($"   样式: {_config.Style}");
            //Debug.WriteLine($"   标题字体大小（显示值）: {_config.TitleStyle.FontSize}");
            //Debug.WriteLine($"   经文字体大小（显示值）: {_config.VerseStyle.FontSize}");
            //#endif
        }
        
        /// <summary>
        /// 初始化UI控件
        /// </summary>
        private void InitializeUI()
        {
            // 初始化样式布局下拉框
            CmbStyleLayout.SelectedIndex = (int)_config.Style;
            
            // 使用 FontService 加载字体列表
            var fontService = FontService.Instance;
            var fontConfig = fontService.GetFontConfig();
            
            // 🆕 使用字典存储：显示名（中文） -> FontFamily（英文）
            _fontDisplayMap = new Dictionary<string, string>();
            if (fontConfig != null && fontConfig.FontCategories != null)
            {
                foreach (var category in fontConfig.FontCategories)
                {
                    foreach (var font in category.Fonts)
                    {
                        if (!string.IsNullOrEmpty(font.Family) && !string.IsNullOrEmpty(font.Name))
                        {
                            // 使用中文名作为显示，避免重复
                            if (!_fontDisplayMap.ContainsKey(font.Name))
                            {
                                _fontDisplayMap[font.Name] = font.Family;
                            }
                        }
                    }
                }
            }
            
            // 提取显示名称列表（中文名）
            var fontDisplayNames = _fontDisplayMap.Keys.ToList();
            
            CmbFont.ItemsSource = fontDisplayNames;
            
            // 🆕 根据配置的 FontFamily（英文）找到对应的中文显示名
            string selectedDisplayName = null;
            foreach (var kvp in _fontDisplayMap)
            {
                if (kvp.Value == _config.FontFamily)
                {
                    selectedDisplayName = kvp.Key;
                    break;
                }
            }
            
            // 尝试选中配置中的字体（使用中文名）
            if (selectedDisplayName != null)
            {
                CmbFont.SelectedItem = selectedDisplayName;
            }
            else if (fontDisplayNames.Count > 0)
            {
                // 如果找不到，选中第一个
                CmbFont.SelectedIndex = 0;
            }
            
            // 标题样式
            SetColorButton(BtnTitleColor, _config.TitleStyle.GetSKColor());
            // 生成字体大小选项：10-200（与幻灯片一致）
            var titleSizes = Enumerable.Range(10, 191).ToList(); // 10 到 200
            CmbTitleSize.ItemsSource = titleSizes;
            CmbTitleSize.SelectedItem = (int)_config.TitleStyle.FontSize;
            ChkTitleBold.IsChecked = _config.TitleStyle.IsBold;
            
            // 经文样式
            SetColorButton(BtnVerseColor, _config.VerseStyle.GetSKColor());
            // 生成字体大小选项：10-200（与幻灯片一致）
            var verseSizes = Enumerable.Range(10, 191).ToList(); // 10 到 200
            CmbVerseSize.ItemsSource = verseSizes;
            CmbVerseSize.SelectedItem = (int)_config.VerseStyle.FontSize;
            ChkVerseBold.IsChecked = _config.VerseStyle.IsBold;
            CmbVerseSpacing.ItemsSource = new[] { 0, 5, 10, 15, 20, 30, 40, 50 };
            CmbVerseSpacing.SelectedItem = (int)_config.VerseStyle.VerseSpacing;
        }
        
        /// <summary>
        /// 设置颜色按钮（背景色 + 自动前景色）
        /// </summary>
        private void SetColorButton(System.Windows.Controls.Button button, SKColor skColor)
        {
            try
            {
                var bgColor = System.Windows.Media.Color.FromArgb(skColor.Alpha, skColor.Red, skColor.Green, skColor.Blue);
                button.Background = new SolidColorBrush(bgColor);
                
                // 根据背景色亮度自动设置前景色（黑色或白色）
                double luminance = (0.299 * bgColor.R + 0.587 * bgColor.G + 0.114 * bgColor.B) / 255;
                button.Foreground = luminance > 0.5 
                    ? new SolidColorBrush(Colors.Black) 
                    : new SolidColorBrush(Colors.White);
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"⚠️ [BibleInsertStylePopup] 设置颜色按钮失败: {ex.Message}");
                #else
                _ = ex;  // 防止未使用变量警告
                #endif
                button.Background = new SolidColorBrush(Colors.Gray);
                button.Foreground = new SolidColorBrush(Colors.White);
            }
        }
        
        /// <summary>
        /// 样式改变事件（统一处理）
        /// </summary>
        private void StyleChanged(object sender, RoutedEventArgs e)
        {
            if (_config == null)
                return;
                
            UpdateAndSaveConfig();
        }
        
        /// <summary>
        /// 标题字体大小改变事件
        /// </summary>
        private void TitleSizeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_config == null)
                return;
                
            UpdateAndSaveConfig();
        }
        
        /// <summary>
        /// 经文字体大小改变事件
        /// </summary>
        private void VerseSizeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_config == null)
                return;
                
            UpdateAndSaveConfig();
        }
        
        /// <summary>
        /// 更新并保存配置到数据库
        /// </summary>
        private void UpdateAndSaveConfig()
        {
            try
            {
                // 更新样式布局
                if (CmbStyleLayout.SelectedItem is ComboBoxItem layoutItem && 
                    layoutItem.Tag is string tag)
                {
                    _config.Style = (BibleTextInsertStyle)int.Parse(tag);
                    _dbManager.SetBibleInsertConfigValue("style", tag);
                }
                
                // 🆕 更新统一字体（将中文显示名转换为英文 FontFamily）
                if (CmbFont.SelectedItem is string fontDisplayName && 
                    _fontDisplayMap != null && 
                    _fontDisplayMap.TryGetValue(fontDisplayName, out string fontFamily))
                {
                    _config.FontFamily = fontFamily;
                    _dbManager.SetBibleInsertConfigValue("font_family", fontFamily);
                    
                    //#if DEBUG
                    //Debug.WriteLine($"📝 [BibleInsertStylePopup] 更新字体配置: {fontDisplayName} -> {fontFamily}");
                    //#endif
                }
                
                // 更新标题样式
                if (CmbTitleSize.SelectedItem != null)
                {
                    _config.TitleStyle.FontSize = (int)CmbTitleSize.SelectedItem;
                    _dbManager.SetBibleInsertConfigValue("title_size", _config.TitleStyle.FontSize.ToString());
                }
                
                _config.TitleStyle.IsBold = ChkTitleBold.IsChecked ?? true;
                _dbManager.SetBibleInsertConfigValue("title_bold", _config.TitleStyle.IsBold ? "1" : "0");
                
                // 更新经文样式
                if (CmbVerseSize.SelectedItem != null)
                {
                    _config.VerseStyle.FontSize = (int)CmbVerseSize.SelectedItem;
                    _dbManager.SetBibleInsertConfigValue("verse_size", _config.VerseStyle.FontSize.ToString());
                }
                
                _config.VerseStyle.IsBold = ChkVerseBold.IsChecked ?? false;
                _dbManager.SetBibleInsertConfigValue("verse_bold", _config.VerseStyle.IsBold ? "1" : "0");
                
                if (CmbVerseSpacing.SelectedItem != null)
                {
                    _config.VerseStyle.VerseSpacing = (float)(int)CmbVerseSpacing.SelectedItem;
                    _dbManager.SetBibleInsertConfigValue("verse_spacing", _config.VerseStyle.VerseSpacing.ToString());
                }
                
                //#if DEBUG
                //Debug.WriteLine($"✅ [BibleInsertStylePopup] 配置已保存到数据库");
                //Debug.WriteLine($"   样式布局: {_config.Style}");
                //Debug.WriteLine($"   统一字体: {_config.FontFamily}");
                //Debug.WriteLine($"   标题: {_config.TitleStyle.FontSize}pt, 粗体={_config.TitleStyle.IsBold}");
                //Debug.WriteLine($"   经文: {_config.VerseStyle.FontSize}pt, 粗体={_config.VerseStyle.IsBold}, 节距={_config.VerseStyle.VerseSpacing}px");
                //#endif
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"❌ [BibleInsertStylePopup] 更新配置失败: {ex.Message}");
                #else
                _ = ex;  // 防止未使用变量警告
                #endif
            }
        }
        
        
        /// <summary>
        /// 标题颜色选择按钮点击事件
        /// </summary>
        private void BtnTitleColor_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var colorDialog = new System.Windows.Forms.ColorDialog();
                var currentColor = _config.TitleStyle.GetSKColor();
                colorDialog.Color = System.Drawing.Color.FromArgb(
                    currentColor.Alpha, currentColor.Red, currentColor.Green, currentColor.Blue);
                
                if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var color = colorDialog.Color;
                    _config.TitleStyle.SetSKColor(new SKColor(color.R, color.G, color.B, color.A));
                    SetColorButton(BtnTitleColor, _config.TitleStyle.GetSKColor());
                    _dbManager.SetBibleInsertConfigValue("title_color", _config.TitleStyle.ColorHex);
                    
                    #if DEBUG
                    Debug.WriteLine($"✅ [BibleInsertStylePopup] 标题颜色已更改: {_config.TitleStyle.ColorHex}");
                    #endif
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"❌ [BibleInsertStylePopup] 选择标题颜色失败: {ex.Message}");
                #else
                _ = ex;  // 防止未使用变量警告
                #endif
            }
        }
        
        /// <summary>
        /// 经文颜色选择按钮点击事件
        /// </summary>
        private void BtnVerseColor_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var colorDialog = new System.Windows.Forms.ColorDialog();
                var currentColor = _config.VerseStyle.GetSKColor();
                colorDialog.Color = System.Drawing.Color.FromArgb(
                    currentColor.Alpha, currentColor.Red, currentColor.Green, currentColor.Blue);
                
                if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var color = colorDialog.Color;
                    _config.VerseStyle.SetSKColor(new SKColor(color.R, color.G, color.B, color.A));
                    SetColorButton(BtnVerseColor, _config.VerseStyle.GetSKColor());
                    _dbManager.SetBibleInsertConfigValue("verse_color", _config.VerseStyle.ColorHex);
                    
                    #if DEBUG
                    Debug.WriteLine($"✅ [BibleInsertStylePopup] 经文颜色已更改: {_config.VerseStyle.ColorHex}");
                    #endif
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($"❌ [BibleInsertStylePopup] 选择经文颜色失败: {ex.Message}");
                #else
                _ = ex;  // 防止未使用变量警告
                #endif
            }
        }
    }
}
