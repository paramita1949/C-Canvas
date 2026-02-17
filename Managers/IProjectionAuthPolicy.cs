using System.Threading.Tasks;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 投影授权策略接口，用于隔离 ProjectionManager 与具体认证实现。
    /// </summary>
    public interface IProjectionAuthPolicy
    {
        string GetIdentitySeed();
        bool IsAuthenticated { get; }
        bool CanUseProjection();
        Task<(bool allowed, string message)> VerifyProjectionPermissionAsync();
        void StartTrialProjection();
        int GetTrialProjectionRemainingSeconds();
        bool IsTrialProjectionExpired();
        void ResetTrialProjection();
    }
}

