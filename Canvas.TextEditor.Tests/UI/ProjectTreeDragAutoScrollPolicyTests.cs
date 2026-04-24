using ImageColorChanger.UI.Modules;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Ui
{
    public sealed class ProjectTreeDragAutoScrollPolicyTests
    {
        [Fact]
        public void ComputeVerticalScrollDelta_WhenCursorNearTop_ReturnsNegativeDelta()
        {
            double delta = ProjectTreeDragAutoScrollPolicy.ComputeVerticalScrollDelta(
                cursorY: 4,
                viewportHeight: 300);

            Assert.True(delta < 0);
        }

        [Fact]
        public void ComputeVerticalScrollDelta_WhenCursorNearBottom_ReturnsPositiveDelta()
        {
            double delta = ProjectTreeDragAutoScrollPolicy.ComputeVerticalScrollDelta(
                cursorY: 296,
                viewportHeight: 300);

            Assert.True(delta > 0);
        }

        [Fact]
        public void ComputeVerticalScrollDelta_WhenCursorInSafeZone_ReturnsZero()
        {
            double delta = ProjectTreeDragAutoScrollPolicy.ComputeVerticalScrollDelta(
                cursorY: 150,
                viewportHeight: 300);

            Assert.Equal(0, delta);
        }

        [Fact]
        public void ComputeVerticalScrollDelta_WhenCursorOutsideTop_ClampsToMaxStep()
        {
            double delta = ProjectTreeDragAutoScrollPolicy.ComputeVerticalScrollDelta(
                cursorY: -20,
                viewportHeight: 300,
                edgeThreshold: 30,
                maxStep: 24);

            Assert.Equal(-24, delta);
        }
    }
}
