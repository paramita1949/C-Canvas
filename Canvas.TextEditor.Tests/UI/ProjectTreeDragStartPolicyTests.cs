using ImageColorChanger.UI.Modules;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Ui
{
    public sealed class ProjectTreeDragStartPolicyTests
    {
        [Fact]
        public void ShouldStartDrag_WhenPressedLessThanDelay_ReturnsFalse()
        {
            bool actual = ProjectTreeDragStartPolicy.ShouldStartDrag(
                elapsedMilliseconds: 250,
                horizontalDistance: 40,
                verticalDistance: 0,
                minimumHorizontalDistance: 4,
                minimumVerticalDistance: 4);

            Assert.False(actual);
        }

        [Fact]
        public void ShouldStartDrag_WhenDelayElapsedAndDistanceReached_ReturnsTrue()
        {
            bool actual = ProjectTreeDragStartPolicy.ShouldStartDrag(
                elapsedMilliseconds: 300,
                horizontalDistance: 0,
                verticalDistance: 8,
                minimumHorizontalDistance: 4,
                minimumVerticalDistance: 4);

            Assert.True(actual);
        }

        [Fact]
        public void ShouldStartDrag_WhenDelayElapsedButDistanceTooSmall_ReturnsFalse()
        {
            bool actual = ProjectTreeDragStartPolicy.ShouldStartDrag(
                elapsedMilliseconds: 500,
                horizontalDistance: 2,
                verticalDistance: 2,
                minimumHorizontalDistance: 4,
                minimumVerticalDistance: 4);

            Assert.False(actual);
        }
    }
}
