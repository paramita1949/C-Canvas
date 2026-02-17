using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ImageColorChanger.Services.Auth;

namespace ImageColorChanger.Services
{
    public partial class AuthService
    {
        private void RaiseUiMessage(string title, string message, UiMessageLevel level = UiMessageLevel.Info)
        {
            UiMessageRequested?.Invoke(this, new UiMessageEventArgs
            {
                Title = title ?? string.Empty,
                Message = message ?? string.Empty,
                Level = level
            });
        }

        private void SetAuthenticatedIdentity(string username, string token)
        {
            _username = username;
            _token = token;
            _isAuthenticated = true;
        }

        private void ClearAuthenticatedIdentity()
        {
            _isAuthenticated = false;
            _username = null;
            _token = null;
        }

        private void ApplyAuthData(AuthData data)
        {
            if (data?.ExpiresAt.HasValue == true)
            {
                _expiresAt = DateTimeOffset.FromUnixTimeSeconds(data.ExpiresAt.Value).LocalDateTime;
            }

            _remainingDays = data?.RemainingDays ?? 0;
            _deviceInfo = data?.DeviceInfo;
            _resetDeviceCount = data?.ResetDeviceCount ?? 0;
        }

        private void UpdateServerTimeBaseline(string serverTimeString)
        {
            if (!string.IsNullOrEmpty(serverTimeString) && DateTime.TryParse(serverTimeString, out var serverTime))
            {
                _lastServerTime = serverTime;
                _lastLocalTime = DateTime.Now;
                _lastTickCount = Environment.TickCount64;
            }
        }

        private AuthResponse DeserializeAuthResponse(string responseContent)
        {
            return JsonSerializer.Deserialize<AuthResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }

        private async Task<HttpResponseMessage> PostJsonWithFailoverAsync(
            string endpoint,
            string jsonContent,
            int timeoutSeconds,
            bool allowFailoverOnFailure = true,
            int? perRequestTimeoutSeconds = null)
        {
            return await TryMultipleApiUrlsAsync(async (apiUrl) =>
            {
                var requestContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                if (perRequestTimeoutSeconds.HasValue)
                {
                    using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(perRequestTimeoutSeconds.Value)))
                    {
                        return await _httpClient.PostAsync(apiUrl + endpoint, requestContent, cts.Token);
                    }
                }

                return await _httpClient.PostAsync(apiUrl + endpoint, requestContent);
            }, timeoutSeconds: timeoutSeconds, allowFailoverOnFailure: allowFailoverOnFailure);
        }

        private (bool success, string message) BuildLoginFailure(AuthResponse authResponse, string fallbackMessage)
        {
            _lastAuthFailureReason = authResponse?.Reason;
            _lastPaymentInfo = authResponse?.PaymentInfo;
            return (false, authResponse?.Message ?? fallbackMessage);
        }

        private static (bool success, string message) BuildHttpRequestFailureResult(
            HttpRequestException ex,
            string defaultMessage,
            bool exposeHttpPrefixMessage = true)
        {
            if (exposeHttpPrefixMessage &&
                !string.IsNullOrWhiteSpace(ex.Message) &&
                ex.Message.StartsWith("HTTP ", StringComparison.Ordinal))
            {
                return (false, ex.Message);
            }

            return (false, defaultMessage);
        }

        private void ApplyLoginSuccess(string username, AuthResponse authResponse)
        {
            _lastAuthFailureReason = null;
            _lastPaymentInfo = null;

            SetAuthenticatedIdentity(username, authResponse.Data?.Token);
            ApplyAuthData(authResponse.Data);

#if DEBUG
            if (_expiresAt.HasValue)
            {
                System.Diagnostics.Debug.WriteLine($"🔐 [AuthService] 过期时间: {_expiresAt}");
            }
#endif

            _lastSuccessfulHeartbeat = DateTime.Now;

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"🔓 [AuthService] 解绑次数: {_resetDeviceCount}次");
            System.Diagnostics.Debug.WriteLine($"🔓 [AuthService] 服务器返回的 ResetDeviceCount: {authResponse.Data?.ResetDeviceCount?.ToString() ?? "null"}");
#endif

            GenerateAuthTokens();

