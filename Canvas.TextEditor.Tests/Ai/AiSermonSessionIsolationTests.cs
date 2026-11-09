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
    public sealed class AiSermonSessionIsolationTests
    {
        [Fact]
        public async Task SetSpeakerAsync_CancelledSelectionDoesNotChangeCurrentSpeakerOrSession()
        {
            using var fixture = new Fixture();
            await fixture.Coordinator.SetSpeakerAsync("本场原传道人");
            await fixture.Coordinator.SendAsrTurnAsync(Turn("本场已经开始接收字幕"));
            await fixture.Scheduler.WaitForIdleAsync(TimeSpan.FromSeconds(5));
            var original = await fixture.Db.AiSpeakers.SingleAsync(speaker => speaker.Name == "本场原传道人");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                fixture.Coordinator.SetSpeakerAsync("已取消的传道人选择", cts.Token));

            Assert.Equal("本场原传道人", fixture.Coordinator.CurrentSpeakerName);
            Assert.Equal(original.Id, (await fixture.Db.AiSermonSessions.SingleAsync()).SpeakerId);
            Assert.False(await fixture.Db.AiSpeakers.AnyAsync(speaker => speaker.Name == "已取消的传道人选择"));
        }

        [Fact]
        public async Task InputMethodTurn_UsesExistingCoordinatorAndCreatesFirstSession()
        {
            using var fixture = new Fixture();
            Type type = typeof(AiAsrTurnEnvelope).Assembly.GetType("ImageColorChanger.Services.Ai.AiInputMethodTurnAggregator");
            Assert.NotNull(type);
            object aggregator = Activator.CreateInstance(type);
            var now = DateTimeOffset.Now;
            type.GetMethod("Reset").Invoke(aggregator, new object[] { string.Empty });
            type.GetMethod("Observe").Invoke(aggregator, new object[] { "输入法新增的讲章内容", now });
            object[] flush = { now.AddSeconds(2), null };
            Assert.True((bool)type.GetMethod("TryFlush").Invoke(aggregator, flush));
            var turn = Assert.IsType<AiAsrTurnEnvelope>(flush[1]);
            Assert.Equal("input-method", turn.Source);
            Assert.False(fixture.Coordinator.HasActiveSession);
            var displayed = new List<string>();
            fixture.Coordinator.AssistantDeltaReceived += displayed.Add;

            await fixture.Coordinator.SendAsrTurnAsync(turn);
            await fixture.Scheduler.WaitForIdleAsync(TimeSpan.FromSeconds(5));

            Assert.Single(fixture.Chat.Requests);
            Assert.Contains(fixture.Chat.Requests[0].Messages, message => message.Name == "asr" && message.Content.Contains(turn.Text));
            Assert.Contains("新请求正常回复", displayed);
            Assert.True(fixture.Coordinator.HasActiveSession);
            Assert.Contains(await fixture.Db.AiConversationRecords.ToListAsync(), record => record.Name == "asr" && record.Content.Contains(turn.Text));
        }

        [Fact]
        public async Task FinalizeThenNewInput_DoesNotReusePreviousSemanticWindow()
        {
            using var fixture = new Fixture();
            await fixture.Coordinator.SendAsrTurnAsync(Turn("上一场独有的旧内容"));
            await fixture.Scheduler.WaitForIdleAsync(TimeSpan.FromSeconds(5));
            await fixture.Coordinator.FinalizeActiveSessionAsync();
            await fixture.Coordinator.SendAsrTurnAsync(Turn("新一场独有的新内容"));
            await fixture.Scheduler.WaitForIdleAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(2, fixture.Chat.Requests.Count);
            var latest = fixture.Chat.Requests.Last();
            Assert.Contains(latest.Messages, m => m.Name == "asr" && m.Content.Contains("新一场独有的新内容"));
            Assert.DoesNotContain(latest.Messages, m => m.Content.Contains("上一场独有的旧内容"));
            Assert.Equal(2, await fixture.Db.AiSermonSessions.CountAsync());
        }

        [Fact]
        public async Task Finalize_DiscardsQueuedWindowWhileWaitingForCurrentRequest()
        {
            using var fixture = new Fixture(blockFirstChat: true);
            await fixture.Coordinator.SendAsrTurnAsync(Turn("当前正在处理的内容"));
            await fixture.Chat.ChatStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.Coordinator.SendAsrTurnAsync(Turn("已排队但结束后不应发送"));
            var ending = fixture.Coordinator.FinalizeActiveSessionAsync();
            Assert.False(ending.IsCompleted);
            fixture.Chat.ReleaseChat.TrySetResult();
            await ending.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.Scheduler.WaitForIdleAsync(TimeSpan.FromSeconds(5));

            Assert.Single(fixture.Chat.Requests);
            Assert.False(fixture.Coordinator.HasActiveSession);
        }

        [Fact]
        public async Task Finalize_WhileFirstInputIsInitializing_DrainsBeforeSettlement()
        {
            using var fixture = new Fixture(blockFirstBalance: true);
            await fixture.Coordinator.SendAsrTurnAsync(Turn("创建会话时的第一段内容"));
            await fixture.Chat.BalanceStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var ending = fixture.Coordinator.FinalizeActiveSessionAsync();
            bool finishedBeforeInitialization = ending.IsCompleted;
            fixture.Chat.ReleaseBalance.TrySetResult();
            await ending.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.Scheduler.WaitForIdleAsync(TimeSpan.FromSeconds(5));

            Assert.False(finishedBeforeInitialization);
            Assert.False(fixture.Coordinator.HasActiveSession);
            Assert.All(await fixture.Db.AiSermonSessions.ToListAsync(), session => Assert.NotNull(session.EndedAt));
        }

        [Fact]
        public async Task FinalizeTimeout_LateOldResponseCannotPublishIntoNewSession()
        {
            using var fixture = new Fixture(blockFirstChat: true);
            var displayed = new List<string>();
            fixture.Coordinator.AssistantDeltaReceived += displayed.Add;
            await fixture.Coordinator.SendAsrTurnAsync(Turn("旧场中挂起的请求"));
            await fixture.Chat.ChatStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var cts = new CancellationTokenSource();
            var ending = fixture.Coordinator.FinalizeActiveSessionAsync(cts.Token);
            cts.Cancel();
            await ending.WaitAsync(TimeSpan.FromSeconds(5));
            bool ended = !fixture.Coordinator.HasActiveSession;

            await fixture.Coordinator.SendAsrTurnAsync(Turn("取消后新一场输入"));
            fixture.Chat.ReleaseChat.TrySetResult();
            await fixture.Chat.FirstChatReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.Scheduler.WaitForIdleAsync(TimeSpan.FromSeconds(5));

            Assert.True(ended);
            Assert.DoesNotContain("旧请求迟到回复", displayed);
            Assert.Contains("新请求正常回复", displayed);
            Assert.Equal(2, await fixture.Db.AiSermonSessions.CountAsync());
            Assert.DoesNotContain(fixture.Chat.Requests.Last().Messages, m => m.Content.Contains("旧场中挂起的请求"));
        }

        [Fact]
        public async Task FinalizeTimeout_LateProjectResponseIsDiscarded()
        {
            using var fixture = new Fixture(blockFirstChat: true);
            var displayed = new List<string>();
            fixture.Coordinator.AssistantDeltaReceived += displayed.Add;
            Task project = fixture.Coordinator.StartProjectAsync(7);
            await fixture.Chat.ChatStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var cts = new CancellationTokenSource();
            var ending = fixture.Coordinator.FinalizeActiveSessionAsync(cts.Token);
            cts.Cancel();
            await ending.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Chat.ReleaseChat.TrySetResult();
            await project.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(fixture.Coordinator.HasActiveSession);
            Assert.DoesNotContain("旧请求迟到回复", displayed);
            Assert.DoesNotContain(await fixture.Db.AiConversationRecords.ToListAsync(), record => record.Content == "旧请求迟到回复");
        }

        [Fact]
        public async Task FinalizeTimeout_LateInitializationBalanceCannotReviveEndedSession()
        {
            using var fixture = new Fixture(blockFirstBalance: true);
            await fixture.Coordinator.SendAsrTurnAsync(Turn("初始化余额查询期间结束"));
            await fixture.Chat.BalanceStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var cts = new CancellationTokenSource();
            var ending = fixture.Coordinator.FinalizeActiveSessionAsync(cts.Token);
            cts.Cancel();
            await ending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(fixture.Coordinator.HasActiveSession);
            fixture.Chat.ReleaseBalance.TrySetResult();
            await fixture.Coordinator.SendAsrTurnAsync(Turn("新场余额独立记录"));
            await fixture.Scheduler.WaitForIdleAsync(TimeSpan.FromSeconds(5));

            var sessions = await fixture.Db.AiSermonSessions.OrderBy(s => s.Id).ToListAsync();
            Assert.Equal(2, sessions.Count);
            Assert.NotNull(sessions[0].EndedAt);
            Assert.Null(sessions[0].StartBalance);
            Assert.Equal(100m, sessions[1].StartBalance);
            Assert.Single(fixture.Chat.Requests);
            Assert.DoesNotContain(fixture.Chat.Requests[0].Messages, m => m.Content.Contains("初始化余额查询期间结束"));
        }

        private static AiAsrTurnEnvelope Turn(string text) => new()
        {
            TurnId = Guid.NewGuid().ToString("N"), Text = text,
            CapturedAt = DateTimeOffset.Now, IsFinal = true, Source = "input-method"
        };

        private sealed class Fixture : IDisposable
        {
            private readonly string _path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"canvas-ai-isolation-{Guid.NewGuid():N}.db");
            public CanvasDbContext Db { get; }
            public ControlledChatClient Chat { get; }
            public AiRealtimeUnderstandingScheduler Scheduler { get; } = new();
            public AiSermonConversationCoordinator Coordinator { get; }
            public Fixture(bool blockFirstChat = false, bool blockFirstBalance = false)
            {
                Db = new CanvasDbContext(_path);
                Db.Database.EnsureCreated();
                Db.EnsureAiSermonSchemaExists();
                Chat = new ControlledChatClient(blockFirstChat, blockFirstBalance);
                Coordinator = new AiSermonConversationCoordinator(
                    new AiSermonContextBuilder(new FakeTextProjectService()), Chat, new FakeBibleService(),
                    new ConfigManager(), new AiSermonHistoryStore(Db), new AiSermonSummaryService(), Scheduler);
            }
            public void Dispose()
            {
                Chat.ReleaseChat.TrySetResult();
                Chat.ReleaseBalance.TrySetResult();
                Db.Dispose();
                try { System.IO.File.Delete(_path); } catch { }
            }
        }

        private sealed class ControlledChatClient : IDeepSeekChatClient
        {
            private readonly bool _blockFirstChat;
            private readonly bool _blockFirstBalance;
            private int _chats;
            private int _balances;
            public List<AiChatRequest> Requests { get; } = new();
            public TaskCompletionSource ChatStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource ReleaseChat { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource FirstChatReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource BalanceStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource ReleaseBalance { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public ControlledChatClient(bool blockFirstChat, bool blockFirstBalance)
            {
                _blockFirstChat = blockFirstChat;
                _blockFirstBalance = blockFirstBalance;
            }
            public async Task<AiChatStreamResult> StreamChatAsync(AiChatRequest request, Action<string> onContentDelta, CancellationToken cancellationToken)
            {
                int call = Interlocked.Increment(ref _chats);
                lock (Requests) Requests.Add(request);
                if (call == 1 && _blockFirstChat)
                {
                    ChatStarted.TrySetResult();
                    // Deliberately emulate a provider that ignores cancellation and calls back late.
                    await ReleaseChat.Task;
                }
                string content = call == 1 && _blockFirstChat ? "旧请求迟到回复" : "新请求正常回复";
                onContentDelta?.Invoke(content);
                if (call == 1) FirstChatReturned.TrySetResult();
                return new AiChatStreamResult { Content = content };
            }
            public async Task<DeepSeekBalanceSnapshot> GetBalanceAsync(CancellationToken cancellationToken)
            {
                if (Interlocked.Increment(ref _balances) == 1 && _blockFirstBalance)
                {
                    BalanceStarted.TrySetResult();
                    await ReleaseBalance.Task;
                }
                return new DeepSeekBalanceSnapshot { IsAvailable = true, Currency = "CNY", TotalBalance = 100m };
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
