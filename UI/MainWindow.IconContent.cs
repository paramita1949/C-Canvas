using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ImageColorChanger.Managers;
using MediaBrush = System.Windows.Media.Brush;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private object BuildIconLabelContent(string iconKey, string label, MediaBrush stroke = null)
        {
            var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            var path = new Path
            {
                Data = FindResource(iconKey) as Geometry,
                Width = 16,
                Height = 16,
                Stretch = Stretch.Uniform,
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Fill = System.Windows.Media.Brushes.Transparent,
                Stroke = stroke ?? (FindResource("BrushIconDefault") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Black),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };

            panel.Children.Add(path);
            panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            return panel;
        }

        private object BuildIconOnlyContent(string iconKey, MediaBrush stroke = null, double size = 16)
        {
            return new Path
            {
                Data = FindResource(iconKey) as Geometry,
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Fill = System.Windows.Media.Brushes.Transparent,
                Stroke = stroke ?? (FindResource("BrushIconDefault") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Black),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
        }

        private void SetProjectionButtonContent(bool isActive)
        {
            BtnProjection.Content = BuildIconLabelContent("IconLucideMonitor", isActive ? "结束" : "投影");
        }

        private void SetSplitStretchButtonContent(bool isStretch)
        {
            string iconKey = isStretch ? "IconLucideMaximize" : "IconLucideMinimize";
            BtnSplitStretchMode.Content = BuildIconLabelContent(iconKey, isStretch ? "拉伸" : "适中");
        }

        private void SetLockProjectionButtonContent(bool isLocked)
        {
            string iconKey = isLocked ? "IconLucideLock" : "IconLucideUnlock";
            MediaBrush stroke = System.Windows.Media.Brushes.White;
            BtnLockProjection.Content = BuildIconLabelContent(iconKey, "锁定投影", stroke);
        }

        private void SetCompositePlayButtonContent(bool isPlaying)
        {
            string iconKey = isPlaying ? "IconLucideX" : "IconLucideClapperboard";
            string label = isPlaying ? "停止" : "合成播放";
            BtnFloatingCompositePlay.Content = BuildIconLabelContent(iconKey, label, System.Windows.Media.Brushes.White);
        }

        private void SetCompositeSpeedButtonContent(double speed)
        {
            BtnCompositeSpeed.Content = BuildIconLabelContent("IconLucideZap", $"{speed:F2}x", System.Windows.Media.Brushes.White);
        }

        private void SetRecordButtonContent(bool isRecording)
        {
            BtnRecord.Content = BuildIconLabelContent(isRecording ? "IconLucideStop" : "IconLucideDisc", isRecording ? "停止" : "录制");
        }

        private void SetPlayButtonContent(bool isPlaying)
        {
            BtnPlay.Content = BuildIconLabelContent(isPlaying ? "IconLucideStop" : "IconLucidePlay", isPlaying ? "停止" : "播放");
        }

        private void SetPauseResumeButtonContent(bool isPaused)
        {
            BtnPauseResume.Content = BuildIconLabelContent(isPaused ? "IconLucidePlay" : "IconLucidePause", isPaused ? "继续" : "暂停");
        }

        private void SetMediaPlayPauseButtonContent(bool isPlaying)
        {
            BtnMediaPlayPause.Content = BuildIconOnlyContent(isPlaying ? "IconLucidePause" : "IconLucidePlay", System.Windows.Media.Brushes.White, 20);
        }

        private void SetMediaPlayModeButtonContent(PlayMode mode)
        {
            string iconKey = mode switch
            {
                PlayMode.Sequential => "IconLucidePlay",
                PlayMode.Random => "IconLucideShuffle",
                PlayMode.LoopOne => "IconLucideRepeat1",
                _ => "IconLucideRepeat"
            };

            BtnPlayMode.Content = BuildIconOnlyContent(iconKey, System.Windows.Media.Brushes.White, 18);
        }

        private void SetPlayCountButtonContent(string countText)
        {
            BtnPlayCount.Content = BuildIconLabelContent("IconLucideRepeat", $"{countText}次");
        }
    }
}
