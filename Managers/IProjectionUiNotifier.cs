namespace ImageColorChanger.Managers
{
    public enum ProjectionUiMessageLevel
    {
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// 投影模块UI通知接口，用于隔离 ProjectionManager 与具体UI框架。
    /// </summary>
    public interface IProjectionUiNotifier
    {
        void ShowMessage(string title, string message, ProjectionUiMessageLevel level = ProjectionUiMessageLevel.Info);
    }
}

