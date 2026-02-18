using System;
using System.Windows.Threading;
using ImageColorChanger.Services;
using ImageColorChanger.Services.Interfaces;

namespace ImageColorChanger.UI.Modules
{
    /// <summary>
    /// 认证模块控制器：负责认证事件接线与退订，避免 MainWindow 直接管理底层事件生命周期。
    /// </summary>
    public sealed class AuthModuleController : IDisposable
    {
        private readonly IAuthService _authService;
        private readonly Dispatcher _dispatcher;
        private readonly Action _onAuthenticationChanged;
        private readonly Action<UiMessageEventArgs> _onUiMessageRequested;
        private readonly Action<ClientNoticesEventArgs> _onClientNoticesRequested;
        private bool _isStarted;

        public AuthModuleController(
            IAuthService authService,
            Dispatcher dispatcher,
            Action onAuthenticationChanged,
            Action<UiMessageEventArgs> onUiMessageRequested,
            Action<ClientNoticesEventArgs> onClientNoticesRequested)
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

        private void OnAuthenticationChanged(object sender, AuthenticationChangedEventArgs e)
        {
            _dispatcher.Invoke(_onAuthenticationChanged);
        }

        private void OnUiMessageRequested(object sender, UiMessageEventArgs e)
        {
            _dispatcher.Invoke(() => _onUiMessageRequested(e));
        }

        private void OnClientNoticesRequested(object sender, ClientNoticesEventArgs e)
        {
            _dispatcher.Invoke(() => _onClientNoticesRequested(e));
        }
    }
}
