using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Core;
using ImageColorChanger.Services.Interfaces;

namespace ImageColorChanger.Services.Ai
{
    public sealed class AiSermonConversationCoordinator
    {
        private readonly AiSermonContextBuilder _contextBuilder;
        private readonly IDeepSeekChatClient _chatClient;
        private readonly IBibleService _bibleService;
        private readonly ConfigManager _config;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly SemaphoreSlim _asrSendGate = new(2, 2);
        private readonly SemaphoreSlim _assistantRenderLock = new(1, 1);
        private readonly List<AiConversationMessage> _visibleMessages = new();
        private readonly List<string> _historicalSignals = new();
        private readonly object _stateLock = new();
        private long _latestAsrSequence;
        private const int MaxHistoricalSignalCount = 800;
        private const int MaxHistoricalSignalLineLength = 180;
        private const int MaxAsrPendingWindow = 2;
        private AiSermonSessionState _session;

        public event Action<AiConversationMessage> MessageAppended;
        public event Action AssistantMessageStarted;
        public event Action<string> AssistantDeltaReceived;
        public event Action<string> StatusChanged;
        public event Action<AiScriptureCandidate> ScriptureCandidateAccepted;

        public AiSermonConversationCoordinator(
            AiSermonContextBuilder contextBuilder,
            IDeepSeekChatClient chatClient,
            IBibleService bibleService,
            ConfigManager config)
        {
            _contextBuilder = contextBuilder ?? throw new ArgumentNullException(nameof(contextBuilder));
            _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
            _bibleService = bibleService ?? throw new ArgumentNullException(nameof(bibleService));
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public bool HasActiveSession => _session != null;

        public async Task StartProjectAsync(int projectId, CancellationToken cancellationToken = default)
        {
            var context = await _contextBuilder.BuildAsync(projectId, cancellationToken).ConfigureAwait(false);
            _session = new AiSermonSessionState
            {
                ProjectId = context.ProjectId,
                ProjectName = context.ProjectName,
                ProjectContext = context.ContextText,
                RuntimeContext = context.RuntimeContextText,
                StartedAt = DateTimeOffset.Now
            };
            lock (_stateLock)
            {
                _visibleMessages.Clear();
                _historicalSignals.Clear();
            }
            StatusChanged?.Invoke($"已绑定项目：{context.ProjectName}");

            string prompt =
                "请解析这个幻灯片项目，建立今天讲章的上下文。请用正常对话简短说明今日主题、显式经文、后续 ASR 应重点关注的经文线索。\n\n" +
                context.ContextText;
            await SendVisibleUserMessageAsync("project_context", prompt, cancellationToken).ConfigureAwait(false);
        }

        public Task SendUserMessageAsync(string text, CancellationToken cancellationToken = default)
        {
            return SendVisibleUserMessageAsync("user", text, cancellationToken);
        }

        public Task SendAsrTurnAsync(AiAsrTurnEnvelope turn, CancellationToken cancellationToken = default)
        {
            if (turn == null || string.IsNullOrWhiteSpace(turn.Text))
            {
                return Task.CompletedTask;
            }

            long asrSeq = Interlocked.Increment(ref _latestAsrSequence);

            AppendHistoricalSignal($"ASR: {NormalizeSignal(turn.Text)}");

            string prompt = _session == null
                ? "下面是一段实时 ASR 原文。ASR 可能来自任意语音识别平台，文本可能包含方言、口音、现场噪音、断句和同音误识别。" +
                  "请先纠错理解为可能的普通话语义，再判断讲员可能在讲什么。" +
                  "如果有明确或合理推测的经文候选，请通过工具提出候选；证据不足则只说明可能方向。\n\n" +
                  $"raw_asr：{turn.Text}"
                : "下面是一段实时 ASR 原文。ASR 可能来自任意语音识别平台，文本可能包含方言、口音、现场噪音、断句和同音误识别。" +
                  "请结合今日幻灯片上下文、全历史线索、最近对话和最近 ASR，先纠错理解为可能的普通话语义，再判断讲员当前可能在讲什么。" +
                  "如果有明确或合理推测的经文候选，请通过工具提出候选；证据不足则只说明可能方向。\n\n" +
                  $"raw_asr：{turn.Text}";
            return SendVisibleUserMessageAsync("asr", prompt, cancellationToken, asrSeq);
        }

        private async Task SendVisibleUserMessageAsync(string name, string content, CancellationToken cancellationToken, long asrSeq = 0)
        {
            if (!_config.AiSermonEnabled)
            {
                StatusChanged?.Invoke("AI讲章理解未启用");
                return;
            }

            if (_session == null &&
                !string.Equals(name, "project_context", StringComparison.Ordinal) &&
                !string.Equals(name, "asr", StringComparison.Ordinal))
            {
                StatusChanged?.Invoke("请先从幻灯片项目右键执行 AI解读");
                return;
            }

            var message = new AiConversationMessage
            {
                Role = "user",
                Name = name,
                Content = content ?? string.Empty
            };
            lock (_stateLock)
            {
                _visibleMessages.Add(message);
            }
            MessageAppended?.Invoke(message);

            bool isAsr = string.Equals(name, "asr", StringComparison.Ordinal);
            if (isAsr && IsStaleAsrRequest(asrSeq))
            {
                return;
            }

            var gate = isAsr ? _asrSendGate : _sendLock;
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (isAsr && IsStaleAsrRequest(asrSeq))
                {
                    return;
                }

                if (!isAsr)
                {
                    AssistantMessageStarted?.Invoke();
                }

                StatusChanged?.Invoke("DeepSeek请求已发送，处理中…");
                var request = new AiChatRequest
                {
                    Messages = BuildMessagesForRequest(name),
                    UserId = _session == null ? "canvas-sermon" : $"canvas-sermon-{_session.ProjectId}",
                    EnableScriptureTool = true
                };

                bool receivedAnyDelta = false;
                var result = await _chatClient.StreamChatAsync(
                    request,
                    chunk =>
                    {
                        if (!string.IsNullOrEmpty(chunk))
                        {
                            receivedAnyDelta = true;
                        }

                        if (!isAsr)
                        {
                            AssistantDeltaReceived?.Invoke(chunk);
                        }
                    },
                    cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(result.Content))
                {
                    lock (_stateLock)
                    {
                        _visibleMessages.Add(new AiConversationMessage
                        {
                            Role = "assistant",
                            Content = result.Content
                        });
                    }

                    if (isAsr)
                    {
                        await EmitAssistantMessageAsync(result.Content, cancellationToken).ConfigureAwait(false);
                    }
                }
                else if (!receivedAnyDelta)
                {
                    StatusChanged?.Invoke("DeepSeek已响应（本次无文本输出）。");
                }

                if (receivedAnyDelta)
                {
                    StatusChanged?.Invoke("DeepSeek已返回结果。");
                }

                await HandleCandidatesAsync(result.ScriptureCandidates, cancellationToken).ConfigureAwait(false);
                if (result.PromptCacheHitTokens > 0 || result.PromptCacheMissTokens > 0)
                {
                    StatusChanged?.Invoke($"AI缓存：hit={result.PromptCacheHitTokens}, miss={result.PromptCacheMissTokens}");
                }
            }
            catch (OperationCanceledException)
            {
                StatusChanged?.Invoke("AI请求已取消");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(ex.Message);
            }
            finally
            {
                gate.Release();
            }
        }

