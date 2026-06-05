using System;

namespace ImageColorChanger.Services.Licensing
{
    public sealed record FeatureGateResult(
        bool IsAllowed,
        string FeatureName,
        string ReasonCode,
        string UserMessage,
        DateTimeOffset? ExpiresAt,
        bool IsGrace);
}
