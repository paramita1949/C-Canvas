using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ImageColorChanger.Core
{
    /// <summary>
    /// 字体服务 - 统一的字体加载和管理API
    /// 用于在整个应用程序中加载和使用字体
    /// </summary>
    public class FontService
    {
        private static FontService _instance;
        private static readonly object _lock = new object();
        
        private FontConfig _fontConfig;
        private Dictionary<string, System.Windows.Media.FontFamily> _fontCache;
        private bool _isInitialized = false;

        /// <summary>
        /// 单例实例
        /// </summary>
        public static FontService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new FontService();
                        }
                    }
                }
                return _instance;
            }
        }

        private FontService()
        {
            _fontCache = new Dictionary<string, System.Windows.Media.FontFamily>();
        }

        /// <summary>
        /// 初始化字体服务
        /// </summary>
        /// <returns>是否初始化成功</returns>
        public bool Initialize()
        {
            if (_isInitialized)
                return true;

            try
            {
                // 使用统一的字体配置文件
                var configFile = "Fonts/fonts.json";
                
                // 使用ResourceLoader加载字体配置（支持PAK）
                var json = ResourceLoader.LoadTextFile(configFile);
                
                if (string.IsNullOrEmpty(json))
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"⚠️ [FontService] 未找到 {configFile}");
#endif
                    return false;
                }

                // 反序列化配置文件
                _fontConfig = JsonSerializer.Deserialize<FontConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (_fontConfig == null || _fontConfig.FontCategories == null || _fontConfig.FontCategories.Count == 0)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"⚠️ [FontService] {configFile} 配置为空");
#endif
                    return false;
                }

                _isInitialized = true;
                
#if DEBUG
                int totalFonts = _fontConfig.FontCategories.Sum(c => c.Fonts.Count);
                System.Diagnostics.Debug.WriteLine($"✅ [FontService] 初始化成功，加载了 {totalFonts} 个字体配置");
