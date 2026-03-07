using System.Linq;
using ImageColorChanger.Core;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Core
{
    public sealed class LyricsSlicePlannerTests
    {
        [Fact]
        public void BuildSegments_WithFixedLines_SplitsByGroupSize()
        {
            var lines = new[] { "a", "b", "c", "d", "e" };

            var segments = LyricsSlicePlanner.BuildSegments(lines, linesPerSlice: 2, useFreeCutPoints: false, cutPoints: null);

            Assert.Equal(3, segments.Count);
            Assert.Equal((1, 2, "a\nb"), (segments[0].StartLine, segments[0].EndLine, segments[0].Text.Replace("\r\n", "\n")));
            Assert.Equal((3, 4, "c\nd"), (segments[1].StartLine, segments[1].EndLine, segments[1].Text.Replace("\r\n", "\n")));
            Assert.Equal((5, 5, "e"), (segments[2].StartLine, segments[2].EndLine, segments[2].Text.Replace("\r\n", "\n")));
        }

        [Fact]
        public void BuildSegments_WithFreeCutPoints_SplitsAtCutEndLines()
        {
            var lines = new[] { "L1", "L2", "L3", "L4", "L5" };

            var segments = LyricsSlicePlanner.BuildSegments(lines, linesPerSlice: 2, useFreeCutPoints: true, cutPoints: new[] { 2, 4 });

            Assert.Equal(3, segments.Count);
            Assert.Equal((1, 2), (segments[0].StartLine, segments[0].EndLine));
            Assert.Equal((3, 4), (segments[1].StartLine, segments[1].EndLine));
            Assert.Equal((5, 5), (segments[2].StartLine, segments[2].EndLine));
        }

        [Fact]
        public void NormalizeCutPoints_RemovesInvalidAndDuplicateValues()
        {
            var points = LyricsSlicePlanner.NormalizeCutPoints(new[] { -1, 1, 2, 2, 5, 8 }, lineCount: 5);

            Assert.Equal(new[] { 1, 2 }, points.ToArray());
        }

        [Fact]
        public void BuildMarkedText_AddsMarkerOnlyOnCutPointLines()
        {
            var lines = new[] { "A", "B", "C", "D" };
            var text = LyricsSlicePlanner.BuildMarkedText(lines, new[] { 2, 3 }, markerPrefix: "● ");
            var rendered = text.Replace("\r\n", "\n");

            Assert.Equal("A\n● B\n● C\nD", rendered);
        }

        [Theory]
        [InlineData("1. Hello", "Hello")]
        [InlineData("12.   Hello", "Hello")]
        [InlineData("✂ Hello", "Hello")]
        [InlineData("🟡 Hello", "Hello")]
        [InlineData("● Hello", "Hello")]
        [InlineData("  ✂   Hello", "Hello")]
        [InlineData("Hello", "Hello")]
        public void StripDisplayPrefix_RemovesNumberAndMarkerPrefixes(string input, string expected)
        {
            var output = LyricsSlicePlanner.StripDisplayPrefix(input);

            Assert.Equal(expected, output);
        }
    }
}
