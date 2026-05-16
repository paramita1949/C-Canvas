using System.Text.RegularExpressions;
using ImageColorChanger.Services.Ai;

namespace Canvas.TextEditor.Tests.Ai
{
    public sealed class AiSermonSpeakerNamingTests
    {
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("未标记讲师")]
        public void ResolveSpeakerNameForSession_GeneratesPreacherNameWhenUnset(string input)
        {
            string name = AiSermonConversationCoordinator.ResolveSpeakerNameForSession(input);

            Assert.Matches(new Regex("^传道人[0-9]{8}$"), name);
        }

        [Fact]
        public void ResolveSpeakerNameForSession_KeepsExplicitName()
        {
            string name = AiSermonConversationCoordinator.ResolveSpeakerNameForSession("A");

            Assert.Equal("A", name);
        }
    }
}