#endif
                
                return true;
            }
            catch (Exception
#if DEBUG
            ex
#endif
            )
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [FontService] 初始化失败: {ex.Message}");
#endif
                return false;
            }
        }

        /// <summary>
        /// 获取字体配置
        /// </summary>
        public FontConfig GetFontConfig()
        {
            if (!_isInitialized)
            {
                Initialize();
            }
            return _fontConfig;
        }

        /// <summary>
        /// 根据字体名称获取FontFamily对象
        /// </summary>
        /// <param name="fontName">字体显示名称（如：阿里巴巴普惠体）</param>
        /// <returns>FontFamily对象，失败返回null</returns>
        public System.Windows.Media.FontFamily GetFontFamily(string fontName)
        {
            if (!_isInitialized)
            {
                Initialize();
            }

            if (string.IsNullOrEmpty(fontName))
                return null;

            // 从缓存中查找
            if (_fontCache.ContainsKey(fontName))
            {
                return _fontCache[fontName];
            }

            // 查找字体配置
            var fontConfig = FindFontByName(fontName);
            if (fontConfig == null)
                return null;

            // 加载字体
            var fontFamily = LoadFontFamily(fontConfig);
            
            // 缓存字体
            if (fontFamily != null)
            {
                _fontCache[fontName] = fontFamily;
            }

            return fontFamily;
        }

        /// <summary>
        /// 根据字体族名称（Family）获取FontFamily对象
        /// </summary>
        /// <param name="familyName">字体族名称（如：Alibaba PuHuiTi）</param>
        /// <returns>FontFamily对象，失败返回null</returns>
        public System.Windows.Media.FontFamily GetFontFamilyByFamily(string familyName)
        {
            if (!_isInitialized)
            {
                Initialize();
            }

            if (string.IsNullOrEmpty(familyName))
                return null;

            // 查找字体配置
            var fontConfig = FindFontByFamily(familyName);
            if (fontConfig == null)
            {
                // 如果找不到，可能是系统字体，直接尝试创建
                try
                {
                    return new System.Windows.Media.FontFamily(familyName);
                }
                catch
                {
                    return null;
                }
            }

            // 从缓存中查找
            if (_fontCache.ContainsKey(fontConfig.Name))
            {
                return _fontCache[fontConfig.Name];
            }

            // 加载字体
            var fontFamily = LoadFontFamily(fontConfig);
            
            // 缓存字体
            if (fontFamily != null)
            {
                _fontCache[fontConfig.Name] = fontFamily;
            }

            return fontFamily;
        }

        /// <summary>
        /// 填充ComboBox控件的字体列表
        /// </summary>
        /// <param name="comboBox">要填充的ComboBox</param>
        /// <param name="showCategoryHeaders">是否显示分类标题</param>
        /// <param name="showFavoriteIcon">是否显示收藏图标</param>
        /// <param name="applyFontToItem">是否将字体应用到ComboBoxItem（预览效果）</param>
        /// <returns>成功加载的字体数量</returns>
        public int PopulateComboBox(System.Windows.Controls.ComboBox comboBox, bool showCategoryHeaders = true, 
            bool showFavoriteIcon = true, bool applyFontToItem = true)
        {
            if (comboBox == null)
                throw new ArgumentNullException(nameof(comboBox));

            if (!_isInitialized)
            {
                Initialize();
            }

            if (_fontConfig == null)
                return 0;

            comboBox.Items.Clear();
            int totalFonts = 0;

            // 按分类加载字体
            foreach (var category in _fontConfig.FontCategories)
            {
                // 添加分类标题（不可选）
                if (showCategoryHeaders)
                {
                    var categoryHeader = new System.Windows.Controls.ComboBoxItem
                    {
                        Content = $"━━ {category.Name} ━━",
                        IsEnabled = false,
                        FontWeight = System.Windows.FontWeights.Bold,
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x21, 0x96, 0xF3))
                    };
                    comboBox.Items.Add(categoryHeader);
                }

                // 添加该分类下的字体
                foreach (var font in category.Fonts)
                {
                    try
                    {
                        System.Windows.Media.FontFamily fontFamily = null;

                        // 如果需要应用字体到Item，则加载字体
                        if (applyFontToItem)
                        {
                            fontFamily = LoadFontFamily(font);
                            if (fontFamily == null)
                            {
#if DEBUG
                                System.Diagnostics.Debug.WriteLine($"⚠️ [FontService] 字体加载失败: {font.Name}");
#endif
                                continue;
                            }
                        }

                        // 创建字体项
                        var displayName = (showFavoriteIcon && font.IsFavorite) 
                            ? $"⭐ {font.Name}" 
                            : $"   {font.Name}";
                        
                        var item = new System.Windows.Controls.ComboBoxItem
                        {
                            Content = displayName,
                            FontFamily = fontFamily,
                            Tag = new FontItemData 
                            { 
                                Config = font, 
                                FontFamily = fontFamily 
                            }
                            // ToolTip = font.Preview  // 已禁用：鼠标悬停提示
                        };

                        comboBox.Items.Add(item);
                        totalFonts++;
                    }
                    catch (Exception
#if DEBUG
                    ex
#endif
                    )
                    {
#if DEBUG
                        System.Diagnostics.Debug.WriteLine($"⚠️ [FontService] 加载字体失败 [{font.Name}]: {ex.Message}");
#endif
                    }
                }
            }

            // 默认选择第一个可用字体
            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i] is System.Windows.Controls.ComboBoxItem item && item.IsEnabled)
                {
                    comboBox.SelectedIndex = i;
                    break;
                }
            }

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"✅ [FontService] ComboBox已填充 {totalFonts} 个字体");
#endif

            return totalFonts;
        }

        /// <summary>
        /// 填充ComboBox控件的字体列表（简化版，只显示名称）
        /// </summary>
        /// <param name="comboBox">要填充的ComboBox</param>
        /// <param name="showCategoryHeaders">是否显示分类标题</param>
        /// <returns>字体名称到CustomFont的映射字典</returns>
        public Dictionary<string, CustomFont> PopulateComboBoxSimple(System.Windows.Controls.ComboBox comboBox, bool showCategoryHeaders = true)
        {
            if (comboBox == null)
                throw new ArgumentNullException(nameof(comboBox));

            if (!_isInitialized)
            {
                Initialize();
            }

            var fontMap = new Dictionary<string, CustomFont>();

            if (_fontConfig == null)
                return fontMap;

            comboBox.Items.Clear();

            // 按分类加载字体
            foreach (var category in _fontConfig.FontCategories)
            {
                // 添加分类标题（不可选）
                if (showCategoryHeaders)
                {
                    var categoryHeader = new System.Windows.Controls.ComboBoxItem
                    {
                        Content = $"━━ {category.Name} ━━",
                        IsEnabled = false,
                        FontWeight = System.Windows.FontWeights.Bold,
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x8C, 0x00)) // 橙色
                    };
                    comboBox.Items.Add(categoryHeader);
                }

                // 添加该分类下的字体
                foreach (var font in category.Fonts)
                {
                    var item = new System.Windows.Controls.ComboBoxItem
                    {
                        Content = font.Name,
                        Tag = font // 保存字体信息
                    };
                    comboBox.Items.Add(item);
                    fontMap[font.Name] = font;
                }
            }

            // 默认选择第一个可用字体
            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i] is System.Windows.Controls.ComboBoxItem item && item.IsEnabled)
                {
                    comboBox.SelectedIndex = i;
                    break;
                }
            }

            return fontMap;
        }

        /// <summary>
        /// 获取所有字体列表
        /// </summary>
        /// <returns>字体配置列表</returns>
        public List<CustomFont> GetAllFonts()
        {
            if (!_isInitialized)
            {
                Initialize();
            }

            if (_fontConfig == null)
                return new List<CustomFont>();

            return _fontConfig.FontCategories
                .SelectMany(c => c.Fonts)
                .ToList();
        }

        /// <summary>
        /// 获取收藏的字体列表
        /// </summary>
        /// <returns>收藏字体配置列表</returns>
        public List<CustomFont> GetFavoriteFonts()
        {
            if (!_isInitialized)
            {
                Initialize();
            }

            if (_fontConfig == null)
                return new List<CustomFont>();

            return _fontConfig.FontCategories
                .SelectMany(c => c.Fonts)
                .Where(f => f.IsFavorite)
                .ToList();
        }

        /// <summary>
        /// 根据分类名称获取字体列表
        /// </summary>
        /// <param name="categoryName">分类名称</param>
        /// <returns>字体配置列表</returns>
        public List<CustomFont> GetFontsByCategory(string categoryName)
        {
            if (!_isInitialized)
            {
                Initialize();
            }

            if (_fontConfig == null || string.IsNullOrEmpty(categoryName))
                return new List<CustomFont>();

            var category = _fontConfig.FontCategories
                .FirstOrDefault(c => c.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase));

            return category?.Fonts ?? new List<CustomFont>();
        }

        /// <summary>
        /// 清除字体缓存
        /// </summary>
        public void ClearCache()
        {
            _fontCache.Clear();
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"🗑️ [FontService] 字体缓存已清除");
#endif
        }

        /// <summary>
        /// 重新加载字体配置
        /// </summary>
        public bool Reload()
        {
            _isInitialized = false;
            _fontCache.Clear();
            _fontConfig = null;
            return Initialize();
        }

        #region 私有辅助方法

        /// <summary>
        /// 根据字体名称查找字体配置
        /// </summary>
        private CustomFont FindFontByName(string fontName)
        {
            if (_fontConfig == null)
                return null;

            return _fontConfig.FontCategories
                .SelectMany(c => c.Fonts)
                .FirstOrDefault(f => f.Name.Equals(fontName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 根据字体族名称查找字体配置
        /// </summary>
        private CustomFont FindFontByFamily(string familyName)
        {
            if (_fontConfig == null)
                return null;

            return _fontConfig.FontCategories
                .SelectMany(c => c.Fonts)
                .FirstOrDefault(f => f.Family.Equals(familyName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 加载FontFamily对象
        /// </summary>
        private System.Windows.Media.FontFamily LoadFontFamily(CustomFont font)
        {
            if (font == null)
                return null;

            try
            {
                // 判断是系统字体还是自定义字体
                if (font.File == "system")
                {
                    // 系统字体
                    return new System.Windows.Media.FontFamily(font.Family);
                }
                else
                {
                    // 自定义字体文件
                    var fontRelativePath = $"Fonts/{font.File}";
                    
                    if (!ResourceLoader.ResourceExists(fontRelativePath))
                    {
#if DEBUG
                        System.Diagnostics.Debug.WriteLine($"⚠️ [FontService] 字体文件不存在: {fontRelativePath}");
#endif
                        return null;
                    }

                    // 使用ResourceLoader加载字体（支持PAK）
                    return ResourceLoader.LoadFont(fontRelativePath, font.Family);
                }
            }
            catch (Exception
#if DEBUG
            ex
#endif
            )
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [FontService] 字体加载失败 [{font.Name}]: {ex.Message}");
#endif
                return null;
            }
        }

        #endregion
    }

    /// <summary>
    /// 字体项数据（用于ComboBoxItem的Tag）
    /// </summary>
    public class FontItemData
    {
        /// <summary>
        /// 字体配置信息
        /// </summary>
        public CustomFont Config { get; set; }

        /// <summary>
        /// 字体族对象
        /// </summary>
        public System.Windows.Media.FontFamily FontFamily { get; set; }
    }
}

