using ImageColorChanger.Managers;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// 基于 WPF MessageBox 的投影通知实现。
    /// </summary>
    public sealed class WpfProjectionUiNotifier : IProjectionUiNotifier
    {
        public void ShowMessage(string title, string message, ProjectionUiMessageLevel level = ProjectionUiMessageLevel.Info)
        {
            var icon = level switch
            {
                ProjectionUiMessageLevel.Error => System.Windows.MessageBoxImage.Error,
                ProjectionUiMessageLevel.Warning => System.Windows.MessageBoxImage.Warning,
                _ => System.Windows.MessageBoxImage.Information
            };

            System.Windows.MessageBox.Show(
                message ?? string.Empty,
                string.IsNullOrWhiteSpace(title) ? "提示" : title,
                System.Windows.MessageBoxButton.OK,
                icon);
        }
    }
}
