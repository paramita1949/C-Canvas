using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Services;

namespace Canvas.TextEditor.Tests.Services
{
    public sealed class UpdateServiceEndpointTests
    {
        private const string PermanentDomain = "https://pan.019890311.xyz/raw";
        private const string FastEndpoint = "http://fast.upgrade.test/raw";
        private const string HungEndpoint = "http://hung.upgrade.test/raw";

        [Fact]
        public void SetDownloadBaseUrls_NormalizesAndReplacesConfiguredEndpoints()
        {
            try
            {
                UpdateService.SetDownloadBaseUrls(new[]
                {
                    " https://download.example.test/canvas/ ",
                    "",
                    "not-a-url",
                    "http://10.0.0.5:8080/releases"
                });

                Assert.Equal(
                    new[]
                    {
                        "https://download.example.test/canvas",
                        "http://10.0.0.5:8080/releases"
                    },
                    UpdateService.GetDownloadBaseUrls());
            }
            finally
            {
                UpdateService.SetDownloadBaseUrls(null);
            }
        }

        [Fact]
        public void PermanentDownloadBaseUrl_IsTheSingleAllowedPermanentDomain()
        {
            Assert.Equal(PermanentDomain, UpdateService.PermanentDownloadBaseUrl);
        }

        [Fact]
        public void SetDownloadBaseUrls_EmptyInputFallsBackToPermanentDomain()
        {
            UpdateService.SetDownloadBaseUrls(Array.Empty<string>());

            Assert.Equal(new[] { PermanentDomain }, UpdateService.GetDownloadBaseUrls());

            UpdateService.SetDownloadBaseUrls(null);

            Assert.Equal(new[] { PermanentDomain }, UpdateService.GetDownloadBaseUrls());
        }

        [Fact]
        public async Task CheckForUpdatesAsync_HungEndpointDoesNotStallTheCheckAndHealthyEndpointWins()
        {
            try
            {
                // 第一个地址完全无响应，第二个地址正常：检查必须在有限时间内从健康地址拿到结果。
                UpdateService.SetHttpClientForTests(new HttpClient(new FakeUpgradeHandler()));
                UpdateService.SetTimeoutsForTests(updateCheckOverallSeconds: 10, healthProbeOverallSeconds: 3, healthProbeSeconds: 2);
                UpdateService.SetDownloadBaseUrls(new[] { HungEndpoint, FastEndpoint });

                var stopwatch = Stopwatch.StartNew();
                var versionInfo = await UpdateService.CheckForUpdatesAsync();
                stopwatch.Stop();

                Assert.NotNull(versionInfo);
                Assert.Equal("999.0.0", versionInfo.Version);
                Assert.Contains(versionInfo.Files, file => file.FileName == "CanvasCast.dll");
                Assert.Equal("更新说明", versionInfo.ReleaseNotes);
                Assert.False(UpdateService.LastCheckFailedToReachAnyEndpoint);
                Assert.True(
                    stopwatch.Elapsed < TimeSpan.FromSeconds(8),
                    $"升级检查耗时 {stopwatch.Elapsed}，说明仍然卡在无响应地址上。");

                // 健康地址被记住：后续检查/下载会从它开始，而不是被卡住的地址。
                var ordered = UpdateService.GetOrderedDownloadBaseUrlsSnapshot();
                Assert.Equal(FastEndpoint, ordered[0]);
                Assert.Equal(new[] { FastEndpoint, HungEndpoint }, ordered.ToArray());

                // UI 依赖的缓存结果也要带上，用于“发现新版本”提示。
                Assert.NotNull(UpdateService.GetLastCheckedVersionInfo());
                Assert.Equal("999.0.0", UpdateService.GetLastCheckedVersionInfo().Version);
            }
            finally
            {
                UpdateService.SetHttpClientForTests(null);
                UpdateService.SetTimeoutsForTests();
                UpdateService.SetDownloadBaseUrls(null);
                UpdateService.ClearLastCheckedVersionInfoForTests();
            }
        }

