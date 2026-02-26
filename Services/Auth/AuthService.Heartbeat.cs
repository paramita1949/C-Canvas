using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace ImageColorChanger.Services
{
    public partial class AuthService
    {
        /// <summary>
        /// 快速验证投影权限（联网验证，用于投影开始时）
        /// </summary>
        public async Task<(bool allowed, string message)> VerifyProjectionPermissionAsync()
        {
            if (_isAuthenticated && CanUseProjection())
            {
#if DEBUG
                System.Diagnostics.Trace.WriteLine($" [投影权限] 已登录且有效，允许投影");
#endif
                return (true, "已登录");
            }

#if DEBUG
            System.Diagnostics.Trace.WriteLine($" [投影权限] 未登录，尝试联网验证...");
#endif

            bool networkAvailable = false;
            try
            {
                using (var networkClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) })
                {
                    var networkResponse = await networkClient.GetAsync("https://www.baidu.com");
                    networkAvailable = networkResponse.IsSuccessStatusCode;
#if DEBUG
                    System.Diagnostics.Trace.WriteLine($" [投影权限] 网络检测（百度）: {(networkAvailable ? "可用" : "不可用")}");
#endif
                }
            }
            catch
            {
#if DEBUG
                System.Diagnostics.Trace.WriteLine($" [投影权限] 网络检测（百度）: 不可用");
#endif
                return (true, "试用模式（离线）");
            }

            if (networkAvailable)
            {
                try
                {
                    var response = await TryMultipleApiUrlsAsync(async (apiUrl) =>
                    {
                        using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20)))
                        {
                            return await _httpClient.GetAsync(apiUrl + "/api/auth/verify", cts.Token);
                        }
                    }, timeoutSeconds: 20);

                    if (response != null && response.IsSuccessStatusCode)
                    {
#if DEBUG
                        System.Diagnostics.Trace.WriteLine($" [投影权限] 服务器正常但未登录，试用投影");
#endif
                        return (false, "检测到网络连接，请先登录后使用投影功能");
                    }

#if DEBUG
                    System.Diagnostics.Trace.WriteLine($" [投影权限] 服务器故障（{response?.StatusCode}），允许试用模式");
