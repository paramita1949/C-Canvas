using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Services.Auth;
using ImageColorChanger.Services.Interfaces;

namespace ImageColorChanger.Services
{
    public sealed class ClientUsageReportService
    {
        private static readonly string[] DefaultEndpoints =
            AuthEndpointCatalog.ApiBaseUrls
                .Select(baseUrl => baseUrl.TrimEnd('/') + AuthEndpointCatalog.ClientUsageReportEndpoint)
                .ToArray();

        private readonly HttpClient _httpClient;
        private readonly Func<string> _hardwareIdProvider;
        private readonly Func<string> _versionProvider;
        private readonly Func<string> _osVersionProvider;
        private readonly Func<string> _deviceNameProvider;
        private readonly string[] _endpoints;

        public ClientVersionPolicyDecision LastPolicy { get; private set; } = ClientVersionPolicyDecision.CreateNone();

        public ClientUsageReportService(IAuthService authService)
            : this(
                new HttpClient { Timeout = TimeSpan.FromSeconds(5) },
                () => authService.GetCurrentHardwareId(),
                UpdateService.GetCurrentVersion,
                GetDefaultOsVersion,
                GetDefaultDeviceName,
                DefaultEndpoints)
        {
        }

        public ClientUsageReportService(
            HttpClient httpClient,
            Func<string> hardwareIdProvider,
            Func<string> versionProvider,
            Func<string> osVersionProvider,
            Func<string> deviceNameProvider,
            string[] endpoints)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _hardwareIdProvider = hardwareIdProvider ?? throw new ArgumentNullException(nameof(hardwareIdProvider));
            _versionProvider = versionProvider ?? throw new ArgumentNullException(nameof(versionProvider));
            _osVersionProvider = osVersionProvider ?? throw new ArgumentNullException(nameof(osVersionProvider));
            _deviceNameProvider = deviceNameProvider ?? throw new ArgumentNullException(nameof(deviceNameProvider));
            _endpoints = endpoints == null || endpoints.Length == 0
                ? DefaultEndpoints
                : endpoints;
        }

        public async Task<ClientVersionPolicyDecision> ReportStartupAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var hardwareId = SafeGet(_hardwareIdProvider);
                var appVersion = SafeGet(_versionProvider);

                if (string.IsNullOrWhiteSpace(hardwareId) || string.IsNullOrWhiteSpace(appVersion))
                {
                    return LastPolicy;
                }

                var payload = new Dictionary<string, string>
                {
                    ["hardware_id"] = hardwareId,
                    ["app_version"] = appVersion,
                    ["channel"] = "stable",
                    ["os_version"] = SafeGet(_osVersionProvider),
                    ["device_name"] = SafeGet(_deviceNameProvider)
                };

                var json = JsonSerializer.Serialize(payload);
                foreach (var endpoint in _endpoints)
                {
                    var url = string.IsNullOrWhiteSpace(endpoint) ? null : endpoint.Trim();
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        continue;
                    }

                    try
                    {
                        using var content = new StringContent(json, Encoding.UTF8, "application/json");
                        using var response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
                        if (response.IsSuccessStatusCode)
                        {
                            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                            LastPolicy = ParseVersionPolicyDecision(responseJson);
                            return LastPolicy;
                        }
                    }
                    catch
                    {
                        // 使用统计不能影响客户端启动或投影流程。
                    }
                }
            }
            catch
            {
                // 使用统计是后台辅助链路，任何异常都静默处理。
            }

            return LastPolicy;
        }

        private static ClientVersionPolicyDecision ParseVersionPolicyDecision(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return ClientVersionPolicyDecision.CreateNone();
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return ClientVersionPolicyDecision.CreateNone();
                }

                JsonElement policy = root;
                if (root.TryGetProperty("policy", out var policyElement) && policyElement.ValueKind == JsonValueKind.Object)
                {
                    policy = policyElement;
                }
                else if (root.TryGetProperty("version_policy", out var versionPolicyElement) && versionPolicyElement.ValueKind == JsonValueKind.Object)
                {
                    policy = versionPolicyElement;
                }

                return new ClientVersionPolicyDecision
                {
                    Action = NormalizeAction(GetFirstString(policy, "action")),
                    CurrentVersion = GetFirstString(policy, "current_version", "latest_version"),
                    RecommendedBelow = GetFirstString(policy, "recommended_below", "min_supported_version"),
                    RequiredBelow = GetFirstString(policy, "required_below", "force_upgrade_below"),
                    Title = GetFirstString(policy, "title", "notice_title"),
                    Message = GetFirstString(policy, "message", "notice_content")
                };
            }
            catch
            {
                return ClientVersionPolicyDecision.CreateNone();
            }
        }

        private static string NormalizeAction(string action)
        {
            var value = (action ?? string.Empty).Trim().ToLowerInvariant();
            return value switch
            {
                "force" => "required",
                "recommend" => "recommended",
                "required" => "required",
                "recommended" => "recommended",
                "optional" => "optional",
                _ => "none"
            };
        }

        private static string GetFirstString(JsonElement element, params string[] names)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    var text = value.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }

            return string.Empty;
        }

        private static string SafeGet(Func<string> provider)
        {
            try
            {
                return provider()?.Trim() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetDefaultOsVersion()
        {
            return Environment.OSVersion.VersionString;
        }

        private static string GetDefaultDeviceName()
        {
            return Environment.MachineName;
        }
    }
}
