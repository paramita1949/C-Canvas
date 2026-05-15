using System;
using System.Collections.Generic;

namespace ImageColorChanger.Services.Ai
{
    public sealed class AiConversationMessage
    {
        public string Role { get; init; } = "user";
        public string Name { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
    }

    public sealed class AiChatRequest
    {
        public IReadOnlyList<AiConversationMessage> Messages { get; init; } = Array.Empty<AiConversationMessage>();
        public string UserId { get; init; } = string.Empty;
        public bool EnableScriptureTool { get; init; } = true;
    }

    public sealed class AiChatStreamResult
    {
        public string Content { get; init; } = string.Empty;
        public IReadOnlyList<AiScriptureCandidate> ScriptureCandidates { get; init; } = Array.Empty<AiScriptureCandidate>();
        public int PromptCacheHitTokens { get; init; }
        public int PromptCacheMissTokens { get; init; }
    }

    public sealed class AiProjectContextEnvelope
    {
        public int ProjectId { get; init; }
        public string ProjectName { get; init; } = string.Empty;
        public string ContextText { get; init; } = string.Empty;
        public string RuntimeContextText { get; init; } = string.Empty;
        public IReadOnlyList<string> ExplicitReferences { get; init; } = Array.Empty<string>();
    }

    public sealed class AiAsrTurnEnvelope
    {
        public string TurnId { get; init; } = string.Empty;
        public string Text { get; init; } = string.Empty;
        public bool IsFinal { get; init; }
        public DateTimeOffset CapturedAt { get; init; }
        public string Source { get; init; } = "realtime-asr";
    }

    public sealed class AiScriptureCandidate
    {
        public string BookName { get; init; } = string.Empty;
        public int BookId { get; init; }
        public int Chapter { get; init; }
        public int StartVerse { get; init; }
        public int EndVerse { get; init; }
        public double Confidence { get; init; }
        public string Reason { get; init; } = string.Empty;
        public string EvidenceText { get; init; } = string.Empty;
        public string SourceTurnId { get; init; } = string.Empty;
    }

    public sealed class AiScriptureCandidateValidationResult
    {
        public bool Accepted { get; init; }
        public string Reason { get; init; } = string.Empty;
        public AiScriptureCandidate Candidate { get; init; }

        public static AiScriptureCandidateValidationResult Accept(AiScriptureCandidate candidate)
            => new() { Accepted = true, Candidate = candidate };

        public static AiScriptureCandidateValidationResult Reject(string reason)
            => new() { Accepted = false, Reason = reason };
    }

    public sealed class AiSermonSessionState
    {
        public int ProjectId { get; init; }
        public string ProjectName { get; init; } = string.Empty;
        public string ProjectContext { get; init; } = string.Empty;
        public string RuntimeContext { get; init; } = string.Empty;
        public int SpeakerId { get; set; }
        public string SpeakerName { get; set; } = "未标记讲师";
        public int HistorySessionId { get; set; }
        public string OutputMode { get; set; } = "concise";
        public string SpeakerStyleSummary { get; set; } = string.Empty;
        public string SessionSummary { get; set; } = string.Empty;
        public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;
    }
}
