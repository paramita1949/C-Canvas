using System;
using System.Windows.Threading;

namespace ImageColorChanger.UI.Modules
{
    /// <summary>
    /// 歌词模块编排控制器，收敛 MainWindow 中的歌词切换与投影联动流程。
    /// </summary>
    public sealed class LyricsModuleController
    {
        private readonly Dispatcher _dispatcher;
        private readonly Func<bool> _isLyricsMode;
        private readonly Func<bool> _isProjectionActive;
        private readonly Action _saveLyricsProject;
        private readonly Action _loadOrCreateLyricsProject;
        private readonly Action _renderLyricsToProjection;
        private readonly Action _clearProjectionImageState;
        private int _imageSwitchVersion;

        public LyricsModuleController(
            Dispatcher dispatcher,
            Func<bool> isLyricsMode,
            Func<bool> isProjectionActive,
            Action saveLyricsProject,
            Action loadOrCreateLyricsProject,
            Action renderLyricsToProjection,
            Action clearProjectionImageState)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _isLyricsMode = isLyricsMode ?? throw new ArgumentNullException(nameof(isLyricsMode));
            _isProjectionActive = isProjectionActive ?? throw new ArgumentNullException(nameof(isProjectionActive));
            _saveLyricsProject = saveLyricsProject ?? throw new ArgumentNullException(nameof(saveLyricsProject));
            _loadOrCreateLyricsProject = loadOrCreateLyricsProject ?? throw new ArgumentNullException(nameof(loadOrCreateLyricsProject));
            _renderLyricsToProjection = renderLyricsToProjection ?? throw new ArgumentNullException(nameof(renderLyricsToProjection));
            _clearProjectionImageState = clearProjectionImageState ?? throw new ArgumentNullException(nameof(clearProjectionImageState));
        }

        public void OnImageChanged()
        {
            // 歌词模块已独立于图片，图片切换不再触发歌词切换。
        }

        public void OnImageChangedInLyricsMode()
        {
            // 歌词模块已独立于图片，图片切换不再触发歌词切换。
        }

        public void OnProjectionStateChanged(bool isProjecting)
        {
            if (!isProjecting || !_isLyricsMode())
            {
                return;
            }

            _clearProjectionImageState();

            var timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(2)
            };

            timer.Tick += (_, __) =>
            {
                timer.Stop();
                _renderLyricsToProjection();
            };
            timer.Start();
        }

        private void SwitchLyricsAndRefreshProjection()
        {
            // 先保存当前项目，避免在图片切换过程中丢失编辑态。
            _saveLyricsProject();

            int version = ++_imageSwitchVersion;

            // 延后到UI消息队列，确保当前图片/选中状态已完成切换，再加载歌词与更新投影。
            _dispatcher.BeginInvoke(new Action(() =>
            {
                if (version != _imageSwitchVersion)
                {
                    return;
                }

                _loadOrCreateLyricsProject();

                if (_isProjectionActive())
                {
                    _renderLyricsToProjection();
                }
            }), DispatcherPriority.Background);
        }
    }
}
