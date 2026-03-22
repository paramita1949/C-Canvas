using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using ImageColorChanger.Services.Lyrics.Output;
using ImageColorChanger.Services.Projection.Output;
using SkiaSharp;

namespace ImageColorChanger.Core
{
    /// <summary>
    /// 配置管理器 - 统一管理应用程序配置
    /// </summary>
    public partial class ConfigManager : ILyricsNdiConfigProvider, IProjectionNdiConfigProvider
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

        private static readonly List<LyricsThemePreset> BuiltInLyricsThemePresets = new List<LyricsThemePreset>
        {
            new LyricsThemePreset { Name = "黑色", BackgroundHex = "#000000" },
            new LyricsThemePreset { Name = "白色", BackgroundHex = "#FFFFFF" },
            new LyricsThemePreset { Name = "深绿", BackgroundHex = "#0B3D2E" },
            new LyricsThemePreset { Name = "深蓝", BackgroundHex = "#102A43" }
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
            
            // Debug.WriteLine($" 配置文件路径: {_configFilePath}");
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
                    if (_config == null)
                    {
                        _config = new AppConfig();
                    }

                    bool migrated = MigrateLegacyNdiConfig();
                    if (migrated)
                    {
                        SaveConfig();
                    }
                    
                    //  确保最近颜色列表不为 null
                    
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($" [ConfigManager] 配置文件已加载: {_configFilePath}");
#endif
                }
                else
                {
                    #if DEBUG
                    Debug.WriteLine($" [ConfigManager] 配置文件不存在，使用默认配置");
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
                Debug.WriteLine($" [ConfigManager] 加载配置文件失败: {ex.Message}");
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
                //Debug.WriteLine($" [ConfigManager] 配置文件已保存: {_configFilePath}");
                //#endif
            }
            catch (Exception
            #if DEBUG
            ex
            #endif
            )
            {
                #if DEBUG
                Debug.WriteLine($" [ConfigManager] 保存配置文件失败: {ex.Message}");
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
        /// 圣经历史记录区域高度（0表示使用比例布局，>0表示使用固定高度）
        /// </summary>
        public double BibleHistoryRowHeight
        {
            get => _config.BibleHistoryRowHeight;
            set
            {
                if (Math.Abs(_config.BibleHistoryRowHeight - value) > 0.001)
                {
                    _config.BibleHistoryRowHeight = value;
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
        /// 菜单栏字号（12-40，扩展范围以适配小型笔记本）
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
        /// 分割图片显示模式
        /// </summary>
        public SplitImageDisplayMode SplitImageDisplayMode
        {
            get => _config.SplitImageDisplayMode;
            set
            {
                if (_config.SplitImageDisplayMode != value)
                {
                    _config.SplitImageDisplayMode = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 歌词投影字号（仅影响投影，不影响主屏编辑字号）
        /// </summary>
        public double LyricsProjectionFontSize
        {
            get => _config.LyricsProjectionFontSize;
            set
            {
                if (Math.Abs(_config.LyricsProjectionFontSize - value) > 0.001)
                {
                    _config.LyricsProjectionFontSize = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 歌词主屏字号（仅影响主屏编辑，不影响投影字号）
        /// </summary>
        public double LyricsMainScreenFontSize
        {
            get => _config.LyricsMainScreenFontSize;
            set
            {
                if (Math.Abs(_config.LyricsMainScreenFontSize - value) > 0.001)
                {
                    _config.LyricsMainScreenFontSize = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 歌词文字水印字号
        /// </summary>
        public double LyricsTextWatermarkFontSize
        {
            get => _config.LyricsTextWatermarkFontSize;
            set
            {
                if (Math.Abs(_config.LyricsTextWatermarkFontSize - value) > 0.001)
                {
                    _config.LyricsTextWatermarkFontSize = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 应用最后一次运行版本（用于一次性升级任务判定）
        /// </summary>
        public string LastRunAppVersion
        {
            get => _config.LastRunAppVersion ?? string.Empty;
            set
            {
                string next = value ?? string.Empty;
                if (!string.Equals(_config.LastRunAppVersion ?? string.Empty, next, StringComparison.OrdinalIgnoreCase))
                {
                    _config.LastRunAppVersion = next;
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
        /// 圣经搜索结果悬浮模式字号
        /// </summary>
        public double BibleSearchFloatingFontSize
        {
            get => _config.BibleSearchFloatingFontSize;
            set
            {
                if (Math.Abs(_config.BibleSearchFloatingFontSize - value) > 0.001)
                {
                    _config.BibleSearchFloatingFontSize = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 圣经搜索结果内嵌模式字号
        /// </summary>
        public double BibleSearchEmbeddedFontSize
        {
            get => _config.BibleSearchEmbeddedFontSize;
            set
            {
                if (Math.Abs(_config.BibleSearchEmbeddedFontSize - value) > 0.001)
                {
                    _config.BibleSearchEmbeddedFontSize = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 歌词文字水印颜色（空字符串表示跟随歌词颜色）
        /// </summary>
        public string LyricsTextWatermarkColorHex
        {
            get => _config.LyricsTextWatermarkColorHex ?? string.Empty;
            set
            {
                string next = value ?? string.Empty;
                if (!string.Equals(_config.LyricsTextWatermarkColorHex ?? string.Empty, next, StringComparison.OrdinalIgnoreCase))
                {
                    _config.LyricsTextWatermarkColorHex = next;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 原图模式滚轮缩放比例（独立于置顶百分比）
        /// </summary>
        public double OriginalModeZoomRatio
        {
            get => _config.OriginalModeZoomRatio;
            set
            {
                double clamped = Math.Max(Constants.MinZoomRatio, Math.Min(Constants.MaxZoomRatio, value));
                if (Math.Abs(_config.OriginalModeZoomRatio - clamped) > 0.001)
                {
                    _config.OriginalModeZoomRatio = clamped;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 原图置顶模式缩放百分比（60-100）
        /// </summary>
        public int OriginalTopScalePercent
        {
            get => _config.OriginalTopScalePercent;
            set
            {
                int normalized = NormalizeOriginalTopScalePercent(value);
                if (_config.OriginalTopScalePercent != normalized)
                {
                    _config.OriginalTopScalePercent = normalized;
                    SaveConfig();
                }
            }
        }

        private static int NormalizeOriginalTopScalePercent(int value)
        {
            int clamped = Math.Max(60, Math.Min(100, value));
            int step = (int)Math.Round((clamped - 60) / 5.0);
            return 60 + step * 5;
        }

        /// <summary>
        /// 圣经拼音预览字号
        /// </summary>
        public double BiblePreviewFontSize
        {
            get
            {
                double raw = _config.BiblePreviewFontSize;
                if (raw <= 0)
                {
                    raw = 35.0;
                }

                return Math.Clamp(raw, 15.0, 70.0);
            }
            set
            {
                double normalized = Math.Clamp(value, 15.0, 70.0);
                if (Math.Abs(_config.BiblePreviewFontSize - normalized) > 0.001)
                {
                    _config.BiblePreviewFontSize = normalized;
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
                    // Debug.WriteLine($" 颜色预设已存在: {name}");
                    return false;
                }

                // 检查是否已存在相同颜色
                if (allPresets.Any(p => p.R == r && p.G == g && p.B == b))
                {
                    // Debug.WriteLine($" 该颜色已在预设中: RGB({r}, {g}, {b})");
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
                // Debug.WriteLine($" 已添加自定义颜色预设: {name} RGB({r}, {g}, {b})");
                return true;
            }
            catch (Exception)
            {
                // Debug.WriteLine($" 添加自定义颜色预设失败: {ex.Message}");
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
                    // Debug.WriteLine($" 已删除自定义颜色预设: {name}");
                    return true;
                }
                
                return false;
            }
            catch (Exception)
            {
                // Debug.WriteLine($" 删除自定义颜色预设失败: {ex.Message}");
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

        #region 歌词主题预设管理

        public List<LyricsThemePreset> GetAllLyricsThemePresets()
        {
            var allPresets = new List<LyricsThemePreset>(BuiltInLyricsThemePresets);

            if (_config.LyricsThemeCustomPresets != null && _config.LyricsThemeCustomPresets.Count > 0)
            {
                foreach (var preset in _config.LyricsThemeCustomPresets)
                {
                    if (preset == null || string.IsNullOrWhiteSpace(preset.Name) || string.IsNullOrWhiteSpace(preset.BackgroundHex))
                    {
                        continue;
                    }

                    if (!allPresets.Any(p => p.Name == preset.Name))
                    {
                        allPresets.Add(new LyricsThemePreset
                        {
                            Name = preset.Name,
                            BackgroundHex = preset.BackgroundHex
                        });
                    }
                }
            }

            return allPresets;
        }

        public bool AddCustomLyricsThemePreset(string name, string backgroundHex)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(backgroundHex))
                {
                    return false;
                }

                string normalizedName = name.Trim();
                string normalizedHex = backgroundHex.Trim().ToUpperInvariant();

                var allPresets = GetAllLyricsThemePresets();
                if (allPresets.Any(p => p.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }

                if (_config.LyricsThemeCustomPresets == null)
                {
                    _config.LyricsThemeCustomPresets = new List<LyricsThemePreset>();
                }

                _config.LyricsThemeCustomPresets.Add(new LyricsThemePreset
                {
                    Name = normalizedName,
                    BackgroundHex = normalizedHex
                });

                SaveConfig();
                return true;
            }
            catch
            {
                return false;
            }
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

        private bool MigrateLegacyNdiConfig()
        {
            if (_config == null)
            {
                return false;
            }

            bool changed = false;
            bool projectionLooksDefault =
                !_config.ProjectionNdiEnabled &&
                string.Equals(_config.ProjectionNdiSenderName ?? "CanvasCast-Projection", "CanvasCast-Projection", StringComparison.Ordinal) &&
                _config.ProjectionNdiWidth == 1920 &&
                _config.ProjectionNdiHeight == 1080 &&
                _config.ProjectionNdiFps == 30 &&
                _config.ProjectionNdiPreferAlpha &&
                _config.ProjectionNdiLyricsTransparentEnabled &&
                _config.ProjectionNdiBibleTransparentEnabled;

            if (projectionLooksDefault && _config.LyricsNdiEnabled)
            {
                _config.ProjectionNdiEnabled = true;
                changed = true;
            }

            if (projectionLooksDefault && !string.IsNullOrWhiteSpace(_config.LyricsNdiSenderName) &&
                !string.Equals(_config.LyricsNdiSenderName, "CanvasCast-Lyrics", StringComparison.Ordinal))
            {
                _config.ProjectionNdiSenderName = _config.LyricsNdiSenderName;
                changed = true;
            }

            if (projectionLooksDefault && _config.LyricsNdiWidth > 0 && _config.LyricsNdiWidth != 1920)
            {
                _config.ProjectionNdiWidth = _config.LyricsNdiWidth;
                changed = true;
            }

            if (projectionLooksDefault && _config.LyricsNdiHeight > 0 && _config.LyricsNdiHeight != 1080)
            {
                _config.ProjectionNdiHeight = _config.LyricsNdiHeight;
                changed = true;
            }

            if (projectionLooksDefault && _config.LyricsNdiFps > 0 && _config.LyricsNdiFps != 30)
            {
                _config.ProjectionNdiFps = _config.LyricsNdiFps;
                changed = true;
            }

            if (projectionLooksDefault && !_config.LyricsNdiPreferAlpha)
            {
                _config.ProjectionNdiPreferAlpha = false;
                changed = true;
            }

            return changed;
        }
    }

    /// <summary>
    /// 应用程序配置模型
    /// </summary>
    public partial class AppConfig
    {
        /// <summary>
        /// 原图显示模式（默认：适中）
        /// </summary>
        public OriginalDisplayMode OriginalDisplayMode { get; set; } = OriginalDisplayMode.Fit;

        /// <summary>
        /// 原图置顶模式缩放百分比（默认：80）
        /// </summary>
        public int OriginalTopScalePercent { get; set; } = 80;

        /// <summary>
        /// 分割图片显示模式（默认：适中居中）
        /// </summary>
        [JsonPropertyName("SplitStretchMode")]
        [JsonConverter(typeof(LegacySplitImageDisplayModeJsonConverter))]
        public SplitImageDisplayMode SplitImageDisplayMode { get; set; } = SplitImageDisplayMode.FitCenter;

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
        /// 原图模式滚轮缩放比例（默认：1.0）
        /// </summary>
        public double OriginalModeZoomRatio { get; set; } = 1.0;

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
        /// 歌词主题自定义预设列表
        /// </summary>
        public List<LyricsThemePreset> LyricsThemeCustomPresets { get; set; } = new List<LyricsThemePreset>();

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
        /// 圣经历史记录区域高度（默认：0，表示使用比例布局）
        /// </summary>
        public double BibleHistoryRowHeight { get; set; } = 0.0;

        /// <summary>
        /// 文件夹标签字号（搜索结果显示，默认：18）
        /// </summary>
        public double FolderTagFontSize { get; set; } = 18.0;
        
        /// <summary>
        /// 菜单栏字号（默认：22，范围12-40，扩展以适配小型笔记本）
        /// </summary>
        public double MenuFontSize { get; set; } = 22.0;

        /// <summary>
        /// 投影动画是否启用（默认：true）
        /// </summary>
        public bool ProjectionAnimationEnabled { get; set; } = true;

        /// <summary>
        /// 投影动画透明度（默认：0.1）
        /// </summary>
        public double ProjectionAnimationOpacity { get; set; } = 0.1;

        /// <summary>
        /// 投影动画时长（毫秒，默认：800）
        /// </summary>
        public int ProjectionAnimationDuration { get; set; } = 800;

        /// <summary>
        /// 圣经弹窗动画是否启用（默认：true）
        /// </summary>
        public bool BiblePopupAnimationEnabled { get; set; } = true;

        /// <summary>
        /// 圣经弹窗动画透明度（默认：0.1）
        /// </summary>
        public double BiblePopupAnimationOpacity { get; set; } = 0.1;

        /// <summary>
        /// 圣经弹窗动画时长（毫秒，默认：800）
        /// </summary>
        public int BiblePopupAnimationDuration { get; set; } = 800;

        /// <summary>
        /// 圣经弹窗动画类型（FadeOnly|TopPush|BottomPush|TopReveal|BottomReveal|ZoomIn）
        /// </summary>
        public string BiblePopupAnimationType { get; set; } = "TopReveal";

        /// <summary>
        /// 是否启用歌词 NDI 输出（默认：关闭）
        /// </summary>
        public bool LyricsNdiEnabled { get; set; } = false;

        /// <summary>
        /// 歌词 NDI 发送端名称
        /// </summary>
        public string LyricsNdiSenderName { get; set; } = "CanvasCast-Lyrics";

        /// <summary>
        /// 歌词 NDI 输出宽度
        /// </summary>
        public int LyricsNdiWidth { get; set; } = 1920;

        /// <summary>
        /// 歌词 NDI 输出高度
        /// </summary>
        public int LyricsNdiHeight { get; set; } = 1080;

        /// <summary>
        /// 歌词 NDI 输出帧率
        /// </summary>
        public int LyricsNdiFps { get; set; } = 30;

        /// <summary>
        /// 歌词 NDI 是否优先 alpha 输出
        /// </summary>
        public bool LyricsNdiPreferAlpha { get; set; } = true;

        /// <summary>
        /// 是否启用全投影 NDI 输出（默认关闭）
        /// </summary>
        public bool ProjectionNdiEnabled { get; set; } = false;

        /// <summary>
        /// 全投影 NDI 发送端名称
        /// </summary>
        public string ProjectionNdiSenderName { get; set; } = "YongMu-NDI";

        /// <summary>
        /// 全投影 NDI 输出宽度
        /// </summary>
        public int ProjectionNdiWidth { get; set; } = 1920;

        /// <summary>
        /// 全投影 NDI 输出高度
        /// </summary>
        public int ProjectionNdiHeight { get; set; } = 1080;

        /// <summary>
        /// 全投影 NDI 输出帧率
        /// </summary>
        public int ProjectionNdiFps { get; set; } = 30;

        /// <summary>
        /// 全投影 NDI 是否优先 alpha 输出
        /// </summary>
        public bool ProjectionNdiPreferAlpha { get; set; } = true;

        /// <summary>
        /// 歌词投影是否允许透明输出（可选）
        /// </summary>
        public bool ProjectionNdiLyricsTransparentEnabled { get; set; } = true;

        /// <summary>
        /// 圣经投影是否允许透明输出（可选）
        /// </summary>
        public bool ProjectionNdiBibleTransparentEnabled { get; set; } = true;

        /// <summary>
        /// 画布宽高比（默认：16:9）
        /// 可选值："16:9" 或 "4:3"
        /// </summary>
        public string CanvasAspectRatio { get; set; } = "16:9";
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

        /// <summary>
        /// 圣经弹窗动画是否启用
        /// </summary>
        public bool BiblePopupAnimationEnabled
        {
            get => _config.BiblePopupAnimationEnabled;
            set
            {
                if (_config.BiblePopupAnimationEnabled != value)
                {
                    _config.BiblePopupAnimationEnabled = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 圣经弹窗动画透明度
        /// </summary>
        public double BiblePopupAnimationOpacity
        {
            get => _config.BiblePopupAnimationOpacity;
            set
            {
                if (Math.Abs(_config.BiblePopupAnimationOpacity - value) > 0.001)
                {
                    _config.BiblePopupAnimationOpacity = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 圣经弹窗动画时长（毫秒）
        /// </summary>
        public int BiblePopupAnimationDuration
        {
            get => _config.BiblePopupAnimationDuration;
            set
            {
                if (_config.BiblePopupAnimationDuration != value)
                {
                    _config.BiblePopupAnimationDuration = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 圣经弹窗动画类型（FadeOnly|TopPush|BottomPush|TopReveal|BottomReveal|ZoomIn）
        /// </summary>
        public string BiblePopupAnimationType
        {
            get => string.IsNullOrWhiteSpace(_config.BiblePopupAnimationType) ? "TopReveal" : _config.BiblePopupAnimationType;
            set
            {
                string normalized = value switch
                {
                    "FadeOnly" => "FadeOnly",
                    "TopPush" => "TopPush",
                    "BottomPush" => "BottomPush",
                    "BottomReveal" => "BottomReveal",
                    "CenterFade" => "ZoomIn",
                    "ZoomIn" => "ZoomIn",
                    _ => "TopReveal"
                };
                if (!string.Equals(_config.BiblePopupAnimationType, normalized, StringComparison.Ordinal))
                {
                    _config.BiblePopupAnimationType = normalized;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 是否启用歌词 NDI 输出
        /// </summary>
        public bool LyricsNdiEnabled
        {
            get => _config.LyricsNdiEnabled;
            set
            {
                if (_config.LyricsNdiEnabled != value)
                {
                    _config.LyricsNdiEnabled = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 歌词 NDI 发送端名称
        /// </summary>
        public string LyricsNdiSenderName
        {
            get => _config.LyricsNdiSenderName ?? "CanvasCast-Lyrics";
            set
            {
                var next = string.IsNullOrWhiteSpace(value) ? "CanvasCast-Lyrics" : value.Trim();
                if (!string.Equals(_config.LyricsNdiSenderName, next, StringComparison.Ordinal))
                {
                    _config.LyricsNdiSenderName = next;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 歌词 NDI 输出宽度
        /// </summary>
        public int LyricsNdiWidth
        {
            get => _config.LyricsNdiWidth;
            set
            {
                int normalized = Math.Max(320, value);
                if (_config.LyricsNdiWidth != normalized)
                {
                    _config.LyricsNdiWidth = normalized;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 歌词 NDI 输出高度
        /// </summary>
        public int LyricsNdiHeight
        {
            get => _config.LyricsNdiHeight;
            set
            {
                int normalized = Math.Max(180, value);
                if (_config.LyricsNdiHeight != normalized)
                {
                    _config.LyricsNdiHeight = normalized;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 歌词 NDI 输出帧率
        /// </summary>
        public int LyricsNdiFps
        {
            get => _config.LyricsNdiFps;
            set
            {
                int normalized = Math.Clamp(value, 1, 120);
                if (_config.LyricsNdiFps != normalized)
                {
                    _config.LyricsNdiFps = normalized;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 歌词 NDI 是否优先 alpha 输出
        /// </summary>
        public bool LyricsNdiPreferAlpha
        {
            get => _config.LyricsNdiPreferAlpha;
            set
            {
                if (_config.LyricsNdiPreferAlpha != value)
                {
                    _config.LyricsNdiPreferAlpha = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 是否启用全投影 NDI 输出
        /// </summary>
        public bool ProjectionNdiEnabled
        {
            get => _config.ProjectionNdiEnabled;
            set
            {
                if (_config.ProjectionNdiEnabled != value)
                {
                    _config.ProjectionNdiEnabled = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 全投影 NDI 发送端名称
        /// </summary>
        public string ProjectionNdiSenderName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_config.ProjectionNdiSenderName) ||
                    string.Equals(_config.ProjectionNdiSenderName, "CanvasCast-Projection", StringComparison.Ordinal))
                {
                    return "YongMu-NDI";
                }

                return _config.ProjectionNdiSenderName;
            }
            set
            {
                var next = string.IsNullOrWhiteSpace(value) ? "YongMu-NDI" : value.Trim();
                if (!string.Equals(_config.ProjectionNdiSenderName, next, StringComparison.Ordinal))
                {
                    _config.ProjectionNdiSenderName = next;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 全投影 NDI 输出宽度
        /// </summary>
        public int ProjectionNdiWidth
        {
            get => _config.ProjectionNdiWidth > 0 ? _config.ProjectionNdiWidth : 1920;
            set
            {
                int normalized = Math.Max(320, value);
                if (_config.ProjectionNdiWidth != normalized)
                {
                    _config.ProjectionNdiWidth = normalized;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 全投影 NDI 输出高度
        /// </summary>
        public int ProjectionNdiHeight
        {
            get => _config.ProjectionNdiHeight > 0 ? _config.ProjectionNdiHeight : 1080;
            set
            {
                int normalized = Math.Max(180, value);
                if (_config.ProjectionNdiHeight != normalized)
                {
                    _config.ProjectionNdiHeight = normalized;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 全投影 NDI 输出帧率
        /// </summary>
        public int ProjectionNdiFps
        {
            get => _config.ProjectionNdiFps > 0 ? _config.ProjectionNdiFps : 30;
            set
            {
                int normalized = Math.Clamp(value, 1, 120);
                if (_config.ProjectionNdiFps != normalized)
                {
                    _config.ProjectionNdiFps = normalized;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 全投影 NDI 是否优先 alpha 输出
        /// </summary>
        public bool ProjectionNdiPreferAlpha
        {
            get => _config.ProjectionNdiPreferAlpha;
            set
            {
                if (_config.ProjectionNdiPreferAlpha != value)
                {
                    _config.ProjectionNdiPreferAlpha = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 歌词透明投影开关（可选透明）
        /// </summary>
        public bool ProjectionNdiLyricsTransparentEnabled
        {
            get => _config.ProjectionNdiLyricsTransparentEnabled;
            set
            {
                if (_config.ProjectionNdiLyricsTransparentEnabled != value)
                {
                    _config.ProjectionNdiLyricsTransparentEnabled = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 圣经透明投影开关（可选透明）
        /// </summary>
        public bool ProjectionNdiBibleTransparentEnabled
        {
            get => _config.ProjectionNdiBibleTransparentEnabled;
            set
            {
                if (_config.ProjectionNdiBibleTransparentEnabled != value)
                {
                    _config.ProjectionNdiBibleTransparentEnabled = value;
                    SaveConfig();
                }
            }
        }

        /// <summary>
        /// 画布宽高比
        /// </summary>
        public string CanvasAspectRatio
        {
            get => _config.CanvasAspectRatio;
            set
            {
                if (_config.CanvasAspectRatio != value)
                {
                    _config.CanvasAspectRatio = value;
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
        /// 歌词投影字号（默认：60）
        /// </summary>
        public double LyricsProjectionFontSize { get; set; } = 60.0;

        /// <summary>
        /// 歌词主屏字号（默认：40）
        /// </summary>
        public double LyricsMainScreenFontSize { get; set; } = 40.0;

        /// <summary>
        /// 歌词文字水印字号（默认：60）
        /// </summary>
        public double LyricsTextWatermarkFontSize { get; set; } = 60.0;

        /// <summary>
        /// 歌词文字水印颜色（默认：空，表示跟随歌词颜色）
        /// </summary>
        public string LyricsTextWatermarkColorHex { get; set; } = "";

        /// <summary>
        /// 应用最后一次运行版本（默认：空）
        /// </summary>
        public string LastRunAppVersion { get; set; } = "";

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
        /// 圣经搜索结果悬浮模式字号（默认：15）
        /// </summary>
        public double BibleSearchFloatingFontSize { get; set; } = 15.0;

        /// <summary>
        /// 圣经搜索结果内嵌模式字号（默认：15）
        /// </summary>
        public double BibleSearchEmbeddedFontSize { get; set; } = 15.0;

        /// <summary>
        /// 圣经拼音预览字号（默认：35）
        /// </summary>
        public double BiblePreviewFontSize { get; set; } = 35.0;

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

    public class LyricsThemePreset
    {
        public string Name { get; set; } = "";
        public string BackgroundHex { get; set; } = "#000000";
    }
}


