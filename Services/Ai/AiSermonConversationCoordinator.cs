using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
        private readonly AiSermonHistoryStore _historyStore;
        private readonly AiSermonSummaryService _summaryService;
        private readonly AiRealtimeUnderstandingScheduler _asrScheduler;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly SemaphoreSlim _asrSendGate = new(2, 2);
        private readonly SemaphoreSlim _assistantRenderLock = new(1, 1);
        private readonly SemaphoreSlim _sessionInitLock = new(1, 1);
        private readonly List<AiConversationMessage> _visibleMessages = new();
        private readonly List<string> _historicalSignals = new();
        private readonly object _stateLock = new();
        private long _latestAsrSequence;
        private const int MaxHistoricalSignalCount = 800;
        private const int MaxHistoricalSignalLineLength = 180;
        private const int MaxAsrPendingWindow = 2;
        private string _selectedSpeakerName = "未标记讲师";
        private bool _dialectSchemeEnabled;
        private readonly HashSet<string> _selectedDialectTags = new(StringComparer.Ordinal);
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
            ConfigManager config,
            AiSermonHistoryStore historyStore,
            AiSermonSummaryService summaryService,
            AiRealtimeUnderstandingScheduler asrScheduler)
        {
            _contextBuilder = contextBuilder ?? throw new ArgumentNullException(nameof(contextBuilder));
            _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
            _bibleService = bibleService ?? throw new ArgumentNullException(nameof(bibleService));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _historyStore = historyStore ?? throw new ArgumentNullException(nameof(historyStore));
            _summaryService = summaryService ?? throw new ArgumentNullException(nameof(summaryService));
            _asrScheduler = asrScheduler ?? throw new ArgumentNullException(nameof(asrScheduler));
            _asrScheduler.ProcessingFailed += ex => StatusChanged?.Invoke($"AI实时理解异常：{ex.Message}");
            _dialectSchemeEnabled = _config.AiSermonDialectSchemeEnabled;
            foreach (string tag in _config.AiSermonSelectedDialectTags)
            {
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    _selectedDialectTags.Add(tag.Trim());
                }
            }
        }

        public bool HasActiveSession => _session != null;
        public string CurrentSpeakerName => _session?.SpeakerName ?? _selectedSpeakerName;

        public async Task StartProjectAsync(int projectId, CancellationToken cancellationToken = default)
        {
            var context = await _contextBuilder.BuildAsync(projectId, cancellationToken).ConfigureAwait(false);
            var speaker = await _historyStore.GetOrCreateSpeakerAsync(_selectedSpeakerName).ConfigureAwait(false);
            _selectedSpeakerName = speaker.Name;
            LoadDialectTagsForSpeaker(_selectedSpeakerName);
            var historySession = await _historyStore.CreateSessionAsync(
                speaker.Id,
                context.ProjectId,
                context.ProjectName,
                "concise").ConfigureAwait(false);
            _session = new AiSermonSessionState
            {
                ProjectId = context.ProjectId,
                ProjectName = context.ProjectName,
                ProjectContext = context.ContextText,
                RuntimeContext = context.RuntimeContextText,
                SpeakerId = speaker.Id,
                SpeakerName = speaker.Name,
                HistorySessionId = historySession.Id,
                OutputMode = historySession.OutputMode,
                SpeakerStyleSummary = speaker.StyleSummary,
                SessionSummary = historySession.Summary,
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

        public async Task SetSpeakerAsync(string speakerName, CancellationToken cancellationToken = default)
        {
            var speaker = await _historyStore.GetOrCreateSpeakerAsync(speakerName).ConfigureAwait(false);
            _selectedSpeakerName = speaker.Name;
            LoadDialectTagsForSpeaker(_selectedSpeakerName);
            if (_session == null)
            {
                StatusChanged?.Invoke($"已选择讲师标签：{speaker.Name}");
                return;
            }

            _session.SpeakerId = speaker.Id;
            _session.SpeakerName = speaker.Name;
            _session.SpeakerStyleSummary = speaker.StyleSummary;
            if (_session.HistorySessionId > 0)
            {
                await _historyStore.UpdateSessionSpeakerAsync(_session.HistorySessionId, speaker.Id).ConfigureAwait(false);
            }
            StatusChanged?.Invoke($"已切换讲师标签：{speaker.Name}");
        }

        public async Task SetOutputModeAsync(string outputMode)
        {
            if (_session == null)
            {
                return;
            }

            string normalized = string.Equals(outputMode, "detailed", StringComparison.OrdinalIgnoreCase)
                ? "detailed"
                : "concise";
            _session.OutputMode = normalized;
            if (_session.HistorySessionId > 0)
            {
                await _historyStore.UpdateSessionOutputModeAsync(_session.HistorySessionId, normalized).ConfigureAwait(false);
            }
            StatusChanged?.Invoke(normalized == "detailed" ? "AI输出模式：详细" : "AI输出模式：简洁");
        }

        public Task SetDialectSchemeAsync(bool enabled, IReadOnlyList<string> dialectTags)
        {
            _dialectSchemeEnabled = enabled;
            _selectedDialectTags.Clear();
            if (dialectTags != null)
            {
                foreach (string tag in dialectTags)
                {
                    string value = (tag ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        _selectedDialectTags.Add(value);
                    }
                }
            }

            var activeDialectTags = GetActiveDialectTags();
            _dialectSchemeEnabled = activeDialectTags.Count > 0;
            _config.AiSermonDialectSchemeEnabled = _dialectSchemeEnabled;
            _config.AiSermonSelectedDialectTags = _selectedDialectTags.ToArray();
            if (_selectedDialectTags.Count == 0)
            {
                StatusChanged?.Invoke("语言：未选择");
            }
            else if (activeDialectTags.Count == 0)
            {
                StatusChanged?.Invoke("语言：国语");
            }
            else
            {
                StatusChanged?.Invoke($"语言：国语 + {string.Join("、", activeDialectTags)}");
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AiSpeakerSessionGroup>> GetHistoryGroupsAsync()
        {
            return _historyStore.GetSessionGroupsBySpeakerAsync();
        }

        public async Task<IReadOnlyList<string>> GetSpeakerNamesAsync()
        {
            var names = (await _historyStore.GetSpeakerNamesAsync().ConfigureAwait(false))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n)
                .ToList();
            if (!names.Contains("未标记讲师", StringComparer.Ordinal))
            {
                names.Insert(0, "未标记讲师");
            }

            return names;
        }

        public Task DeleteSpeakerAsync(string speakerName)
        {
            return _historyStore.ArchiveSpeakerAsync(speakerName);
        }

        public Task DeleteHistoryMessageAsync(int messageId)
        {
            return _historyStore.DeleteMessageAsync(messageId);
        }

        public Task DeleteHistorySessionAsync(int sessionId)
        {
            return _historyStore.DeleteSessionAsync(sessionId);
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
            return _asrScheduler.EnqueueAsync(
                turn,
                ProcessAsrWindowAsync,
                cancellationToken);
        }

        private async Task ProcessAsrWindowAsync(AiAsrSemanticWindowSnapshot snapshot, CancellationToken cancellationToken)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.WindowText))
            {
                return;
            }

            string prompt = _session == null
                ? "下面是一段实时 ASR 原文。ASR 可能来自任意语音识别平台，文本可能包含方言、口音、现场噪音、断句和同音误识别。" +
                  "请先纠错理解为可能的普通话语义，再判断讲员可能在讲什么。" +
                  BuildDialectPromptHint() +
                  "如果有明确或合理推测的经文候选，请通过工具提出候选；证据不足则只说明可能方向。\n\n" +
                  $"raw_asr_window：\n{snapshot.WindowText}"
                : "下面是一段实时 ASR 原文。ASR 可能来自任意语音识别平台，文本可能包含方言、口音、现场噪音、断句和同音误识别。" +
                  "请结合今日幻灯片上下文、讲师长期风格、本场摘要、全历史线索、最近对话和最近 ASR，先纠错理解为可能的普通话语义，再判断讲员当前可能在讲什么。" +
                  BuildDialectPromptHint() +
                  "如果有明确或合理推测的经文候选，请通过工具提出候选；证据不足则只说明可能方向。\n\n" +
                  $"raw_asr_window_version：{snapshot.Version}\n" +
                  $"raw_asr_window：\n{snapshot.WindowText}";
            await SendVisibleUserMessageAsync("asr", prompt, cancellationToken, asrSummarySnapshot: snapshot).ConfigureAwait(false);
        }

        private async Task SendVisibleUserMessageAsync(
            string name,
            string content,
            CancellationToken cancellationToken,
            long asrSeq = 0,
            AiAsrSemanticWindowSnapshot asrSummarySnapshot = null)
        {
            if (!_config.AiSermonEnabled)
            {
                StatusChanged?.Invoke("AI讲章理解未启用");
                return;
            }

            if (string.Equals(name, "asr", StringComparison.Ordinal))
            {
                await EnsureSessionForAsrAsync(cancellationToken).ConfigureAwait(false);
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
            await SaveHistoryMessageAsync(message).ConfigureAwait(false);

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
                TracePromptCacheLayout(request.Messages);

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
                    AiConversationMessage assistantMessage;
                    lock (_stateLock)
                    {
                        assistantMessage = new AiConversationMessage
                        {
                            Role = "assistant",
                            Content = result.Content
                        };
                        _visibleMessages.Add(assistantMessage);
                    }
                    await SaveHistoryMessageAsync(assistantMessage).ConfigureAwait(false);

                    if (isAsr)
                    {
                        await EmitAssistantMessageAsync(result.Content, cancellationToken).ConfigureAwait(false);
                    }

                    await UpdateSummariesAfterAssistantAsync(asrSummarySnapshot, result.Content).ConfigureAwait(false);
                }
                else if (!receivedAnyDelta)
                {
                    StatusChanged?.Invoke("DeepSeek已响应（本次无文本输出）。");
                    await UpdateSummariesAfterAssistantAsync(asrSummarySnapshot, string.Empty).ConfigureAwait(false);
                }

                if (receivedAnyDelta)
                {
                    StatusChanged?.Invoke("DeepSeek已返回结果。");
                }

                await HandleCandidatesAsync(result.ScriptureCandidates, cancellationToken).ConfigureAwait(false);
                if (result.PromptCacheHitTokens > 0 || result.PromptCacheMissTokens > 0)
                {
                    int totalCacheTokens = result.PromptCacheHitTokens + result.PromptCacheMissTokens;
                    int hitRate = totalCacheTokens <= 0
                        ? 0
                        : (int)Math.Round(result.PromptCacheHitTokens * 100d / totalCacheTokens);
                    StatusChanged?.Invoke($"AI缓存：hit={result.PromptCacheHitTokens}, miss={result.PromptCacheMissTokens}, 命中率={hitRate}%");
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
                    Content = BuildSystemPrompt(GetActiveDialectTags())
                }
            };

            AiSermonSessionState sessionSnapshot = _session;
            if (sessionSnapshot != null)
            {
                messages.Add(new AiConversationMessage
                {
                    Role = "user",
                    Name = "project_context",
                    Content = BuildStableProjectContext(sessionSnapshot)
                });

                string speakerProfile = BuildSpeakerProfileContext(sessionSnapshot);
                if (!string.IsNullOrWhiteSpace(speakerProfile))
                {
                    messages.Add(new AiConversationMessage
                    {
                        Role = "user",
                        Name = "speaker_profile",
                        Content = speakerProfile
                    });
                }
            }

            List<AiConversationMessage> visibleSnapshot;
            lock (_stateLock)
            {
                visibleSnapshot = _visibleMessages.ToList();
            }

            string sessionContext = BuildSessionRuntimeContext(sessionSnapshot);
            if (!string.IsNullOrWhiteSpace(sessionContext))
            {
                messages.Add(new AiConversationMessage
                {
                    Role = "user",
                    Name = "session_context",
                    Content = sessionContext
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

            var visibleWindow = BuildVisibleTailMessages(visibleSnapshot, currentMessageName);
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
                    await UpdateSpeakerStyleSummaryAsync(string.Empty, null, result.Candidate).ConfigureAwait(false);
                    ScriptureCandidateAccepted?.Invoke(result.Candidate);
                    StatusChanged?.Invoke($"AI经文候选已确认：{result.Candidate.BookName}{result.Candidate.Chapter}章{result.Candidate.StartVerse}节");
                }
                else
                {
                    StatusChanged?.Invoke($"AI经文候选未写入：{result.Reason}");
                }
            }
        }

        private static string BuildSystemPrompt(IReadOnlyCollection<string> activeDialectTags)
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
                "允许合理推测候选：当上下文与历史线索能支持时，可以提交 confidence >= 0.55 的候选。\n" +
                BuildDialectSystemHint(activeDialectTags);
        }

        private static string BuildStableProjectContext(AiSermonSessionState session)
        {
            var parts = new List<string>
            {
                $"今日讲章稳定上下文：\n{session.RuntimeContext}",
                $"讲师标签：{session.SpeakerName}"
            };

            parts.Add(string.Equals(session.OutputMode, "detailed", StringComparison.OrdinalIgnoreCase)
                ? "输出偏好：详细说明判断理由、历史线索和不确定点。"
                : "输出偏好：简洁给出当前理解、候选经文和必要提醒。");
            return string.Join("\n\n", parts);
        }

        private static string BuildSpeakerProfileContext(AiSermonSessionState session)
        {
            if (!string.IsNullOrWhiteSpace(session.SpeakerStyleSummary))
            {
                return
                    "讲师长期画像摘要（用于预测该讲师下一步可能引用的经文范围、常见章节、讲道习惯和表达偏好）：\n" +
                    session.SpeakerStyleSummary;
            }

            return string.Empty;
        }

        private static string BuildSessionRuntimeContext(AiSermonSessionState session)
        {
            if (session == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(session.SessionSummary))
            {
                return $"本场动态摘要：\n{session.SessionSummary}";
            }

            return string.Empty;
        }

        private static IReadOnlyList<AiConversationMessage> BuildVisibleTailMessages(
            IReadOnlyList<AiConversationMessage> visibleSnapshot,
            string currentMessageName)
        {
            if (visibleSnapshot == null || visibleSnapshot.Count == 0)
            {
                return Array.Empty<AiConversationMessage>();
            }

            var tail = visibleSnapshot
                .Where(m => string.Equals(currentMessageName, "project_context", StringComparison.Ordinal) ||
                            !string.Equals(m.Name, "project_context", StringComparison.Ordinal))
                .Where(m => !string.Equals(m.Name, "asr", StringComparison.Ordinal))
                .TakeLast(8)
                .ToList();

            if (string.Equals(currentMessageName, "asr", StringComparison.Ordinal))
            {
                var currentAsr = visibleSnapshot
                    .LastOrDefault(m => string.Equals(m.Name, "asr", StringComparison.Ordinal));
                if (currentAsr != null)
                {
                    tail.Add(currentAsr);
                }
            }

            return tail;
        }

        private string BuildDialectPromptHint()
        {
            if (!_dialectSchemeEnabled)
            {
                return string.Empty;
            }

            var activeDialectTags = GetActiveDialectTags();
            if (activeDialectTags.Count == 0)
            {
                return string.Empty;
            }

            string labels = string.Join("、", activeDialectTags);
            string perDialect = string.Join(
                "；",
                activeDialectTags.Select(tag => $"若文本疑似{tag}音系，优先尝试{tag}同音/近音映射后再判经文"));
            return $"已启用方言增强，当前标签：{labels}。{perDialect}。";
        }

        private static string BuildDialectSystemHint(IReadOnlyCollection<string> activeDialectTags)
        {
            if (activeDialectTags == null || activeDialectTags.Count == 0)
            {
                return "语言基线：国语。若仅国语场景，不要输出任何方言增强策略。";
            }

            string labels = string.Join("、", activeDialectTags);
            string bullets = string.Join(
                "\n",
                activeDialectTags.Select(tag => $"- {tag}：优先进行该方言口音的同音、近音和连读纠错，再回归普通话语义"));
            return $"语言基线：国语；方言增强标签={labels}。\n方言纠错策略：\n{bullets}";
        }

        private List<string> GetActiveDialectTags()
        {
            return _selectedDialectTags
                .Where(tag => !string.Equals(tag, "国语", StringComparison.Ordinal))
                .ToList();
        }

        private void LoadDialectTagsForSpeaker(string speaker)
        {
            _selectedDialectTags.Clear();
            var bindings = _config.AiSermonSpeakerDialectBindings ?? Array.Empty<AiSpeakerDialectBindingEntry>();
            var binding = bindings.FirstOrDefault(entry =>
                entry != null && string.Equals(entry.Speaker?.Trim(), speaker, StringComparison.Ordinal));
            if (binding?.Tags != null)
            {
                foreach (string tag in binding.Tags)
                {
                    string value = (tag ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        _selectedDialectTags.Add(value);
                    }
                }
            }

            if (_selectedDialectTags.Count == 0)
            {
                _selectedDialectTags.Add("国语");
            }

            _config.AiSermonSelectedDialectTags = _selectedDialectTags.ToArray();
            _dialectSchemeEnabled = GetActiveDialectTags().Count > 0;
            _config.AiSermonDialectSchemeEnabled = _dialectSchemeEnabled;
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

            lock (_stateLock)
            {
                if (_historicalSignals.Count > 0 &&
                    string.Equals(_historicalSignals[^1], trimmed, StringComparison.Ordinal))
                {
                    return;
                }

                _historicalSignals.Add(trimmed);
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

            return "全历史线索（按顺序，已去除秒级时间以提升缓存稳定性）:\n" + string.Join("\n", historySnapshot.TakeLast(80));
        }

        private static void TracePromptCacheLayout(IReadOnlyList<AiConversationMessage> messages)
        {
            if (messages == null || messages.Count == 0)
            {
                return;
            }

            var parts = messages
                .Select((message, index) =>
                {
                    string name = string.IsNullOrWhiteSpace(message.Name) ? message.Role : message.Name;
                    string hash = StableShortHash(message.Content);
                    return $"{index}:{name}:{(message.Content ?? string.Empty).Length}:{hash}";
                });
            Debug.WriteLine("[AiSermon][CacheLayout] " + string.Join(" | ", parts));
        }

        private static string StableShortHash(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            byte[] hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash, 0, 4);
        }

        private async Task SaveHistoryMessageAsync(AiConversationMessage message)
        {
            if (_session == null || _session.HistorySessionId <= 0 || message == null)
            {
                return;
            }

            try
            {
                await _historyStore.AppendMessageAsync(
                    _session.HistorySessionId,
                    message.Role,
                    message.Content,
                    message.Name).ConfigureAwait(false);
            }
            catch
            {
                // 历史记录不能影响实时 ASR 理解链路。
            }
        }

        private async Task UpdateSpeakerStyleSummaryAsync(
            string assistantContent,
            AiAsrSemanticWindowSnapshot snapshot,
            AiScriptureCandidate scriptureCandidate = null)
        {
            if (_session == null || _session.SpeakerId <= 0)
            {
                return;
            }

            string summary = _summaryService.BuildSpeakerStyleSummary(
                _session.SpeakerStyleSummary,
                assistantContent,
                snapshot,
                scriptureCandidate);
            if (string.Equals(summary, _session.SpeakerStyleSummary, StringComparison.Ordinal))
            {
                return;
            }

            _session.SpeakerStyleSummary = summary;
            await _historyStore.UpdateSpeakerStyleSummaryAsync(_session.SpeakerId, summary).ConfigureAwait(false);
        }

        private async Task UpdateSummariesAfterAssistantAsync(
            AiAsrSemanticWindowSnapshot snapshot,
            string assistantContent)
        {
            if (_session == null || _session.HistorySessionId <= 0)
            {
                return;
            }

            string sessionSummary = _summaryService.BuildSessionSummary(_session.SessionSummary, snapshot, assistantContent);
            if (!string.Equals(sessionSummary, _session.SessionSummary, StringComparison.Ordinal))
            {
                _session.SessionSummary = sessionSummary;
                await _historyStore.UpdateSessionSummaryAsync(_session.HistorySessionId, sessionSummary).ConfigureAwait(false);
            }

            await UpdateSpeakerStyleSummaryAsync(assistantContent, snapshot).ConfigureAwait(false);
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

        private async Task EnsureSessionForAsrAsync(CancellationToken cancellationToken)
        {
            if (_session != null)
            {
                return;
            }

            await _sessionInitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_session != null)
                {
                    return;
                }

                var speaker = await _historyStore.GetOrCreateSpeakerAsync(_selectedSpeakerName).ConfigureAwait(false);
                _selectedSpeakerName = speaker.Name;
                LoadDialectTagsForSpeaker(_selectedSpeakerName);

                var historySession = await _historyStore.CreateSessionAsync(
                    speaker.Id,
                    projectId: 0,
                    title: $"实时会话 {DateTime.Now:MM-dd HH:mm}",
                    outputMode: "concise").ConfigureAwait(false);

                _session = new AiSermonSessionState
                {
                    ProjectId = 0,
                    ProjectName = "未绑定项目",
                    ProjectContext = string.Empty,
                    RuntimeContext = "当前会话由实时识别自动创建，尚未绑定幻灯片项目。",
                    SpeakerId = speaker.Id,
                    SpeakerName = speaker.Name,
                    HistorySessionId = historySession.Id,
                    OutputMode = historySession.OutputMode,
                    SpeakerStyleSummary = speaker.StyleSummary,
                    SessionSummary = historySession.Summary,
                    StartedAt = DateTimeOffset.Now
                };

                StatusChanged?.Invoke("已自动创建实时历史会话");
            }
            finally
            {
                _sessionInitLock.Release();
            }
        }
    }
}
