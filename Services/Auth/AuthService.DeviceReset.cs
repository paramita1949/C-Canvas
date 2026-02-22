using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ImageColorChanger.Services
{
    public partial class AuthService
    {
        public async Task<(bool success, string message, int remainingCount)> ResetDevicesAsync(string password)
        {
            if (!_isAuthenticated || string.IsNullOrEmpty(_username))
            {
#if DEBUG
                System.Diagnostics.Trace.WriteLine($" [解绑设备] 未登录或用户名为空");
                System.Diagnostics.Trace.WriteLine($"   IsAuthenticated: {_isAuthenticated}");
                System.Diagnostics.Trace.WriteLine($"   Username: {_username ?? "null"}");
#endif
                return (false, "请先登录", 0);
            }

            const int maxRetries = 2;
            const int retryDelayMs = 1000;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
#if DEBUG
                    if (attempt > 0)
                    {
                        System.Diagnostics.Trace.WriteLine($" [解绑设备] 第 {attempt + 1} 次尝试（重试 {attempt}）");
                    }
                    else
                    {
                        System.Diagnostics.Trace.WriteLine($" [解绑设备] 开始重置设备: {_username}");
                    }
                    System.Diagnostics.Trace.WriteLine($" [解绑设备] 当前解绑次数: {_resetDeviceCount}");
                    System.Diagnostics.Trace.WriteLine($" [解绑设备] 请求URL: {_authApiClient.CurrentApiBaseUrl}/api/user/reset-devices");
#endif

                    var hardwareId = _authDeviceFingerprint.GetHardwareId();
                    var requestData = new
                    {
                        username = _username,
                        password = password,
                        hardware_id = hardwareId
                    };

                    var jsonContent = JsonSerializer.Serialize(requestData);
#if DEBUG
                    System.Diagnostics.Trace.WriteLine($" [解绑设备] 请求数据: username={_username}, password=***, hardware_id={hardwareId.Substring(0, 16)}...");
                    System.Diagnostics.Trace.WriteLine($" [解绑设备] 正在发送HTTP POST请求...");
#endif

                    var response = await TryMultipleApiUrlsAsync(async (apiUrl) =>
                    {
                        var requestContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                        using (var unbindClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
                        {
                            return await unbindClient.PostAsync(apiUrl + "/api/user/reset-devices", requestContent);
                        }
                    }, timeoutSeconds: 30);

                    if (response == null)
                    {
#if DEBUG
                        System.Diagnostics.Trace.WriteLine($" [解绑设备] 所有API地址均失败");
#endif
                        return (false, "网络连接失败，无法连接到验证服务器", 0);
                    }

#if DEBUG
                    System.Diagnostics.Trace.WriteLine($" [解绑设备] HTTP状态码: {response.StatusCode}");
                    System.Diagnostics.Trace.WriteLine($" [解绑设备] 响应头: {response.Headers}");
#endif

                    var responseContent = await response.Content.ReadAsStringAsync();

#if DEBUG
                    System.Diagnostics.Trace.WriteLine($" [解绑设备] 服务器响应内容: {responseContent}");
                    System.Diagnostics.Trace.WriteLine($" [解绑设备] 响应长度: {responseContent.Length} 字节");
#endif

                    var resetResponse = JsonSerializer.Deserialize<ResetDeviceResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (resetResponse == null)
                    {
#if DEBUG
                        System.Diagnostics.Trace.WriteLine($" [解绑设备] JSON 反序列化失败，返回 null");
#endif
                        if (attempt == maxRetries)
                        {
                            return (false, "服务器响应解析失败", 0);
                        }

#if DEBUG
                        System.Diagnostics.Trace.WriteLine($" [解绑设备] 等待 {retryDelayMs}ms 后重试...");
#endif
                        await Task.Delay(retryDelayMs);
                        continue;
                    }

#if DEBUG
                    System.Diagnostics.Trace.WriteLine($" [解绑设备] 解析结果:");
                    System.Diagnostics.Trace.WriteLine($"   Success: {resetResponse.Success}");
                    System.Diagnostics.Trace.WriteLine($"   Message: {resetResponse.Message}");
                    System.Diagnostics.Trace.WriteLine($"   ResetCount: {resetResponse.ResetCount}");
                    System.Diagnostics.Trace.WriteLine($"   ResetRemaining: {resetResponse.ResetRemaining}");
#endif

                    if (!resetResponse.Success)
                    {
#if DEBUG
                        System.Diagnostics.Trace.WriteLine($" [解绑设备] 服务器返回失败: {resetResponse.Message}");
#endif
                        return (false, resetResponse.Message, resetResponse.ResetCount);
                    }

                    _resetDeviceCount = resetResponse.ResetRemaining;

#if DEBUG
                    System.Diagnostics.Trace.WriteLine($" [解绑设备] 设备重置成功，剩余{_resetDeviceCount}次");
                    System.Diagnostics.Trace.WriteLine($" [解绑设备] 本地_resetDeviceCount已更新为: {_resetDeviceCount}");
#endif
                    return (true, resetResponse.Message, _resetDeviceCount);
                }
                catch (TaskCanceledException ex)
                {
#if DEBUG
                    System.Diagnostics.Trace.WriteLine($" [解绑设备] 请求超时: {ex.Message}");
#endif
                    if (attempt == maxRetries)
                    {
                        return (false, $"请求超时（30秒），请检查网络连接后重试。{ex.Message}", 0);
                    }
#if DEBUG
                    System.Diagnostics.Trace.WriteLine($" [解绑设备] 等待 {retryDelayMs}ms 后重试...");
#endif
                    await Task.Delay(retryDelayMs);
                }
                catch (HttpRequestException ex)
                {
#if DEBUG
                    System.Diagnostics.Trace.WriteLine($" [解绑设备] HTTP请求异常: {ex.Message}");
                    System.Diagnostics.Trace.WriteLine($" [解绑设备] 异常堆栈: {ex.StackTrace}");
#endif
                    if (attempt == maxRetries)
                    {
                        return (false, $"网络请求失败: {ex.Message}", 0);
                    }
#if DEBUG
                    System.Diagnostics.Trace.WriteLine($" [解绑设备] 等待 {retryDelayMs}ms 后重试...");
#endif
                    await Task.Delay(retryDelayMs);
                }
                catch (JsonException ex)
                {
#if DEBUG
                    System.Diagnostics.Trace.WriteLine($" [解绑设备] JSON解析异常: {ex.Message}");
                    System.Diagnostics.Trace.WriteLine($" [解绑设备] 异常堆栈: {ex.StackTrace}");
#endif
                    if (attempt == maxRetries)
                    {
                        return (false, $"响应解析失败: {ex.Message}", 0);
                    }
#if DEBUG
                    System.Diagnostics.Trace.WriteLine($" [解绑设备] 等待 {retryDelayMs}ms 后重试...");
#endif
                    await Task.Delay(retryDelayMs);
                }
                catch (Exception ex)
                {
#if DEBUG
                    System.Diagnostics.Trace.WriteLine($" [解绑设备] 未知异常: {ex.GetType().Name}");
                    System.Diagnostics.Trace.WriteLine($" [解绑设备] 异常消息: {ex.Message}");
                    System.Diagnostics.Trace.WriteLine($" [解绑设备] 异常堆栈: {ex.StackTrace}");
#endif
                    if (attempt == maxRetries)
                    {
                        return (false, $"重置失败: {ex.Message}", 0);
                    }
#if DEBUG
                    System.Diagnostics.Trace.WriteLine($" [解绑设备] 等待 {retryDelayMs}ms 后重试...");
#endif
                    await Task.Delay(retryDelayMs);
                }
            }

            return (false, "解绑失败，已达到最大重试次数", 0);
        }

        private class ResetDeviceResponse
        {
            [JsonPropertyName("success")]
            public bool Success { get; set; }

            [JsonPropertyName("message")]
            public string Message { get; set; }

            [JsonPropertyName("reset_count")]
            public int ResetCount { get; set; }

            [JsonPropertyName("reset_remaining")]
            public int ResetRemaining { get; set; }
        }
    }
}


