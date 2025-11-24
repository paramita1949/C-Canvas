using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Diagnostics;
using SkiaSharp;

namespace ImageColorChanger.Core
{
    /// <summary>
    /// 配置管理器 - 统一管理应用程序配置
    /// </summary>
    public partial class ConfigManager
    {
        private static ConfigManager _instance;
        private static readonly object _lock = new object();
        
        /// <summary>
        /// 单例实例
        /// </summary>
        public static ConfigManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new ConfigManager();
                        }
                    }
                }
                return _instance;
            }
        }
        
        private readonly string _configFilePath;
        private AppConfig _config;
        
        /// <summary>
        /// 内置颜色预设（不可修改）
        /// </summary>
        private static readonly List<ColorPreset> BuiltInPresets = new List<ColorPreset>
        {
            new ColorPreset { Name = "淡黄", R = 174, G = 159, B = 112 },
            new ColorPreset { Name = "纯黄", R = 255, G = 255, B = 0 },
            new ColorPreset { Name = "秋麒麟", R = 218, G = 165, B = 32 },
            new ColorPreset { Name = "晒黑", R = 210, G = 180, B = 140 },
            new ColorPreset { Name = "结实的树", R = 222, G = 184, B = 135 },
            new ColorPreset { Name = "沙棕色", R = 244, G = 164, B = 96 },
            new ColorPreset { Name = "纯白", R = 255, G = 255, B = 255 }
        };

        /// <summary>
        /// 文件夹标记颜色池（明亮且易区分的颜色）
        /// </summary>
        private static readonly string[] FolderColorPool = new[]
        {
            "#FF6B6B", // 红色
            "#4ECDC4", // 青色
            "#45B7D1", // 蓝色
            "#FFA07A", // 橙色
            "#98D8C8", // 薄荷绿
            "#F7DC6F", // 黄色
            "#BB8FCE", // 紫色
            "#85C1E2", // 天蓝
            "#F8B88B", // 桃色
            "#52B788", // 森林绿
            "#E76F51", // 珊瑚色
            "#2A9D8F", // 深青
            "#E9C46A", // 金黄
            "#F4A261", // 杏色
            "#8E7AB5", // 淡紫
            "#FB6F92", // 粉红
            "#06AED5", // 深蓝
            "#FFB703", // 琥珀色
            "#06FFA5", // 翡翠绿
            "#FF006E"  // 玫红
        };

        public ConfigManager(string configFilePath = null)
        {
            // 如果没有指定路径，则使用主程序所在目录
            if (string.IsNullOrEmpty(configFilePath))
            {
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                _configFilePath = Path.Combine(appDirectory, "config.json");
            }
            else
            {
                _configFilePath = configFilePath;
            }
            
            // Debug.WriteLine($"📁 配置文件路径: {_configFilePath}");
            LoadConfig();
        }

        /// <summary>
        /// 加载配置文件
        /// </summary>
        public void LoadConfig()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath);
                    _config = JsonSerializer.Deserialize<AppConfig>(json);
                    
                    // ✅ 确保最近颜色列表不为 null
                    
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"💾 [ConfigManager] 配置文件已加载: {_configFilePath}");
#endif
                }
                else
                {
                    #if DEBUG
                    Debug.WriteLine($"⚠️ [ConfigManager] 配置文件不存在，使用默认配置");
                    #endif
                    _config = new AppConfig();
                    SaveConfig();
                }
            }
            catch (Exception
            #if DEBUG
            ex
            #endif
            )
            {
                #if DEBUG
                Debug.WriteLine($"❌ [ConfigManager] 加载配置文件失败: {ex.Message}");
                #endif
                _config = new AppConfig();
            }
        }

        /// <summary>
        /// 保存配置文件
        /// </summary>
        public void SaveConfig()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                string json = JsonSerializer.Serialize(_config, options);
                File.WriteAllText(_configFilePath, json);
                
                //#if DEBUG
                //Debug.WriteLine($"💾 [ConfigManager] 配置文件已保存: {_configFilePath}");
                //#endif
            }
            catch (Exception
            #if DEBUG
            ex
            #endif
            )
            {
                #if DEBUG
                Debug.WriteLine($"❌ [ConfigManager] 保存配置文件失败: {ex.Message}");
                #endif
            }
        }

        #region 配置属性访问器

        /// <summary>
        /// 原图显示模式
        /// </summary>
        public OriginalDisplayMode OriginalDisplayMode
        {
            get => _config.OriginalDisplayMode;
            set
            {
                if (_config.OriginalDisplayMode != value)
                {
                    _config.OriginalDisplayMode = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 窗口状态
        /// </summary>
        public string WindowState
        {
            get => _config.WindowState;
            set
            {
                if (_config.WindowState != value)
                {
                    _config.WindowState = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 最后打开的文件夹
        /// </summary>
        public string LastOpenedFolder
        {
            get => _config.LastOpenedFolder;
            set
            {
                if (_config.LastOpenedFolder != value)
                {
                    _config.LastOpenedFolder = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 缩放比例
        /// </summary>
        public double ZoomRatio
        {
            get => _config.ZoomRatio;
            set
            {
                if (Math.Abs(_config.ZoomRatio - value) > 0.001)
                {
                    _config.ZoomRatio = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 自动保存启用
        /// </summary>
        public bool AutoSaveEnabled
        {
            get => _config.AutoSaveEnabled;
            set
            {
                if (_config.AutoSaveEnabled != value)
                {
                    _config.AutoSaveEnabled = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 目标颜色 - 红色分量
        /// </summary>
        public byte TargetColorR
        {
            get => _config.TargetColorR;
            set
            {
                if (_config.TargetColorR != value)
                {
                    _config.TargetColorR = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 目标颜色 - 绿色分量
        /// </summary>
        public byte TargetColorG
        {
            get => _config.TargetColorG;
            set
            {
                if (_config.TargetColorG != value)
                {
                    _config.TargetColorG = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 目标颜色 - 蓝色分量
        /// </summary>
        public byte TargetColorB
        {
            get => _config.TargetColorB;
            set
            {
                if (_config.TargetColorB != value)
                {
                    _config.TargetColorB = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 目标颜色名称
        /// </summary>
        public string TargetColorName
        {
            get => _config.TargetColorName;
            set
            {
                if (_config.TargetColorName != value)
                {
                    _config.TargetColorName = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 文件夹字号
        /// </summary>
        public double FolderFontSize
        {
            get => _config.FolderFontSize;
            set
            {
                if (Math.Abs(_config.FolderFontSize - value) > 0.001)
                {
                    _config.FolderFontSize = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 文件字号
        /// </summary>
        public double FileFontSize
        {
            get => _config.FileFontSize;
            set
            {
                if (Math.Abs(_config.FileFontSize - value) > 0.001)
                {
                    _config.FileFontSize = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 导航栏宽度
        /// </summary>
        public double NavigationPanelWidth
        {
            get => _config.NavigationPanelWidth;
            set
            {
                if (Math.Abs(_config.NavigationPanelWidth - value) > 0.001)
                {
                    _config.NavigationPanelWidth = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 文件夹标签字号（搜索结果显示）
        /// </summary>
        public double FolderTagFontSize
        {
            get => _config.FolderTagFontSize;
            set
            {
                if (Math.Abs(_config.FolderTagFontSize - value) > 0.001)
                {
                    _config.FolderTagFontSize = value;
                    SaveConfig();
                }
            }
        }
        
        /// <summary>
        /// 菜单栏字号（18-40，按照Python版本设计）
        /// </summary>
        public double MenuFontSize
        {
            get => _config.MenuFontSize;
            set
            {
                if (Math.Abs(_config.MenuFontSize - value) > 0.001)
                {
                    _config.MenuFontSize = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 全局默认歌词颜色
        /// </summary>
        public string DefaultLyricsColor
        {
            get => _config.DefaultLyricsColor;
            set
            {
                if (_config.DefaultLyricsColor != value)
                {
                    _config.DefaultLyricsColor = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 圣经译本
        /// </summary>
        public string BibleVersion
        {
            get => _config.BibleVersion;
            set
            {
                if (_config.BibleVersion != value)
                {
                    _config.BibleVersion = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 圣经数据库文件名
        /// </summary>
        public string BibleDatabaseFileName
        {
            get => _config.BibleDatabaseFileName;
            set
            {
                if (_config.BibleDatabaseFileName != value)
                {
                    _config.BibleDatabaseFileName = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 圣经字体
        /// </summary>
        public string BibleFontFamily
        {
            get => _config.BibleFontFamily;
            set
            {
                if (_config.BibleFontFamily != value)
                {
                    _config.BibleFontFamily = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 圣经字体大小
        /// </summary>
        public double BibleFontSize
        {
            get => _config.BibleFontSize;
            set
            {
                if (Math.Abs(_config.BibleFontSize - value) > 0.001)
                {
                    _config.BibleFontSize = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 圣经行距
        /// </summary>
        public double BibleLineHeight
        {
            get => _config.BibleLineHeight;
            set
            {
                if (Math.Abs(_config.BibleLineHeight - value) > 0.001)
                {
                    _config.BibleLineHeight = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 圣经背景色
        /// </summary>
        public string BibleBackgroundColor
        {
            get => _config.BibleBackgroundColor;
            set
            {
                if (_config.BibleBackgroundColor != value)
                {
                    _config.BibleBackgroundColor = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 圣经经文文字颜色
        /// </summary>
        public string BibleTextColor
        {
            get => _config.BibleTextColor;
            set
            {
                if (_config.BibleTextColor != value)
                {
                    _config.BibleTextColor = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 圣经标题颜色
        /// </summary>
        public string BibleTitleColor
        {
            get => _config.BibleTitleColor;
            set
            {
                if (_config.BibleTitleColor != value)
                {
                    _config.BibleTitleColor = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 圣经节号颜色
        /// </summary>
        public string BibleVerseNumberColor
        {
            get => _config.BibleVerseNumberColor;
            set
            {
                if (_config.BibleVerseNumberColor != value)
                {
                    _config.BibleVerseNumberColor = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 圣经选中高亮颜色
        /// </summary>
        public string BibleHighlightColor
        {
            get => _config.BibleHighlightColor;
            set
            {
                if (_config.BibleHighlightColor != value)
                {
                    _config.BibleHighlightColor = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 圣经标题字体大小
        /// </summary>
        public double BibleTitleFontSize
        {
            get => _config.BibleTitleFontSize;
            set
            {
                if (Math.Abs(_config.BibleTitleFontSize - value) > 0.001)
                {
                    _config.BibleTitleFontSize = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 圣经节号字体大小
        /// </summary>
        public double BibleVerseNumberFontSize
        {
            get => _config.BibleVerseNumberFontSize;
            set
            {
                if (Math.Abs(_config.BibleVerseNumberFontSize - value) > 0.001)
                {
                    _config.BibleVerseNumberFontSize = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 圣经左右边距
        /// </summary>
        public double BibleMargin
        {
            get => _config.BibleMargin;
            set
            {
                if (Math.Abs(_config.BibleMargin - value) > 0.001)
                {
                    _config.BibleMargin = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 圣经节间距（节与节之间的间距）
        /// </summary>
        public double BibleVerseSpacing
        {
            get => _config.BibleVerseSpacing;
            set
            {
                if (Math.Abs(_config.BibleVerseSpacing - value) > 0.001)
                {
                    _config.BibleVerseSpacing = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 圣经设置窗口X坐标（null表示使用自动计算位置）
        /// </summary>
        public double? BibleSettingsWindowLeft
        {
            get => _config.BibleSettingsWindowLeft;
            set
            {
                if (_config.BibleSettingsWindowLeft != value)
                {
                    _config.BibleSettingsWindowLeft = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 圣经设置窗口Y坐标（null表示使用自动计算位置）
        /// </summary>
        public double? BibleSettingsWindowTop
        {
            get => _config.BibleSettingsWindowTop;
            set
            {
                if (_config.BibleSettingsWindowTop != value)
                {
                    _config.BibleSettingsWindowTop = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 是否在退出程序时保存圣经投影记录（默认：false）
        /// </summary>
        public bool SaveBibleHistory
        {
            get => _config.SaveBibleHistory;
            set
            {
                if (_config.SaveBibleHistory != value)
                {
                    _config.SaveBibleHistory = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 合成播放默认时长（秒）
        /// </summary>
        public double CompositePlaybackDefaultDuration
        {
            get => _config.CompositePlaybackDefaultDuration;
            set
            {
                if (Math.Abs(_config.CompositePlaybackDefaultDuration - value) > 0.001)
                {
                    _config.CompositePlaybackDefaultDuration = value;
                    SaveConfig();
                }
            }
        }

        #endregion

        #region 颜色预设管理

        /// <summary>
        /// 获取所有颜色预设（内置 + 自定义）
        /// </summary>
        public List<ColorPreset> GetAllColorPresets()
        {
            var allPresets = new List<ColorPreset>(BuiltInPresets);
            
            if (_config.CustomColorPresets != null && _config.CustomColorPresets.Count > 0)
            {
                allPresets.AddRange(_config.CustomColorPresets);
            }
            
            return allPresets;
        }

        /// <summary>
        /// 添加自定义颜色预设
        /// </summary>
        public bool AddCustomColorPreset(string name, byte r, byte g, byte b)
        {
            try
            {
                // 检查是否已存在同名预设
                var allPresets = GetAllColorPresets();
                if (allPresets.Any(p => p.Name == name))
                {
                    // Debug.WriteLine($"⚠️ 颜色预设已存在: {name}");
                    return false;
                }

                // 检查是否已存在相同颜色
                if (allPresets.Any(p => p.R == r && p.G == g && p.B == b))
                {
                    // Debug.WriteLine($"⚠️ 该颜色已在预设中: RGB({r}, {g}, {b})");
                    return false;
                }

                if (_config.CustomColorPresets == null)
                {
                    _config.CustomColorPresets = new List<ColorPreset>();
                }

                _config.CustomColorPresets.Add(new ColorPreset 
                { 
                    Name = name, 
                    R = r, 
                    G = g, 
                    B = b 
                });
                
                SaveConfig();
                // Debug.WriteLine($"✅ 已添加自定义颜色预设: {name} RGB({r}, {g}, {b})");
                return true;
            }
            catch (Exception)
            {
                // Debug.WriteLine($"❌ 添加自定义颜色预设失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 删除自定义颜色预设
        /// </summary>
        public bool RemoveCustomColorPreset(string name)
        {
            try
            {
                if (_config.CustomColorPresets == null)
                    return false;

                var preset = _config.CustomColorPresets.FirstOrDefault(p => p.Name == name);
                if (preset != null)
                {
                    _config.CustomColorPresets.Remove(preset);
                    SaveConfig();
                    // Debug.WriteLine($"✅ 已删除自定义颜色预设: {name}");
                    return true;
                }
                
                return false;
            }
            catch (Exception)
            {
                // Debug.WriteLine($"❌ 删除自定义颜色预设失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 根据颜色值查找预设名称
        /// </summary>
        public string FindPresetName(byte r, byte g, byte b)
        {
            var preset = GetAllColorPresets()
                .FirstOrDefault(p => p.R == r && p.G == g && p.B == b);
            
            return preset?.Name;
        }

        /// <summary>
        /// 设置当前颜色（会自动匹配预设名称）
        /// </summary>
        public void SetCurrentColor(byte r, byte g, byte b, string customName = null)
        {
            TargetColorR = r;
            TargetColorG = g;
            TargetColorB = b;
            
            // 查找预设名称
            var presetName = FindPresetName(r, g, b);
            TargetColorName = presetName ?? customName ?? "自定义";
        }


        #endregion

        #region 文件夹颜色管理

        /// <summary>
        /// 获取文件夹的标记颜色（优先使用自定义颜色，否则使用默认颜色池）
        /// </summary>
        /// <param name="folderId">文件夹ID</param>
        /// <param name="customColor">自定义颜色（如果为null或空，则使用默认颜色池）</param>
        public string GetFolderColor(int folderId, string customColor = null)
        {
            // 如果有自定义颜色，优先使用
            if (!string.IsNullOrEmpty(customColor))
            {
                return customColor;
            }
            
            // 否则使用文件夹ID对颜色池取模，确保相同文件夹总是相同颜色
            int colorIndex = (folderId - 1) % FolderColorPool.Length;
            return FolderColorPool[colorIndex];
        }

        /// <summary>
        /// 获取所有文件夹颜色池
        /// </summary>
        public static string[] GetFolderColorPool()
        {
            return FolderColorPool;
        }

        #endregion
    }

    /// <summary>
    /// 应用程序配置模型
    /// </summary>
    public partial class AppConfig
    {
        /// <summary>
        /// 原图显示模式（默认：拉伸）
        /// </summary>
        public OriginalDisplayMode OriginalDisplayMode { get; set; } = OriginalDisplayMode.Stretch;

        /// <summary>
        /// 窗口状态（默认：最大化）
        /// </summary>
        public string WindowState { get; set; } = "Maximized";

        /// <summary>
        /// 最后打开的文件夹
        /// </summary>
        public string LastOpenedFolder { get; set; }

        /// <summary>
        /// 缩放比例（默认：1.0）
        /// </summary>
        public double ZoomRatio { get; set; } = 1.0;

        /// <summary>
        /// 是否启用自动保存
        /// </summary>
        public bool AutoSaveEnabled { get; set; } = false;

        /// <summary>
        /// 目标颜色 - 红色分量（默认：218，秋麒麟）
        /// </summary>
        public byte TargetColorR { get; set; } = 218;

        /// <summary>
        /// 目标颜色 - 绿色分量（默认：165，秋麒麟）
        /// </summary>
        public byte TargetColorG { get; set; } = 165;

        /// <summary>
        /// 目标颜色 - 蓝色分量（默认：32，秋麒麟）
        /// </summary>
        public byte TargetColorB { get; set; } = 32;

        /// <summary>
        /// 目标颜色名称（默认：秋麒麟）
        /// </summary>
        public string TargetColorName { get; set; } = "秋麒麟";

        /// <summary>
        /// 自定义颜色预设列表
        /// </summary>
        public List<ColorPreset> CustomColorPresets { get; set; } = new List<ColorPreset>();

        /// <summary>
        /// 边框设置面板最近使用颜色（最多6个）
        /// </summary>

        /// <summary>
        /// 文本颜色设置面板最近使用颜色（最多6个）
        /// </summary>

        /// <summary>
        /// 背景设置面板最近使用颜色（最多6个）
        /// </summary>

        /// <summary>
        /// 文件夹字号（默认：26）
        /// </summary>
        public double FolderFontSize { get; set; } = 26.0;

        /// <summary>
        /// 文件字号（默认：26）
        /// </summary>
        public double FileFontSize { get; set; } = 26.0;

        /// <summary>
        /// 导航栏宽度（默认：370，圣经模式5列表格需要370宽度）
        /// </summary>
        public double NavigationPanelWidth { get; set; } = 370.0;

        /// <summary>
        /// 文件夹标签字号（搜索结果显示，默认：18）
        /// </summary>
        public double FolderTagFontSize { get; set; } = 18.0;
        
        /// <summary>
        /// 菜单栏字号（默认：22，范围18-40，按照Python版本设计）
        /// </summary>
        public double MenuFontSize { get; set; } = 22.0;

        /// <summary>
        /// 投影动画是否启用（默认：false）
        /// </summary>
        public bool ProjectionAnimationEnabled { get; set; } = false;

        /// <summary>
        /// 投影动画透明度（默认：0.0）
        /// </summary>
        public double ProjectionAnimationOpacity { get; set; } = 0.0;

        /// <summary>
        /// 投影动画时长（毫秒，默认：300）
        /// </summary>
        public int ProjectionAnimationDuration { get; set; } = 300;
    }

    /// <summary>
    /// ConfigManager 类 - 配置管理器（属性访问器部分）
    /// </summary>
    public partial class ConfigManager
    {
        /// <summary>
        /// 投影动画是否启用
        /// </summary>
        public bool ProjectionAnimationEnabled
        {
            get => _config.ProjectionAnimationEnabled;
            set
            {
                if (_config.ProjectionAnimationEnabled != value)
                {
                    _config.ProjectionAnimationEnabled = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 投影动画透明度
        /// </summary>
        public double ProjectionAnimationOpacity
        {
            get => _config.ProjectionAnimationOpacity;
            set
            {
                if (Math.Abs(_config.ProjectionAnimationOpacity - value) > 0.001)
                {
                    _config.ProjectionAnimationOpacity = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 投影动画时长（毫秒）
        /// </summary>
        public int ProjectionAnimationDuration
        {
            get => _config.ProjectionAnimationDuration;
            set
            {
                if (_config.ProjectionAnimationDuration != value)
                {
                    _config.ProjectionAnimationDuration = value;
                    SaveConfig();
                }
            }
        }
    }

    /// <summary>
    /// AppConfig 类 - 继续定义（其他属性）
    /// </summary>
    public partial class AppConfig
    {
        /// <summary>
        /// 全局默认歌词颜色（默认：#FFFFFF 白色）
        /// </summary>
        public string DefaultLyricsColor { get; set; } = "#FFFFFF";

        /// <summary>
        /// 圣经译本（默认：和合本）
        /// </summary>
        public string BibleVersion { get; set; } = "和合本";

        /// <summary>
        /// 圣经数据库文件名（默认：bible.db）
        /// </summary>
        public string BibleDatabaseFileName { get; set; } = "bible.db";

        /// <summary>
        /// 圣经字体（默认：等线）
        /// </summary>
        public string BibleFontFamily { get; set; } = "DengXian";

        /// <summary>
        /// 圣经字体大小（经文和节号，默认：46）
        /// </summary>
        public double BibleFontSize { get; set; } = 46.0;

        /// <summary>
        /// 圣经行距（默认：15.0 = 30 × 1.0 × 0.5，对应显示值1.0）
        /// </summary>
        public double BibleLineHeight { get; set; } = 15.0;

        /// <summary>
        /// 圣经背景色（默认：#000000 黑色）
        /// </summary>
        public string BibleBackgroundColor { get; set; } = "#000000";

        /// <summary>
        /// 圣经经文文字颜色（默认：#FF9A35 橙色 RGB(255, 154, 53)）
        /// </summary>
        public string BibleTextColor { get; set; } = "#FF9A35";

        /// <summary>
        /// 圣经标题颜色（默认：#FF0000 红色）
        /// </summary>
        public string BibleTitleColor { get; set; } = "#FF0000";

        /// <summary>
        /// 圣经节号颜色（默认：#FFFF00 黄色）
        /// </summary>
        public string BibleVerseNumberColor { get; set; } = "#FFFF00";

        /// <summary>
        /// 圣经选中高亮颜色（默认：#FFFF00 黄色）
        /// </summary>
        public string BibleHighlightColor { get; set; } = "#FFFF00";

        /// <summary>
        /// 圣经标题字体大小（默认：61.3 = 46 * 1.333）
        /// </summary>
        public double BibleTitleFontSize { get; set; } = 61.3;

        /// <summary>
        /// 圣经节号字体大小（默认：46，与经文相同）
        /// </summary>
        public double BibleVerseNumberFontSize { get; set; } = 46.0;

        /// <summary>
        /// 圣经左右边距（默认：15）
        /// </summary>
        public double BibleMargin { get; set; } = 15.0;

        /// <summary>
        /// 圣经节间距（节与节之间的间距，默认：15）
        /// </summary>
        public double BibleVerseSpacing { get; set; } = 15.0;

        /// <summary>
        /// 圣经设置窗口X坐标（默认：null，使用自动计算位置）
        /// </summary>
        public double? BibleSettingsWindowLeft { get; set; } = null;

        /// <summary>
        /// 圣经设置窗口Y坐标（默认：null，使用自动计算位置）
        /// </summary>
        public double? BibleSettingsWindowTop { get; set; } = null;

        /// <summary>
        /// 是否在退出程序时保存圣经投影记录（默认：false）
        /// </summary>
        public bool SaveBibleHistory { get; set; } = false;

        /// <summary>
        /// 合成播放默认时长（秒，默认：105）
        /// </summary>
        public double CompositePlaybackDefaultDuration { get; set; } = 105.0;
    }

    /// <summary>
    /// 颜色预设模型
    /// </summary>
    public class ColorPreset
    {
        public string Name { get; set; }
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }

        /// <summary>
        /// 转换为 SKColor
        /// </summary>
        public SKColor ToSKColor() => new SKColor(R, G, B);

        /// <summary>
        /// 是否为内置预设（只读）
        /// </summary>
        public bool IsBuiltIn { get; set; } = false;
    }
}

