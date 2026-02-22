using System;

namespace ImageColorChanger.Services
{
    public partial class AuthService
    {
        #region 试用投影验证（防止破解随机时间限制）

        private const int TRIAL_MIN_SECONDS = 30;
        private const int TRIAL_MAX_SECONDS = 60;

        /// <summary>
        /// 开始试用投影（未登录状态）
        /// 每次点击都生成新的随机时长和加密令牌
        /// </summary>
        public void StartTrialProjection()
        {
            if (_isAuthenticated)
            {
                _trialProjectionSession.Reset();
                return;
            }

            var random = new Random();
            var randomDuration = random.Next(TRIAL_MIN_SECONDS, TRIAL_MAX_SECONDS + 1);
            int durationSeconds = Math.Min(randomDuration, TRIAL_MAX_SECONDS);
            _trialProjectionSession.Start(Environment.TickCount64, durationSeconds);
            _trialProjectionSession.SetToken(GenerateTrialProjectionToken());

#if DEBUG
            System.Diagnostics.Trace.WriteLine($" [试用投影] 已启动，时长: {_trialProjectionSession.DurationSeconds}秒");
#endif
        }

        /// <summary>
        /// 检查试用投影是否已过期
        /// </summary>
        public bool IsTrialProjectionExpired()
        {
            return GetTrialProjectionStatus() != Auth.AuthTrialProjectionPolicy.StatusValid;
        }

        /// <summary>
        /// 获取试用投影状态验证码
        /// </summary>
        private int GetTrialProjectionStatus()
        {
            bool tokenValid = true;
            if (!_isAuthenticated && _trialProjectionSession.IsStarted)
            {
                tokenValid = ValidateTrialProjectionToken();
            }

            if (!tokenValid)
            {
#if DEBUG
                System.Diagnostics.Trace.WriteLine($" [试用投影] 令牌验证失败，可能被篡改");
#endif
            }

            return _authTrialProjectionPolicy.GetStatus(
                _isAuthenticated,
                _trialProjectionSession.StartTick,
                _trialProjectionSession.DurationSeconds,
                TRIAL_MAX_SECONDS,
                tokenValid,
                Environment.TickCount64);
        }

        /// <summary>
        /// 获取试用投影剩余时间（秒）
        /// </summary>
        public int GetTrialProjectionRemainingSeconds()
        {
            long currentTick = Environment.TickCount64;
            int status = GetTrialProjectionStatus();
            return _authTrialProjectionPolicy.GetRemainingSeconds(
                _isAuthenticated,
                _trialProjectionSession.StartTick,
                status,
                Auth.AuthTrialProjectionPolicy.StatusValid,
                currentTick,
                _trialProjectionSession.DurationSeconds,
                TRIAL_MAX_SECONDS);
        }

        /// <summary>
        /// 重置试用投影状态
        /// </summary>
        public void ResetTrialProjection()
        {
            _trialProjectionSession.Reset();

#if DEBUG
            System.Diagnostics.Trace.WriteLine($" [试用投影] 已重置");
#endif
        }

        /// <summary>
        /// 生成试用投影令牌
        /// </summary>
        private string GenerateTrialProjectionToken()
        {
            return _authTrialProjectionToken.Generate(
                _trialProjectionSession.StartTick,
                _trialProjectionSession.DurationSeconds,
                TRIAL_MAX_SECONDS,
                Environment.MachineName,
                Environment.UserName);
        }

        /// <summary>
        /// 验证试用投影令牌
        /// </summary>
        private bool ValidateTrialProjectionToken()
        {
            return _authTrialProjectionToken.Validate(
                _trialProjectionSession.Token,
                _trialProjectionSession.StartTick,
                _trialProjectionSession.DurationSeconds,
                TRIAL_MAX_SECONDS,
                Environment.MachineName,
                Environment.UserName);
        }

        #endregion
    }
}


