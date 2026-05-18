using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using ImageColorChanger.UI;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Ui
{
    public sealed class LiveCaptionOverlayWindowActionTests
    {
        [Fact]
        public void Constructor_ShouldExposeOneUnifiedStyleSettingsAction()
        {
            RunInSta(() =>
            {
                var window = new LiveCaptionOverlayWindow();
                try
                {
                    IReadOnlyList<string> labels = FindTextBlocks(window)
                        .Select(text => text.Text)
                        .Where(text => !string.IsNullOrWhiteSpace(text))
                        .ToArray();

                    Assert.Contains("样式设置", labels);
                    Assert.DoesNotContain("本机样式", labels);
                    Assert.DoesNotContain("投影样式", labels);
                    Assert.DoesNotContain("NDI样式", labels);
                }
                finally
                {
                    window.Close();
                }
            });
        }

        private static IEnumerable<TextBlock> FindTextBlocks(DependencyObject root)
        {
            if (root == null)
            {
                yield break;
            }

            if (root is TextBlock textBlock)
            {
                yield return textBlock;
            }

            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is DependencyObject dependencyObject)
                {
                    foreach (TextBlock nested in FindTextBlocks(dependencyObject))
                    {
                        yield return nested;
                    }
                }
            }
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
