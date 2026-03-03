using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Repositories.TextEditor;
using ImageColorChanger.Services.TextEditor;
using ImageColorChanger.Services.TextEditor.Models;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.TextEditor
{
    public sealed class TextElementPersistenceServiceTests
    {
        [Fact]
        public async Task SaveAsync_UpdatesElements_AndSavesRichTextSpans()
        {
            var elementRepo = new FakeTextElementRepository();
            var spanRepo = new FakeRichTextSpanRepository();
            var sessionService = new FakeEditSessionService();
            var serializer = new RichTextSerializer();
            var service = new TextElementPersistenceService(elementRepo, spanRepo, sessionService, serializer);

            var element = new TextElement
            {
                Id = 42,
                Content = "old",
                FontFamily = "Microsoft YaHei",
                FontSize = 60
            };
            var snapshot = new TextBoxSnapshot(
                element,
                "新内容",
                new List<RichTextSpan>
                {
                    new RichTextSpan { TextElementId = 42, SpanOrder = 0, Text = "新内容" }
                },
                TextLayoutProfile.Default,
                wasInEditMode: true);

            await service.SaveAsync(new[] { snapshot });

            Assert.Equal(1, elementRepo.UpdateRangeCallCount);
            Assert.Single(elementRepo.UpdatedElements);
            Assert.Equal("新内容", elementRepo.UpdatedElements[0].Content);
            Assert.Single(spanRepo.SaveForElementCalls);
            Assert.Equal(42, spanRepo.SaveForElementCalls[0].TextElementId);
            Assert.Single(spanRepo.SaveForElementCalls[0].Spans);
            Assert.Equal("新内容", spanRepo.SaveForElementCalls[0].Spans[0].Text);
            Assert.Empty(spanRepo.DeletedByElementIdCalls);
            Assert.Contains(42, sessionService.EditedElementIds);
        }

        [Fact]
        public async Task SaveAsync_DeletesRichTextSpans_WhenNoSpansProvided()
        {
            var elementRepo = new FakeTextElementRepository();
            var spanRepo = new FakeRichTextSpanRepository();
            var sessionService = new FakeEditSessionService();
            var serializer = new RichTextSerializer();
            var service = new TextElementPersistenceService(elementRepo, spanRepo, sessionService, serializer);

            var element = new TextElement { Id = 9, Content = "x" };
            var snapshot = new TextBoxSnapshot(
                element,
                "x",
                new List<RichTextSpan>(),
                TextLayoutProfile.Default,
                wasInEditMode: false);

            await service.SaveAsync(new[] { snapshot });

            Assert.Empty(spanRepo.SaveForElementCalls);
            Assert.Single(spanRepo.DeletedByElementIdCalls);
            Assert.Equal(9, spanRepo.DeletedByElementIdCalls[0]);
        }

        [Fact]
        public async Task SaveAsync_Should_KeepComponentFields()
        {
            var elementRepo = new FakeTextElementRepository();
            var spanRepo = new FakeRichTextSpanRepository();
            var sessionService = new FakeEditSessionService();
            var serializer = new RichTextSerializer();
            var service = new TextElementPersistenceService(elementRepo, spanRepo, sessionService, serializer);

            var element = new TextElement
            {
                Id = 100,
                Content = "notice",
                ComponentType = "Notice",
                ComponentConfigJson = "{\"durationMinutes\":3}"
            };

            var snapshot = new TextBoxSnapshot(
                element,
                "notice-updated",
                new List<RichTextSpan>(),
                TextLayoutProfile.Default,
                wasInEditMode: false);

            await service.SaveAsync(new[] { snapshot });

            Assert.Single(elementRepo.UpdatedElements);
            Assert.Equal("Notice", elementRepo.UpdatedElements[0].ComponentType);
            Assert.Equal("{\"durationMinutes\":3}", elementRepo.UpdatedElements[0].ComponentConfigJson);
        }

        private sealed class FakeTextElementRepository : ITextElementRepository
        {
            public int UpdateRangeCallCount { get; private set; }

            public IReadOnlyList<TextElement> UpdatedElements { get; private set; } = Array.Empty<TextElement>();

            public Task<TextElement> AddAsync(TextElement element) => throw new NotImplementedException();

            public Task DeleteAsync(int elementId) => throw new NotImplementedException();

            public Task DeleteByProjectAsync(int projectId) => throw new NotImplementedException();

            public Task<List<TextElement>> GetByProjectAsync(int projectId) => throw new NotImplementedException();

            public Task<List<TextElement>> GetBySlideWithRichTextAsync(int slideId) => throw new NotImplementedException();

            public Task RebindProjectElementsToSlideAsync(int projectId, int targetSlideId) => throw new NotImplementedException();

            public Task UpdateAsync(TextElement element) => throw new NotImplementedException();

            public Task UpdateRangeAsync(IEnumerable<TextElement> elements)
            {
                UpdateRangeCallCount++;
                UpdatedElements = elements?.ToList() ?? new List<TextElement>();
                return Task.CompletedTask;
            }
        }

        private sealed class FakeRichTextSpanRepository : IRichTextSpanRepository
        {
            public List<(int TextElementId, IReadOnlyList<RichTextSpan> Spans)> SaveForElementCalls { get; } = new List<(int, IReadOnlyList<RichTextSpan>)>();

            public List<int> DeletedByElementIdCalls { get; } = new List<int>();

            public Task<RichTextSpan> AddAsync(RichTextSpan span) => throw new NotImplementedException();

            public Task DeleteByTextElementIdAsync(int textElementId)
            {
                DeletedByElementIdCalls.Add(textElementId);
                return Task.CompletedTask;
            }

            public Task<List<RichTextSpan>> GetByTextElementIdAsync(int textElementId) => throw new NotImplementedException();

            public Task SaveForTextElementAsync(int textElementId, IEnumerable<RichTextSpan> spans)
            {
                SaveForElementCalls.Add((textElementId, spans?.ToList() ?? new List<RichTextSpan>()));
                return Task.CompletedTask;
            }
        }

        private sealed class FakeEditSessionService : ITextBoxEditSessionService
        {
            public List<int> EditedElementIds { get; } = new List<int>();

            public IDisposable BeginSaving(IEnumerable<int> textElementIds)
            {
                return new SaveScope();
            }

            public TextBoxEditSessionState GetState(int textElementId)
            {
                return TextBoxEditSessionState.Idle;
            }

            public void SetEditing(int textElementId, bool isEditing)
            {
                EditedElementIds.Add(textElementId);
            }

            public void SetSelected(int textElementId, bool isSelected)
            {
            }

            private sealed class SaveScope : IDisposable
            {
                public void Dispose()
                {
                }
            }
        }
    }
}
