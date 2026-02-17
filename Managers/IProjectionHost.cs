namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 投影模块宿主能力接口，用于隔离 ProjectionManager 对 MainWindow 具体实现的依赖。
    /// </summary>
    public interface IProjectionHost
    {
        bool IsInLyricsMode { get; }

        void SwitchToPreviousSimilarImage();

        void SwitchToNextSimilarImage();

        void RecordProjectionSync();

        void ForwardProjectionKeyDown(System.Windows.Input.KeyEventArgs e);
    }
}
