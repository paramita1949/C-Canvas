using System;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// ProjectionManager 投影试用计时与账号巡检逻辑（部分类）。
    /// </summary>
    public partial class ProjectionManager
    {
        /// <summary>
        /// 启动投影计时器（未登录状态的随机时间限制）
        /// </summary>
        private void StartProjectionTimer()
        {
            _authPolicy.StartTrialProjection();
            _projectionStartTime = DateTime.Now;
            _projectionStartTick = Environment.TickCount64;
            _localProjectionChecksum = GenerateLocalProjectionChecksum();

            _projectionTimer = new System.Threading.Timer(
                CheckProjectionTimeLimit,
                null,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1));
        }

        /// <summary>
        /// 停止投影计时器
        /// </summary>
        private void StopProjectionTimer()
        {
            _projectionTimer?.Dispose();
            _projectionTimer = null;
            _projectionStartTime = null;
        }

        /// <summary>
        /// 检查投影时间限制（定时器回调）
        /// </summary>
        private void CheckProjectionTimeLimit(object state)
        {
            if (!_projectionStartTime.HasValue || _projectionWindow == null)
            {
                StopProjectionTimer();
                return;
            }

            if (_authPolicy.IsAuthenticated && _authPolicy.CanUseProjection())
            {
                StopProjectionTimer();
                _authPolicy.ResetTrialProjection();
                _localProjectionChecksum = null;
                return;
            }

            bool checksumValid = ValidateLocalProjectionChecksum();
            if (!checksumValid)
            {
                RunOnMainDispatcher(() => { CloseProjection(); });
                StopProjectionTimer();
                _authPolicy.ResetTrialProjection();
                _localProjectionChecksum = null;
                return;
            }

            bool authExpired = _authPolicy.IsTrialProjectionExpired();
            long currentTick = Environment.TickCount64;
            long elapsedMilliseconds = currentTick - _projectionStartTick;
            int elapsedSeconds = (int)(elapsedMilliseconds / 1000);
            var elapsedByDateTime = (DateTime.Now - _projectionStartTime.Value).TotalSeconds;
            int actualElapsedSeconds = Math.Max(elapsedSeconds, (int)elapsedByDateTime);
            int trialDuration = GetTrialDurationSeconds();
            bool localExpired = actualElapsedSeconds >= trialDuration;

            if (authExpired || localExpired)
            {
                RunOnMainDispatcher(() => { CloseProjection(); });
                StopProjectionTimer();
                _authPolicy.ResetTrialProjection();
                _localProjectionChecksum = null;
            }
        }

        /// <summary>
        /// 定期检查账号有效期（已登录状态）
        /// </summary>
        private void CheckAuthenticationPeriodically()
        {
            _projectionTimer = new System.Threading.Timer(
                _ =>
                {
                    if (_projectionWindow == null)
                    {
                        StopProjectionTimer();
                        return;
                    }

                    if (!_authPolicy.IsAuthenticated || !_authPolicy.CanUseProjection())
                    {
                        RunOnMainDispatcher(() =>
                        {
                            CloseProjection();
                            _uiNotifier.ShowMessage(
                                "账号已过期",
                                "您的账号已过期，投影功能已自动关闭。",
                                ProjectionUiMessageLevel.Warning);
                        });

                        StopProjectionTimer();
                    }
                },
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(20));
        }

        /// <summary>
        /// 生成本地投影校验和
        /// </summary>
        private string GenerateLocalProjectionChecksum()
        {
            try
            {
                const string LocalSecretKey1 = "ProjectionManager_Local_Checksum_2024";
                const string LocalSecretKey2 = "MultiLayer_AntiCrack_Protection_System";
                var trialDuration = GetTrialDurationSeconds();
                var data = $"{LocalSecretKey1}:{_projectionStartTick}:{trialDuration}:{Environment.ProcessorCount}:{LocalSecretKey2}";

                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data));
                    return Convert.ToBase64String(hashBytes);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 验证本地投影校验和
        /// </summary>
        private bool ValidateLocalProjectionChecksum()
        {
            if (string.IsNullOrEmpty(_localProjectionChecksum))
            {
                return false;
            }

            var expectedChecksum = GenerateLocalProjectionChecksum();
            return _localProjectionChecksum == expectedChecksum;
        }
    }
}
