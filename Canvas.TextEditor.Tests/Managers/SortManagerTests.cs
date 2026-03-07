using ImageColorChanger.Managers;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Managers
{
    public sealed class SortManagerTests
    {
        [Fact]
        public void GetSortKey_WithHugePrefixNumber_DoesNotThrowAndClampsToIntMax()
        {
            var manager = new SortManager();

            var key = manager.GetSortKey("第99999999999999999999首 测试.mp4");

            Assert.Equal(int.MaxValue, key.prefixNumber);
        }

        [Fact]
        public void GetSortKey_WithHugeSuffixNumber_DoesNotThrowAndClampsToIntMax()
        {
            var manager = new SortManager();

            var key = manager.GetSortKey("测试_99999999999999999999.mp4");

            Assert.Equal(int.MaxValue, key.suffixNumber);
        }
    }
}

