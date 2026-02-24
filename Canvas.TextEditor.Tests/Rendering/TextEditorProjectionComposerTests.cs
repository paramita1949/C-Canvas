using ImageColorChanger.Services.TextEditor.Rendering;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Rendering
{
    public sealed class TextEditorProjectionComposerTests
    {
        [Fact]
        public void Compose_ShowsHint_WhenProjectionIsNotActive()
        {
            var composer = new TextEditorProjectionComposer();
            int hintCount = 0;
            int updateCount = 0;

            composer.Compose(new TextEditorProjectionComposeRequest
            {
                IsProjectionActive = false,
                ShowProjectionNotActiveHint = () => hintCount++,
                UpdateProjectionContent = () => updateCount++
            });

            Assert.Equal(1, hintCount);
            Assert.Equal(0, updateCount);
        }

        [Fact]
        public void Compose_UpdatesImmediately_WhenAnimationDisabled()
        {
            var composer = new TextEditorProjectionComposer();
            int updateCount = 0;

            composer.Compose(new TextEditorProjectionComposeRequest
            {
                IsProjectionActive = true,
                AnimationEnabled = false,
                UpdateProjectionContent = () => updateCount++
            });

            Assert.Equal(1, updateCount);
        }
    }
}
