using System.Reflection;
using ImageColorChanger.Managers;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.TextEditor
{
    public sealed class HdpFormatCompatibilityTests
    {
        [Fact]
        public void TextElementData_ShouldContain_TextVerticalAlign_Field()
        {
            PropertyInfo property = typeof(TextElementData).GetProperty("TextVerticalAlign");
            Assert.NotNull(property);
            Assert.Equal(typeof(string), property.PropertyType);
        }
    }
}
