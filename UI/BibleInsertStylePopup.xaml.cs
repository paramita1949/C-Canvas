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
        private const string DefaultTitleColorHex = "#FF0000";
        private const string DefaultVerseColorHex = "#FF9A35";
        private const string DefaultVerseNumberColorHex = "#FFFF00";
        private const string DefaultPopupBackgroundColorHex = "#1C2740";

        private sealed class PopupNumericOption
        {
            public PopupNumericOption(int value, string label)
            {
                Value = value;
                Label = label;
            }

            public int Value { get; }

            public string Label { get; }

            public override string ToString() => Label;
        }

        private BibleTextInsertConfig _config;
        private readonly Database.DatabaseManager _dbManager;
        private Dictionary<string, string> _fontDisplayMap; // 字体显示名（中文）-> FontFamily（英文）
        public event Action PopupStyleChanged;
        
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
            //Debug.WriteLine($" [BibleInsertStylePopup] 初始化完成");
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
                        //Debug.WriteLine($" [BibleInsertStylePopup] 已设置 Popup 窗口不置顶");
                        //#endif
                    }
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($" [BibleInsertStylePopup] 设置窗口属性失败: {ex.Message}");
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
            _config.FontFamily = _dbManager.GetBibleInsertConfigValue("font_family", "DengXian");

            _config.TitleStyle.ColorHex = _dbManager.GetBibleInsertConfigValue("title_color", DefaultTitleColorHex);
            _config.TitleStyle.FontSize = float.Parse(_dbManager.GetBibleInsertConfigValue("title_size", "50"));
            _config.TitleStyle.IsBold = _dbManager.GetBibleInsertConfigValue("title_bold", "1") == "1";

            _config.VerseStyle.ColorHex = _dbManager.GetBibleInsertConfigValue("verse_color", DefaultVerseColorHex);
            _config.VerseStyle.FontSize = float.Parse(_dbManager.GetBibleInsertConfigValue("verse_size", "40"));
            _config.VerseStyle.IsBold = _dbManager.GetBibleInsertConfigValue("verse_bold", "0") == "1";
            _config.VerseStyle.VerseSpacing = float.Parse(_dbManager.GetBibleInsertConfigValue("verse_spacing", "1.2"));

            _config.VerseNumberStyle.ColorHex = _dbManager.GetBibleInsertConfigValue("verse_number_color", DefaultVerseNumberColorHex);
            _config.VerseNumberStyle.FontSize = float.Parse(_dbManager.GetBibleInsertConfigValue("verse_number_size", "40"));
            _config.VerseNumberStyle.IsBold = _dbManager.GetBibleInsertConfigValue("verse_number_bold", "1") == "1";

            _config.AutoHideNavigationAfterInsert = _dbManager.GetBibleInsertConfigValue("auto_hide_navigation", "1") == "1";
            var popupPosition = _dbManager.GetBibleInsertConfigValue("popup_position", "Top");
            _config.PopupPosition = popupPosition switch
            {
                "Top" => BiblePopupPosition.Top,
                "Center" => BiblePopupPosition.Center,
                _ => BiblePopupPosition.Bottom
            };
            _config.PopupFontFamily = _dbManager.GetBibleInsertConfigValue("popup_font_family", "Microsoft YaHei");
            if (!int.TryParse(_dbManager.GetBibleInsertConfigValue("popup_title_format", "0"), out var popupTitleFormat))
            {
                popupTitleFormat = 0;
            }
            _config.PopupTitleFormat = Enum.IsDefined(typeof(BiblePopupTitleFormat), popupTitleFormat)
                ? (BiblePopupTitleFormat)popupTitleFormat
                : BiblePopupTitleFormat.DotChapterVerse;
            _config.PopupTitleStyle.ColorHex = _dbManager.GetBibleInsertConfigValue("popup_title_color", _config.TitleStyle.ColorHex);
            _config.PopupTitleStyle.FontSize = float.Parse(_dbManager.GetBibleInsertConfigValue("popup_title_size", "70"));
            _config.PopupTitleStyle.IsBold = _dbManager.GetBibleInsertConfigValue("popup_title_bold", _config.TitleStyle.IsBold ? "1" : "0") == "1";

            _config.PopupVerseStyle.ColorHex = _dbManager.GetBibleInsertConfigValue("popup_verse_color", _config.VerseStyle.ColorHex);
            _config.PopupVerseStyle.FontSize = float.Parse(_dbManager.GetBibleInsertConfigValue("popup_verse_size", "60"));
            _config.PopupVerseStyle.IsBold = _dbManager.GetBibleInsertConfigValue("popup_verse_bold", _config.VerseStyle.IsBold ? "1" : "0") == "1";
            _config.PopupVerseStyle.VerseSpacing = float.Parse(_dbManager.GetBibleInsertConfigValue("popup_verse_spacing", _config.VerseStyle.VerseSpacing.ToString("F1")));

            _config.PopupVerseNumberStyle.ColorHex = _dbManager.GetBibleInsertConfigValue("popup_verse_number_color", _config.VerseNumberStyle.ColorHex);
            _config.PopupVerseNumberStyle.FontSize = float.Parse(_dbManager.GetBibleInsertConfigValue("popup_verse_number_size", "60"));
            _config.PopupVerseNumberStyle.IsBold = _dbManager.GetBibleInsertConfigValue("popup_verse_number_bold", _config.VerseNumberStyle.IsBold ? "1" : "0") == "1";

            _config.PopupBackgroundColorHex = _dbManager.GetBibleInsertConfigValue("popup_bg_color", DefaultPopupBackgroundColorHex);
            if (!int.TryParse(_dbManager.GetBibleInsertConfigValue("popup_bg_opacity", "0"), out var popupOpacity))
            {
                popupOpacity = 0;
            }
            _config.PopupBackgroundOpacity = Math.Clamp(popupOpacity, 0, 100);

            if (!int.TryParse(_dbManager.GetBibleInsertConfigValue("popup_duration_minutes", "3"), out var popupDurationMinutes))
            {
                popupDurationMinutes = 3;
            }
            _config.PopupDurationMinutes = popupDurationMinutes;

            if (!int.TryParse(_dbManager.GetBibleInsertConfigValue("popup_verse_count", "4"), out var popupVerseCount))
            {
                popupVerseCount = 4;
            }
            _config.PopupVerseCount = popupVerseCount;

            if (!int.TryParse(_dbManager.GetBibleInsertConfigValue("slide_pinyin_quick_locate_action", "0"), out var quickLocateAction))
            {
                quickLocateAction = 0;
            }
            _config.QuickLocateSlideAction = Enum.IsDefined(typeof(BibleQuickLocateSlideAction), quickLocateAction)
                ? (BibleQuickLocateSlideAction)quickLocateAction
                : BibleQuickLocateSlideAction.HistoryFirst;
            
            //#if DEBUG
            //Debug.WriteLine($"[BibleInsertStylePopup] 从数据库加载配置");
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
            
            // 使用字典存储：显示名（中文） -> FontFamily（英文）
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
            
            // 根据配置的 FontFamily（英文）找到对应的中文显示名
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
            SetColorButton(BtnTitleColor, _config.TitleStyle.GetSKColor(), forceBlackText: true);
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

            // 节距（行间距）选项：1.0-2.5，步长0.1
            var verseSpacingOptions = new List<double>();
            for (double i = 1.0; i <= 2.5; i += 0.1)
            {
                verseSpacingOptions.Add(Math.Round(i, 1));
            }
            CmbVerseSpacing.ItemsSource = verseSpacingOptions;
            CmbVerseSpacing.SelectedItem = Math.Round(_config.VerseStyle.VerseSpacing, 1);

            // 节号样式
            SetColorButton(BtnVerseNumberColor, _config.VerseNumberStyle.GetSKColor());
            // 生成字体大小选项：10-200（与幻灯片一致）
            var verseNumberSizes = Enumerable.Range(10, 191).ToList(); // 10 到 200
            CmbVerseNumberSize.ItemsSource = verseNumberSizes;
            CmbVerseNumberSize.SelectedItem = (int)_config.VerseNumberStyle.FontSize;
            ChkVerseNumberBold.IsChecked = _config.VerseNumberStyle.IsBold;

            CmbPopupPosition.SelectedIndex = _config.PopupPosition switch
            {
                BiblePopupPosition.Top => 0,
                BiblePopupPosition.Center => 1,
                _ => 2
            };
            CmbPopupFont.ItemsSource = fontDisplayNames;
            string popupDisplayName = null;
            foreach (var kvp in _fontDisplayMap)
            {
                if (kvp.Value == _config.PopupFontFamily)
                {
                    popupDisplayName = kvp.Key;
                    break;
                }
            }
            if (popupDisplayName != null)
            {
                CmbPopupFont.SelectedItem = popupDisplayName;
            }
            else if (fontDisplayNames.Count > 0)
            {
                CmbPopupFont.SelectedIndex = 0;
            }
            CmbPopupTitleFormat.SelectedIndex = (int)_config.PopupTitleFormat;

            SetColorButton(BtnPopupTitleColor, _config.PopupTitleStyle.GetSKColor(), forceBlackText: true);
            CmbPopupTitleSize.ItemsSource = titleSizes;
            CmbPopupTitleSize.SelectedItem = (int)_config.PopupTitleStyle.FontSize;
            ChkPopupTitleBold.IsChecked = _config.PopupTitleStyle.IsBold;

            SetColorButton(BtnPopupVerseColor, _config.PopupVerseStyle.GetSKColor());
            CmbPopupVerseSize.ItemsSource = verseSizes;
            CmbPopupVerseSize.SelectedItem = (int)_config.PopupVerseStyle.FontSize;
            ChkPopupVerseBold.IsChecked = _config.PopupVerseStyle.IsBold;
            CmbPopupVerseSpacing.ItemsSource = verseSpacingOptions;
            CmbPopupVerseSpacing.SelectedItem = Math.Round(_config.PopupVerseStyle.VerseSpacing, 1);

            SetColorButton(BtnPopupVerseNumberColor, _config.PopupVerseNumberStyle.GetSKColor());
            CmbPopupVerseNumberSize.ItemsSource = verseNumberSizes;
            CmbPopupVerseNumberSize.SelectedItem = (int)_config.PopupVerseNumberStyle.FontSize;
            ChkPopupVerseNumberBold.IsChecked = _config.PopupVerseNumberStyle.IsBold;

            SetColorButton(BtnPopupBackgroundColor, ParseHexColor(_config.PopupBackgroundColorHex, DefaultPopupBackgroundColorHex), forceBlackText: true);
            CmbPopupBackgroundOpacity.ItemsSource = Enumerable.Range(0, 21).Select(i => i * 5).ToList();
            CmbPopupBackgroundOpacity.SelectedItem = (_config.PopupBackgroundOpacity / 5) * 5;

            var popupDurationOptions = Enumerable.Range(1, 10)
                .Select(v => new PopupNumericOption(v, $"{v} 分钟"))
                .ToList();
            CmbPopupDurationMinutes.ItemsSource = popupDurationOptions;
            CmbPopupDurationMinutes.SelectedItem = popupDurationOptions
                .FirstOrDefault(v => v.Value == _config.PopupDurationMinutes)
                ?? popupDurationOptions.First();

            var popupVerseCountOptions = Enumerable.Range(1, 10)
                .Select(v => new PopupNumericOption(v, $"{v}节"))
                .ToList();
            CmbPopupVerseCount.ItemsSource = popupVerseCountOptions;
            CmbPopupVerseCount.SelectedItem = popupVerseCountOptions
                .FirstOrDefault(v => v.Value == _config.PopupVerseCount)
                ?? popupVerseCountOptions.First();

            string quickLocateActionTag = _config.QuickLocateSlideAction == BibleQuickLocateSlideAction.DirectInsert
                ? "1"
                : "0";
            foreach (var item in CmbSlidePinyinQuickLocateAction.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag?.ToString(), quickLocateActionTag, StringComparison.Ordinal))
                {
                    CmbSlidePinyinQuickLocateAction.SelectedItem = item;
                    break;
                }
            }
            if (CmbSlidePinyinQuickLocateAction.SelectedItem == null && CmbSlidePinyinQuickLocateAction.Items.Count > 0)
            {
                CmbSlidePinyinQuickLocateAction.SelectedIndex = 0;
            }

            // 默认打开“插入样式”页签
            SwitchStyleTab(false);
        }

        private void BtnInsertStyleTab_Click(object sender, RoutedEventArgs e) => SwitchStyleTab(false);

        private void BtnPopupStyleTab_Click(object sender, RoutedEventArgs e) => SwitchStyleTab(true);

        private void SwitchStyleTab(bool showPopupStyle)
        {
            InsertStylePanel.Visibility = showPopupStyle ? Visibility.Collapsed : Visibility.Visible;
            PopupStylePanel.Visibility = showPopupStyle ? Visibility.Visible : Visibility.Collapsed;

            if (showPopupStyle)
            {
                BtnInsertStyleTab.Background = new SolidColorBrush(System.Windows.Media.Colors.White);
                BtnInsertStyleTab.Foreground = new SolidColorBrush(ParseUiColor("#6B7280"));
                BtnInsertStyleTab.BorderBrush = System.Windows.Media.Brushes.Transparent;

                BtnPopupStyleTab.Background = new SolidColorBrush(ParseUiColor("#EFF6FF"));
                BtnPopupStyleTab.Foreground = new SolidColorBrush(ParseUiColor("#2563EB"));
                BtnPopupStyleTab.BorderBrush = new SolidColorBrush(ParseUiColor("#BFDBFE"));
            }
            else
            {
                BtnPopupStyleTab.Background = new SolidColorBrush(System.Windows.Media.Colors.White);
                BtnPopupStyleTab.Foreground = new SolidColorBrush(ParseUiColor("#6B7280"));
                BtnPopupStyleTab.BorderBrush = System.Windows.Media.Brushes.Transparent;

                BtnInsertStyleTab.Background = new SolidColorBrush(ParseUiColor("#EFF6FF"));
                BtnInsertStyleTab.Foreground = new SolidColorBrush(ParseUiColor("#2563EB"));
                BtnInsertStyleTab.BorderBrush = new SolidColorBrush(ParseUiColor("#BFDBFE"));
            }
        }

        private static System.Windows.Media.Color ParseUiColor(string hex)
        {
            return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        }
        
        /// <summary>
        /// 设置颜色按钮（背景色 + 自动前景色）
        /// </summary>
        private void SetColorButton(System.Windows.Controls.Button button, SKColor skColor, bool forceBlackText = false)
        {
            try
            {
                var bgColor = System.Windows.Media.Color.FromArgb(skColor.Alpha, skColor.Red, skColor.Green, skColor.Blue);
                button.Background = new SolidColorBrush(bgColor);

                if (forceBlackText)
                {
                    button.Foreground = new SolidColorBrush(Colors.Black);
                }
                else
                {
                    // 根据背景色亮度自动设置前景色（黑色或白色）
                    double luminance = (0.299 * bgColor.R + 0.587 * bgColor.G + 0.114 * bgColor.B) / 255;
                    button.Foreground = luminance > 0.5
                        ? new SolidColorBrush(Colors.Black)
                        : new SolidColorBrush(Colors.White);
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($" [BibleInsertStylePopup] 设置颜色按钮失败: {ex.Message}");
                #else
                _ = ex;  // 防止未使用变量警告
                #endif
                button.Background = new SolidColorBrush(Colors.Gray);
                button.Foreground = new SolidColorBrush(forceBlackText ? Colors.Black : Colors.White);
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
                
                // 更新统一字体（将中文显示名转换为英文 FontFamily）
                if (CmbFont.SelectedItem is string fontDisplayName && 
                    _fontDisplayMap != null && 
                    _fontDisplayMap.TryGetValue(fontDisplayName, out string fontFamily))
                {
                    _config.FontFamily = fontFamily;
                    _dbManager.SetBibleInsertConfigValue("font_family", fontFamily);
                    
                    //#if DEBUG
                    //Debug.WriteLine($"[BibleInsertStylePopup] 更新字体配置: {fontDisplayName} -> {fontFamily}");
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
                    _config.VerseStyle.VerseSpacing = (float)(double)CmbVerseSpacing.SelectedItem;
                    _dbManager.SetBibleInsertConfigValue("verse_spacing", _config.VerseStyle.VerseSpacing.ToString("F1"));
                }

                // 更新节号样式
                _config.VerseNumberStyle.IsBold = ChkVerseNumberBold.IsChecked ?? true;
                _dbManager.SetBibleInsertConfigValue("verse_number_bold", _config.VerseNumberStyle.IsBold ? "1" : "0");

                if (CmbPopupPosition.SelectedItem is ComboBoxItem popupItem &&
                    popupItem.Tag is string popupTag)
                {
                    _config.PopupPosition = popupTag switch
                    {
                        "Top" => BiblePopupPosition.Top,
                        "Center" => BiblePopupPosition.Center,
                        _ => BiblePopupPosition.Bottom
                    };
                    _dbManager.SetBibleInsertConfigValue("popup_position", popupTag);
                }

                if (CmbPopupFont.SelectedItem is string popupFontDisplayName &&
                    _fontDisplayMap != null &&
                    _fontDisplayMap.TryGetValue(popupFontDisplayName, out string popupFontFamily))
                {
                    _config.PopupFontFamily = popupFontFamily;
                    _dbManager.SetBibleInsertConfigValue("popup_font_family", popupFontFamily);
                }
                if (CmbPopupTitleFormat.SelectedItem is ComboBoxItem popupTitleFormatItem &&
                    popupTitleFormatItem.Tag is string popupTitleFormatTag &&
                    int.TryParse(popupTitleFormatTag, out var popupTitleFormatValue))
                {
                    _config.PopupTitleFormat = Enum.IsDefined(typeof(BiblePopupTitleFormat), popupTitleFormatValue)
                        ? (BiblePopupTitleFormat)popupTitleFormatValue
                        : BiblePopupTitleFormat.DotChapterVerse;
                    _dbManager.SetBibleInsertConfigValue("popup_title_format", ((int)_config.PopupTitleFormat).ToString());
                }

                if (CmbPopupTitleSize.SelectedItem != null)
                {
                    _config.PopupTitleStyle.FontSize = (int)CmbPopupTitleSize.SelectedItem;
                    _dbManager.SetBibleInsertConfigValue("popup_title_size", _config.PopupTitleStyle.FontSize.ToString());
                }
                _config.PopupTitleStyle.IsBold = ChkPopupTitleBold.IsChecked ?? _config.PopupTitleStyle.IsBold;
                _dbManager.SetBibleInsertConfigValue("popup_title_bold", _config.PopupTitleStyle.IsBold ? "1" : "0");

                if (CmbPopupVerseSize.SelectedItem != null)
                {
                    _config.PopupVerseStyle.FontSize = (int)CmbPopupVerseSize.SelectedItem;
                    _dbManager.SetBibleInsertConfigValue("popup_verse_size", _config.PopupVerseStyle.FontSize.ToString());
                }
                _config.PopupVerseStyle.IsBold = ChkPopupVerseBold.IsChecked ?? _config.PopupVerseStyle.IsBold;
                _dbManager.SetBibleInsertConfigValue("popup_verse_bold", _config.PopupVerseStyle.IsBold ? "1" : "0");
                if (CmbPopupVerseSpacing.SelectedItem != null)
                {
                    _config.PopupVerseStyle.VerseSpacing = (float)(double)CmbPopupVerseSpacing.SelectedItem;
                    _dbManager.SetBibleInsertConfigValue("popup_verse_spacing", _config.PopupVerseStyle.VerseSpacing.ToString("F1"));
                }

                if (CmbPopupVerseNumberSize.SelectedItem != null)
                {
                    _config.PopupVerseNumberStyle.FontSize = (int)CmbPopupVerseNumberSize.SelectedItem;
                    _dbManager.SetBibleInsertConfigValue("popup_verse_number_size", _config.PopupVerseNumberStyle.FontSize.ToString());
                }
                _config.PopupVerseNumberStyle.IsBold = ChkPopupVerseNumberBold.IsChecked ?? _config.PopupVerseNumberStyle.IsBold;
                _dbManager.SetBibleInsertConfigValue("popup_verse_number_bold", _config.PopupVerseNumberStyle.IsBold ? "1" : "0");

                if (CmbPopupBackgroundOpacity.SelectedItem is int opacity)
                {
                    _config.PopupBackgroundOpacity = Math.Clamp(opacity, 0, 100);
                    _dbManager.SetBibleInsertConfigValue("popup_bg_opacity", _config.PopupBackgroundOpacity.ToString());
                }

                if (CmbPopupDurationMinutes.SelectedItem is PopupNumericOption popupDurationOption)
                {
                    _config.PopupDurationMinutes = popupDurationOption.Value;
                    _dbManager.SetBibleInsertConfigValue("popup_duration_minutes", _config.PopupDurationMinutes.ToString());
                }
                else if (CmbPopupDurationMinutes.SelectedItem is int popupDurationMinutes)
                {
                    _config.PopupDurationMinutes = popupDurationMinutes;
                    _dbManager.SetBibleInsertConfigValue("popup_duration_minutes", _config.PopupDurationMinutes.ToString());
                }

                if (CmbPopupVerseCount.SelectedItem is PopupNumericOption popupVerseCountOption)
                {
                    _config.PopupVerseCount = popupVerseCountOption.Value;
                    _dbManager.SetBibleInsertConfigValue("popup_verse_count", _config.PopupVerseCount.ToString());
                }
                else if (CmbPopupVerseCount.SelectedItem is int popupVerseCount)
                {
                    _config.PopupVerseCount = popupVerseCount;
                    _dbManager.SetBibleInsertConfigValue("popup_verse_count", _config.PopupVerseCount.ToString());
                }

                if (CmbSlidePinyinQuickLocateAction.SelectedItem is ComboBoxItem quickLocateActionItem &&
                    quickLocateActionItem.Tag is string quickLocateActionTag &&
                    int.TryParse(quickLocateActionTag, out var quickLocateActionValue))
                {
                    _config.QuickLocateSlideAction = Enum.IsDefined(typeof(BibleQuickLocateSlideAction), quickLocateActionValue)
                        ? (BibleQuickLocateSlideAction)quickLocateActionValue
                        : BibleQuickLocateSlideAction.HistoryFirst;
                    _dbManager.SetBibleInsertConfigValue("slide_pinyin_quick_locate_action", ((int)_config.QuickLocateSlideAction).ToString());
                }

                //#if DEBUG
                //Debug.WriteLine($" [BibleInsertStylePopup] 配置已保存到数据库");
                //Debug.WriteLine($"   样式布局: {_config.Style}");
                //Debug.WriteLine($"   统一字体: {_config.FontFamily}");
                //Debug.WriteLine($"   标题: {_config.TitleStyle.FontSize}pt, 粗体={_config.TitleStyle.IsBold}");
                //Debug.WriteLine($"   经文: {_config.VerseStyle.FontSize}pt, 粗体={_config.VerseStyle.IsBold}, 节距={_config.VerseStyle.VerseSpacing}px");
                //Debug.WriteLine($"   节号: 粗体={_config.VerseNumberStyle.IsBold}");
                //#endif
                NotifyPopupStyleChanged();
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($" [BibleInsertStylePopup] 更新配置失败: {ex.Message}");
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
                    SetColorButton(BtnTitleColor, _config.TitleStyle.GetSKColor(), forceBlackText: true);
                    _dbManager.SetBibleInsertConfigValue("title_color", _config.TitleStyle.ColorHex);
                    
                    #if DEBUG
                    Debug.WriteLine($" [BibleInsertStylePopup] 标题颜色已更改: {_config.TitleStyle.ColorHex}");
                    #endif
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($" [BibleInsertStylePopup] 选择标题颜色失败: {ex.Message}");
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
                    Debug.WriteLine($" [BibleInsertStylePopup] 经文颜色已更改: {_config.VerseStyle.ColorHex}");
                    #endif
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($" [BibleInsertStylePopup] 选择经文颜色失败: {ex.Message}");
                #else
                _ = ex;  // 防止未使用变量警告
                #endif
            }
        }

        /// <summary>
        /// 节号颜色选择按钮点击事件
        /// </summary>
        private void BtnVerseNumberColor_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var colorDialog = new System.Windows.Forms.ColorDialog();
                var currentColor = _config.VerseNumberStyle.GetSKColor();
                colorDialog.Color = System.Drawing.Color.FromArgb(
                    currentColor.Alpha, currentColor.Red, currentColor.Green, currentColor.Blue);

                if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var color = colorDialog.Color;
                    _config.VerseNumberStyle.SetSKColor(new SKColor(color.R, color.G, color.B, color.A));
                    SetColorButton(BtnVerseNumberColor, _config.VerseNumberStyle.GetSKColor());
                    _dbManager.SetBibleInsertConfigValue("verse_number_color", _config.VerseNumberStyle.ColorHex);

                    #if DEBUG
                    Debug.WriteLine($" [BibleInsertStylePopup] 节号颜色已更改: {_config.VerseNumberStyle.ColorHex}");
                    #endif
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                Debug.WriteLine($" [BibleInsertStylePopup] 选择节号颜色失败: {ex.Message}");
                #else
                _ = ex;  // 防止未使用变量警告
                #endif
            }
        }

        /// <summary>
        /// 弹窗背景色按钮点击事件
        /// </summary>
        private void BtnPopupBackgroundColor_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var colorDialog = new System.Windows.Forms.ColorDialog();
                var currentColor = ParseHexColor(_config.PopupBackgroundColorHex, DefaultPopupBackgroundColorHex);
                colorDialog.Color = System.Drawing.Color.FromArgb(
                    currentColor.Alpha, currentColor.Red, currentColor.Green, currentColor.Blue);

                if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var color = colorDialog.Color;
                    _config.PopupBackgroundColorHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                    SetColorButton(BtnPopupBackgroundColor, ParseHexColor(_config.PopupBackgroundColorHex, DefaultPopupBackgroundColorHex), forceBlackText: true);
                    _dbManager.SetBibleInsertConfigValue("popup_bg_color", _config.PopupBackgroundColorHex);
                    NotifyPopupStyleChanged();
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($" [BibleInsertStylePopup] 选择弹窗背景色失败: {ex.Message}");
#else
                _ = ex;