#if DEBUG
            if (_deviceInfo != null)
            {
                System.Diagnostics.Debug.WriteLine($"🔐 [AuthService] 设备绑定信息:");
                System.Diagnostics.Debug.WriteLine($"   已绑定设备: {_deviceInfo.BoundDevices}台");
                System.Diagnostics.Debug.WriteLine($"   最大设备数: {_deviceInfo.MaxDevices}台");
                System.Diagnostics.Debug.WriteLine($"   剩余可绑定: {_deviceInfo.RemainingSlots}台");
                if (_deviceInfo.IsNewDevice)
                {
                    System.Diagnostics.Debug.WriteLine($"   ✨ 这是新绑定的设备");
                }
            }
#endif

            UpdateServerTimeBaseline(authResponse.Data?.ServerTimeString);
#if DEBUG
            if (_lastServerTime.HasValue && _lastLocalTime.HasValue)
            {
                System.Diagnostics.Debug.WriteLine($"🔐 [AuthService] 服务器时间: {_lastServerTime}");
                System.Diagnostics.Debug.WriteLine($"🔐 [AuthService] 本地时间: {_lastLocalTime}");
                System.Diagnostics.Debug.WriteLine($"🔐 [AuthService] TickCount: {_lastTickCount}");
                var timeDiff = (_lastLocalTime.Value - _lastServerTime.Value).TotalSeconds;
                System.Diagnostics.Debug.WriteLine($"🔐 [AuthService] 时间差: {timeDiff:F1}秒 (本地-服务器)");
            }
#endif

            TryShowHolidayBonusNotification(authResponse.Data?.HolidayBonus);
            TryShowClientNotices(authResponse.Data, "login");
            StartHeartbeat();
            _ = SaveAuthDataAsync();
            AuthenticationChanged?.Invoke(this, new AuthenticationChangedEventArgs
            {
                IsAuthenticated = true,
                IsAutoLogin = false
            });

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"✅ [AuthService] 登录成功: {_username}, 剩余{_remainingDays}天");
#endif
        }

        private bool HandleInvalidAuthResponse(
            AuthResponse authResponse,
            string defaultFailureReason,
            string contextTag,
            bool allowLocalCacheFallback)
        {
            string failureReason = authResponse?.Message ?? defaultFailureReason;

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"❌ [{contextTag}] 服务器返回失败: {failureReason}");
            System.Diagnostics.Debug.WriteLine($"   失效原因(reason): {authResponse?.Reason}");
#endif

            var decision = _authHeartbeatPolicy.EvaluateFailure(
                authResponse?.Reason,
                authResponse?.Message,
                failureReason);
            if (decision.ShouldForceLogout)
            {
                Logout();
                RaiseUiMessage(decision.Title, decision.Message, UiMessageLevel.Warning);
                return false;
            }

            if (!allowLocalCacheFallback)
            {
#if DEBUG
                if (authResponse == null)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ [{contextTag}] 响应解析失败（可能网络问题），不强制退出");
                }
                else if (string.IsNullOrEmpty(authResponse.Reason))
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ [{contextTag}] 无明确失效原因，不强制退出");
                }
