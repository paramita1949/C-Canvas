using System;
using System.Threading.Tasks;

namespace ImageColorChanger.Services.Interfaces
{
    /// <summary>
    /// 认证门面：为窗口层提供最小认证能力，避免直接依赖 AuthService 单例。
    /// </summary>
    public interface IAuthFacade
    {
        event EventHandler<AuthService.ServerSwitchEventArgs> ServerSwitching;

        string LastAuthFailureReason { get; }

        Task<(bool success, string message)> LoginAsync(string username, string password);

        Task<(bool success, string message)> RegisterAsync(string username, string password, string email);

        Task<(bool success, string message)> SendVerificationCodeAsync(string username, string email);

        Task<(bool success, string message)> ResetPasswordAsync(string email, string code, string newPassword);
    }
}
