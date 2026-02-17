using System;

namespace ImageColorChanger.Services.Auth
{
    internal sealed class AuthProjectionAccessPolicy
    {
        internal enum DenyReason
        {
            None = 0,
            NotAuthenticated = 1,
            TrialInvalid = 2,
            TokenInvalid = 3,
            ExpirationUnknown = 4,
            Expired = 5
        }

        internal readonly struct AccessDecision
        {
            public AccessDecision(bool allowed, DenyReason reason)
            {
                Allowed = allowed;
                Reason = reason;
            }

            public bool Allowed { get; }
            public DenyReason Reason { get; }
        }

        public AccessDecision Evaluate(
            bool isAuthenticated,
            bool isTrialStatusValid,
            bool authTokensValid,
            DateTime? expiresAt,
            DateTime estimatedServerTime)
        {
            if (!isAuthenticated)
            {
                if (!isTrialStatusValid)
                {
                    return new AccessDecision(false, DenyReason.TrialInvalid);
                }

                return new AccessDecision(false, DenyReason.NotAuthenticated);
            }

            if (!authTokensValid)
            {
                return new AccessDecision(false, DenyReason.TokenInvalid);
            }

            if (expiresAt == null)
            {
                return new AccessDecision(false, DenyReason.ExpirationUnknown);
            }

            if (estimatedServerTime >= expiresAt.Value)
            {
                return new AccessDecision(false, DenyReason.Expired);
            }

            return new AccessDecision(true, DenyReason.None);
        }
    }
}
