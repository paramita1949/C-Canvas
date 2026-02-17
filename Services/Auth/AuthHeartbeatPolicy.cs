using System;

namespace ImageColorChanger.Services.Auth
{
    internal sealed class AuthHeartbeatPolicy
    {
        internal readonly struct LogoutDecision
        {
            public LogoutDecision(bool shouldForceLogout, string title, string message)
            {
                ShouldForceLogout = shouldForceLogout;
                Title = title;
                Message = message;
            }

            public bool ShouldForceLogout { get; }
            public string Title { get; }
            public string Message { get; }
        }

        internal readonly struct OfflineDecision
        {
            public OfflineDecision(bool exceeded, double offlineDays)
            {
                Exceeded = exceeded;
                OfflineDays = offlineDays;
            }

            public bool Exceeded { get; }
            public double OfflineDays { get; }
        }

        public LogoutDecision EvaluateFailure(string reason, string serverMessage, string defaultMessage)
        {
            string title = "登录已失效";
            string message = string.IsNullOrWhiteSpace(defaultMessage) ? "账号已失效" : defaultMessage;

            if (reason == "device_unbound" || reason == "device_reset" || reason == "device_mismatch")
            {
                return new LogoutDecision(true, "设备验证失败", "您的设备已被解绑，请重新登录");
            }

            if (reason == "disabled")
            {
                return new LogoutDecision(true, "账号已被禁用", "您的账号已被禁用，请联系管理员");
            }

            if (reason == "expired")
            {
                return new LogoutDecision(true, "账号已过期", "您的账号已过期，请联系管理员续期");
            }

            if (reason == "session_expired")
            {
                return new LogoutDecision(true, "登录已失效", "登录已失效，请重新登录");
            }

            if (reason == "user_not_found")
            {
                return new LogoutDecision(true, "账号不存在", "账号不存在，请联系管理员");
            }

            if (!string.IsNullOrWhiteSpace(serverMessage) &&
                (serverMessage.Contains("设备已被") || serverMessage.Contains("解绑")))
            {
                return new LogoutDecision(true, "设备验证失败", "您的设备已被解绑，请重新登录");
            }

            return new LogoutDecision(false, title, message);
        }

        public OfflineDecision EvaluateOffline(DateTime? lastSuccessfulHeartbeat, DateTime now, int maxOfflineDays)
        {
            if (lastSuccessfulHeartbeat == null)
            {
                return new OfflineDecision(false, 0);
            }

            double offlineDays = (now - lastSuccessfulHeartbeat.Value).TotalDays;
            return new OfflineDecision(offlineDays > maxOfflineDays, offlineDays);
        }
    }
}
