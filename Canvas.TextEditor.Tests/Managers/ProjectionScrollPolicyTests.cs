using ImageColorChanger.Managers;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Managers
{
    public sealed class ProjectionScrollPolicyTests
    {
        [Fact]
        public void CalculateMainScrollableRatio_WhenScrollableHeightIsZero_ReturnsZero()
        {
            var ratio = ProjectionScrollPolicy.CalculateMainScrollableRatio(120, 0);
            Assert.Equal(0, ratio);
        }

        [Fact]
        public void CalculateMainScrollableRatio_WhenOffsetExceedsRange_IsClampedToOne()
        {
            var ratio = ProjectionScrollPolicy.CalculateMainScrollableRatio(2500, 1000);
            Assert.Equal(1, ratio);
        }

        [Fact]
        public void CalculateByScrollableHeights_UsesUnifiedScrollableRatio()
        {
            var projectionOffset = ProjectionScrollPolicy.CalculateByScrollableHeights(
                mainScrollTop: 300,
                mainScrollableHeight: 1200,
                projectionScrollableHeight: 2000);

            Assert.Equal(500, projectionOffset);
        }
    }
}
