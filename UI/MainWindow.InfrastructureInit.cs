using System;
using System.Windows;
using ImageColorChanger.Core;
using ImageColorChanger.Database;
using ImageColorChanger.Managers;
using LibVLCSharp.WPF;
using MessageBox = System.Windows.MessageBox;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 基础设施初始化（数据库/屏幕/视频）
    /// </summary>
    public partial class MainWindow
    {
        private void InitializeDatabase()
        {
            try
            {
                _configManager = new ConfigManager();
                _dbManager = new DatabaseManager();

                _dbManager.MigrateAddLoopCount();
                _dbManager.MigrateAddHighlightColor();
                _dbManager.MigrateAddBibleHistoryTable();
                _dbManager.MigrateAddBibleInsertConfigTable();
                _dbManager.MigrateAddUnderlineSupport();
                _dbManager.MigrateAddRichTextSupport();
                _dbManager.MigrateCreateRichTextSpansTable();
                _dbManager.MigrateAddShadowTypeAndPreset();
                _dbManager.MigrateAddVideoBackgroundSupport();

                _sortManager = new SortManager();
                _searchManager = new SearchManager(_dbManager, _configManager);
                _importManager = new ImportManager(_dbManager, _sortManager);
                _slideExportManager = new SlideExportManager(_dbManager);
                _slideImportManager = new SlideImportManager(_dbManager);

                LoadSearchScopes();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"数据库初始化失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                _videoPlayerManager = new VideoPlayerManager(this);

                if (_projectionManager != null)
                {
                    var field = typeof(ProjectionManager).GetField(
                        "_videoPlayerManager",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    field?.SetValue(_projectionManager, _videoPlayerManager);
                }

                _videoPlayerManager.VideoTrackDetected += VideoPlayerManager_VideoTrackDetected;

                _mainVideoView = new VideoView
                {
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
                    Margin = new Thickness(0)
                };

                VideoContainer.Children.Add(_mainVideoView);

                bool mediaPlayerInitialized = false;
                SizeChangedEventHandler sizeChangedHandler = null;

                sizeChangedHandler = (s, e) =>
                {
                    try
                    {
                        if (!mediaPlayerInitialized && _mainVideoView.ActualWidth > 0 && _mainVideoView.ActualHeight > 0)
                        {
                            _videoPlayerManager.InitializeMediaPlayer(_mainVideoView);
                            _videoPlayerManager.SetMainVideoView(_mainVideoView);
                            mediaPlayerInitialized = true;
                            _mainVideoView.SizeChanged -= sizeChangedHandler;
                        }
                    }
                    catch (Exception)
                    {
                    }
                };

                _mainVideoView.SizeChanged += sizeChangedHandler;

                _videoPlayerManager.PlayStateChanged += OnVideoPlayStateChanged;
                _videoPlayerManager.MediaChanged += OnVideoMediaChanged;
                _videoPlayerManager.MediaEnded += OnVideoMediaEnded;
                _videoPlayerManager.ProgressUpdated += OnVideoProgressUpdated;

                _videoPlayerManager.SetVolume(50);
                VolumeSlider.Value = 50;

                BtnPlayMode.Content = "🔀";
                BtnPlayMode.ToolTip = "播放模式：随机";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"视频播放器初始化失败: {ex.Message}\n\n部分功能可能无法使用。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
