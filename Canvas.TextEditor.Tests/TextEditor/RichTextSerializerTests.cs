using System.Collections.Generic;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Services.TextEditor;
using ImageColorChanger.Services.TextEditor.Models;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.TextEditor
{
    public sealed class RichTextSerializerTests
    {
        [Fact]
        public void UpgradeToV2_AssignsParagraphAndRunIndexes_WhenContentMatches()
        {
            var serializer = new RichTextSerializer();
            var spans = new List<RichTextSpan>
            {
                new RichTextSpan { TextElementId = 9, SpanOrder = 0, Text = "甲" },
                new RichTextSpan { TextElementId = 9, SpanOrder = 1, Text = "乙" }
            };

            var upgraded = serializer.UpgradeToV2("甲\n乙", spans, 9);

            Assert.Equal(2, upgraded.Count);
            Assert.Equal(0, upgraded[0].ParagraphIndex);
            Assert.Equal(0, upgraded[0].RunIndex);
            Assert.Equal("甲", upgraded[0].Text);
            Assert.Equal(1, upgraded[1].ParagraphIndex);
            Assert.Equal(0, upgraded[1].RunIndex);
            Assert.Equal("乙", upgraded[1].Text);
            Assert.All(upgraded, span =>
                Assert.Equal(RichTextDocumentV2.CurrentFormatVersion, span.FormatVersion));
        }

        [Fact]
        public void UpgradeToV2_FallsBackToSingleParagraph_WhenContentDoesNotMatch()
        {
            var serializer = new RichTextSerializer();
            var spans = new List<RichTextSpan>
            {
                new RichTextSpan { TextElementId = 10, SpanOrder = 0, Text = "A" },
                new RichTextSpan { TextElementId = 10, SpanOrder = 1, Text = "B" }
            };

            var upgraded = serializer.UpgradeToV2("AB-C", spans, 10);

            Assert.Equal(2, upgraded.Count);
            Assert.Equal(0, upgraded[0].ParagraphIndex);
            Assert.Equal(0, upgraded[0].RunIndex);
            Assert.Equal(0, upgraded[1].ParagraphIndex);
            Assert.Equal(1, upgraded[1].RunIndex);
            Assert.All(upgraded, span =>
                Assert.Equal(RichTextDocumentV2.CurrentFormatVersion, span.FormatVersion));
        }
    }
}