#endif
            }
        }

        private void BtnResetInsertColors_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;

            _config.TitleStyle.ColorHex = DefaultTitleColorHex;
            _config.VerseStyle.ColorHex = DefaultVerseColorHex;
            _config.VerseNumberStyle.ColorHex = DefaultVerseNumberColorHex;

            SetColorButton(BtnTitleColor, _config.TitleStyle.GetSKColor(), forceBlackText: true);
            SetColorButton(BtnVerseColor, _config.VerseStyle.GetSKColor());
            SetColorButton(BtnVerseNumberColor, _config.VerseNumberStyle.GetSKColor());

            _dbManager.SetBibleInsertConfigValue("title_color", _config.TitleStyle.ColorHex);
            _dbManager.SetBibleInsertConfigValue("verse_color", _config.VerseStyle.ColorHex);
            _dbManager.SetBibleInsertConfigValue("verse_number_color", _config.VerseNumberStyle.ColorHex);
        }

        private void BtnResetPopupColors_Click(object sender, RoutedEventArgs e)
        {
            _ = sender;
            _ = e;

            _config.PopupTitleStyle.ColorHex = DefaultTitleColorHex;
            _config.PopupVerseStyle.ColorHex = DefaultVerseColorHex;
            _config.PopupVerseNumberStyle.ColorHex = DefaultVerseNumberColorHex;
            _config.PopupBackgroundColorHex = DefaultPopupBackgroundColorHex;

            SetColorButton(BtnPopupTitleColor, _config.PopupTitleStyle.GetSKColor(), forceBlackText: true);
            SetColorButton(BtnPopupVerseColor, _config.PopupVerseStyle.GetSKColor());
            SetColorButton(BtnPopupVerseNumberColor, _config.PopupVerseNumberStyle.GetSKColor());
            SetColorButton(BtnPopupBackgroundColor, ParseHexColor(_config.PopupBackgroundColorHex, DefaultPopupBackgroundColorHex), forceBlackText: true);

            _dbManager.SetBibleInsertConfigValue("popup_title_color", _config.PopupTitleStyle.ColorHex);
            _dbManager.SetBibleInsertConfigValue("popup_verse_color", _config.PopupVerseStyle.ColorHex);
            _dbManager.SetBibleInsertConfigValue("popup_verse_number_color", _config.PopupVerseNumberStyle.ColorHex);
            _dbManager.SetBibleInsertConfigValue("popup_bg_color", _config.PopupBackgroundColorHex);

            NotifyPopupStyleChanged();
        }

        private void BtnPopupTitleColor_Click(object sender, RoutedEventArgs e)
        {
            if (TrySelectColor(_config.PopupTitleStyle.GetSKColor(), out var newColor))
            {
                _config.PopupTitleStyle.SetSKColor(newColor);
                SetColorButton(BtnPopupTitleColor, newColor, forceBlackText: true);
                _dbManager.SetBibleInsertConfigValue("popup_title_color", _config.PopupTitleStyle.ColorHex);
                NotifyPopupStyleChanged();
            }
        }

        private void BtnPopupVerseColor_Click(object sender, RoutedEventArgs e)
        {
            if (TrySelectColor(_config.PopupVerseStyle.GetSKColor(), out var newColor))
            {
                _config.PopupVerseStyle.SetSKColor(newColor);
                SetColorButton(BtnPopupVerseColor, newColor);
                _dbManager.SetBibleInsertConfigValue("popup_verse_color", _config.PopupVerseStyle.ColorHex);
                NotifyPopupStyleChanged();
            }
        }

        private void BtnPopupVerseNumberColor_Click(object sender, RoutedEventArgs e)
        {
            if (TrySelectColor(_config.PopupVerseNumberStyle.GetSKColor(), out var newColor))
            {
                _config.PopupVerseNumberStyle.SetSKColor(newColor);
                SetColorButton(BtnPopupVerseNumberColor, newColor);
                _dbManager.SetBibleInsertConfigValue("popup_verse_number_color", _config.PopupVerseNumberStyle.ColorHex);
                NotifyPopupStyleChanged();
            }
        }

        private void NotifyPopupStyleChanged()
        {
            PopupStyleChanged?.Invoke();
        }

        private static bool TrySelectColor(SKColor currentColor, out SKColor selectedColor)
        {
            selectedColor = currentColor;
            try
            {
                var colorDialog = new System.Windows.Forms.ColorDialog
                {
                    Color = System.Drawing.Color.FromArgb(currentColor.Alpha, currentColor.Red, currentColor.Green, currentColor.Blue)
                };

                if (colorDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    return false;
                }

                var color = colorDialog.Color;
                selectedColor = new SKColor(color.R, color.G, color.B, color.A);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static SKColor ParseHexColor(string hex, string fallbackHex)
        {
            try
            {
                return SKColor.Parse(hex);
            }
            catch
            {
                return SKColor.Parse(fallbackHex);
            }
        }

        /// <summary>
        /// 节号大小变更事件
        /// </summary>
        private void VerseNumberSizeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbVerseNumberSize.SelectedItem != null)
            {
                _config.VerseNumberStyle.FontSize = (int)CmbVerseNumberSize.SelectedItem;
                _dbManager.SetBibleInsertConfigValue("verse_number_size", _config.VerseNumberStyle.FontSize.ToString());
            }
        }
    }
}


