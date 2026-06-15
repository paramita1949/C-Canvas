namespace ImageColorChanger.Services.Auth
{
    internal static class AuthEndpointCatalog
    {
        private const string SupabaseFunctionsBaseUrl = "https://xndazekofkznjnxguvys.supabase.co/functions/v1";

        public static string[] ApiBaseUrls => new[] { SupabaseFunctionsBaseUrl };

        public const string VerifyEndpoint = "/canvas-auth-verify";
        public const string HeartbeatEndpoint = "/canvas-auth-heartbeat";
        public const string NoticeAckEndpoint = "/canvas-auth-notice-ack";
        public const string SendVerificationCodeEndpoint = "/canvas-user-send-verification-code";
        public const string ResetPasswordEndpoint = "/canvas-user-reset-password";
        public const string RegisterEndpoint = "/canvas-user-register";
        public const string ResetDevicesEndpoint = "/canvas-user-reset-devices";
        public const string ReachabilityEndpoint = "/canvas-auth-health";
        public const string ClientUsageReportEndpoint = "/canvas-client-version-report";
    }
}
