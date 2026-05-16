using ImageColorChanger.Services.Ai;

namespace Canvas.TextEditor.Tests.Ai
{
    public sealed class AiSermonCacheLayoutDebugTests
    {
        [Fact]
        public void IsCacheLayoutDebugOutputEnabled_DefaultsToFalse()
        {
            Assert.False(AiSermonConversationCoordinator.IsCacheLayoutDebugOutputEnabled(null));
            Assert.False(AiSermonConversationCoordinator.IsCacheLayoutDebugOutputEnabled(string.Empty));
            Assert.False(AiSermonConversationCoordinator.IsCacheLayoutDebugOutputEnabled("0"));
        }

        [Fact]
        public void IsCacheLayoutDebugOutputEnabled_RequiresExplicitOptIn()
        {
            Assert.True(AiSermonConversationCoordinator.IsCacheLayoutDebugOutputEnabled("1"));
        }
    }
}
