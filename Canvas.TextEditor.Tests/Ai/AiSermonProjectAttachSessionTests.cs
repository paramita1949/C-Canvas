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

        [Fact]
        public async Task StartProjectAsync_ReportsDetailedRequestProgressStatuses()
        {
            string dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"canvas-ai-status-{Guid.NewGuid():N}.db");
            try
            {
                using var context = new CanvasDbContext(dbPath);
                context.Database.EnsureCreated();
                context.EnsureAiSermonSchemaExists();

                var coordinator = new AiSermonConversationCoordinator(
                    new AiSermonContextBuilder(new FakeTextProjectService()),
                    new RecordingChatClient(),
                    new FakeBibleService(),
                    new ConfigManager(),
                    new AiSermonHistoryStore(context),
                    new AiSermonSummaryService(),
                    new AiRealtimeUnderstandingScheduler());
                var statuses = new List<string>();
                coordinator.StatusChanged += statuses.Add;

                await coordinator.StartProjectAsync(7);

                Assert.Contains("正在整理提示词…", statuses);
                Assert.Contains("正在发送提示词…", statuses);
                Assert.Contains("已收到反馈，正在生成摘要…", statuses);
                Assert.Contains("反馈接收完成。", statuses);
                Assert.DoesNotContain("DeepSeek请求已发送，处理中…", statuses);
                Assert.DoesNotContain("DeepSeek已返回结果。", statuses);
            }
            finally
            {
                try { System.IO.File.Delete(dbPath); } catch { }
            }
        }

        [Fact]
        public async Task SendAsrTurnAsync_WhenScriptureCandidateReturned_ConfirmsCandidateBeforeCompletion()
        {
            string dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"canvas-ai-candidate-priority-{Guid.NewGuid():N}.db");
            try
            {
                using var context = new CanvasDbContext(dbPath);
                context.Database.EnsureCreated();
                context.EnsureAiSermonSchemaExists();

                var chat = new CandidateChatClient();
                var scheduler = new AiRealtimeUnderstandingScheduler();
                var coordinator = new AiSermonConversationCoordinator(
                    new AiSermonContextBuilder(new FakeTextProjectService()),
                    chat,
                    new FakeBibleService(),
                    new ConfigManager(),
                    new AiSermonHistoryStore(context),
                    new AiSermonSummaryService(),
                    scheduler);

            var order = new List<string>();
                coordinator.StatusChanged += status =>
                {
                    if (status.StartsWith("AI经文候选已确认", StringComparison.Ordinal))
                    {
                        order.Add("candidate");
                    }
                    else if (string.Equals(status, "反馈接收完成。", StringComparison.Ordinal))
                    {
                        order.Add("complete");
                    }
                };

                await coordinator.SendAsrTurnAsync(new AiAsrTurnEnvelope
                {
                    TurnId = "asr-candidate",
                    Text = "我们今天讲约翰福音三章十六节",
                    CapturedAt = DateTimeOffset.Now,
                    IsFinal = true
                });
                await scheduler.WaitForIdleAsync(TimeSpan.FromSeconds(5));

                Assert.Equal(new[] { "candidate", "complete" }, order);
            }
            finally
            {
                try { System.IO.File.Delete(dbPath); } catch { }
            }
        }

        [Fact]
        public async Task SendAsrTurnAsync_BuildsScriptureHistoryPriorityPrompt()
        {
            string dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"canvas-ai-asr-history-priority-{Guid.NewGuid():N}.db");
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

                await coordinator.SendAsrTurnAsync(new AiAsrTurnEnvelope
                {
                    TurnId = "asr-scripture-priority",
                    Text = "我们继续看约翰福音三章十六节",
                    CapturedAt = DateTimeOffset.Now,
                    IsFinal = true
                });
                await scheduler.WaitForIdleAsync(TimeSpan.FromSeconds(5));

                var asrMessage = Assert.Single(chat.Requests.Last().Messages, message => message.Name == "asr");
                Assert.Contains("经文历史写入优先", asrMessage.Content, StringComparison.Ordinal);
                Assert.Contains("不要输出讲章摘要", asrMessage.Content, StringComparison.Ordinal);
                Assert.DoesNotContain("本次输出按简洁模式", asrMessage.Content, StringComparison.Ordinal);

                var assistantMessages = await context.AiConversationRecords
                    .Where(message => message.Role == "assistant")
                    .ToListAsync();
                Assert.Empty(assistantMessages);
            }
            finally
            {
                try { System.IO.File.Delete(dbPath); } catch { }
            }
        }

        [Fact]
        public async Task SendAsrTurnAsync_DetailedMode_EmitsPromptPreviewForInspection()
        {
            string dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"canvas-ai-detailed-prompt-{Guid.NewGuid():N}.db");
            try
            {
                using var context = new CanvasDbContext(dbPath);
                context.Database.EnsureCreated();
                context.EnsureAiSermonSchemaExists();

                var scheduler = new AiRealtimeUnderstandingScheduler();
                var coordinator = new AiSermonConversationCoordinator(
                    new AiSermonContextBuilder(new FakeTextProjectService()),
                    new RecordingChatClient(),
                    new FakeBibleService(),
                    new ConfigManager(),
                    new AiSermonHistoryStore(context),
                    new AiSermonSummaryService(),
                    scheduler);
                var debugMessages = new List<string>();
                coordinator.DebugMessageEmitted += debugMessages.Add;

                await coordinator.StartProjectAsync(7);
                await coordinator.SetOutputModeAsync("detailed");

                await coordinator.SendAsrTurnAsync(new AiAsrTurnEnvelope
                {
                    TurnId = "asr-detailed",
                    Text = "我们继续看约翰福音三章十六节",
                    CapturedAt = DateTimeOffset.Now,
                    IsFinal = true
                });
                await scheduler.WaitForIdleAsync(TimeSpan.FromSeconds(5));

                Assert.Contains(debugMessages, message => message.Contains("提示词预览（详细模式）", StringComparison.Ordinal));
            }
            finally
            {
                try { System.IO.File.Delete(dbPath); } catch { }
            }
        }

        [Fact]
        public async Task SendAsrTurnAsync_ConciseMode_DoesNotEmitPromptPreview()
        {
            string dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"canvas-ai-concise-prompt-{Guid.NewGuid():N}.db");
            try
            {
                using var context = new CanvasDbContext(dbPath);
                context.Database.EnsureCreated();
                context.EnsureAiSermonSchemaExists();

                var scheduler = new AiRealtimeUnderstandingScheduler();
                var coordinator = new AiSermonConversationCoordinator(
                    new AiSermonContextBuilder(new FakeTextProjectService()),
                    new RecordingChatClient(),
                    new FakeBibleService(),
                    new ConfigManager(),
                    new AiSermonHistoryStore(context),
                    new AiSermonSummaryService(),
                    scheduler);
                var debugMessages = new List<string>();
                coordinator.DebugMessageEmitted += debugMessages.Add;

                await coordinator.StartProjectAsync(7);

                await coordinator.SendAsrTurnAsync(new AiAsrTurnEnvelope
                {
                    TurnId = "asr-concise",
                    Text = "我们继续看约翰福音三章十六节",
                    CapturedAt = DateTimeOffset.Now,
                    IsFinal = true
                });
                await scheduler.WaitForIdleAsync(TimeSpan.FromSeconds(5));

                Assert.DoesNotContain(debugMessages, message => message.Contains("提示词预览（详细模式）", StringComparison.Ordinal));
            }
            finally
            {
                try { System.IO.File.Delete(dbPath); } catch { }
            }
        }

        [Fact]
        public async Task StartProjectAsync_WhenBalanceBelowMinimum_DoesNotSendAiRequest()
        {
            string dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"canvas-ai-low-balance-{Guid.NewGuid():N}.db");
            try
            {
                using var context = new CanvasDbContext(dbPath);
                context.Database.EnsureCreated();
                context.EnsureAiSermonSchemaExists();

                var chat = new LowBalanceChatClient();
                var coordinator = new AiSermonConversationCoordinator(
                    new AiSermonContextBuilder(new FakeTextProjectService()),
                    chat,
                    new FakeBibleService(),
                    new ConfigManager(),
                    new AiSermonHistoryStore(context),
                    new AiSermonSummaryService(),
                    new AiRealtimeUnderstandingScheduler());
                var statuses = new List<string>();
                coordinator.StatusChanged += statuses.Add;

                await coordinator.StartProjectAsync(7);

                Assert.Empty(chat.Requests);
                Assert.Contains("余额低于0.05，AI已停止工作。", statuses);
            }
            finally
            {
                try { System.IO.File.Delete(dbPath); } catch { }
            }
        }

        [Fact]
        public async Task FinalizeActiveSessionAsync_PersistsLastKnownCostAndEndsSession()
        {
            string dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"canvas-ai-finalize-session-{Guid.NewGuid():N}.db");
            try
            {
                using var context = new CanvasDbContext(dbPath);
                context.Database.EnsureCreated();
                context.EnsureAiSermonSchemaExists();

                var chat = new BalanceSequenceChatClient(100m, 99.60m, 99.25m);
                var coordinator = new AiSermonConversationCoordinator(
                    new AiSermonContextBuilder(new FakeTextProjectService()),
                    chat,
                    new FakeBibleService(),
                    new ConfigManager(),
                    new AiSermonHistoryStore(context),
                    new AiSermonSummaryService(),
                    new AiRealtimeUnderstandingScheduler());

                await coordinator.StartProjectAsync(7);
                await coordinator.FinalizeActiveSessionAsync(CancellationToken.None);

                var session = await context.AiSermonSessions.SingleAsync();
                Assert.Equal(100m, session.StartBalance);
                Assert.Equal(99.25m, session.LastBalance);
                Assert.Equal(0.75m, session.SessionCost);
                Assert.Equal("CNY", session.BalanceCurrency);
                Assert.NotNull(session.EndedAt);
                Assert.False(coordinator.HasActiveSession);
            }
            finally
            {
                try { System.IO.File.Delete(dbPath); } catch { }
            }
        }

        [Fact]
        public async Task FinalizeActiveSessionAsync_WaitsForRunningAsrBeforeSettlement()
        {
            string dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"canvas-ai-finalize-running-asr-{Guid.NewGuid():N}.db");
            try
            {
                using var context = new CanvasDbContext(dbPath);
                context.Database.EnsureCreated();
                context.EnsureAiSermonSchemaExists();

                var chat = new BlockingAsrChatClient(100m, 99.80m, 99.20m);
                var scheduler = new AiRealtimeUnderstandingScheduler();
                var coordinator = new AiSermonConversationCoordinator(
                    new AiSermonContextBuilder(new FakeTextProjectService()),
                    chat,
                    new FakeBibleService(),
                    new ConfigManager(),
                    new AiSermonHistoryStore(context),
                    new AiSermonSummaryService(),
                    scheduler);

                await coordinator.StartProjectAsync(7);
                await coordinator.SendAsrTurnAsync(new AiAsrTurnEnvelope
                {
                    TurnId = "asr-running-finalize",
                    Text = "我们继续看约翰福音三章十六节",
                    CapturedAt = DateTimeOffset.Now,
                    IsFinal = true
                });
                await chat.WaitForAsrStartedAsync();

                var finalizeTask = coordinator.FinalizeActiveSessionAsync(CancellationToken.None);
                await Task.Delay(100);

                Assert.False(finalizeTask.IsCompleted);

                chat.ReleaseAsr();
                await finalizeTask.WaitAsync(TimeSpan.FromSeconds(5));

                var session = await context.AiSermonSessions.SingleAsync();
                Assert.Equal(100m, session.StartBalance);
                Assert.Equal(99.20m, session.LastBalance);
                Assert.Equal(0.80m, session.SessionCost);
                Assert.NotNull(session.EndedAt);
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

        private sealed class CandidateChatClient : IDeepSeekChatClient
        {
            public Task<AiChatStreamResult> StreamChatAsync(
                AiChatRequest request,
                Action<string> onContentDelta,
                CancellationToken cancellationToken)
            {
                onContentDelta?.Invoke("候选摘要");
                return Task.FromResult(new AiChatStreamResult
                {
                    Content = "候选摘要",
                    ScriptureCandidates = new[]
                    {
                        new AiScriptureCandidate
                        {
                            BookId = 43,
                            BookName = "约翰福音",
                            Chapter = 3,
                            StartVerse = 16,
                            EndVerse = 16,
                            Confidence = 0.95,
                            Reason = "ASR明确提到约翰福音三章十六节"
                        }
                    }
                });
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

        private sealed class LowBalanceChatClient : IDeepSeekChatClient
        {
            public List<AiChatRequest> Requests { get; } = new();

            public Task<AiChatStreamResult> StreamChatAsync(
                AiChatRequest request,
                Action<string> onContentDelta,
                CancellationToken cancellationToken)
            {
                Requests.Add(request);
                return Task.FromResult(new AiChatStreamResult { Content = "不应发送" });
            }

            public Task<DeepSeekBalanceSnapshot> GetBalanceAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(new DeepSeekBalanceSnapshot
                {
                    IsAvailable = true,
                    Currency = "CNY",
                    TotalBalance = 0.04m
                });
            }
        }

        private sealed class BalanceSequenceChatClient : IDeepSeekChatClient
        {
            private readonly Queue<decimal> _balances;

            public BalanceSequenceChatClient(params decimal[] balances)
            {
                _balances = new Queue<decimal>(balances);
            }

            public Task<AiChatStreamResult> StreamChatAsync(
                AiChatRequest request,
                Action<string> onContentDelta,
                CancellationToken cancellationToken)
            {
                onContentDelta?.Invoke("已理解");
                return Task.FromResult(new AiChatStreamResult { Content = "已理解" });
            }

            public Task<DeepSeekBalanceSnapshot> GetBalanceAsync(CancellationToken cancellationToken)
            {
                decimal balance = _balances.Count > 0 ? _balances.Dequeue() : 99.25m;
                return Task.FromResult(new DeepSeekBalanceSnapshot
                {
                    IsAvailable = true,
                    Currency = "CNY",
                    TotalBalance = balance
                });
            }
        }

        private sealed class BlockingAsrChatClient : IDeepSeekChatClient
        {
            private readonly Queue<decimal> _balances;
            private readonly TaskCompletionSource _asrStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource _releaseAsr = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public BlockingAsrChatClient(params decimal[] balances)
            {
                _balances = new Queue<decimal>(balances);
            }

            public async Task<AiChatStreamResult> StreamChatAsync(
                AiChatRequest request,
                Action<string> onContentDelta,
                CancellationToken cancellationToken)
            {
                bool isAsr = request.Messages.Any(message => string.Equals(message.Name, "asr", StringComparison.Ordinal));
                if (isAsr)
                {
                    _asrStarted.TrySetResult();
                    await _releaseAsr.Task.WaitAsync(cancellationToken);
                }

                onContentDelta?.Invoke("已理解");
                return new AiChatStreamResult { Content = "已理解" };
            }

            public Task<DeepSeekBalanceSnapshot> GetBalanceAsync(CancellationToken cancellationToken)
            {
                decimal balance = _balances.Count > 0 ? _balances.Dequeue() : 99.20m;
                return Task.FromResult(new DeepSeekBalanceSnapshot
                {
                    IsAvailable = true,
                    Currency = "CNY",
                    TotalBalance = balance
                });
            }

            public Task WaitForAsrStartedAsync()
            {
                return _asrStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }

            public void ReleaseAsr()
            {
                _releaseAsr.TrySetResult();
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