#endif
                    return (true, "试用模式（服务器异常）");
                }
                catch (TaskCanceledException)
                {
#if DEBUG
                    System.Diagnostics.Trace.WriteLine($" [投影权限] 服务器超时，允许试用模式");
#endif
                    return (true, "试用模式（服务器超时）");
                }
                catch (Exception ex)
                {
#if DEBUG
                    System.Diagnostics.Trace.WriteLine($" [投影权限] 服务器连接失败: {ex.Message}，允许试用模式");
#else
                    _ = ex;
#endif
                    return (true, "试用模式（服务器不可达）");
                }
            }

            return (false, "请先登录");
        }

        /// <summary>
        /// 手动刷新账号信息（尝试从服务器获取最新信息）
        /// </summary>
        public async Task<bool> RefreshAccountInfoAsync()
        {
            if (!_isAuthenticated || string.IsNullOrEmpty(_username))
            {
                return false;
            }

            try
            {
#if DEBUG
                System.Diagnostics.Trace.WriteLine($" [刷新] 尝试从服务器刷新账号信息...");
#endif
                var jsonContent = BuildTokenHardwarePayloadJson();
                var response = await PostJsonWithFailoverAsync(
                    HEARTBEAT_ENDPOINT,
                    jsonContent,
                    timeoutSeconds: 8,
                    perRequestTimeoutSeconds: 8);

                if (response == null)
                {
#if DEBUG
                    System.Diagnostics.Trace.WriteLine($" [刷新] 网络连接失败，使用本地缓存");
#endif
                    return false;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var authResponse = DeserializeAuthResponse(responseContent);
                if (authResponse == null || !authResponse.Success || !authResponse.Valid)
                {
                    HandleInvalidAuthResponse(authResponse, "账号验证失败", "刷新", allowLocalCacheFallback: false);
                    return false;
                }

                ApplyServerAuthData(authResponse.Data, source: "refresh", updateLastSuccessfulHeartbeat: false, persistLocalCache: true);

#if DEBUG
                System.Diagnostics.Trace.WriteLine($" [刷新] 成功，剩余{_remainingDays}天，解绑{_resetDeviceCount}次");
                if (_deviceInfo != null)
                {
                    System.Diagnostics.Trace.WriteLine($" [刷新] 设备: 已绑定{_deviceInfo.BoundDevices}/{_deviceInfo.MaxDevices}, 剩余{_deviceInfo.RemainingSlots}");
                }
#endif
                return true;
            }
            catch (TaskCanceledException)
            {
#if DEBUG
                System.Diagnostics.Trace.WriteLine($"⏱ [刷新] 超时，使用本地缓存");
#endif
                return false;
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Trace.WriteLine($" [刷新] 异常: {ex.Message}，使用本地缓存");
#else
                _ = ex;
#endif
                return false;
            }
        }

        /// <summary>
        /// 轻量拉取通知更新（不刷新账号缓存字段）
        /// </summary>
        private async Task PullNoticeUpdatesAsync()
        {
            if (!_isAuthenticated || string.IsNullOrEmpty(_username) || string.IsNullOrWhiteSpace(_token))
            {
                return;
            }

            try
            {
#if DEBUG
                System.Diagnostics.Trace.WriteLine($" [通知心跳] 开始检查通知更新...");
#endif
                var jsonContent = BuildTokenHardwarePayloadJson();
                const int maxAttempts = 2;
                const int requestTimeoutSeconds = 30;
                HttpResponseMessage response = null;
                Exception lastAttemptException = null;
                var overallStopwatch = System.Diagnostics.Stopwatch.StartNew();

                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    var attemptStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        response = await PostJsonWithFailoverAsync(
                            HEARTBEAT_ENDPOINT,
                            jsonContent,
                            timeoutSeconds: requestTimeoutSeconds,
                            allowFailoverOnFailure: false,
                            perRequestTimeoutSeconds: requestTimeoutSeconds);

                        attemptStopwatch.Stop();
#if DEBUG
                        string attemptUrl = $"{_authApiClient.CurrentApiBaseUrl}{HEARTBEAT_ENDPOINT}";
                        System.Diagnostics.Trace.WriteLine(
                            $" [通知心跳] 第{attempt}/{maxAttempts}次请求完成: " +
                            $"{(response == null ? "无响应" : $"HTTP {(int)response.StatusCode}")}, " +
                            $"耗时={attemptStopwatch.ElapsedMilliseconds}ms, URL={attemptUrl}");
#endif

                        if (response != null)
                        {
                            break;
                        }
                    }
                    catch (TaskCanceledException ex)
                    {
                        lastAttemptException = ex;
                        attemptStopwatch.Stop();
#if DEBUG
                        string timeoutUrl = $"{_authApiClient.CurrentApiBaseUrl}{HEARTBEAT_ENDPOINT}";
                        System.Diagnostics.Trace.WriteLine(
                            $"⏱ [通知心跳] 第{attempt}/{maxAttempts}次请求超时: " +
                            $"timeout={requestTimeoutSeconds}s, 耗时={attemptStopwatch.ElapsedMilliseconds}ms, URL={timeoutUrl}");
#endif
                    }
                    catch (TimeoutException ex)
                    {
                        lastAttemptException = ex;
                        attemptStopwatch.Stop();
#if DEBUG
                        string timeoutUrl = $"{_authApiClient.CurrentApiBaseUrl}{HEARTBEAT_ENDPOINT}";
                        System.Diagnostics.Trace.WriteLine(
                            $"⏱ [通知心跳] 第{attempt}/{maxAttempts}次请求超时异常: " +
                            $"耗时={attemptStopwatch.ElapsedMilliseconds}ms, URL={timeoutUrl}");
#endif
                    }
                    catch (Exception ex)
                    {
                        lastAttemptException = ex;
                        attemptStopwatch.Stop();
#if DEBUG
                        string exceptionUrl = $"{_authApiClient.CurrentApiBaseUrl}{HEARTBEAT_ENDPOINT}";
                        System.Diagnostics.Trace.WriteLine(
                            $" [通知心跳] 第{attempt}/{maxAttempts}次请求异常: {ex.Message}, " +
                            $"耗时={attemptStopwatch.ElapsedMilliseconds}ms, URL={exceptionUrl}");
#endif
                    }

                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(250);
                    }
                }

                overallStopwatch.Stop();

                if (response == null)
                {
#if DEBUG
                    string failedUrl = $"{_authApiClient.CurrentApiBaseUrl}{HEARTBEAT_ENDPOINT}";
                    System.Diagnostics.Trace.WriteLine(
                        $" [通知心跳] 网络连接失败，跳过本次通知检查: " +
                        $"尝试次数={maxAttempts}, 总耗时={overallStopwatch.ElapsedMilliseconds}ms, " +
                        $"最后异常={(lastAttemptException == null ? "无" : lastAttemptException.GetType().Name)}, URL={failedUrl}");
#endif
                    return;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var authResponse = DeserializeAuthResponse(responseContent);
                if (authResponse == null || !authResponse.Success || !authResponse.Valid)
                {
#if DEBUG
                    var preview = responseContent?.Length > 140
                        ? responseContent.Substring(0, 140) + "..."
                        : responseContent;
                    string invalidUrl = $"{_authApiClient.CurrentApiBaseUrl}{HEARTBEAT_ENDPOINT}";
                    System.Diagnostics.Trace.WriteLine(
                        $" [通知心跳] 服务器未返回有效通知数据，本次跳过: " +
                        $"HTTP={(int)response.StatusCode}, success={authResponse?.Success.ToString() ?? "null"}, " +
                        $"valid={authResponse?.Valid.ToString() ?? "null"}, URL={invalidUrl}, body={preview}");
#endif
                    return;
                }

                ApplyServerAuthData(authResponse.Data, source: "notice-heartbeat", updateLastSuccessfulHeartbeat: false, persistLocalCache: false);

#if DEBUG
                string successUrl = $"{_authApiClient.CurrentApiBaseUrl}{HEARTBEAT_ENDPOINT}";
                System.Diagnostics.Trace.WriteLine(
                    $" [通知心跳] 通知检查完成: HTTP={(int)response.StatusCode}, " +
                    $"总耗时={overallStopwatch.ElapsedMilliseconds}ms, URL={successUrl}");
#endif
            }
            catch (TaskCanceledException)
            {
#if DEBUG
                System.Diagnostics.Trace.WriteLine($"⏱ [通知心跳] 检查超时，跳过本次");
#endif
            }
            catch (TimeoutException)
            {
#if DEBUG
                System.Diagnostics.Trace.WriteLine($"⏱ [通知心跳] 检查超时，跳过本次");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Trace.WriteLine($" [通知心跳] 检查异常: {ex.Message}");
#else
                _ = ex;
#endif
            }
        }

        /// <summary>
        /// 心跳检查（定期验证账号状态）
        /// </summary>
        private async void HeartbeatCallback(object state)
        {
            if (!_isAuthenticated || string.IsNullOrEmpty(_username))
            {
                StopHeartbeat();
                return;
            }

            try
            {
#if DEBUG
                System.Diagnostics.Trace.WriteLine($" [心跳] 开始心跳检查... (当前时间: {DateTime.Now:HH:mm:ss})");
#endif
                var jsonContent = BuildTokenHardwarePayloadJson();
                var response = await PostJsonWithFailoverAsync(HEARTBEAT_ENDPOINT, jsonContent, timeoutSeconds: 20);

                if (response == null)
                {
#if DEBUG
                    System.Diagnostics.Trace.WriteLine($" [心跳] 所有API地址均失败，网络连接失败");
#endif
                    return;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var authResponse = DeserializeAuthResponse(responseContent);
                if (authResponse == null || !authResponse.Success || !authResponse.Valid)
                {
                    bool keepUsingLocalCache = HandleInvalidAuthResponse(
                        authResponse,
                        "账号已失效",
                        "心跳",
                        allowLocalCacheFallback: true);
                    if (keepUsingLocalCache)
                    {
                        return;
                    }
                    return;
                }

#if DEBUG
                System.Diagnostics.Trace.WriteLine($" [心跳] 服务器返回的 ServerTimeString: {authResponse.Data?.ServerTimeString ?? "null"}");
                if (!string.IsNullOrEmpty(authResponse.Data?.ServerTimeString))
                {
                    if (DateTime.TryParse(authResponse.Data.ServerTimeString, out var serverTime))
                    {
                        System.Diagnostics.Trace.WriteLine($" [心跳] 服务器时间已更新: {serverTime}");
                    }
                    else
                    {
                        System.Diagnostics.Trace.WriteLine($" [心跳] 服务器时间解析失败: {authResponse.Data.ServerTimeString}");
                    }
                }
                else
                {
                    System.Diagnostics.Trace.WriteLine($" [心跳] 服务器未返回 server_time 字段");
                }
#endif

                ApplyServerAuthData(authResponse.Data, source: "heartbeat", updateLastSuccessfulHeartbeat: true, persistLocalCache: true);

#if DEBUG
                var nextHeartbeat = DateTime.Now.Add(AUTH_HEARTBEAT_INTERVAL);
                System.Diagnostics.Trace.WriteLine($" [心跳] 心跳正常，剩余{_remainingDays}天，解绑{_resetDeviceCount}次");
                System.Diagnostics.Trace.WriteLine($" [心跳] 下次心跳时间: {nextHeartbeat:HH:mm:ss}");
#endif
            }
            catch (Exception ex)
            {
                var offlineBaseline = ResolveOfflineBaselineTime();
                var offlineDecision = _authHeartbeatPolicy.EvaluateOffline(offlineBaseline, DateTime.Now, MAX_OFFLINE_DAYS);
                if (offlineDecision.Exceeded)
                {
                    _ = ex;
#if DEBUG
                    System.Diagnostics.Trace.WriteLine($" [AuthService] 离线基准时间: {offlineBaseline:O}");
                    System.Diagnostics.Trace.WriteLine($" [AuthService] 离线时间超过 {MAX_OFFLINE_DAYS} 天，强制退出");
#endif
                    Logout();
                    RaiseUiMessage(
                        "离线时间过长",
                        $"账号已离线超过 {MAX_OFFLINE_DAYS} 天，请重新联网登录验证。",
                        UiMessageLevel.Warning);
                    return;
                }

                if (CanUseProjection())
                {
#if DEBUG
                    System.Diagnostics.Trace.WriteLine($" [AuthService] 本地缓存有效，允许离线使用");
#endif
                    return;
                }

#if DEBUG
                System.Diagnostics.Trace.WriteLine($" [AuthService] 心跳网络异常且本地缓存已过期");
#endif
                Logout();
            }
        }

        /// <summary>
        /// 启动心跳定时器
        /// </summary>
        private void StartHeartbeat()
        {
            StopHeartbeat();
            _heartbeatScheduler.Start(
                HeartbeatCallback,
                NoticeHeartbeatCallback,
                AUTH_HEARTBEAT_INTERVAL,
                NOTICE_HEARTBEAT_INTERVAL);

#if DEBUG
            var firstHeartbeat = DateTime.Now.Add(AUTH_HEARTBEAT_INTERVAL);
            var secondHeartbeat = firstHeartbeat.Add(AUTH_HEARTBEAT_INTERVAL);
            System.Diagnostics.Trace.WriteLine($" [心跳] 认证心跳已启动（每2小时检查一次）");
            System.Diagnostics.Trace.WriteLine($" [心跳] 首次心跳时间: {firstHeartbeat:HH:mm:ss}");
            System.Diagnostics.Trace.WriteLine($" [心跳] 第二次心跳时间: {secondHeartbeat:HH:mm:ss}");
#endif
        }

        private async void NoticeHeartbeatCallback(object state)
        {
            if (!_isAuthenticated || string.IsNullOrEmpty(_username))
            {
                _heartbeatScheduler.StopNoticeHeartbeat();
                return;
            }

            try
            {
                await PullNoticeUpdatesAsync();
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Trace.WriteLine($" [通知心跳] 执行异常: {ex.Message}");
#else
                _ = ex;
#endif
            }
        }

        /// <summary>
        /// 停止心跳定时器
        /// </summary>
        private void StopHeartbeat()
        {
            _heartbeatScheduler.StopAll();
        }
    }
}



