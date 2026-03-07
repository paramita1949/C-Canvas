namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 项目树：启动装配
    /// </summary>
    public partial class MainWindow
    {
        private void InitializeProjectTreeBootstrap()
        {
            ProjectTree.ItemsSource = _filteredProjectTreeItems;
            ProjectTree.PreviewMouseLeftButtonDown += ProjectTree_PreviewMouseLeftButtonDown;
            ProjectTree.PreviewMouseLeftButtonUp += ProjectTree_PreviewMouseLeftButtonUp;
            ProjectTree.PreviewMouseMove += ProjectTree_PreviewMouseMove;
            ProjectTree.Drop += ProjectTree_Drop;
            ProjectTree.DragOver += ProjectTree_DragOver;
            ProjectTree.DragLeave += ProjectTree_DragLeave;
            ProjectTree.AllowDrop = true;
        }
    }
}