        private bool IsStaleAsrRequest(long asrSeq)
        {
            if (asrSeq <= 0)
            {
                return false;
            }

            long latest = Interlocked.Read(ref _latestAsrSequence);
            return (latest - asrSeq) >= MaxAsrPendingWindow;
        }

        private IReadOnlyList<AiConversationMessage> BuildMessagesForRequest(string currentMessageName)
        {
            var messages = new List<AiConversationMessage>
            {
                new()
                {
                    Role = "system",
                    Content = BuildSystemPrompt()
                }
            };

            AiSermonSessionState sessionSnapshot = _session;
            if (sessionSnapshot != null)
            {
                messages.Add(new AiConversationMessage
                {
                    Role = "user",
                    Name = "project_context",
                    Content = $"今日讲章稳定上下文：\n{sessionSnapshot.RuntimeContext}"
                });
            }

            string historicalContext = BuildHistoricalContextMessage();
            if (!string.IsNullOrWhiteSpace(historicalContext))
            {
                messages.Add(new AiConversationMessage
                {
                    Role = "user",
                    Name = "history_context",
                    Content = historicalContext
                });
            }

            List<AiConversationMessage> visibleSnapshot;
            lock (_stateLock)
            {
                visibleSnapshot = _visibleMessages.ToList();
            }

            var visibleWindow = visibleSnapshot
                .Where(m => string.Equals(currentMessageName, "project_context", StringComparison.Ordinal) ||
                            !string.Equals(m.Name, "project_context", StringComparison.Ordinal))
                .TakeLast(10);
            messages.AddRange(visibleWindow);
            return messages;
        }

