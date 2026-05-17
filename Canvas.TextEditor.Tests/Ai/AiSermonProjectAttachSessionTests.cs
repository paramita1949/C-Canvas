using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Core;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Bible;
using ImageColorChanger.Services.Ai;
using ImageColorChanger.Services.Interfaces;
using ImageColorChanger.Services.TextEditor.Application;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Canvas.TextEditor.Tests.Ai
{
    public sealed class AiSermonProjectAttachSessionTests
    {
        [Fact]
        public async Task StartProjectAsync_WhenRealtimeSessionExists_AttachesProjectWithoutStartingNewHistorySession()
        {
            string dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"canvas-ai-attach-{Guid.NewGuid():N}.db");
            try
            {
                using var context = new CanvasDbContext(dbPath);
                context.Database.EnsureCreated();
                context.EnsureAiSermonSchemaExists();

                var chat = new RecordingChatClient();
                var scheduler = new AiRealtimeUnderstandingScheduler();
                var coordinator = new AiSermonConversationCoordinator(
                    new AiSermonContextBuilder(new FakeTextProjectService()),
                    chat,
                    new FakeBibleService(),
                    new ConfigManager(),
                    new AiSermonHistoryStore(context),
                    new AiSermonSummaryService(),
                    scheduler);

                await coordinator.SendAsrTurnAsync(
                    new AiAsrTurnEnvelope
                    {
                        TurnId = "asr-1",
                        Text = "我们今天先问下一代在哪里",
                        CapturedAt = DateTimeOffset.Now,
                        IsFinal = true
                    });
                await scheduler.WaitForIdleAsync(TimeSpan.FromSeconds(5));

                await coordinator.StartProjectAsync(7);

                var sessions = await context.AiSermonSessions
                    .OrderBy(session => session.Id)
                    .ToListAsync();
                var messages = await context.AiConversationRecords
                    .OrderBy(message => message.Id)
                    .ToListAsync();

                Assert.Single(sessions);
                Assert.Equal(7, sessions[0].ProjectId);
                Assert.Equal("主日信息", sessions[0].Title);
                Assert.Contains(messages, message => message.SessionId == sessions[0].Id && message.Name == "asr");
                Assert.Contains(messages, message => message.SessionId == sessions[0].Id && message.Name == "project_context");
                Assert.Contains(chat.Requests.Last().Messages, message =>
                    message.Name == "history_context" &&
                    message.Content.Contains("ASR:", StringComparison.Ordinal));
            }
            finally
            {
                try { System.IO.File.Delete(dbPath); } catch { }
            }
        }

        private sealed class RecordingChatClient : IDeepSeekChatClient
        {
            public List<AiChatRequest> Requests { get; } = new();

            public Task<AiChatStreamResult> StreamChatAsync(
                AiChatRequest request,
                Action<string> onContentDelta,
                CancellationToken cancellationToken)
            {
                Requests.Add(request);
                onContentDelta?.Invoke("已理解");
                return Task.FromResult(new AiChatStreamResult { Content = "已理解" });
            }

            public Task<DeepSeekBalanceSnapshot> GetBalanceAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(new DeepSeekBalanceSnapshot
                {
                    IsAvailable = true,
                    Currency = "CNY",
                    TotalBalance = 100m
                });
            }
        }

        private sealed class FakeTextProjectService : ITextProjectService
        {
            public Task<TextProject> LoadProjectAsync(int projectId)
            {
                return Task.FromResult(new TextProject
                {
                    Id = projectId,
                    Name = "主日信息",
                    CanvasWidth = 1920,
                    CanvasHeight = 1080
                });
            }

            public Task<List<Slide>> GetSlidesByProjectWithElementsAsync(int projectId)
            {
                return Task.FromResult(new List<Slide>
                {
                    new()
                    {
                        Id = 1,
                        ProjectId = projectId,
                        SortOrder = 0,
                        Title = "下一代在哪里",
                        Elements = new List<TextElement>
                        {
                            new() { Id = 1, Content = "出埃及记2章1-10节", ZIndex = 0 }
                        }
                    }
                });
            }

            public Task<TextProject> CreateProjectAsync(string name, int canvasWidth = 1920, int canvasHeight = 1080) => throw new NotSupportedException();
            public Task<List<TextProject>> GetAllProjectsAsync() => throw new NotSupportedException();
            public Task SaveProjectAsync(TextProject project) => throw new NotSupportedException();
            public Task DeleteProjectAsync(int projectId) => throw new NotSupportedException();
            public Task UpdateBackgroundImageAsync(int projectId, string imagePath) => throw new NotSupportedException();
            public Task<bool> ProjectHasSlidesAsync(int projectId) => throw new NotSupportedException();
            public Task<int> GetSlideCountAsync(int projectId) => throw new NotSupportedException();
            public Task<int> GetMaxSlideSortOrderAsync(int projectId) => throw new NotSupportedException();
            public Task<Slide> AddSlideAsync(Slide slide) => throw new NotSupportedException();
            public Task AddSlidesAsync(IEnumerable<Slide> slides) => throw new NotSupportedException();
            public Task<Slide> GetSlideByIdAsync(int slideId) => throw new NotSupportedException();
            public Task UpdateSlideAsync(Slide slide) => throw new NotSupportedException();
            public Task UpdateSlideThumbnailAsync(int slideId, string thumbnailPath) => throw new NotSupportedException();
            public Task<List<Slide>> GetSlidesByProjectAsync(int projectId) => throw new NotSupportedException();
            public Task UpdateSlideSortOrdersAsync(IEnumerable<Slide> slides) => throw new NotSupportedException();
            public Task ShiftSlideSortOrdersAsync(int projectId, int fromSortOrder, int delta) => throw new NotSupportedException();
            public Task DeleteSlideAsync(int slideId) => throw new NotSupportedException();
            public Task DeleteSlidesByProjectAsync(int projectId) => throw new NotSupportedException();
            public Task<List<TextElement>> GetElementsBySlideWithRichTextAsync(int slideId) => throw new NotSupportedException();
            public Task RebindProjectElementsToSlideAsync(int projectId, int targetSlideId) => throw new NotSupportedException();
            public Task<TextElement> AddElementAsync(TextElement element) => throw new NotSupportedException();
            public Task UpdateElementAsync(TextElement element) => throw new NotSupportedException();
            public Task UpdateElementsAsync(IEnumerable<TextElement> elements) => throw new NotSupportedException();
            public Task<RichTextSpan> AddRichTextSpanAsync(RichTextSpan span) => throw new NotSupportedException();
            public Task DeleteRichTextSpansByElementIdAsync(int textElementId) => throw new NotSupportedException();
            public Task SaveRichTextSpansAsync(int textElementId, List<RichTextSpan> spans) => throw new NotSupportedException();
            public Task DeleteElementAsync(int elementId) => throw new NotSupportedException();
            public Task DeleteAllElementsAsync(int projectId) => throw new NotSupportedException();
            public Task<List<TextElement>> GetElementsByProjectAsync(int projectId) => throw new NotSupportedException();
            public Task<MediaFile> GetMediaFileByPathAsync(string path) => throw new NotSupportedException();
            public TextElement CloneElement(TextElement source) => throw new NotSupportedException();
        }

        private sealed class FakeBibleService : IBibleService
        {
            public Task<BibleVerse> GetVerseAsync(int book, int chapter, int verse) => throw new NotSupportedException();
            public Task<List<BibleVerse>> GetChapterVersesAsync(int book, int chapter) => throw new NotSupportedException();
            public Task<List<BibleVerse>> GetVerseRangeAsync(int book, int chapter, int startVerse, int endVerse) => throw new NotSupportedException();
            public Task<List<BibleTitle>> GetChapterTitlesAsync(int book, int chapter) => throw new NotSupportedException();
            public Task<List<object>> GetChapterContentAsync(int book, int chapter) => throw new NotSupportedException();
            public Task<List<BibleSearchResult>> SearchVersesAsync(string keyword, int? bookId = null) => throw new NotSupportedException();
            public Task<List<BibleSearchResult>> SearchVersesByPinyinAsync(string pinyinKeyword, int? bookId = null) => throw new NotSupportedException();
            public int GetChapterCount(int book) => 150;
            public Task<Dictionary<(int book, int chapter), int>> GetAllVerseCountsAsync() => throw new NotSupportedException();
            public Task<int> GetVerseCountAsync(int book, int chapter) => Task.FromResult(80);
            public Task<bool> IsDatabaseAvailableAsync() => Task.FromResult(true);
            public Task<Dictionary<string, string>> GetMetadataAsync() => Task.FromResult(new Dictionary<string, string>());
            public void UpdateDatabasePath() { }
        }
    }
}
