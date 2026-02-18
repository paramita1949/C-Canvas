using System;
using System.Windows;
using ImageColorChanger.Core;
using ImageColorChanger.Managers;
using ImageColorChanger.Services;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 投影管理器装配
    /// </summary>
    public partial class MainWindow
    {
        private sealed class ProjectionManagerOptions
        {
            public required Window MainWindow { get; init; }
            public required System.Windows.Controls.ScrollViewer MainScrollViewer { get; init; }
            public required System.Windows.Controls.Image MainImageControl { get; init; }
            public required ImageProcessor ImageProcessor { get; init; }
            public required GPUContext GpuContext { get; init; }
            public required System.Windows.Controls.ComboBox ScreenComboBox { get; init; }
            public required IProjectionAuthPolicy AuthPolicy { get; init; }
            public required IProjectionUiNotifier UiNotifier { get; init; }
            public required IProjectionHost Host { get; init; }
            public required IProjectionWindowFactory WindowFactory { get; init; }
        }

        private ProjectionManagerOptions BuildProjectionManagerOptions()
        {
            return new ProjectionManagerOptions
            {
                MainWindow = this,
                MainScrollViewer = ImageScrollViewer,
                MainImageControl = ImageDisplay,
                ImageProcessor = _imageProcessor ?? throw new InvalidOperationException("ImageProcessor 未初始化"),
                GpuContext = _gpuContext ?? throw new InvalidOperationException("GPUContext 未初始化"),
                ScreenComboBox = ScreenSelector,
                AuthPolicy = new AuthServiceProjectionAuthPolicy(_authService),
                UiNotifier = new WpfProjectionUiNotifier(),
                Host = new DelegateProjectionHost(
                    () => IsInLyricsMode,
                    SwitchToPreviousSimilarImage,
                    SwitchToNextSimilarImage,
                    () => _fpsMonitor?.RecordProjectionSync(),
                    ForwardProjectionKeyDownFromProjection),
                WindowFactory = new WpfProjectionWindowFactory()
            };
        }

        private ProjectionManager CreateProjectionManager()
        {
            var options = BuildProjectionManagerOptions();
            return new ProjectionManager(
                options.MainWindow,
                options.MainScrollViewer,
                options.MainImageControl,
                options.ImageProcessor,
                options.GpuContext,
                options.ScreenComboBox,
                options.AuthPolicy,
                options.UiNotifier,
                options.Host,
                options.WindowFactory,
                null);
        }
    }
}