        private async Task HandleCandidatesAsync(
            IReadOnlyList<AiScriptureCandidate> candidates,
            CancellationToken cancellationToken)
        {
            if (!_config.AiSermonAutoWriteHistory || candidates == null || candidates.Count == 0)
            {
                return;
            }

            var validator = new AiScriptureCandidateValidator(_config.AiSermonMinWriteConfidence);
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await validator.ValidateAsync(
                    candidate,
                    (book, chapter) => _bibleService.GetVerseCountAsync(book, chapter)).ConfigureAwait(false);

                if (result.Accepted)
                {
                    AppendHistoricalSignal(
                        $"经文候选确认: {result.Candidate.BookName}{result.Candidate.Chapter}章{result.Candidate.StartVerse}-{result.Candidate.EndVerse}节");
                    ScriptureCandidateAccepted?.Invoke(result.Candidate);
                    StatusChanged?.Invoke($"AI经文候选已确认：{result.Candidate.BookName}{result.Candidate.Chapter}章{result.Candidate.StartVerse}节");
                }
                else
                {
                    StatusChanged?.Invoke($"AI经文候选未写入：{result.Reason}");
                }
            }
        }

        private static string BuildSystemPrompt()
        {
            return
                "你是 Canvas 程序内置的讲章经文理解助手。\n" +
                "你的任务：根据今日幻灯片项目上下文和实时 ASR 字幕，理解讲员当前可能在讲的主题、经卷、章节、经文。\n" +
                "你会额外收到按时间累计的全历史线索，请把这些线索与当前输入联合判断，不要只依据最新一句话。\n" +
                "ASR 可能来自任意语音识别平台，现场语言可能包含普通话、方言、地方口音、吴语、宁波话、余姚话、绍兴话或混合表达。\n" +
                "ASR 文本不一定可靠，可能存在方言或口音导致的误识别、同音词、近音词、断句错误、圣经书卷名/人物名/地名/神学词汇误写、现场噪音、重复、残句和口语化表达。\n" +
                "不要机械相信 ASR 字面文本。你必须先结合今日主题、幻灯片上下文、历史 ASR、已确认经文候选和圣经常识，推断讲员真实想表达的普通话语义，再判断是否指向经文。\n" +
                "像正常 AI 对话一样用简洁中文流式反馈理解。\n" +
                "当你认为某段 ASR 明确指向某处经文时，调用 propose_scripture_candidate 提出候选。\n" +
                "候选字段规则：只识别到经卷时只填 bookName；识别到章节时填 bookName+chapter；识别到具体经文时再填 startVerse/endVerse。\n" +
                "你只能提出候选，不能声称已经写入历史记录。\n" +
                "不确定时必须说明不确定，不要强行猜测具体章节。\n" +
                "如果只是普通讲道内容，没有足够证据，不要调用工具。\n" +
                "允许合理推测候选：当上下文与历史线索能支持时，可以提交 confidence >= 0.55 的候选。";
        }

        private void AppendHistoricalSignal(string signal)
        {
            string trimmed = (signal ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return;
            }

            if (trimmed.Length > MaxHistoricalSignalLineLength)
            {
                trimmed = trimmed.Substring(0, MaxHistoricalSignalLineLength) + "...";
            }

            string stamped = $"{DateTime.Now:HH:mm:ss} {trimmed}";
            lock (_stateLock)
            {
                if (_historicalSignals.Count > 0 &&
                    string.Equals(_historicalSignals[^1], stamped, StringComparison.Ordinal))
                {
                    return;
                }

                _historicalSignals.Add(stamped);
                if (_historicalSignals.Count > MaxHistoricalSignalCount)
                {
                    _historicalSignals.RemoveAt(0);
                }
            }
        }

        private string BuildHistoricalContextMessage()
        {
            List<string> historySnapshot;
            lock (_stateLock)
            {
                historySnapshot = _historicalSignals.ToList();
            }

            if (historySnapshot.Count == 0)
            {
                return string.Empty;
            }

            return "全历史线索（按时间）:\n" + string.Join("\n", historySnapshot);
        }

        private static string NormalizeSignal(string value)
        {
            return (value ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
        }

        private async Task EmitAssistantMessageAsync(string content, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            await _assistantRenderLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                AssistantMessageStarted?.Invoke();
                AssistantDeltaReceived?.Invoke(content);
            }
            finally
            {
                _assistantRenderLock.Release();
            }
        }
    }
}