#endif
                return false;
            }

            if (CanUseProjection())
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 心跳失败，但本地缓存显示未过期，继续使用");
#endif
                return true;
            }

            Logout();
            return false;
        }

        private string BuildTokenHardwarePayloadJson()
        {
            var requestData = new
            {
                token = _token,
                hardware_id = _authDeviceFingerprint.GetHardwareId()
            };
            return JsonSerializer.Serialize(requestData);
        }

        private void ApplyServerAuthData(AuthData data, string source, bool updateLastSuccessfulHeartbeat, bool persistLocalCache)
        {
            UpdateServerTimeBaseline(data?.ServerTimeString);
            ApplyAuthData(data);
            TryShowHolidayBonusNotification(data?.HolidayBonus);
            TryShowClientNotices(data, source);

            if (updateLastSuccessfulHeartbeat)
            {
                _lastSuccessfulHeartbeat = DateTime.Now;
            }

            if (persistLocalCache)
            {
                _ = SaveAuthDataAsync();
            }
        }

        private void ApplyPersistedSnapshot(AuthStateSnapshot authData)
        {
            _username = authData.Username;
            _token = authData.Token;

            if (!string.IsNullOrWhiteSpace(authData.ExpiresAt))
            {
                _expiresAt = DateTime.Parse(authData.ExpiresAt);
            }

            _remainingDays = authData.RemainingDays;

            if (!string.IsNullOrWhiteSpace(authData.LastServerTime))
            {
                _lastServerTime = DateTime.Parse(authData.LastServerTime);
            }

            if (!string.IsNullOrWhiteSpace(authData.LastLocalTime))
            {
                _lastLocalTime = DateTime.Parse(authData.LastLocalTime);
            }

            _lastTickCount = authData.LastTickCount;
            _resetDeviceCount = authData.ResetDeviceCount;
            UpdateShownClientNoticeKeys(authData.ShownClientNoticeKeys);

            if (!string.IsNullOrWhiteSpace(authData.LastSuccessfulHeartbeat))
            {
                _lastSuccessfulHeartbeat = DateTime.Parse(authData.LastSuccessfulHeartbeat);
            }

            _deviceInfo = MapDeviceInfo(authData.DeviceInfo);
        }

        private void UpdateShownClientNoticeKeys(List<string> shownClientNoticeKeys)
        {
            _shownClientNoticeKeys.Clear();
            if (shownClientNoticeKeys == null)
            {
                return;
            }

            foreach (var key in shownClientNoticeKeys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    _shownClientNoticeKeys.Add(key);
                }
            }
        }

        private DeviceInfo MapDeviceInfo(DeviceInfoSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return null;
            }

            return new DeviceInfo
            {
                BoundDevices = snapshot.BoundDevices,
                MaxDevices = snapshot.MaxDevices,
                RemainingSlots = snapshot.RemainingSlots,
                IsNewDevice = snapshot.IsNewDevice
            };
        }

        private void RebuildTokensForLoadedState()
        {
            _isAuthenticated = true;
            GenerateAuthTokens();
            _isAuthenticated = false;
        }

        private bool ValidateAndTrackLoadedFileVersion(long fileVersion)
        {
            var maxVersion = GetMaxFileVersionFromRegistry();
            if (fileVersion > 0 && fileVersion < maxVersion)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"🔒 [AuthService] 检测到文件回滚攻击！");
                System.Diagnostics.Debug.WriteLine($"🔒 [AuthService] 文件版本: {fileVersion}");
                System.Diagnostics.Debug.WriteLine($"🔒 [AuthService] 最大版本: {maxVersion}");
#endif
                DeleteAuthData();
                RaiseUiMessage("安全警告", "检测到凭证文件异常，请重新登录。", UiMessageLevel.Warning);
                return false;
            }

            if (fileVersion > maxVersion)
            {
                SaveMaxFileVersionToRegistry(fileVersion);
            }

            return true;
        }

        private bool ValidateStartupOfflineWindow()
        {
            var startupOfflineDecision = _authHeartbeatPolicy.EvaluateOffline(_lastSuccessfulHeartbeat, DateTime.Now, MAX_OFFLINE_DAYS);
            if (!startupOfflineDecision.Exceeded)
            {
                return true;
            }

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"🔒 [AuthService] 启动时离线时长检测: {startupOfflineDecision.OfflineDays:F1} 天");
            System.Diagnostics.Debug.WriteLine($"🔒 [AuthService] 离线时间超过 {MAX_OFFLINE_DAYS} 天，清除登录状态");
#endif
            DeleteAuthData();
            RaiseUiMessage("离线时间过长", $"账号已离线超过 {MAX_OFFLINE_DAYS} 天，请重新联网登录验证。", UiMessageLevel.Warning);
            return false;
        }

        private void CompleteAutoLoginSuccess()
        {
            StartHeartbeat();
            AuthenticationChanged?.Invoke(this, new AuthenticationChangedEventArgs
            {
                IsAuthenticated = true,
                IsAutoLogin = true
            });
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"💾 [AuthService] 自动登录成功: {_username}, 剩余{_remainingDays}天");
#endif
        }

        private void HandleAutoLoginExpired()
        {
            ClearAuthenticatedIdentity();
            DeleteAuthData();
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"💾 [AuthService] 本地登录已过期");
#endif
        }
    }
}
