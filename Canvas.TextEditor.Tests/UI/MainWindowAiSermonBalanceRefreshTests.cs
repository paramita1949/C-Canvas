using System;
using ImageColorChanger.UI;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.UI
{
    public sealed class MainWindowAiSermonBalanceRefreshTests
    {
        [Fact]
        public void AiBalanceRefreshInterval_IsFiveMinutes()
        {
            Assert.Equal(TimeSpan.FromMinutes(5), MainWindow.AiBalanceRefreshInterval);
        }

        [Fact]
        public void AiAsrFlushInterval_PrioritizesRealtimeProcessing()
        {
            Assert.Equal(TimeSpan.FromMilliseconds(150), MainWindow.AiAsrFlushInterval);
        }
    }
}
