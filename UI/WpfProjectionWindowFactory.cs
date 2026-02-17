using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImageColorChanger.Managers;
using LibVLCSharp.WPF;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfImage = System.Windows.Controls.Image;
using WpfPanel = System.Windows.Controls.Panel;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// WPF 投影窗口工厂。
    /// </summary>
    public sealed class WpfProjectionWindowFactory : IProjectionWindowFactory
    {
        public ProjectionWindowLayout CreateProjectionWindow(Action<VideoView> onVideoViewLoaded)
        {
            var projectionWindow = new Window
            {
                Title = "投影",
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                WindowState = WindowState.Normal,
                Background = WpfBrushes.Black,
                Topmost = true,
                ShowInTaskbar = false
            };

            var projectionScrollViewer = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                Background = WpfBrushes.Black,
                CanContentScroll = false
            };

            var projectionContainer = new Grid
            {
                Background = WpfBrushes.Black,
                HorizontalAlignment = WpfHorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top
            };

            var projectionImageControl = new WpfImage
            {
                Stretch = Stretch.None,
                HorizontalAlignment = WpfHorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };

            var projectionVisualBrushRect = new System.Windows.Shapes.Rectangle
            {
                Stretch = Stretch.Fill,
                HorizontalAlignment = WpfHorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Visibility = Visibility.Collapsed,
                CacheMode = new BitmapCache
                {
                    EnableClearType = false,
                    RenderAtScale = 1.0,
                    SnapsToDevicePixels = true
                }
            };

            projectionContainer.Children.Add(projectionVisualBrushRect);
            projectionContainer.Children.Add(projectionImageControl);
            projectionScrollViewer.Content = projectionContainer;

            var projectionVideoContainer = new Grid
            {
                Background = WpfBrushes.Black,
                Visibility = Visibility.Collapsed,
                HorizontalAlignment = WpfHorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var projectionVideoImage = new WpfImage
            {
                HorizontalAlignment = WpfHorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Stretch = Stretch.Uniform,
                CacheMode = new BitmapCache
                {
                    EnableClearType = false,
                    RenderAtScale = 1.0,
                    SnapsToDevicePixels = true
                }
            };

            RenderOptions.SetBitmapScalingMode(projectionVideoImage, BitmapScalingMode.Linear);
            RenderOptions.SetCachingHint(projectionVideoImage, CachingHint.Cache);
            projectionVideoContainer.Children.Add(projectionVideoImage);

            var projectionVideoView = new VideoView
            {
                HorizontalAlignment = WpfHorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            projectionVideoView.Loaded += (s, e) => onVideoViewLoaded?.Invoke(projectionVideoView);
            projectionVideoContainer.Children.Add(projectionVideoView);

            var projectionMediaFileNameBorder = new Grid
            {
                Background = WpfBrushes.Black,
                Visibility = Visibility.Collapsed
            };

            var fileNameStack = new StackPanel
            {
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var iconText = new TextBlock
            {
                Text = "🎵",
                FontSize = 120,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 30),
                Foreground = WpfBrushes.White
            };

            var projectionMediaFileNameText = new TextBlock
            {
                Text = "媒体文件",
                FontSize = 42,
                FontWeight = FontWeights.Medium,
                Foreground = WpfBrushes.White,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                MaxWidth = 1200,
                Padding = new Thickness(20, 0, 20, 0)
            };

            fileNameStack.Children.Add(iconText);
            fileNameStack.Children.Add(projectionMediaFileNameText);
            projectionMediaFileNameBorder.Children.Add(fileNameStack);
            projectionVideoContainer.Children.Add(projectionMediaFileNameBorder);

            var projectionBibleTitleBorder = new Border
            {
                Background = new SolidColorBrush(WpfColor.FromRgb(28, 28, 28)),
                Padding = new Thickness(20, 15, 20, 15),
                Visibility = Visibility.Collapsed,
                VerticalAlignment = VerticalAlignment.Top
            };
            WpfPanel.SetZIndex(projectionBibleTitleBorder, 100);

            var projectionBibleTitleText = new TextBlock
            {
                Text = string.Empty,
                FontFamily = new WpfFontFamily("Microsoft YaHei UI"),
                FontSize = 32,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(255, 87, 34))
            };
            projectionBibleTitleBorder.Child = projectionBibleTitleText;

            var mainGrid = new Grid
            {
                Background = WpfBrushes.Black
            };
            mainGrid.Children.Add(projectionScrollViewer);
            mainGrid.Children.Add(projectionVideoContainer);
            mainGrid.Children.Add(projectionBibleTitleBorder);
            projectionWindow.Content = mainGrid;

            return new ProjectionWindowLayout
            {
                Window = projectionWindow,
                ScrollViewer = projectionScrollViewer,
                ProjectionContainer = projectionContainer,
                ProjectionImageControl = projectionImageControl,
                ProjectionVisualBrushRect = projectionVisualBrushRect,
                ProjectionVideoContainer = projectionVideoContainer,
                ProjectionVideoImage = projectionVideoImage,
                ProjectionVideoView = projectionVideoView,
                ProjectionMediaFileNameBorder = projectionMediaFileNameBorder,
                ProjectionMediaFileNameText = projectionMediaFileNameText,
                ProjectionBibleTitleBorder = projectionBibleTitleBorder,
                ProjectionBibleTitleText = projectionBibleTitleText
            };
        }
    }
}
