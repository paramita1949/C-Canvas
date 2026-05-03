using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using ImageColorChanger.Core;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Migrations;
using ImageColorChanger.Managers;
using ImageColorChanger.Services.Projection.Output;
using ImageColorChanger.Services.Ndi;
using ImageColorChanger.Utils;
using MessageBox = System.Windows.MessageBox;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 基础设施初始化（数据库/屏幕/视频）
    /// </summary>
    public partial class MainWindow
    {
        private const string StartupMigrationStampKey = "startup.migration.runner.version";
        private const string StartupMediaNameRepairStampKey = "startup.media.displayname.repair.v1";

        private void InitializeDatabase()
        {
            try
            {
                var warmupTask = App.DatabaseWarmupTask;
                if (warmupTask != null && !warmupTask.IsCompleted)
                {
                    var waitSw = Stopwatch.StartNew();
                    StartupPerfLogger.Mark("MainWindow.InitializeUI.DatabaseWarmup.Wait.Begin");
                    warmupTask.GetAwaiter().GetResult();
                    StartupPerfLogger.Mark("MainWindow.InitializeUI.DatabaseWarmup.Wait.Completed", $"ElapsedMs={waitSw.ElapsedMilliseconds}");
                }

                _configManager = _mainWindowServices.GetRequired<ConfigManager>();
                // NDI 开关改为会话级：每次启动强制回到关闭状态，需手动重新开启。
                _configManager.ProjectionNdiEnabled = false;
                _configManager.LiveCaptionNdiEnabled = false;
                _projectionNdiOutputManager = _mainWindowServices.GetRequired<ProjectionNdiOutputManager>();
                _ndiRouter = _mainWindowServices.GetRequired<INdiRouter>();
                var dbManager = DatabaseManagerService;
                _dbContext = dbManager.GetDbContext();
                dbManager.NormalizeFolderVideoPlayModes("random");
                RunMediaDisplayNameRepairIfNeeded(dbManager);

                // 迁移只在版本变化时执行，避免每次启动重复跑 schema 检查。
                var currentVersion = Services.UpdateService.GetCurrentVersion() ?? "0.0.0.0";
                var lastMigratedVersion = dbManager.GetSetting(StartupMigrationStampKey, string.Empty) ?? string.Empty;
                bool shouldRunStartupMigrations = !string.Equals(
                    lastMigratedVersion,
                    currentVersion,
                    StringComparison.OrdinalIgnoreCase);

                if (shouldRunStartupMigrations)
                {
                    var migrationSw = Stopwatch.StartNew();
                    using (var migrationRunner = new DatabaseMigrationRunner(_dbContext))
                    {
                        migrationRunner.RunStartupMigrations();
                    }

                    dbManager.SaveSetting(StartupMigrationStampKey, currentVersion);
                    StartupPerfLogger.Mark(
                        "MainWindow.InitializeUI.Database.Migrations.Completed",
                        $"ElapsedMs={migrationSw.ElapsedMilliseconds}; Version={currentVersion}");
                }
                else
                {
                    StartupPerfLogger.Mark(
                        "MainWindow.InitializeUI.Database.Migrations.Skipped",
                        $"Version={currentVersion}");
                }

                // 垂直文本对齐为增量列能力：无论版本戳是否命中，都做一次轻量补齐检查。
                using (var incrementalRunner = new DatabaseMigrationRunner(_dbContext))
                {
                    incrementalRunner.MigrateAddTextVerticalAlignSupport();
                }


                // 显式注入设置存储，避免控件直接依赖 DatabaseManager
                _uiSettingsStore = _mainWindowServices.GetRequired<Services.Interfaces.IUiSettingsStore>();
                BackgroundSettingsPanel.SettingsStore = _uiSettingsStore;
                BorderSettingsPanel.SettingsStore = _uiSettingsStore;
                TextColorSettingsPanel.SettingsStore = _uiSettingsStore;
                TextColorSettingsPanel.ColorApplied -= OnTextColorAppliedFromPanel;
                TextColorSettingsPanel.ColorApplied += OnTextColorAppliedFromPanel;
                NoticeSettingsPanel.ConfigChanged -= OnNoticeConfigChangedFromPanel;
                NoticeSettingsPanel.ConfigChanged += OnNoticeConfigChangedFromPanel;
                InitializeThemeSettings();

                // 搜索范围在 Window_Loaded 的项目树刷新后统一加载，避免启动阶段重复查询。
            }
            catch (Exception ex)
            {
                MessageBox.Show($"数据库初始化失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RunMediaDisplayNameRepairIfNeeded(DatabaseManager dbManager)
        {
            try
            {
                string repairedStamp = dbManager.GetSetting(StartupMediaNameRepairStampKey, "0") ?? "0";
                if (string.Equals(repairedStamp, "1", StringComparison.Ordinal))
                {
                    return;
                }

                int repairedCount = RepairLegacyMediaDisplayNames();
                dbManager.SaveSetting(StartupMediaNameRepairStampKey, "1");

#if DEBUG
                Debug.WriteLine($"[MediaNameRepair] completed. repaired={repairedCount}");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[MediaNameRepair] failed: {ex.Message}");
#else
                _ = ex;
#endif
            }
        }

        private int RepairLegacyMediaDisplayNames()
        {
            if (_dbContext == null)
            {
                return 0;
            }

            var mediaFiles = _dbContext.MediaFiles.ToList();
            int repaired = 0;

            foreach (var mediaFile in mediaFiles)
            {
                if (string.IsNullOrWhiteSpace(mediaFile?.Path))
                {
                    continue;
                }

                string physicalBaseName = Path.GetFileNameWithoutExtension(mediaFile.Path);
                if (string.IsNullOrWhiteSpace(physicalBaseName))
                {
                    continue;
                }

                if (!TryRepairDisplayNameFromPath(mediaFile.Name, physicalBaseName, out string repairedName))
                {
                    continue;
                }

                if (string.Equals(repairedName, mediaFile.Name, StringComparison.Ordinal))
                {
                    continue;
                }

#if DEBUG
                Debug.WriteLine($"[MediaNameRepair] id={mediaFile.Id}, old='{mediaFile.Name}', new='{repairedName}', path='{mediaFile.Path}'");
#endif
                mediaFile.Name = repairedName;
                repaired++;
            }

            if (repaired > 0)
            {
                _dbContext.SaveChanges();
            }

            return repaired;
        }

        private static bool TryRepairDisplayNameFromPath(string currentDisplayName, string physicalBaseName, out string repairedName)
        {
            repairedName = currentDisplayName;

            if (string.IsNullOrWhiteSpace(physicalBaseName))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(currentDisplayName))
            {
                repairedName = physicalBaseName;
                return true;
            }

            // 形如 "915. -48我心唯一爱慕"：序号后正文被错误截断，优先按物理文件名修复。
            var seqMatch = Regex.Match(currentDisplayName, @"^\s*(\d+)\.\s*(.+)$");
            if (seqMatch.Success)
            {
                string seq = seqMatch.Groups[1].Value;
                string currentCore = seqMatch.Groups[2].Value.Trim();
                if ((currentCore.StartsWith("-") || currentCore.StartsWith("_")) &&
                    physicalBaseName.EndsWith(currentCore, StringComparison.OrdinalIgnoreCase) &&
                    physicalBaseName.Length > currentCore.Length)
                {
                    repairedName = $"{seq}. {physicalBaseName}";
                    return true;
                }

                return false;
            }

            // 无序号但正文以连接符开头，且物理名以该字符串结尾，视为截断。
            string trimmed = currentDisplayName.Trim();
            if ((trimmed.StartsWith("-") || trimmed.StartsWith("_")) &&
                physicalBaseName.EndsWith(trimmed, StringComparison.OrdinalIgnoreCase) &&
                physicalBaseName.Length > trimmed.Length)
            {
                repairedName = physicalBaseName;
                return true;
            }

            return false;
        }

        private void InitializeScreenSelector()
        {
            // 屏幕选择器由 ProjectionManager 管理，这里不需要再初始化
            // ProjectionManager 会在初始化时自动填充屏幕列表并选择扩展屏
        }

        /// <summary>
        /// 初始化视频播放器
        /// </summary>
        private void InitializeVideoPlayer()
        {
            try
            {
                if (_videoPlayerManager != null)
                {
                    return;
                }

                _videoPlayerManager = new VideoPlayerManager(this);

                if (_projectionManager != null)
                {
                    _projectionManager.AttachVideoPlayerManager(_videoPlayerManager);
                }

                _mediaModuleController?.Dispose();
                _mediaModuleController = new Modules.MediaModuleController(
                    _videoPlayerManager,
                    VideoPlayerManager_VideoTrackDetected,
                    OnVideoPlayStateChanged,
                    OnVideoMediaChanged,
                    OnVideoMediaEnded,
                    OnVideoProgressUpdated,
                    OnVideoPlaybackError);
                _mediaModuleController.Attach();

                _mediaModuleController.InitializeMainVideoView(VideoContainer);

                _videoPlayerManager.SetVolume(50);
                VolumeSlider.Value = 50;

                SetMediaPlayModeButtonContent(PlayMode.Random);
                BtnPlayMode.ToolTip = "播放模式：随机";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"视频播放器初始化失败: {ex.Message}\n\n部分功能可能无法使用。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private bool EnsureVideoPlayerInitialized(string reason = null)
        {
            if (_videoPlayerManager != null)
            {
                return true;
            }

            var sw = Stopwatch.StartNew();
            StartupPerfLogger.Mark("MainWindow.VideoPlayer.LazyInit.Begin", $"Reason={reason ?? "Unknown"}");
            try
            {
                InitializeVideoPlayer();
                StartupPerfLogger.Mark("MainWindow.VideoPlayer.LazyInit.Completed", $"ElapsedMs={sw.ElapsedMilliseconds}; Success={_videoPlayerManager != null}");
                return _videoPlayerManager != null;
            }
            catch (Exception ex)
            {
                StartupPerfLogger.Error("MainWindow.VideoPlayer.LazyInit.Failed", ex);
                return false;
            }
        }
    }
}

