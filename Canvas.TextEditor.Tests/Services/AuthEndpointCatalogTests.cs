using System;
using System.Linq;
using ImageColorChanger.Services.Auth;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class AuthEndpointCatalogTests
    {
        [Fact]
        public void ApiBaseUrls_Should_TargetSupabaseFunctionsOnly()
        {
            var urls = AuthEndpointCatalog.ApiBaseUrls;

            Assert.Single(urls);
            Assert.Equal("https://xndazekofkznjnxguvys.supabase.co/functions/v1", urls[0]);
            Assert.All(urls, url =>
            {
                Assert.Contains("supabase.co", url, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("jiucai", url, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("xian.edu", url, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("019890311", url, StringComparison.OrdinalIgnoreCase);
            });
        }

        [Fact]
        public void AuthEndpoints_Should_UseSupabaseFunctionNames_NotLegacyCfPaths()
        {
            var endpoints = new[]
            {
                AuthEndpointCatalog.VerifyEndpoint,
                AuthEndpointCatalog.HeartbeatEndpoint,
                AuthEndpointCatalog.NoticeAckEndpoint,
                AuthEndpointCatalog.SendVerificationCodeEndpoint,
                AuthEndpointCatalog.ResetPasswordEndpoint,
                AuthEndpointCatalog.RegisterEndpoint,
                AuthEndpointCatalog.ResetDevicesEndpoint,
                AuthEndpointCatalog.ReachabilityEndpoint,
                AuthEndpointCatalog.ClientUsageReportEndpoint
            };

            Assert.All(endpoints, endpoint =>
            {
                Assert.StartsWith("/canvas-", endpoint, StringComparison.Ordinal);
                Assert.DoesNotContain("/api/auth", endpoint, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("/api/user", endpoint, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("/api/client", endpoint, StringComparison.OrdinalIgnoreCase);
            });

            Assert.Equal(endpoints.Length, endpoints.Distinct(StringComparer.Ordinal).Count());
        }
    }
}
