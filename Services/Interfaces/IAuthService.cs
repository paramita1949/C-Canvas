using System;
using System.Threading.Tasks;

namespace ImageColorChanger.Services.Interfaces
{
    /// <summary>
    /// 认证服务接口 — 使上层代码依赖抽象而非具体的 AuthService 实现
    /// </summary>
    public interface IAuthService
    {
        #region 属性

        bool IsAuthenticated { get; }
        string Username { get; }
        string LastAuthFailureReason { get; }
        global::ImageColorChanger.Services.PaymentInfo LastPaymentInfo { get; }
        DateTime? ExpiresAt { get; }
        int RemainingDays { get; }
        global::ImageColorChanger.Services.DeviceInfo DeviceBindingInfo { get; }
        int ResetDeviceCount { get; }

        #endregion

        #region 事件

        event EventHandler<AuthenticationChangedEventArgs> AuthenticationChanged;
        event EventHandler<UiMessageEventArgs> UiMessageRequested;
        event EventHandler<ClientNoticesEventArgs> ClientNoticesRequested;
        event EventHandler<ServerSwitchEventArgs> ServerSwitching;

        #endregion

        #region 账号操作

        Task InitializeAsync();
        Task FlushAuthStateAsync();
        Task<(bool success, string message)> LoginAsync(string username, string password);
        Task<(bool success, string message)> SendVerificationCodeAsync(string username, string email);
        Task<(bool success, string message)> ResetPasswordAsync(string email, string code, string newPassword);
        Task<(bool success, string message)> RegisterAsync(string username, string password, string email = null);
        void Logout();

        #endregion

        #region 设备管理

        Task<(bool success, string message, int remainingCount)> ResetDevicesAsync(string password);
        string GetCurrentHardwareId();

        #endregion

        #region 投影权限

        bool CanUseProjection();
        Task<(bool allowed, string message)> VerifyProjectionPermissionAsync();
        Task<bool> RefreshAccountInfoAsync();
        string GetStatusSummary();
        string GetDeviceBindingSummary();

        #endregion

        #region 试用投影

        void StartTrialProjection();
        bool IsTrialProjectionExpired();
        int GetTrialProjectionRemainingSeconds();
        void ResetTrialProjection();

        #endregion
    }
}
