using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
                VerticalAlignment = VerticalAlignment.Top,
                ClipToBounds = true
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

            var projectionNoticeOverlayContainer = new Grid
            {
                HorizontalAlignment = WpfHorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ClipToBounds = true,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };

            var projectionNoticeOverlayImage = new WpfImage
            {
                Stretch = Stretch.Fill,
                HorizontalAlignment = WpfHorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                CacheMode = new BitmapCache
                {
                    EnableClearType = false,
                    RenderAtScale = 1.0,
                    SnapsToDevicePixels = true
                }
            };
            RenderOptions.SetBitmapScalingMode(projectionNoticeOverlayImage, BitmapScalingMode.LowQuality);
            RenderOptions.SetCachingHint(projectionNoticeOverlayImage, CachingHint.Cache);
            projectionNoticeOverlayContainer.Children.Add(projectionNoticeOverlayImage);

            var projectionCaptionOverlayContainer = new Grid
            {
                HorizontalAlignment = WpfHorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ClipToBounds = true,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };

            var projectionCaptionBar = new Border
            {
                Background = new SolidColorBrush(WpfColor.FromArgb(206, 0, 0, 0)),
                BorderBrush = new SolidColorBrush(WpfColor.FromArgb(180, 33, 150, 243)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(18, 18, 18, 0),
                Padding = new Thickness(28, 16, 28, 16),
                HorizontalAlignment = WpfHorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top
            };

            var projectionCaptionOverlayText = new TextBlock
            {
                Text = string.Empty,
                FontFamily = new WpfFontFamily("Microsoft YaHei UI"),
                FontSize = 46,
                FontWeight = FontWeights.Bold,
                Foreground = WpfBrushes.White,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                LineHeight = 62
            };
            projectionCaptionBar.Child = projectionCaptionOverlayText;
            projectionCaptionOverlayContainer.Children.Add(projectionCaptionBar);

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
                Text = "音频",
                FontSize = 72,
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

            var projectionBiblePopupBorder = new Border
            {
                Background = new SolidColorBrush(WpfColor.FromArgb(255, 0, 0, 0)),
                BorderBrush = new SolidColorBrush(WpfColor.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(36, 24, 16, 24),
                Visibility = Visibility.Collapsed,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(30, 0, 30, 40),
                MaxWidth = 1500
            };
            WpfPanel.SetZIndex(projectionBiblePopupBorder, 120);

            var biblePopupGrid = new Grid();
            biblePopupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            biblePopupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var biblePopupTextPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Vertical
            };

            var projectionBiblePopupReferenceText = new TextBlock
            {
                Text = string.Empty,
                FontFamily = new WpfFontFamily("Microsoft YaHei UI"),
                FontSize = 42,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(255, 213, 79)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var projectionBiblePopupContentText = new TextBlock
            {
                Text = string.Empty,
                FontFamily = new WpfFontFamily("Microsoft YaHei UI"),
                FontSize = 56,
                FontWeight = FontWeights.Bold,
                Foreground = WpfBrushes.White,
                TextWrapping = TextWrapping.Wrap,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                LineHeight = 70
            };

            var projectionBiblePopupContentScrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = true,
                MaxHeight = 210,
                Content = projectionBiblePopupContentText
            };

            biblePopupTextPanel.Children.Add(projectionBiblePopupReferenceText);
            biblePopupTextPanel.Children.Add(projectionBiblePopupContentScrollViewer);
            Grid.SetColumn(biblePopupTextPanel, 0);
            biblePopupGrid.Children.Add(biblePopupTextPanel);

            var projectionBiblePopupCloseButton = new System.Windows.Controls.Button
            {
                Width = 36,
                Height = 36,
                Padding = new Thickness(0),
                HorizontalAlignment = WpfHorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(12, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = WpfBrushes.Transparent,
                BorderBrush = WpfBrushes.Transparent,
                BorderThickness = new Thickness(0),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            projectionBiblePopupCloseButton.Content = new System.Windows.Shapes.Path
            {
                Data = System.Windows.Media.Geometry.Parse("M18 6 L6 18 M6 6 L18 18"),
                Stroke = WpfBrushes.White,
                StrokeThickness = 2.2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Stretch = Stretch.None,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(projectionBiblePopupCloseButton, 1);
            biblePopupGrid.Children.Add(projectionBiblePopupCloseButton);

            projectionBiblePopupBorder.Child = biblePopupGrid;

            var mainGrid = new Grid
            {
                Background = WpfBrushes.Black,
                ClipToBounds = true
            };
            mainGrid.Children.Add(projectionScrollViewer);
            mainGrid.Children.Add(projectionVideoContainer);
            mainGrid.Children.Add(projectionNoticeOverlayContainer);
            mainGrid.Children.Add(projectionCaptionOverlayContainer);
            mainGrid.Children.Add(projectionBibleTitleBorder);
            // 弹窗必须挂在可视视口层（mainGrid），不能挂在可滚动内容层（projectionContainer）。
            // projectionContainer 高度会动态扩展为 imageHeight + viewportHeight，挂在其上会导致定位偏移/裁切。
            mainGrid.Children.Add(projectionBiblePopupBorder);
            projectionWindow.Content = mainGrid;

            return new ProjectionWindowLayout
            {
                Window = projectionWindow,
                ScrollViewer = projectionScrollViewer,
                ProjectionContainer = projectionContainer,
                ProjectionImageControl = projectionImageControl,
                ProjectionVisualBrushRect = projectionVisualBrushRect,
                ProjectionNoticeOverlayContainer = projectionNoticeOverlayContainer,
                ProjectionNoticeOverlayImage = projectionNoticeOverlayImage,
                ProjectionCaptionOverlayContainer = projectionCaptionOverlayContainer,
                ProjectionCaptionOverlayBorder = projectionCaptionBar,
                ProjectionCaptionOverlayText = projectionCaptionOverlayText,
                ProjectionVideoContainer = projectionVideoContainer,
                ProjectionVideoImage = projectionVideoImage,
                ProjectionVideoView = projectionVideoView,
                ProjectionMediaFileNameBorder = projectionMediaFileNameBorder,
                ProjectionMediaFileNameText = projectionMediaFileNameText,
                ProjectionBibleTitleBorder = projectionBibleTitleBorder,
                ProjectionBibleTitleText = projectionBibleTitleText,
                ProjectionBiblePopupBorder = projectionBiblePopupBorder,
                ProjectionBiblePopupReferenceText = projectionBiblePopupReferenceText,
                ProjectionBiblePopupContentScrollViewer = projectionBiblePopupContentScrollViewer,
                ProjectionBiblePopupContentText = projectionBiblePopupContentText,
                ProjectionBiblePopupCloseButton = projectionBiblePopupCloseButton
            };
        }
    }
}