        [Fact]
        public async Task CheckForUpdatesAsync_AllEndpointsUnreachable_ReportsUnreachableInsteadOfUpToDate()
        {
            try
            {
                UpdateService.SetHttpClientForTests(new HttpClient(new FakeUpgradeHandler()));
                UpdateService.SetTimeoutsForTests(updateCheckOverallSeconds: 3, healthProbeOverallSeconds: 1, healthProbeSeconds: 1);
                UpdateService.SetDownloadBaseUrls(new[] { HungEndpoint, "http://hung2.upgrade.test/raw" });

                var stopwatch = Stopwatch.StartNew();
                var versionInfo = await UpdateService.CheckForUpdatesAsync();
                stopwatch.Stop();

                Assert.Null(versionInfo);

                // 关键：所有地址都连不上时必须能与“已是最新版本”区分开，UI 才能给出正确弹窗。
                Assert.True(UpdateService.LastCheckFailedToReachAnyEndpoint);
                Assert.True(
                    stopwatch.Elapsed < TimeSpan.FromSeconds(10),
                    $"全部地址无响应时耗时 {stopwatch.Elapsed}，超出整体时限预期。");
            }
            finally
            {
                UpdateService.SetHttpClientForTests(null);
                UpdateService.SetTimeoutsForTests();
                UpdateService.SetDownloadBaseUrls(null);
            }
        }

        [Fact]
        public void UpdateServiceSource_EmbedsOnlyThePermanentDomainAndNoIpLiteral()
        {
            string source = File.ReadAllText(FindWorkspaceFile("Services/UpdateService.cs"));

            var urls = Regex.Matches(source, "(?i)https?://[^\\s\"']+")
                .Select(match => match.Value.TrimEnd('/', '"', '\'', ',', ';', ')'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Assert.Equal(new[] { PermanentDomain.TrimEnd('/') }, urls);

            // 旧版内置的是 http://<IP>:<port> 形式的升级地址，这里确保不再出现任何 IP:端口 主机。
            var ipHosts = Regex.Matches(source, @"\d{1,3}(?:\.\d{1,3}){3}:\d+")
                .Select(match => match.Value)
                .ToArray();
            Assert.Empty(ipHosts);
            Assert.DoesNotContain("http://", source, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ManualUpdateCheck_UiAlwaysShowsPopupFeedback()
        {
            string source = File.ReadAllText(FindWorkspaceFile("UI/MainWindow.Import.cs"));

            Assert.Contains("private async System.Threading.Tasks.Task OpenVersionUpgradeAsync()", source);
            // 无新版本时也要有明确反馈，而不是静默返回。
            Assert.Contains("当前已是最新版本", source);
            // 所有地址不可达时给出失败弹窗。
            Assert.Contains("UpdateService.LastCheckFailedToReachAnyEndpoint", source);
            Assert.Contains("检查更新失败", source);
            // 反馈统一走弹窗。
            Assert.Contains("System.Windows.MessageBox.Show", source);
        }

        private static string FindWorkspaceFile(string relativePath)
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException(relativePath);
        }

        /// <summary>
        /// 假升级服务器：fast.upgrade.test 返回一个必然比当前版本高的版本，其它地址永不响应。
        /// </summary>
        private sealed class FakeUpgradeHandler : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var uri = request.RequestUri;

                if (uri == null || !string.Equals(uri.Host, "fast.upgrade.test", StringComparison.OrdinalIgnoreCase))
                {
                    // 模拟“卡死”的地址：一直不返回，只能靠超时收尾。
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    throw new OperationCanceledException(cancellationToken);
                }

                switch (uri.AbsolutePath)
                {
                    case "/raw/latest.txt":
                        return Text("999.0.0");
                    case "/raw/v999.0.0/files.txt":
                        return Text("CanvasCast.dll\nResources.pak\n");
                    case "/raw/v999.0.0/read.txt":
                        return Text("更新说明");
                    default:
                        return new HttpResponseMessage(HttpStatusCode.NotFound);
                }
            }

            private static HttpResponseMessage Text(string content)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(content, Encoding.UTF8)
                };
            }
        }
    }
}
