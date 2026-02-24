using System.Collections.Generic;
using ImageColorChanger.Services.TextEditor.Rendering;
using ImageColorChanger.UI.Controls;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Rendering
{
    public sealed class TextEditorRenderSafetyServiceTests
    {
        [Fact]
        public void Execute_InvokesBeforeRenderAction_AndAfterRenderAction()
        {
            var service = new TextEditorRenderSafetyService();
            var textBoxes = new List<DraggableTextBox>();
            int beforeCount = 0;
            int renderCount = 0;
            int afterCount = 0;

            service.Execute(
                textBoxes,
                () => { renderCount++; },
                beforeRenderAction: () => { beforeCount++; },
                afterRenderAction: () => { afterCount++; });

            Assert.Equal(1, beforeCount);
            Assert.Equal(1, renderCount);
            Assert.Equal(1, afterCount);
        }
    }
}
