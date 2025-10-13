using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Diagnostics;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageColorChanger.Core
{
    /// <summary>
    /// 配置管理器 - 统一管理应用程序配置
    /// </summary>
    public class ConfigManager
    {
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
                    // Debug.WriteLine($"✅ 配置文件已加载: {_configFilePath}");
                    // Debug.WriteLine($"   原图显示模式: {_config.OriginalDisplayMode} ({(int)_config.OriginalDisplayMode})");
                    // Debug.WriteLine($"   缩放比例: {_config.ZoomRatio}");
                }
                else
                {
                    // Debug.WriteLine($"⚠️ 配置文件不存在，使用默认配置");
                    _config = new AppConfig();
                    SaveConfig();
                }
            }
            catch (Exception)
            {
                // Debug.WriteLine($"❌ 加载配置文件失败: {ex.Message}");
                // Debug.WriteLine($"   错误详情: {ex}");
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
                // Debug.WriteLine($"✅ 配置文件已保存: {_configFilePath}");
                // Debug.WriteLine($"   原图显示模式: {_config.OriginalDisplayMode} ({(int)_config.OriginalDisplayMode})");
                // Debug.WriteLine($"   缩放比例: {_config.ZoomRatio}");
            }
            catch (Exception)
            {
                // Debug.WriteLine($"❌ 保存配置文件失败: {ex.Message}");
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
        /// 获取文件夹的标记颜色
        /// </summary>
        public string GetFolderColor(int folderId)
        {
            // 使用文件夹ID对颜色池取模，确保相同文件夹总是相同颜色
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
    public class AppConfig
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
        /// 目标颜色 - 红色分量（默认：174，淡黄色）
        /// </summary>
        public byte TargetColorR { get; set; } = 174;

        /// <summary>
        /// 目标颜色 - 绿色分量（默认：159，淡黄色）
        /// </summary>
        public byte TargetColorG { get; set; } = 159;

        /// <summary>
        /// 目标颜色 - 蓝色分量（默认：112，淡黄色）
        /// </summary>
        public byte TargetColorB { get; set; } = 112;

        /// <summary>
        /// 目标颜色名称（默认：淡黄）
        /// </summary>
        public string TargetColorName { get; set; } = "淡黄";

        /// <summary>
        /// 自定义颜色预设列表
        /// </summary>
        public List<ColorPreset> CustomColorPresets { get; set; } = new List<ColorPreset>();

        /// <summary>
        /// 文件夹字号（默认：13）
        /// </summary>
        public double FolderFontSize { get; set; } = 13.0;

        /// <summary>
        /// 文件字号（默认：13）
        /// </summary>
        public double FileFontSize { get; set; } = 13.0;
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
        /// 转换为 Rgba32
        /// </summary>
        public Rgba32 ToRgba32() => new Rgba32(R, G, B);

        /// <summary>
        /// 是否为内置预设（只读）
        /// </summary>
        public bool IsBuiltIn { get; set; } = false;
    }
}

