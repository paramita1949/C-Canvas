using System;

namespace ImageColorChanger.Services
{
    public partial class AuthService
    {
        private void GenerateAuthTokens()
        {
            var bundle = _authTokenGuard.Generate(_username, _token, _expiresAt);
            _authToken1 = bundle.Token1;
            _authToken2 = bundle.Token2;
            _authChecksum = bundle.Checksum;
        }

        private bool ValidateAuthTokens()
        {
            if (!_authTokenGuard.Validate(_username, _token, _expiresAt, _authToken1, _authToken2, _authChecksum))
            {
#if DEBUG
                System.Diagnostics.Trace.WriteLine($"⚠️ [AuthService] 令牌完整性验证失败");
#endif
                return false;
            }

            return true;
        }

        public bool CanUseProjection()
        {
            bool trialValid = GetTrialProjectionStatus() == Auth.AuthTrialProjectionPolicy.StatusValid;
            bool tokensValid = _isAuthenticated && ValidateAuthTokens();
            var estimatedServerTime = GetEstimatedServerTime();
            var decision = _authProjectionAccessPolicy.Evaluate(
                _isAuthenticated,
                trialValid,
                tokensValid,
                _expiresAt,
                estimatedServerTime);

#if DEBUG
            if (decision.Reason == Auth.AuthProjectionAccessPolicy.DenyReason.TokenInvalid)
            {
                System.Diagnostics.Trace.WriteLine($"⚠️ [AuthService] 令牌验证失败，拒绝投影");
            }

            if (decision.Reason == Auth.AuthProjectionAccessPolicy.DenyReason.Expired)
            {
                System.Diagnostics.Trace.WriteLine($"⚠️ [AuthService] 账号已过期");
                System.Diagnostics.Trace.WriteLine($"   估算服务器时间: {estimatedServerTime}");
                System.Diagnostics.Trace.WriteLine($"   过期时间: {_expiresAt}");
            }
#endif
            return decision.Allowed;
        }

        private DateTime GetEstimatedServerTime()
        {
            if (_lastServerTime == null)
            {
#if DEBUG
                System.Diagnostics.Trace.WriteLine($"🕐 [AuthService] 无服务器时间记录，使用本地时间: {DateTime.Now}");
#endif
                return DateTime.Now;
            }

            DateTime now = DateTime.Now;
            long currentTick = Environment.TickCount64;
            var result = _authClock.Estimate(_lastServerTime, _lastLocalTime, _lastTickCount, currentTick, now);

            if (result.TickResetRecovered)
            {
#if DEBUG
                System.Diagnostics.Trace.WriteLine("⚠️ [AuthService] TickCount 异常（可能系统重启），执行基准重建");
                if (result.LocalTimeRolledBack)
                {
                    System.Diagnostics.Trace.WriteLine("⚠️ [AuthService] 检测到时间回退，强制使用正向流逝");
                }
#endif
                _lastServerTime = result.UpdatedLastServerTime;
                _lastLocalTime = result.UpdatedLastLocalTime;
                _lastTickCount = result.UpdatedLastTickCount;
                RequestPersistAuthData();
#if DEBUG
                System.Diagnostics.Trace.WriteLine($"✅ [AuthService] TickCount 基准已重建: Tick={_lastTickCount}");
#endif
            }

#if DEBUG
            if (result.LocalTickSkewDetected)
            {
                var localElapsed = now - _lastLocalTime.Value;
                System.Diagnostics.Trace.WriteLine($"⚠️ [AuthService] 检测到时间异常！");
                System.Diagnostics.Trace.WriteLine($"⚠️ [AuthService] 本地时间流逝: {localElapsed.TotalSeconds:F1} 秒");
                System.Diagnostics.Trace.WriteLine($"⚠️ [AuthService] Tick流逝: {result.ElapsedByTick.TotalSeconds:F1} 秒");
                System.Diagnostics.Trace.WriteLine($"⚠️ [AuthService] 差异: {result.LocalTickSkewSeconds:F1} 秒（可能本地时间被修改）");
            }
#endif

            return result.EstimatedServerTime;
        }

        public string GetStatusSummary()
        {
            if (!_isAuthenticated)
            {
                return "未登录";
            }

            if (!CanUseProjection())
            {
                return "账号已过期";
            }

            if (_remainingDays <= 7)
            {
                return $"账号即将过期（剩余{_remainingDays}天）";
            }

            return $"已登录 - {_username}（剩余{_remainingDays}天）";
        }

        public string GetDeviceBindingSummary()
        {
            if (_deviceInfo == null)
            {
                return "设备信息未知";
            }

            if (_deviceInfo.RemainingSlots <= 0)
            {
                return $"设备已满：{_deviceInfo.BoundDevices}/{_deviceInfo.MaxDevices}台";
            }

            return $"设备绑定：{_deviceInfo.BoundDevices}/{_deviceInfo.MaxDevices}台（剩余{_deviceInfo.RemainingSlots}个槽位）";
        }
    }
}

