using System;
using ImageColorChanger.Utils;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private void MainWindow_ContentRendered(object sender, EventArgs e)
        {
            StartupPerfLogger.Mark("MainWindow.ContentRendered");
        }
    }
}
