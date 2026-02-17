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
            if (!_isLyricsMode())
            {
                return;
            }

            SwitchLyricsAndRefreshProjection();
        }

        public void OnImageChangedInLyricsMode()
        {
            SwitchLyricsAndRefreshProjection();
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
            _saveLyricsProject();
            _loadOrCreateLyricsProject();

            if (_isProjectionActive())
            {
                _renderLyricsToProjection();
            }
        }
    }
}
