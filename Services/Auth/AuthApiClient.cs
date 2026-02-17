using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace ImageColorChanger.Services.Auth
{
    internal sealed class AuthApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly string[] _apiBaseUrls;

        public AuthApiClient(HttpClient httpClient, string[] apiBaseUrls)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _apiBaseUrls = apiBaseUrls ?? throw new ArgumentNullException(nameof(apiBaseUrls));
            if (_apiBaseUrls.Length == 0)
            {
                throw new ArgumentException("apiBaseUrls must not be empty", nameof(apiBaseUrls));
            }
        }

        public string CurrentApiBaseUrl { get; private set; }

        public async Task<HttpResponseMessage> ExecuteWithFailoverAsync(
            Func<string, Task<HttpResponseMessage>> requestFunc,
            Action<string, int, int, string> onServerSwitching,
            int timeoutSeconds = 20,
            bool allowFailoverOnFailure = true)
        {
            Exception lastException = null;
            int attemptNumber = 0;
            int totalServers = _apiBaseUrls.Length;

            foreach (var apiUrl in _apiBaseUrls)
            {
                attemptNumber++;

                try
                {
                    CurrentApiBaseUrl = apiUrl;
                    onServerSwitching?.Invoke(apiUrl, attemptNumber, totalServers, $"正在连接服务器 ({attemptNumber}/{totalServers})...");

                    using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
                    {
                        var response = await requestFunc(apiUrl);
                        if (response.IsSuccessStatusCode)
                        {
                            onServerSwitching?.Invoke(apiUrl, attemptNumber, totalServers, "连接成功");
                            return response;
                        }

                        string failureBody = string.Empty;
                        try
                        {
                            failureBody = await response.Content.ReadAsStringAsync();
                            if (!string.IsNullOrEmpty(failureBody) && failureBody.Length > 180)
                            {
                                failureBody = failureBody.Substring(0, 180);
                            }
                        }
                        catch
                        {
                        }

                        int statusCode = (int)response.StatusCode;
                        if (statusCode >= 400 && statusCode < 500)
                        {
                            return response;
                        }

                        if (!allowFailoverOnFailure)
                        {
                            return response;
                        }

                        lastException = new HttpRequestException(
                            $"HTTP {(int)response.StatusCode} ({response.StatusCode}) from {apiUrl}. {failureBody}".Trim());
                    }
                }
                catch (OperationCanceledException)
                {
                    lastException = new TimeoutException($"请求超时({timeoutSeconds}秒)");
                    if (!allowFailoverOnFailure)
                    {
                        break;
                    }

                    if (attemptNumber < totalServers)
                    {
                        onServerSwitching?.Invoke(apiUrl, attemptNumber, totalServers, "服务器超时，切换到备用服务器...");
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (!allowFailoverOnFailure)
                    {
                        break;
                    }

                    if (attemptNumber < totalServers)
                    {
                        onServerSwitching?.Invoke(apiUrl, attemptNumber, totalServers, "服务器连接失败，切换到备用服务器...");
                    }
                }
            }

            onServerSwitching?.Invoke("", totalServers, totalServers, "所有验证服务器均无法访问");

            if (lastException != null)
            {
                throw lastException;
            }

            return null;
        }
    }
}
