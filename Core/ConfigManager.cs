using System;
using System.IO;
using System.Text.Json;
using System.Diagnostics;

namespace ImageColorChanger.Core
{
    /// <summary>
    /// 配置管理器 - 统一管理应用程序配置
    /// </summary>
    public class ConfigManager
    {
        private readonly string _configFilePath;
        private AppConfig _config;

        public ConfigManager(string configFilePath = "config.json")
        {
            _configFilePath = configFilePath;
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
                    Debug.WriteLine($"✅ 配置文件已加载: {_configFilePath}");
                }
                else
                {
                    Debug.WriteLine($"⚠️ 配置文件不存在，使用默认配置");
                    _config = new AppConfig();
                    SaveConfig();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 加载配置文件失败: {ex.Message}");
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
                Debug.WriteLine($"✅ 配置文件已保存: {_configFilePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 保存配置文件失败: {ex.Message}");
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
    }
}

