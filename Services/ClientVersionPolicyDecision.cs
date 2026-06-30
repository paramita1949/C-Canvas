using System;

namespace ImageColorChanger.Services
{
    public sealed class ClientVersionPolicyDecision
    {
        public static ClientVersionPolicyDecision CreateNone()
        {
            return new ClientVersionPolicyDecision
            {
                Action = "none"
            };
        }

        public string Action { get; set; } = "none";
        public string CurrentVersion { get; set; } = string.Empty;
        public string RecommendedBelow { get; set; } = string.Empty;
        public string RequiredBelow { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;

        public bool RequiresUpgrade =>
            string.Equals(Action, "required", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Action, "force", StringComparison.OrdinalIgnoreCase);

        public bool ShouldRecommend =>
            string.Equals(Action, "recommended", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Action, "recommend", StringComparison.OrdinalIgnoreCase);

        public bool IsOptional =>
            string.Equals(Action, "optional", StringComparison.OrdinalIgnoreCase);

        public bool HasAction =>
            RequiresUpgrade ||
            ShouldRecommend ||
            IsOptional;
    }
}
