using System;
using System.Windows.Threading;
using ImageColorChanger.Services;

namespace ImageColorChanger.UI.Modules
{
    /// <summary>
    /// 认证模块控制器：负责认证事件接线与退订，避免 MainWindow 直接管理底层事件生命周期。
    /// </summary>
    public sealed class AuthModuleController : IDisposable
    {
        private readonly AuthService _authService;
        private readonly Dispatcher _dispatcher;
        private readonly Action _onAuthenticationChanged;
        private readonly Action<AuthService.UiMessageEventArgs> _onUiMessageRequested;
        private readonly Action<AuthService.ClientNoticesEventArgs> _onClientNoticesRequested;
        private bool _isStarted;

        public AuthModuleController(
            AuthService authService,
            Dispatcher dispatcher,
            Action onAuthenticationChanged,
            Action<AuthService.UiMessageEventArgs> onUiMessageRequested,
            Action<AuthService.ClientNoticesEventArgs> onClientNoticesRequested)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _onAuthenticationChanged = onAuthenticationChanged ?? throw new ArgumentNullException(nameof(onAuthenticationChanged));
            _onUiMessageRequested = onUiMessageRequested ?? throw new ArgumentNullException(nameof(onUiMessageRequested));
            _onClientNoticesRequested = onClientNoticesRequested ?? throw new ArgumentNullException(nameof(onClientNoticesRequested));
        }

        public void Start()
        {
            if (_isStarted)
            {
                return;
            }

            _authService.AuthenticationChanged += OnAuthenticationChanged;
            _authService.UiMessageRequested += OnUiMessageRequested;
            _authService.ClientNoticesRequested += OnClientNoticesRequested;
            _isStarted = true;
        }

        public void Dispose()
        {
            if (!_isStarted)
            {
                return;
            }

            _authService.AuthenticationChanged -= OnAuthenticationChanged;
            _authService.UiMessageRequested -= OnUiMessageRequested;
            _authService.ClientNoticesRequested -= OnClientNoticesRequested;
            _isStarted = false;
        }

        private void OnAuthenticationChanged(object sender, AuthService.AuthenticationChangedEventArgs e)
        {
            _dispatcher.Invoke(_onAuthenticationChanged);
        }

        private void OnUiMessageRequested(object sender, AuthService.UiMessageEventArgs e)
        {
            _dispatcher.Invoke(() => _onUiMessageRequested(e));
        }

        private void OnClientNoticesRequested(object sender, AuthService.ClientNoticesEventArgs e)
        {
            _dispatcher.Invoke(() => _onClientNoticesRequested(e));
        }
    }
}
