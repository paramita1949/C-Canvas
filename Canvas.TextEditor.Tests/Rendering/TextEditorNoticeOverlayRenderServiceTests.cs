using ImageColorChanger.Services.TextEditor.Rendering;
using ImageColorChanger.UI.Controls;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Rendering
{
    public sealed class TextEditorNoticeOverlayRenderServiceTests
    {
        [Fact]
        public void ExecuteSafely_Should_DelegateToRenderSafetyService()
        {
            var renderSafety = new FakeRenderSafetyService();
            var service = new TextEditorNoticeOverlayRenderService(renderSafety);
            int renderCallCount = 0;

            int result = service.ExecuteSafely(
                textBoxes: null,
                renderFunc: () =>
                {
                    renderCallCount++;
                    return 7;
                });

            Assert.Equal(1, renderSafety.ExecuteCallCount);
            Assert.Equal(1, renderCallCount);
            Assert.Equal(7, result);
        }

        private sealed class FakeRenderSafetyService : ITextEditorRenderSafetyService
        {
            public int ExecuteCallCount { get; private set; }

            public void Execute(
                IEnumerable<DraggableTextBox> textBoxes,
                Action renderAction,
                Action beforeRenderAction = null,
                Action afterRenderAction = null)
            {
                ExecuteCallCount++;
                beforeRenderAction?.Invoke();
                renderAction?.Invoke();
                afterRenderAction?.Invoke();
            }

            public T Execute<T>(
                IEnumerable<DraggableTextBox> textBoxes,
                Func<T> renderFunc,
                Action beforeRenderAction = null,
                Action afterRenderAction = null)
            {
                ExecuteCallCount++;
                beforeRenderAction?.Invoke();
                try
                {
                    return renderFunc();
                }
                finally
                {
                    afterRenderAction?.Invoke();
                }
            }
        }
    }
}
