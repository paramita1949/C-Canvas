namespace ImageColorChanger.Services.Interfaces
{
    /// <summary>
    /// UI 设置存储抽象，避免控件直接依赖 DatabaseManager。
    /// </summary>
    public interface IUiSettingsStore
    {
        string GetValue(string key);
        void SaveValue(string key, string value);
    }
}
