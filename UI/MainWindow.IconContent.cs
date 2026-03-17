using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Linq;
using ImageColorChanger.Core;
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
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            if (stroke != null)
            {
                path.Stroke = stroke;
            }
            else
            {
                path.SetResourceReference(Shape.StrokeProperty, "BrushIconDefault");
            }

            panel.Children.Add(path);

            panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            return panel;
        }

        private object BuildIconOnlyContent(string iconKey, MediaBrush stroke = null, double size = 16)
        {
            var path = new Path
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
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };

            if (stroke != null)
            {
                path.Stroke = stroke;
            }
            else
            {
                path.SetResourceReference(Shape.StrokeProperty, "BrushIconDefault");
            }

            return path;
        }

        private void SetProjectionButtonContent(bool isActive)
        {
            BtnProjection.Content = BuildIconLabelContent("IconLucideMonitor", isActive ? "结束" : "投影");
        }

        private void SetSplitStretchButtonContent(SplitImageDisplayMode mode)
        {
            string iconKey = mode switch
            {
                SplitImageDisplayMode.Fill => "IconLucideMaximize",
                SplitImageDisplayMode.FitTop => "IconLucideAlignStartVertical",
                _ => "IconLucideMinimize"
            };

            string label = mode switch
            {
                SplitImageDisplayMode.Fill => "拉伸",
                SplitImageDisplayMode.FitTop => "置顶",
                _ => "适中"
            };

            BtnSplitStretchMode.Content = BuildIconLabelContent(iconKey, label);
        }

        private void SetSlideOutputModeButtonContent(Database.Models.Enums.SlideOutputMode mode)
        {
            if (BtnSlideOutputMode == null)
            {
                return;
            }

            string label = mode == Database.Models.Enums.SlideOutputMode.Transparent ? "透明" : "普通";
            BtnSlideOutputMode.Content = BuildIconLabelContent("IconLucideSparkles", label);
            NormalizeSlideOutputModeTextStyle();
        }

        private void NormalizeSlideOutputModeTextStyle()
        {
            if (BtnSlideOutputMode?.Content is not StackPanel slidePanel)
            {
                return;
            }

            if (slidePanel.Children.OfType<TextBlock>().FirstOrDefault() is not TextBlock slideText)
            {
                return;
            }

            // 始终对齐“适中”按钮文字样式，避免“普通”按钮出现视觉不一致。
            slideText.FontFamily = BtnSplitStretchMode?.FontFamily ?? slideText.FontFamily;
            slideText.FontSize = BtnSplitStretchMode?.FontSize ?? slideText.FontSize;
            slideText.FontWeight = BtnSplitStretchMode?.FontWeight ?? slideText.FontWeight;
        }

        private void SetLockProjectionButtonContent(bool isLocked)
        {
            string iconKey = isLocked ? "IconLucideLock" : "IconLucideUnlock";
            MediaBrush stroke = System.Windows.Media.Brushes.White;
            BtnLockProjection.Content = BuildIconLabelContent(iconKey, "锁定", stroke);
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

        private void SetCompositePauseButtonContent(bool isPaused)
        {
            if (BtnCompositePause == null)
            {
                return;
            }

            string iconKey = isPaused ? "IconLucidePlay" : "IconLucidePause";
            string label = isPaused ? "继续" : "暂停";
            BtnCompositePause.Content = BuildIconLabelContent(iconKey, label, System.Windows.Media.Brushes.White);
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
