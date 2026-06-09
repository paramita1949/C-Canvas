using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using ImageColorChanger.UI.Modules;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Ui
{
    public sealed class TopMenuButtonNormalizerTests
    {
        [Fact]
        public void NormalizeButtons_ResetsLocalSizeOverridesToSharedTopMenuMetrics()
        {
            RunInSta(() =>
            {
                var regularButton = new Button();
                var oversizedButton = new Button
                {
                    Width = 140,
                    MinWidth = 92,
                    Height = 48,
                    Padding = new Thickness(20, 8, 20, 8),
                    Margin = new Thickness(12, 0, 12, 0),
                    FontSize = 28
                };

                TopMenuButtonNormalizer.NormalizeButtons(
                    new[] { regularButton, oversizedButton },
                    fontSize: 21);

                Assert.True(double.IsNaN(regularButton.Width));
                Assert.True(double.IsNaN(oversizedButton.Width));
                Assert.Equal(60, regularButton.MinWidth);
                Assert.Equal(60, oversizedButton.MinWidth);
                Assert.Equal(32, regularButton.Height);
                Assert.Equal(32, oversizedButton.Height);
                Assert.Equal(new Thickness(8, 4, 8, 4), regularButton.Padding);
                Assert.Equal(new Thickness(8, 4, 8, 4), oversizedButton.Padding);
                Assert.Equal(new Thickness(3, 0, 3, 0), regularButton.Margin);
                Assert.Equal(new Thickness(3, 0, 3, 0), oversizedButton.Margin);
                Assert.Equal(21, regularButton.FontSize);
                Assert.Equal(21, oversizedButton.FontSize);
                Assert.Equal(VerticalAlignment.Center, regularButton.VerticalAlignment);
                Assert.Equal(VerticalAlignment.Center, oversizedButton.VerticalAlignment);
            });
        }

        private static void RunInSta(Action action)
        {
            Exception captured = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (captured != null)
            {
                ExceptionDispatchInfo.Capture(captured).Throw();
            }
        }
    }
}
