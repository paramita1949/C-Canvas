using System.Threading.Tasks;
using ImageColorChanger.Managers;
using ImageColorChanger.Services.Interfaces;

namespace ImageColorChanger.Services
{
    /// <summary>
    /// 基于 AuthService 的投影授权策略实现。
    /// </summary>
    public sealed class AuthServiceProjectionAuthPolicy : IProjectionAuthPolicy
    {
        private readonly IAuthService _authService;

        public AuthServiceProjectionAuthPolicy(IAuthService authService)
        {
            _authService = authService ?? throw new System.ArgumentNullException(nameof(authService));
        }

        public string GetIdentitySeed()
        {
            return _authService.Username ?? System.Environment.MachineName;
        }

        public bool IsAuthenticated => _authService.IsAuthenticated;

        public bool CanUseProjection()
        {
            return _authService.CanUseProjection();
        }

        public Task<(bool allowed, string message)> VerifyProjectionPermissionAsync()
        {
            return _authService.VerifyProjectionPermissionAsync();
        }

        public void StartTrialProjection()
        {
            _authService.StartTrialProjection();
        }

        public int GetTrialProjectionRemainingSeconds()
        {
            return _authService.GetTrialProjectionRemainingSeconds();
        }

        public bool IsTrialProjectionExpired()
        {
            return _authService.IsTrialProjectionExpired();
        }

        public void ResetTrialProjection()
        {
            _authService.ResetTrialProjection();
        }
    }
}
