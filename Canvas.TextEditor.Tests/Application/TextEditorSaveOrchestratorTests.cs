using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Services.TextEditor;
using ImageColorChanger.Services.TextEditor.Application;
using ImageColorChanger.Services.TextEditor.Application.Models;
using ImageColorChanger.Services.TextEditor.Models;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Application
{
    public sealed class TextEditorSaveOrchestratorTests
    {
        [Fact]
        public async Task SaveAsync_PersistsSnapshotsAndCallbacks_WhenRequestIsValid()
        {
            var persistence = new FakePersistenceService();
            var orchestrator = new TextEditorSaveOrchestrator(persistence);
            var snapshot = CreateSnapshot(7, "hello");
            bool splitSaved = false;
            bool thumbnailSaved = false;

            var request = new TextEditorSaveRequest
            {
                Trigger = SaveTrigger.Manual,
                Snapshots = new[] { snapshot },
                PersistAdditionalStateAsync = _ =>
                {
                    splitSaved = true;
                    return Task.CompletedTask;
                },
                SaveThumbnailAsync = _ =>
                {
                    thumbnailSaved = true;
                    return Task.FromResult("thumb-7.png");
                }
            };

            var result = await orchestrator.SaveAsync(request);

            Assert.True(result.Succeeded);
            Assert.Equal(SaveTrigger.Manual, result.Trigger);
            Assert.True(result.TextElementsSaved);
            Assert.True(result.AdditionalStateSaved);
            Assert.True(result.ThumbnailSaved);
            Assert.Equal("thumb-7.png", result.ThumbnailPath);
            Assert.True(splitSaved);
            Assert.True(thumbnailSaved);
            Assert.Equal(1, persistence.SaveCallCount);
            Assert.Single(persistence.SavedSnapshots);
            Assert.Same(snapshot, persistence.SavedSnapshots[0]);
        }

        [Fact]
        public async Task SaveAsync_ReturnsFailure_WhenPersistenceThrows()
        {
            var persistence = new FakePersistenceService
            {
                SaveException = new InvalidOperationException("boom")
            };
            var orchestrator = new TextEditorSaveOrchestrator(persistence);
            bool splitSaved = false;
            bool thumbnailSaved = false;

            var result = await orchestrator.SaveAsync(new TextEditorSaveRequest
            {
                Trigger = SaveTrigger.SlideSwitch,
                Snapshots = new[] { CreateSnapshot(11, "x") },
                PersistAdditionalStateAsync = _ =>
                {
                    splitSaved = true;
                    return Task.CompletedTask;
                },
                SaveThumbnailAsync = _ =>
                {
                    thumbnailSaved = true;
                    return Task.FromResult("never.png");
                }
            });

            Assert.False(result.Succeeded);
            Assert.Equal(SaveTrigger.SlideSwitch, result.Trigger);
            Assert.NotNull(result.Exception);
            Assert.Contains("boom", result.Exception.Message);
            Assert.False(splitSaved);
            Assert.False(thumbnailSaved);
            Assert.False(result.AdditionalStateSaved);
            Assert.False(result.ThumbnailSaved);
        }

        [Fact]
        public async Task SaveAsync_ThrowsArgumentNullException_WhenRequestIsNull()
        {
            var orchestrator = new TextEditorSaveOrchestrator(new FakePersistenceService());
            await Assert.ThrowsAsync<ArgumentNullException>(() => orchestrator.SaveAsync(null));
        }

        private static TextBoxSnapshot CreateSnapshot(int id, string content)
        {
            return new TextBoxSnapshot(
                new TextElement { Id = id, Content = content },
                content,
                new List<RichTextSpan>(),
                TextLayoutProfile.Default,
                wasInEditMode: false);
        }

        private sealed class FakePersistenceService : ITextElementPersistenceService
        {
            public int SaveCallCount { get; private set; }

            public Exception SaveException { get; set; }

            public IReadOnlyList<TextBoxSnapshot> SavedSnapshots { get; private set; } = Array.Empty<TextBoxSnapshot>();

            public Task SaveAsync(IReadOnlyCollection<TextBoxSnapshot> snapshots, CancellationToken cancellationToken = default)
            {
                SaveCallCount++;
                SavedSnapshots = snapshots == null
                    ? Array.Empty<TextBoxSnapshot>()
                    : new List<TextBoxSnapshot>(snapshots);

                if (SaveException != null)
                {
                    throw SaveException;
                }

                return Task.CompletedTask;
            }
        }
    }
}
