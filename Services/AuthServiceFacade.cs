using System;
using System.Threading.Tasks;
using ImageColorChanger.Services.Interfaces;

namespace ImageColorChanger.Services
{
    /// <summary>
    /// AuthService 的窗口层门面适配器。
    /// </summary>
    public sealed class AuthServiceFacade : IAuthFacade
    {
        private readonly IAuthService _authService;

        public AuthServiceFacade(IAuthService authService)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        }

        public event EventHandler<ServerSwitchEventArgs> ServerSwitching
        {
            add => _authService.ServerSwitching += value;
            remove => _authService.ServerSwitching -= value;
        }

        public string LastAuthFailureReason => _authService.LastAuthFailureReason;

        public Task<(bool success, string message)> LoginAsync(string username, string password)
            => _authService.LoginAsync(username, password);

        public Task<(bool success, string message)> RegisterAsync(string username, string password, string email)
            => _authService.RegisterAsync(username, password, email);

        public Task<(bool success, string message)> SendVerificationCodeAsync(string username, string email)
            => _authService.SendVerificationCodeAsync(username, email);

        public Task<(bool success, string message)> ResetPasswordAsync(string email, string code, string newPassword)
            => _authService.ResetPasswordAsync(email, code, newPassword);
    }
}
