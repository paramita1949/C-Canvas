using System;
using System.Net.Http;
using System.Net.Http.Headers;
using ImageColorChanger.Services.Auth;
using ImageColorChanger.Services.Interfaces;

namespace ImageColorChanger.Services
{
    /// <summary>
    /// 网络验证服务（骨架）
    /// 具体实现已按职责拆分到 AuthService.*.cs 分部文件。
    /// </summary>
    public partial class AuthService : IAuthService
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        static AuthService()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Clear();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CanvasCast/1.0");
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        // 多个验证API地址（按优先级排序 - 优先使用域名，IP地址作为最后备用）
        private static readonly string[] API_BASE_URLS = new[]
        {
            "https://wx.019890311.xyz",
            "https://xian.edu.kg",
            "https://jiucai.org.cn",
            "https://www.xian.edu.kg",
            "https://ym.jiucai.org.cn",
            "http://106.14.145.43:23412",
            "http://139.159.157.28:45851"
        };

        private const string VERIFY_ENDPOINT = "/api/auth/verify";
        private const string HEARTBEAT_ENDPOINT = "/api/auth/heartbeat";
        private const string NOTICE_ACK_ENDPOINT = "/api/auth/notice-ack";

        private static readonly string AUTH_DATA_FILE = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CanvasCast",
            ".auth"
        );

        private string _username;
        private string _token;
        private bool _isAuthenticated;
        private DateTime? _expiresAt;
        private DateTime? _lastServerTime;
        private DateTime? _lastLocalTime;
        private long _lastTickCount;
        private int _remainingDays;
        private DeviceInfo _deviceInfo;
        private int _resetDeviceCount = 0;
        private readonly AuthHeartbeatScheduler _heartbeatScheduler = new AuthHeartbeatScheduler();
        private DateTime? _lastSuccessfulHeartbeat;
        private const int MAX_OFFLINE_DAYS = 90;
        private static readonly TimeSpan AUTH_HEARTBEAT_INTERVAL = TimeSpan.FromHours(2);
        private static readonly TimeSpan NOTICE_HEARTBEAT_INTERVAL = TimeSpan.FromMinutes(10);

        private static System.Threading.Mutex _appMutex;
        private const string MUTEX_NAME = "Global\\CanvasCast_SingleInstance_E8F3C2A1";

        private static long _currentFileVersion = 0;
        private const string VERSION_REGISTRY_KEY = @"Software\CanvasCast\Auth";
        private const string VERSION_REGISTRY_VALUE = "MaxFileVersion";

        private string _authToken1;
        private string _authToken2;
        private long _authChecksum;

        private readonly AuthTrialProjectionSession _trialProjectionSession = new AuthTrialProjectionSession();
        private string _lastHolidayBonusKey;
        private string _lastAuthFailureReason;
        private PaymentInfo _lastPaymentInfo;
        private readonly System.Collections.Generic.HashSet<string> _shownClientNoticeKeys = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        private readonly AuthClock _authClock = new AuthClock();
        private readonly AuthStateStore _authStateStore = new AuthStateStore();
        private readonly AuthApiClient _authApiClient;
        private readonly AuthHeartbeatPolicy _authHeartbeatPolicy = new AuthHeartbeatPolicy();
        private readonly AuthDeviceFingerprint _authDeviceFingerprint = new AuthDeviceFingerprint();
        private readonly AuthTokenGuard _authTokenGuard = new AuthTokenGuard();
        private readonly AuthTrialProjectionToken _authTrialProjectionToken = new AuthTrialProjectionToken();
        private readonly AuthTrialProjectionPolicy _authTrialProjectionPolicy = new AuthTrialProjectionPolicy();
        private readonly AuthProjectionAccessPolicy _authProjectionAccessPolicy = new AuthProjectionAccessPolicy();

        private static AuthService _instance;
        private static readonly object _lock = new object();

        public static AuthService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new AuthService();
                        }
                    }
                }

                return _instance;
            }
        }

        private AuthService()
        {
            _isAuthenticated = false;
            _authApiClient = new AuthApiClient(_httpClient, API_BASE_URLS);

            try
            {
                bool createdNew;
                _appMutex = new System.Threading.Mutex(true, MUTEX_NAME, out createdNew);

#if DEBUG
                if (!createdNew)
                {
                    System.Diagnostics.Debug.WriteLine("🔒 [AuthService] 检测到多开实例");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("🔒 [AuthService] 已创建全局互斥锁");
                }
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 创建互斥锁失败: {ex.Message}");
#else
                _ = ex;
#endif
            }

            _ = TryLoadAuthDataAsync();
        }
    }
}
