using ImageColorChanger.UI.Modules;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow Lyrics Host Callbacks
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// 当图片切换时调用（供主窗口调用）
        /// 如果在歌词模式，自动切换到新图片的歌词
        /// </summary>
        public void OnImageChanged()
        {
            EnsureLyricsModuleController();
            _lyricsModuleController.OnImageChanged();
        }

        /// <summary>
        /// 图片切换时的回调（在歌词模式下调用）
        /// 保存当前歌词，加载新图片的歌词，更新投影
        /// </summary>
        public void OnImageChangedInLyricsMode()
        {
            EnsureLyricsModuleController();
            _lyricsModuleController.OnImageChangedInLyricsMode();
        }

        /// <summary>
        /// 投影状态改变时的回调（供主窗口调用）
        /// 当投影开启时，如果在歌词模式，自动投影歌词
        /// </summary>
        public void OnProjectionStateChanged(bool isProjecting)
        {
            EnsureLyricsModuleController();
            _lyricsModuleController.OnProjectionStateChanged(isProjecting);
        }

        private void EnsureLyricsModuleController()
        {
            _lyricsModuleController ??= new LyricsModuleController(
                Dispatcher,
                () => _isLyricsMode,
                () => _projectionManager != null && _projectionManager.IsProjecting,
                SaveLyricsProject,
                LoadOrCreateLyricsProject,
                RenderLyricsToProjection,
                () => _projectionManager?.ClearImageState());
        }
    }
}
