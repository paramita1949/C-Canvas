using ImageColorChanger.Core;
using ImageColorChanger.Managers;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Managers
{
    public sealed class ProjectionLayoutPolicyTests
    {
        [Fact]
        public void CalculateImageMargin_NormalMode_AlwaysReturnsTopLeftOrigin()
        {
            var margin = ProjectionLayoutPolicy.CalculateImageMargin(
                imageWidth: 1200,
                imageHeight: 1800,
                containerWidth: 1920,
                containerHeight: 1080,
                isOriginalMode: false,
                originalDisplayMode: OriginalDisplayMode.Fit);

            Assert.Equal(0, margin.Left);
            Assert.Equal(0, margin.Top);
        }
    }
}
