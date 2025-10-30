using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Management;
using System.Linq;

namespace ImageColorChanger.Services
{
    /// <summary>
    /// 验证响应数据结构
    /// </summary>
    public class AuthResponse
    {
        public bool Success { get; set; }
        public bool Valid { get; set; }
        public string Message { get; set; }
        public string Reason { get; set; }
        public AuthData Data { get; set; }
        
        [JsonPropertyName("server_time")]
        public string ServerTimeString { get; set; }
    }

    public class AuthData
    {
        public string Username { get; set; }
        public string Email { get; set; }
        
        [JsonPropertyName("license_type")]
        public string LicenseType { get; set; }
        
        [JsonPropertyName("expires_at")]
        public long? ExpiresAt { get; set; }  // Unix时间戳（秒）
        
        [JsonPropertyName("remaining_days")]
        public int? RemainingDays { get; set; }
        
        [JsonPropertyName("remaining_hours")]
        public int? RemainingHours { get; set; }
        
        public string Token { get; set; }
        
        [JsonPropertyName("server_time")]
        public string ServerTimeString { get; set; }
        
        public string Warning { get; set; }
        
        [JsonPropertyName("device_info")]
        public DeviceInfo DeviceInfo { get; set; }
        
        [JsonPropertyName("reset_device_count")]
        public int? ResetDeviceCount { get; set; }
        
        [JsonPropertyName("trial_days")]
        public int? TrialDays { get; set; }
        
        [JsonPropertyName("max_devices")]
        public int? MaxDevices { get; set; }
    }

    /// <summary>
    /// 设备绑定信息
    /// </summary>
    public class DeviceInfo
    {
        [JsonPropertyName("bound_devices")]
        public int BoundDevices { get; set; }
        
        [JsonPropertyName("max_devices")]
        public int MaxDevices { get; set; }
        
        [JsonPropertyName("remaining_slots")]
        public int RemainingSlots { get; set; }
        
        [JsonPropertyName("is_new_device")]
        public bool IsNewDevice { get; set; }
    }

    /// <summary>
    /// 网络验证服务
    /// 负责与Cloudflare Workers API通信，验证用户身份和有效期
    /// 使用服务器时间防止本地时间篡改
    /// </summary>
    public class AuthService
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        
        // TODO: 替换为实际的Cloudflare Workers API地址
        private const string API_BASE_URL = "https://wx.019890311.xyz";
        private const string VERIFY_ENDPOINT = "/api/auth/verify";
        private const string HEARTBEAT_ENDPOINT = "/api/auth/heartbeat";
        
        // 持久化文件路径
        private static readonly string AUTH_DATA_FILE = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CanvasCast",
            ".auth"
        );
        
        private string _username;
        private string _token;
        private bool _isAuthenticated;
        private DateTime? _expiresAt;
        private DateTime? _lastServerTime; // 最后一次服务器时间
        private DateTime? _lastLocalTime;  // 对应的本地时间（仅用于兼容旧版本）
        private long _lastTickCount;       // 最后一次记录的 TickCount64（防篡改）
        private int _remainingDays;
        private DeviceInfo _deviceInfo;    // 设备绑定信息
        private int _resetDeviceCount = 0;     // 剩余重置设备次数（默认0）
        private System.Threading.Timer _heartbeatTimer;
        private DateTime? _lastSuccessfulHeartbeat; // 最后一次成功心跳的时间
        private const int MAX_OFFLINE_DAYS = 14;  // 最长离线天数（14天）
        
        // 🔒 全局互斥锁（防止多开）
        private static System.Threading.Mutex _appMutex;
        private const string MUTEX_NAME = "Global\\CanvasCast_SingleInstance_E8F3C2A1";
        
        // 🔒 文件版本号（防止旧文件覆盖新文件）
        private static long _currentFileVersion = 0;  // 当前文件版本号（基于ticks，单调递增）
        private const string VERSION_REGISTRY_KEY = @"Software\CanvasCast\Auth";
        private const string VERSION_REGISTRY_VALUE = "MaxFileVersion";
        
        // 🔒 分散验证：多个验证令牌，防止单点破解
        private string _authToken1;  // 验证令牌1
        private string _authToken2;  // 验证令牌2
        private long _authChecksum;  // 验证校验和
        
        // 🔒 试用投影验证（防止破解随机时间限制）
        private long _trialProjectionStartTick;  // 试用投影开始时刻（TickCount64）
        private int _trialDurationSeconds;       // 试用时长（秒）
        private string _trialProjectionToken;    // 试用投影令牌（SHA256）
        
        // 单例模式
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
            
            // 🔒 创建全局互斥锁（防止多开）
            try
            {
                bool createdNew;
                _appMutex = new System.Threading.Mutex(true, MUTEX_NAME, out createdNew);
                
                if (!createdNew)
                {
                    // 已经有实例在运行
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"🔒 [AuthService] 检测到多开实例");
                    #endif
                    
                    // 注意：这里不强制退出，由应用层决定如何处理
                    // 但互斥锁会在进程退出时自动释放
                }
                else
                {
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"🔒 [AuthService] 已创建全局互斥锁");
                    #endif
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 创建互斥锁失败: {ex.Message}");
                #else
                _ = ex;
                #endif
            }
            
            // 尝试从本地加载登录状态
            _ = TryLoadAuthDataAsync();
        }

        /// <summary>
        /// 检查是否为唯一实例（防止多开）
        /// </summary>
        public static bool CheckSingleInstance()
        {
            try
            {
                bool createdNew;
                var testMutex = new System.Threading.Mutex(false, MUTEX_NAME, out createdNew);
                
                if (!createdNew)
                {
                    testMutex.Close();
                    return false; // 已有实例运行
                }
                
                testMutex.Close();
                return true; // 唯一实例
            }
            catch
            {
                return true; // 检测失败，允许继续
            }
        }
        
        /// <summary>
        /// 从注册表读取最大文件版本号（防止旧文件回滚）
        /// </summary>
        private static long GetMaxFileVersionFromRegistry()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(VERSION_REGISTRY_KEY))
                {
                    var value = key?.GetValue(VERSION_REGISTRY_VALUE);
                    if (value != null && long.TryParse(value.ToString(), out var version))
                    {
                        return version;
                    }
                }
            }
            catch
            {
                // 读取失败，返回0
            }
            return 0;
        }
        
        /// <summary>
        /// 保存最大文件版本号到注册表
        /// </summary>
        private static void SaveMaxFileVersionToRegistry(long version)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(VERSION_REGISTRY_KEY))
                {
                    key?.SetValue(VERSION_REGISTRY_VALUE, version, Microsoft.Win32.RegistryValueKind.QWord);
                }
            }
            catch
            {
                // 保存失败，静默忽略
            }
        }
        
        /// <summary>
        /// 是否已认证
        /// </summary>
        public bool IsAuthenticated => _isAuthenticated;

        /// <summary>
        /// 用户名
        /// </summary>
        public string Username => _username;

        /// <summary>
        /// 账号到期时间
        /// </summary>
        public DateTime? ExpiresAt => _expiresAt;

        /// <summary>
        /// 剩余天数
        /// </summary>
        public int RemainingDays => _remainingDays;

        /// <summary>
        /// 设备绑定信息
        /// </summary>
        public DeviceInfo DeviceBindingInfo => _deviceInfo;

        /// <summary>
        /// 剩余重置设备次数
        /// </summary>
        public int ResetDeviceCount => _resetDeviceCount;

        /// <summary>
        /// 获取当前设备的硬件ID（用于显示）
        /// </summary>
        public string GetCurrentHardwareId()
        {
            return GetHardwareId();
        }

        /// <summary>
        /// 认证状态改变事件参数
        /// </summary>
        public class AuthenticationChangedEventArgs : EventArgs
        {
            public bool IsAuthenticated { get; set; }
            public bool IsAutoLogin { get; set; }
        }

        /// <summary>
        /// 事件：认证状态改变
        /// </summary>
        public event EventHandler<AuthenticationChangedEventArgs> AuthenticationChanged;

        /// <summary>
        /// 登录验证
        /// </summary>
        public async Task<(bool success, string message)> LoginAsync(string username, string password)
        {
            try
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"🔐 [AuthService] 开始登录验证: {username}");
                #endif

                // 获取混合后的硬件ID
                var hardwareId = GetHardwareId();
                
                var requestData = new
                {
                    username = username,
                    password = password,
                    hardware_id = hardwareId,
                    device_name = Environment.MachineName,
                    os_version = Environment.OSVersion.ToString(),
                    app_version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                };

                var jsonContent = JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(API_BASE_URL + VERIFY_ENDPOINT, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"🔐 [AuthService] 服务器响应: {responseContent}");
                #endif

                var authResponse = JsonSerializer.Deserialize<AuthResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (authResponse == null)
                {
                    return (false, "服务器响应解析失败");
                }

                if (!authResponse.Success)
                {
                    return (false, authResponse.Message ?? "验证失败");
                }

                if (!authResponse.Valid)
                {
                    return (false, authResponse.Message ?? "账号无效");
                }

                // 登录成功，保存状态
                _username = username;
                _token = authResponse.Data?.Token;
                
                // 解析过期时间（Unix时间戳转DateTime）
                if (authResponse.Data?.ExpiresAt.HasValue == true)
                {
                    _expiresAt = DateTimeOffset.FromUnixTimeSeconds(authResponse.Data.ExpiresAt.Value).LocalDateTime;
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"🔐 [AuthService] 过期时间: {_expiresAt}");
                    #endif
                }
                
                _remainingDays = authResponse.Data?.RemainingDays ?? 0;
                _deviceInfo = authResponse.Data?.DeviceInfo;
                _resetDeviceCount = authResponse.Data?.ResetDeviceCount ?? 0;  // 默认0次
                _isAuthenticated = true;
                
                // 🔒 初始化心跳时间
                _lastSuccessfulHeartbeat = DateTime.Now;
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"🔓 [AuthService] 解绑次数: {_resetDeviceCount}次");
                System.Diagnostics.Debug.WriteLine($"🔓 [AuthService] 服务器返回的 ResetDeviceCount: {authResponse.Data?.ResetDeviceCount?.ToString() ?? "null"}");
                #endif
                
                // 🔒 生成验证令牌（防止跳过登录）
                GenerateAuthTokens();
                
                // 输出设备绑定信息
                #if DEBUG
                if (_deviceInfo != null)
                {
                    System.Diagnostics.Debug.WriteLine($"🔐 [AuthService] 设备绑定信息:");
                    System.Diagnostics.Debug.WriteLine($"   已绑定设备: {_deviceInfo.BoundDevices}台");
                    System.Diagnostics.Debug.WriteLine($"   最大设备数: {_deviceInfo.MaxDevices}台");
                    System.Diagnostics.Debug.WriteLine($"   剩余可绑定: {_deviceInfo.RemainingSlots}台");
                    if (_deviceInfo.IsNewDevice)
                    {
                        System.Diagnostics.Debug.WriteLine($"   ✨ 这是新绑定的设备");
                    }
                }
                #endif
                
                // 保存服务器时间和本地时间的对应关系
                if (!string.IsNullOrEmpty(authResponse.Data?.ServerTimeString))
                {
                    if (DateTime.TryParse(authResponse.Data.ServerTimeString, out var serverTime))
                    {
                        _lastServerTime = serverTime;
                        _lastLocalTime = DateTime.Now;
                        _lastTickCount = Environment.TickCount64; // 记录 TickCount，防篡改
                        
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"🔐 [AuthService] 服务器时间: {_lastServerTime}");
                        System.Diagnostics.Debug.WriteLine($"🔐 [AuthService] 本地时间: {_lastLocalTime}");
                        System.Diagnostics.Debug.WriteLine($"🔐 [AuthService] TickCount: {_lastTickCount}");
                        var timeDiff = (_lastLocalTime.Value - _lastServerTime.Value).TotalSeconds;
                        System.Diagnostics.Debug.WriteLine($"🔐 [AuthService] 时间差: {timeDiff:F1}秒 (本地-服务器)");
                        #endif
                    }
                }

                // 启动心跳
                StartHeartbeat();

                // 保存登录状态到本地
                _ = SaveAuthDataAsync();

                // 触发事件（手动登录）
                AuthenticationChanged?.Invoke(this, new AuthenticationChangedEventArgs 
                { 
                    IsAuthenticated = true, 
                    IsAutoLogin = false 
                });

                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"✅ [AuthService] 登录成功: {_username}, 剩余{_remainingDays}天");
                #endif

                return (true, $"登录成功！账号有效期剩余 {_remainingDays} 天");
            }
            catch (HttpRequestException ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [AuthService] 网络请求失败: {ex.Message}");
                #else
                _ = ex; // 避免未使用变量警告
                #endif
                return (false, "网络连接失败，请检查网络设置");
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [AuthService] 登录异常: {ex.Message}");
                #endif
                return (false, $"登录失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 注册新账号（自动获取硬件ID）
        /// </summary>
        public async Task<(bool success, string message)> RegisterAsync(string username, string password, string email = null)
        {
            try
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"📝 [AuthService] 开始注册: {username}");
                #endif

                // 获取5项硬件指纹
                var cpuId = GetCpuId();
                var motherboardSerial = GetBoardSerial();
                var diskSerial = GetDiskSerial();
                var biosUuid = GetBiosUuid();
                var windowsInstallId = GetWindowsInstallId();

                var requestData = new
                {
                    username = username,
                    password = password,
                    email = email,
                    cpu_id = cpuId,
                    motherboard_serial = motherboardSerial,
                    disk_serial = diskSerial,
                    bios_uuid = biosUuid,
                    windows_install_id = windowsInstallId,
                    device_name = Environment.MachineName
                };

                var jsonContent = JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(API_BASE_URL + "/api/user/register", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"📝 [AuthService] 服务器响应: {responseContent}");
                #endif

                var registerResponse = JsonSerializer.Deserialize<AuthResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (registerResponse == null)
                {
                    return (false, "服务器响应解析失败");
                }

                if (!registerResponse.Success)
                {
                    return (false, registerResponse.Message ?? "注册失败");
                }

                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"✅ [AuthService] 注册成功: {username}");
                if (registerResponse.Data != null)
                {
                    System.Diagnostics.Debug.WriteLine($"📝 [注册] 试用期: {registerResponse.Data.TrialDays}天");
                    System.Diagnostics.Debug.WriteLine($"📝 [注册] 最大设备数: {registerResponse.Data.MaxDevices}");
                    if (registerResponse.Data.ExpiresAt.HasValue)
                    {
                        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(registerResponse.Data.ExpiresAt.Value).LocalDateTime;
                        System.Diagnostics.Debug.WriteLine($"📝 [注册] 过期时间: {expiresAt}");
                    }
                }
                #endif

                return (true, registerResponse.Message ?? "注册成功！");
            }
            catch (HttpRequestException ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [AuthService] 网络请求失败: {ex.Message}");
                #else
                _ = ex; // 避免未使用变量警告
                #endif
                return (false, "网络连接失败，请检查网络设置");
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [AuthService] 注册异常: {ex.Message}");
                #endif
                return (false, $"注册失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 退出登录
        /// </summary>
        public void Logout()
        {
            _isAuthenticated = false;
            _username = null;
            _token = null;
            _expiresAt = null;
            _remainingDays = 0;
            _lastServerTime = null;
            _lastLocalTime = null;
            
            StopHeartbeat();
            
            // 删除本地保存的登录状态
            DeleteAuthData();
            
            AuthenticationChanged?.Invoke(this, new AuthenticationChangedEventArgs 
            { 
                IsAuthenticated = false, 
                IsAutoLogin = false 
            });

            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"🔐 [AuthService] 已退出登录");
            #endif
        }

        /// <summary>
        /// 快速验证投影权限（联网验证，用于投影开始时）
        /// 如果未登录，强制要求联网验证
        /// </summary>
        public async Task<(bool allowed, string message)> VerifyProjectionPermissionAsync()
        {
            // 如果已登录且有效，快速通过
            if (_isAuthenticated && CanUseProjection())
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"✅ [投影权限] 已登录且有效，允许投影");
                #endif
                return (true, "已登录");
            }

            // 未登录，尝试联网验证
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"⚠️ [投影权限] 未登录，尝试联网验证...");
            #endif

            // 第一步：检查基础网络（百度）
            bool networkAvailable = false;
            try
            {
                using (var networkClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(2) })
                {
                    var networkResponse = await networkClient.GetAsync("https://www.baidu.com");
                    networkAvailable = networkResponse.IsSuccessStatusCode;
                    
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"ℹ️ [投影权限] 网络检测（百度）: {(networkAvailable ? "可用" : "不可用")}");
                    #endif
                }
            }
            catch
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"ℹ️ [投影权限] 网络检测（百度）: 不可用");
                #endif
                // 网络不可用，允许试用模式
                return (true, "试用模式（离线）");
            }

            // 第二步：如果网络可用，检查服务器健康状态
            if (networkAvailable)
            {
                try
                {
                    using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(3)))
                    {
                        var response = await _httpClient.GetAsync(API_BASE_URL + "/api/auth/verify", cts.Token);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            // 服务器正常，但用户未登录
                            #if DEBUG
                            System.Diagnostics.Debug.WriteLine($"⚠️ [投影权限] 服务器正常但未登录，试用投影");
                            #endif
                            return (false, "检测到网络连接，请先登录后使用投影功能");
                        }
                        else
                        {
                            // 服务器返回错误状态码（如500），视为服务器故障
                            #if DEBUG
                            System.Diagnostics.Debug.WriteLine($"⚠️ [投影权限] 服务器故障（{response.StatusCode}），允许试用模式");
                            #endif
                            return (true, "试用模式（服务器异常）");
                        }
                    }
                }
                catch (System.Threading.Tasks.TaskCanceledException)
                {
                    // 服务器超时，视为服务器故障
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"ℹ️ [投影权限] 服务器超时，允许试用模式");
                    #endif
                    return (true, "试用模式（服务器超时）");
                }
                catch (Exception ex)
                {
                    // 服务器连接失败，允许试用
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"ℹ️ [投影权限] 服务器连接失败: {ex.Message}，允许试用模式");
                    #else
                    _ = ex;
                    #endif
                    return (true, "试用模式（服务器不可达）");
                }
            }

            return (false, "请先登录");
        }

        /// <summary>
        /// 手动刷新账号信息（尝试从服务器获取最新信息）
        /// 成功返回true，失败返回false（使用本地缓存）
        /// </summary>
        public async Task<bool> RefreshAccountInfoAsync()
        {
            if (!_isAuthenticated || string.IsNullOrEmpty(_username))
            {
                return false;
            }

            try
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"🔄 [刷新] 尝试从服务器刷新账号信息...");
                #endif

                // 获取混合后的硬件ID
                var hardwareId = GetHardwareId();
                
                var requestData = new
                {
                    token = _token,
                    hardware_id = hardwareId
                };

                var jsonContent = JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // 设置较短的超时时间（8秒），快速失败
                using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8)))
                {
                    var response = await _httpClient.PostAsync(API_BASE_URL + HEARTBEAT_ENDPOINT, content, cts.Token);
                    var responseContent = await response.Content.ReadAsStringAsync();

                    var authResponse = JsonSerializer.Deserialize<AuthResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (authResponse == null || !authResponse.Success || !authResponse.Valid)
                    {
                        // 检查失效原因
                        string failureReason = authResponse?.Message ?? "账号验证失败";
                        
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"❌ [刷新] 服务器返回失败: {failureReason}");
                        System.Diagnostics.Debug.WriteLine($"   失效原因(reason): {authResponse?.Reason}");
                        #endif
                        
                        // 🔒 只有成功获得服务器响应且明确返回失效原因时才强制退出
                        // 如果 authResponse 为 null（网络问题/解析失败），不强制退出
                        if (authResponse != null && !string.IsNullOrEmpty(authResponse.Reason))
                        {
                            bool forceLogout = false;
                            string logoutTitle = "登录已失效";
                            string logoutMessage = failureReason;
                            
                            // 1. 设备被删除/解绑
                            if (authResponse.Reason == "device_unbound" || 
                                authResponse.Reason == "device_reset" ||
                                authResponse.Reason == "device_mismatch")
                            {
                                forceLogout = true;
                                logoutTitle = "设备验证失败";
                                logoutMessage = "您的设备已被解绑，请重新登录";
                                #if DEBUG
                                System.Diagnostics.Debug.WriteLine($"🔒 [刷新] 设备已被删除或不匹配，强制退出");
                                #endif
                            }
                            
                            // 2. 账号被禁用
                            if (authResponse.Reason == "disabled")
                            {
                                forceLogout = true;
                                logoutTitle = "账号已被禁用";
                                logoutMessage = "您的账号已被禁用，请联系管理员";
                                #if DEBUG
                                System.Diagnostics.Debug.WriteLine($"🔒 [刷新] 账号已被管理员禁用，强制退出");
                                #endif
                            }
                            
                            // 3. 账号已过期
                            if (authResponse.Reason == "expired")
                            {
                                forceLogout = true;
                                logoutTitle = "账号已过期";
                                logoutMessage = "您的账号已过期，请联系管理员续期";
                                #if DEBUG
                                System.Diagnostics.Debug.WriteLine($"🔒 [刷新] 账号已过期，强制退出");
                                #endif
                            }
                            
                            // 4. 会话过期
                            if (authResponse.Reason == "session_expired")
                            {
                                forceLogout = true;
                                logoutTitle = "登录已失效";
                                logoutMessage = "登录已失效，请重新登录";
                                #if DEBUG
                                System.Diagnostics.Debug.WriteLine($"🔒 [刷新] 会话已过期（可能账号被删除或在其他设备登录），强制退出");
                                #endif
                            }
                            
                            // 5. 用户不存在
                            if (authResponse.Reason == "user_not_found")
                            {
                                forceLogout = true;
                                logoutTitle = "账号不存在";
                                logoutMessage = "账号不存在，请联系管理员";
                                #if DEBUG
                                System.Diagnostics.Debug.WriteLine($"🔒 [刷新] 用户不存在（账号已被删除），强制退出");
                                #endif
                            }
                            
                            // 执行强制退出
                            if (forceLogout)
                            {
                                Logout();
                                
                                // 通知UI显示消息
                                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                                {
                                    System.Windows.MessageBox.Show(
                                        logoutMessage,
                                        logoutTitle,
                                        System.Windows.MessageBoxButton.OK,
                                        System.Windows.MessageBoxImage.Warning);
                                });
                                return false;
                            }
                        }
                        
                        // 网络问题或其他失效原因，不强制退出，返回false让UI处理
                        #if DEBUG
                        if (authResponse == null)
                        {
                            System.Diagnostics.Debug.WriteLine($"⚠️ [刷新] 响应解析失败（可能网络问题），不强制退出");
                        }
                        else if (string.IsNullOrEmpty(authResponse.Reason))
                        {
                            System.Diagnostics.Debug.WriteLine($"⚠️ [刷新] 无明确失效原因，不强制退出");
                        }
                        #endif
                        
                        return false;
                    }

                    // 更新所有信息
                    if (!string.IsNullOrEmpty(authResponse.Data?.ServerTimeString))
                    {
                        if (DateTime.TryParse(authResponse.Data.ServerTimeString, out var serverTime))
                        {
                            _lastServerTime = serverTime;
                            _lastLocalTime = DateTime.Now;
                            _lastTickCount = Environment.TickCount64;
                        }
                    }
                    
                    // 解析过期时间（Unix时间戳转DateTime）
                    if (authResponse.Data?.ExpiresAt.HasValue == true)
                    {
                        _expiresAt = DateTimeOffset.FromUnixTimeSeconds(authResponse.Data.ExpiresAt.Value).LocalDateTime;
                    }
                    
                    _remainingDays = authResponse.Data?.RemainingDays ?? 0;
                    _deviceInfo = authResponse.Data?.DeviceInfo;
                    _resetDeviceCount = authResponse.Data?.ResetDeviceCount ?? 0;
                    
                    // 更新本地缓存
                    _ = SaveAuthDataAsync();

                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"✅ [刷新] 成功，剩余{_remainingDays}天，解绑{_resetDeviceCount}次");
                    if (_deviceInfo != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"✅ [刷新] 设备: 已绑定{_deviceInfo.BoundDevices}/{_deviceInfo.MaxDevices}, 剩余{_deviceInfo.RemainingSlots}");
                    }
                    #endif

                    return true;
                }
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⏱️ [刷新] 超时，使用本地缓存");
                #endif
                return false;
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [刷新] 异常: {ex.Message}，使用本地缓存");
                #else
                _ = ex;
                #endif
                return false;
            }
        }

        /// <summary>
        /// 心跳检查（定期验证账号状态）
        /// </summary>
        private async void HeartbeatCallback(object state)
        {
            if (!_isAuthenticated || string.IsNullOrEmpty(_username))
            {
                StopHeartbeat();
                return;
            }

            try
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"💓 [心跳] 开始心跳检查... (当前时间: {DateTime.Now:HH:mm:ss})");
                #endif

                // 获取混合后的硬件ID
                var hardwareId = GetHardwareId();
                
                var requestData = new
                {
                    token = _token,
                    hardware_id = hardwareId
                };

                var jsonContent = JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(API_BASE_URL + HEARTBEAT_ENDPOINT, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                var authResponse = JsonSerializer.Deserialize<AuthResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (authResponse == null || !authResponse.Success || !authResponse.Valid)
                {
                    // 检查失效原因
                    string failureReason = authResponse?.Message ?? "账号已失效";
                    
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"❌ [AuthService] 心跳检查失败: {failureReason}");
                    System.Diagnostics.Debug.WriteLine($"   失效原因(reason): {authResponse?.Reason}");
                    #endif
                    
                    // 🔒 需要立即强制退出的情况（不检查本地缓存）
                    bool forceLogout = false;
                    string logoutTitle = "登录已失效";
                    string logoutMessage = failureReason;
                    
                    // 1. 设备被重置/解绑
                    if (authResponse?.Reason == "device_reset" || 
                        authResponse?.Reason == "device_unbound" ||
                        authResponse?.Reason == "device_mismatch" ||
                        authResponse?.Message?.Contains("设备已被") == true || 
                        authResponse?.Message?.Contains("解绑") == true)
                    {
                        forceLogout = true;
                        logoutTitle = "设备验证失败";
                        logoutMessage = "您的设备已被解绑，请重新登录";
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"🔒 [AuthService] 设备已被管理员重置，强制退出");
                        #endif
                    }
                    
                    // 2. 账号被禁用
                    if (authResponse?.Reason == "disabled")
                    {
                        forceLogout = true;
                        logoutTitle = "账号已被禁用";
                        logoutMessage = "您的账号已被禁用，请联系管理员";
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"🔒 [AuthService] 账号已被管理员禁用，强制退出");
                        #endif
                    }
                    
                    // 3. 账号已过期
                    if (authResponse?.Reason == "expired")
                    {
                        forceLogout = true;
                        logoutTitle = "账号已过期";
                        logoutMessage = "您的账号已过期，请联系管理员续期";
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"🔒 [AuthService] 账号已过期，强制退出");
                        #endif
                    }
                    
                    // 4. 会话过期（可能是在其他设备登录、账号被删除、或凭证文件跨版本复制）
                    if (authResponse?.Reason == "session_expired")
                    {
                        forceLogout = true;
                        logoutTitle = "登录已失效";
                        logoutMessage = "登录已失效，请重新登录";
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"🔒 [AuthService] 会话已过期（可能账号被删除或在其他设备登录），强制退出");
                        #endif
                    }
                    
                    // 5. 用户不存在
                    if (authResponse?.Reason == "user_not_found")
                    {
                        forceLogout = true;
                        logoutTitle = "账号不存在";
                        logoutMessage = "账号不存在，请联系管理员";
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"🔒 [AuthService] 用户不存在（账号已被删除），强制退出");
                        #endif
                    }
                    
                    // 执行强制退出
                    if (forceLogout)
                    {
                        Logout();
                        
                        // 通知UI显示消息
                        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            System.Windows.MessageBox.Show(
                                logoutMessage,
                                logoutTitle,
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Warning);
                        });
                        return;
                    }
                    
                    // 其他失效原因（网络问题等），检查本地缓存
                    if (CanUseProjection())
                    {
                        // 本地缓存显示还在有效期内，可能是网络问题，继续使用
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 心跳失败，但本地缓存显示未过期，继续使用");
                        #endif
                        return;
                    }
                    
                    // 本地缓存也显示已过期，真的失效了
                    Logout();
                    return;
                }

                // 更新服务器时间和过期时间
                if (!string.IsNullOrEmpty(authResponse.Data?.ServerTimeString))
                {
                    if (DateTime.TryParse(authResponse.Data.ServerTimeString, out var serverTime))
                    {
                        _lastServerTime = serverTime;
                        _lastLocalTime = DateTime.Now;
                        _lastTickCount = Environment.TickCount64; // 记录 TickCount，防篡改
                    }
                }
                
                // 解析过期时间（Unix时间戳转DateTime）
                if (authResponse.Data?.ExpiresAt.HasValue == true)
                {
                    _expiresAt = DateTimeOffset.FromUnixTimeSeconds(authResponse.Data.ExpiresAt.Value).LocalDateTime;
                }
                _remainingDays = authResponse.Data?.RemainingDays ?? 0;
                _deviceInfo = authResponse.Data?.DeviceInfo;  // 更新设备信息
                _resetDeviceCount = authResponse.Data?.ResetDeviceCount ?? 0;  // 更新解绑次数（默认0）
                
                // 🔒 记录成功心跳时间（用于离线时长检测）
                _lastSuccessfulHeartbeat = DateTime.Now;
                
                // 更新本地缓存
                _ = SaveAuthDataAsync();

                #if DEBUG
                var nextHeartbeat = DateTime.Now.AddMinutes(20);
                System.Diagnostics.Debug.WriteLine($"✅ [心跳] 心跳正常，剩余{_remainingDays}天，解绑{_resetDeviceCount}次");
                System.Diagnostics.Debug.WriteLine($"💓 [心跳] 下次心跳时间: {nextHeartbeat:HH:mm:ss}");
                #endif
            }
            catch (Exception ex)
            {
                // 网络异常，检查本地缓存和离线时长
                
                // 🔒 检查离线时长
                if (_lastSuccessfulHeartbeat != null)
                {
                    var offlineDays = (DateTime.Now - _lastSuccessfulHeartbeat.Value).TotalDays;
                    
                    // 调试信息已注释（保留异常变量避免警告）
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 心跳网络异常: {ex.Message}");
                    //System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 最后成功心跳: {_lastSuccessfulHeartbeat}");
                    //System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 离线时长: {offlineDays:F1} 天");
                    //#else
                    _ = ex; // 避免未使用变量警告
                    //#endif
                    
                    if (offlineDays > MAX_OFFLINE_DAYS)
                    {
                        // 离线时间超过限制，强制退出
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"🔒 [AuthService] 离线时间超过 {MAX_OFFLINE_DAYS} 天，强制退出");
                        #endif
                        
                        Logout();
                        
                        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            System.Windows.MessageBox.Show(
                                $"账号已离线超过 {MAX_OFFLINE_DAYS} 天，请重新联网登录验证。",
                                "离线时间过长",
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Warning);
                        });
                        return;
                    }
                }
                
                // 检查账号是否过期
                if (CanUseProjection())
                {
                    // 本地缓存显示还在有效期内，允许离线使用
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 本地缓存有效，允许离线使用");
                    #endif
                    return;
                }
                
                // 本地缓存也过期了，强制退出
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 心跳网络异常且本地缓存已过期");
                #endif
                
                Logout();
            }
        }

        /// <summary>
        /// 启动心跳定时器（登录后1分钟首次检查，之后每20分钟检查一次）
        /// </summary>
        private void StartHeartbeat()
        {
            StopHeartbeat();
            _heartbeatTimer = new System.Threading.Timer(
                HeartbeatCallback,
                null,
                TimeSpan.FromMinutes(1),   // 登录后1分钟首次检查
                TimeSpan.FromMinutes(20)   // 之后每20分钟检查一次
            );

            #if DEBUG
            var firstHeartbeat = DateTime.Now.AddMinutes(1);
            var secondHeartbeat = DateTime.Now.AddMinutes(21); // 首次1分钟 + 间隔20分钟
            System.Diagnostics.Debug.WriteLine($"💓 [心跳] 心跳已启动（登录后1分钟首次检查，之后每20分钟）");
            System.Diagnostics.Debug.WriteLine($"💓 [心跳] 首次心跳时间: {firstHeartbeat:HH:mm:ss}");
            System.Diagnostics.Debug.WriteLine($"💓 [心跳] 第二次心跳时间: {secondHeartbeat:HH:mm:ss}");
            #endif
        }

        /// <summary>
        /// 停止心跳定时器
        /// </summary>
        private void StopHeartbeat()
        {
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
        }

        /// <summary>
        /// 生成验证令牌（登录成功时调用）
        /// </summary>
        private void GenerateAuthTokens()
        {
            if (string.IsNullOrEmpty(_username) || string.IsNullOrEmpty(_token))
            {
                _authToken1 = null;
                _authToken2 = null;
                _authChecksum = 0;
                return;
            }
            
            // 令牌1：用户名 + Token 的哈希
            using (var sha256 = SHA256.Create())
            {
                var bytes1 = Encoding.UTF8.GetBytes($"{_username}:{_token}:TOKEN1");
                var hash1 = sha256.ComputeHash(bytes1);
                _authToken1 = Convert.ToBase64String(hash1);
            }
            
            // 令牌2：过期时间 + 剩余天数的哈希
            using (var sha256 = SHA256.Create())
            {
                var bytes2 = Encoding.UTF8.GetBytes($"{_expiresAt?.Ticks}:{_remainingDays}:TOKEN2");
                var hash2 = sha256.ComputeHash(bytes2);
                _authToken2 = Convert.ToBase64String(hash2);
            }
            
            // 校验和：令牌1和令牌2的组合哈希
            _authChecksum = _authToken1.GetHashCode() ^ _authToken2.GetHashCode();
        }
        
        /// <summary>
        /// 验证令牌完整性（防止单点破解）
        /// </summary>
        private bool ValidateAuthTokens()
        {
            // 如果没有令牌，认为未登录
            if (string.IsNullOrEmpty(_authToken1) || string.IsNullOrEmpty(_authToken2))
            {
                return false;
            }
            
            // 验证校验和
            var expectedChecksum = _authToken1.GetHashCode() ^ _authToken2.GetHashCode();
            if (_authChecksum != expectedChecksum)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 令牌校验和不匹配，可能被篡改");
                #endif
                return false;
            }
            
            // 重新验证令牌1
            using (var sha256 = SHA256.Create())
            {
                var bytes1 = Encoding.UTF8.GetBytes($"{_username}:{_token}:TOKEN1");
                var hash1 = sha256.ComputeHash(bytes1);
                var expectedToken1 = Convert.ToBase64String(hash1);
                if (_authToken1 != expectedToken1)
                {
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 令牌1验证失败");
                    #endif
                    return false;
                }
            }
            
            // 重新验证令牌2
            using (var sha256 = SHA256.Create())
            {
                var bytes2 = Encoding.UTF8.GetBytes($"{_expiresAt?.Ticks}:{_remainingDays}:TOKEN2");
                var hash2 = sha256.ComputeHash(bytes2);
                var expectedToken2 = Convert.ToBase64String(hash2);
                if (_authToken2 != expectedToken2)
                {
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 令牌2验证失败");
                    #endif
                    return false;
                }
            }
            
            return true;
        }

        /// <summary>
        /// 验证是否可以使用投影功能（防止时间篡改 + 防止单点破解）
        /// </summary>
        public bool CanUseProjection()
        {
            // 🔒 多重验证1：检查认证状态
            if (!_isAuthenticated)
            {
                // 🔒 隐藏验证：未登录时检查试用投影状态
                // 即使破解者跳过 IsTrialProjectionExpired()，这里也会验证
                if (GetTrialProjectionStatus() != 0x1A2B3C4D)
                {
                    return false;
                }
                return false; // 未登录不允许使用（试用投影由其他逻辑控制）
            }
            
            // 🔒 多重验证2：验证令牌完整性（防止跳过登录）
            if (!ValidateAuthTokens())
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 令牌验证失败，拒绝投影");
                #endif
                return false;
            }

            // 使用服务器时间进行验证
            var estimatedServerTime = GetEstimatedServerTime();
            
            if (_expiresAt == null)
            {
                return false;
            }

            bool isValid = estimatedServerTime < _expiresAt.Value;

            #if DEBUG
            if (!isValid)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 账号已过期");
                System.Diagnostics.Debug.WriteLine($"   估算服务器时间: {estimatedServerTime}");
                System.Diagnostics.Debug.WriteLine($"   过期时间: {_expiresAt}");
            }
            #endif

            return isValid;
        }

        /// <summary>
        /// 获取估算的服务器时间（使用 TickCount64 防止本地时间篡改）
        /// TickCount64 是系统启动后的毫秒数，不受系统时间修改影响
        /// </summary>
        private DateTime GetEstimatedServerTime()
        {
            if (_lastServerTime == null)
            {
                // 如果没有服务器时间记录，使用本地时间（降级方案）
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"🕐 [AuthService] 无服务器时间记录，使用本地时间: {DateTime.Now}");
                #endif
                return DateTime.Now;
            }

            // 使用 TickCount64 计算真实流逝的时间（不受系统时间修改影响）
            long currentTick = Environment.TickCount64;
            long elapsedMilliseconds = currentTick - _lastTickCount;
            
            // 防止负数（系统重启导致 TickCount 重置）
            if (elapsedMilliseconds < 0)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] TickCount 异常（可能系统重启），使用本地时间");
                #endif
                // 降级到使用本地时间差（虽然不完美，但总比崩溃好）
                if (_lastLocalTime != null)
                {
                    var localElapsed = DateTime.Now - _lastLocalTime.Value;
                    // 防止用户回退时间
                    if (localElapsed.TotalSeconds < 0)
                    {
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 检测到时间回退，强制使用正向流逝");
                        #endif
                        localElapsed = TimeSpan.Zero;
                    }
                    return _lastServerTime.Value + localElapsed;
                }
                return DateTime.Now;
            }
            
            // 估算当前的服务器时间
            var elapsedTimeSpan = TimeSpan.FromMilliseconds(elapsedMilliseconds);
            var estimatedServerTime = _lastServerTime.Value + elapsedTimeSpan;

            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"🕐 [AuthService] 上次服务器时间: {_lastServerTime.Value}");
            System.Diagnostics.Debug.WriteLine($"🕐 [AuthService] 上次Tick: {_lastTickCount}");
            System.Diagnostics.Debug.WriteLine($"🕐 [AuthService] 当前Tick: {currentTick}");
            System.Diagnostics.Debug.WriteLine($"🕐 [AuthService] 真实流逝: {elapsedTimeSpan.TotalSeconds:F1} 秒");
            System.Diagnostics.Debug.WriteLine($"🕐 [AuthService] 估算服务器时间: {estimatedServerTime}");
            
            // 额外检测：对比本地时间流逝，检测时间篡改
            if (_lastLocalTime != null)
            {
                var localElapsed = DateTime.Now - _lastLocalTime.Value;
                var timeDiff = Math.Abs((localElapsed - elapsedTimeSpan).TotalSeconds);
                if (timeDiff > 600) // 差异超过1分钟
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 检测到时间异常！");
                    System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 本地时间流逝: {localElapsed.TotalSeconds:F1} 秒");
                    System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] Tick流逝: {elapsedTimeSpan.TotalSeconds:F1} 秒");
                    System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 差异: {timeDiff:F1} 秒（可能本地时间被修改）");
                }
            }
            #endif

            return estimatedServerTime;
        }

        /// <summary>
        /// 获取硬件ID（用于设备绑定）
        /// 混合5项硬件信息生成唯一硬件ID
        /// </summary>
        private string GetHardwareId()
        {
            try
            {
                var cpuId = GetCpuId();
                var boardSerial = GetBoardSerial();
                var diskSerial = GetDiskSerial();
                var biosUuid = GetBiosUuid();
                var windowsInstallId = GetWindowsInstallId();
                
                // 组合5项硬件信息
                var combined = $"{cpuId}|{boardSerial}|{diskSerial}|{biosUuid}|{windowsInstallId}";
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"🔐 [硬件ID] CPU: {cpuId}");
                System.Diagnostics.Debug.WriteLine($"🔐 [硬件ID] 主板: {boardSerial}");
                System.Diagnostics.Debug.WriteLine($"🔐 [硬件ID] 硬盘: {diskSerial}");
                System.Diagnostics.Debug.WriteLine($"🔐 [硬件ID] BIOS UUID: {biosUuid}");
                System.Diagnostics.Debug.WriteLine($"🔐 [硬件ID] Windows安装ID: {windowsInstallId}");
                #endif
                
                // 生成SHA256哈希
                using (var sha256 = SHA256.Create())
                {
                    var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
                    var hardwareId = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                    
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"🔐 [硬件ID] 最终哈希: {hardwareId}");
                    #endif
                    
                    return hardwareId;
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 获取硬件ID失败: {ex.Message}");
                #else
                _ = ex; // 避免未使用变量警告
                #endif
                // 降级方案：使用机器名的哈希
                using (var sha256 = SHA256.Create())
                {
                    var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(Environment.MachineName));
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                }
            }
        }

        /// <summary>
        /// 获取CPU ID
        /// </summary>
        private string GetCpuId()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        var cpuId = obj["ProcessorId"]?.ToString();
                        if (!string.IsNullOrEmpty(cpuId) && cpuId != "UNKNOWN")
                        {
                            return cpuId;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [硬件ID] 获取CPU ID失败: {ex.Message}");
                #else
                _ = ex;
                #endif
            }
            return "CPU_UNKNOWN";
        }

        /// <summary>
        /// 获取主板序列号
        /// </summary>
        private string GetBoardSerial()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        var serial = obj["SerialNumber"]?.ToString();
                        if (!string.IsNullOrEmpty(serial) && serial != "UNKNOWN")
                        {
                            return serial;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [硬件ID] 获取主板序列号失败: {ex.Message}");
                #else
                _ = ex;
                #endif
            }
            return "BOARD_UNKNOWN";
        }

        /// <summary>
        /// 获取硬盘序列号（物理磁盘）
        /// </summary>
        private string GetDiskSerial()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_PhysicalMedia"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        var serial = obj["SerialNumber"]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(serial))
                        {
                            return serial;
                        }
                    }
                }
                
                // 备用方案：使用 Win32_DiskDrive
                using (var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        var serial = obj["SerialNumber"]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(serial))
                        {
                            return serial;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [硬件ID] 获取硬盘序列号失败: {ex.Message}");
                #else
                _ = ex;
                #endif
            }
            return "DISK_UNKNOWN";
        }

        /// <summary>
        /// 获取内存信息（使用内存条序列号）
        /// </summary>
        private string GetMemorySerial()
        {
            try
            {
                var memorySerials = new System.Collections.Generic.List<string>();
                using (var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_PhysicalMemory"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        var serial = obj["SerialNumber"]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(serial))
                        {
                            memorySerials.Add(serial);
                        }
                    }
                }
                
                if (memorySerials.Count > 0)
                {
                    // 将所有内存序列号排序后组合（防止插槽顺序变化）
                    memorySerials.Sort();
                    return string.Join("_", memorySerials);
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [硬件ID] 获取内存序列号失败: {ex.Message}");
                #else
                _ = ex;
                #endif
            }
            return "MEMORY_UNKNOWN";
        }

        /// <summary>
        /// 获取BIOS UUID
        /// </summary>
        private string GetBiosUuid()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        var uuid = obj["UUID"]?.ToString();
                        if (!string.IsNullOrEmpty(uuid) && uuid != "UNKNOWN")
                        {
                            return uuid;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [硬件ID] 获取BIOS UUID失败: {ex.Message}");
                #else
                _ = ex;
                #endif
            }
            return null;
        }

        /// <summary>
        /// 获取Windows安装ID（MachineGuid）
        /// </summary>
        private string GetWindowsInstallId()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    if (key != null)
                    {
                        var guid = key.GetValue("MachineGuid")?.ToString();
                        if (!string.IsNullOrEmpty(guid))
                        {
                            return guid;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [硬件ID] 获取Windows安装ID失败: {ex.Message}");
                #else
                _ = ex;
                #endif
            }
            return null;
        }

        /// <summary>
        /// 获取认证状态摘要（用于UI显示）
        /// </summary>
        public string GetStatusSummary()
        {
            if (!_isAuthenticated)
            {
                return "未登录";
            }

            if (!CanUseProjection())
            {
                return "账号已过期";
            }

            if (_remainingDays <= 7)
            {
                return $"账号即将过期（剩余{_remainingDays}天）";
            }

            return $"已登录 - {_username}（剩余{_remainingDays}天）";
        }

        /// <summary>
        /// 获取设备绑定信息摘要
        /// </summary>
        public string GetDeviceBindingSummary()
        {
            if (_deviceInfo == null)
            {
                return "设备信息未知";
            }

            if (_deviceInfo.RemainingSlots <= 0)
            {
                return $"设备已满：{_deviceInfo.BoundDevices}/{_deviceInfo.MaxDevices}台";
            }

            return $"设备绑定：{_deviceInfo.BoundDevices}/{_deviceInfo.MaxDevices}台（剩余{_deviceInfo.RemainingSlots}个槽位）";
        }

        /// <summary>
        /// 用户自助重置绑定设备（需要密码验证，限3次）
        /// </summary>
        public async Task<(bool success, string message, int remainingCount)> ResetDevicesAsync(string password)
        {
            if (!_isAuthenticated || string.IsNullOrEmpty(_username))
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [解绑设备] 未登录或用户名为空");
                System.Diagnostics.Debug.WriteLine($"   IsAuthenticated: {_isAuthenticated}");
                System.Diagnostics.Debug.WriteLine($"   Username: {_username ?? "null"}");
                #endif
                return (false, "请先登录", 0);
            }

            try
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"🔄 [解绑设备] 开始重置设备: {_username}");
                System.Diagnostics.Debug.WriteLine($"🔄 [解绑设备] 当前解绑次数: {_resetDeviceCount}");
                System.Diagnostics.Debug.WriteLine($"🔄 [解绑设备] 请求URL: {API_BASE_URL}/api/user/reset-devices");
                #endif

                // 获取当前设备的硬件ID
                var hardwareId = GetHardwareId();
                
                var requestData = new
                {
                    username = _username,
                    password = password,
                    hardware_id = hardwareId  // 只能解绑当前设备
                };

                var jsonContent = JsonSerializer.Serialize(requestData);
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"🔄 [解绑设备] 请求数据: username={_username}, password=***, hardware_id={hardwareId.Substring(0, 16)}...");
                #endif
                
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"🔄 [解绑设备] 正在发送HTTP POST请求...");
                #endif
                
                var response = await _httpClient.PostAsync(API_BASE_URL + "/api/user/reset-devices", content);
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"🔄 [解绑设备] HTTP状态码: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"🔄 [解绑设备] 响应头: {response.Headers}");
                #endif
                
                var responseContent = await response.Content.ReadAsStringAsync();

                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"🔄 [解绑设备] 服务器响应内容: {responseContent}");
                System.Diagnostics.Debug.WriteLine($"🔄 [解绑设备] 响应长度: {responseContent.Length} 字节");
                #endif

                var resetResponse = JsonSerializer.Deserialize<ResetDeviceResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (resetResponse == null)
                {
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"❌ [解绑设备] JSON 反序列化失败，返回 null");
                    #endif
                    return (false, "服务器响应解析失败", 0);
                }

                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"🔄 [解绑设备] 解析结果:");
                System.Diagnostics.Debug.WriteLine($"   Success: {resetResponse.Success}");
                System.Diagnostics.Debug.WriteLine($"   Message: {resetResponse.Message}");
                System.Diagnostics.Debug.WriteLine($"   ResetCount: {resetResponse.ResetCount}");
                System.Diagnostics.Debug.WriteLine($"   ResetRemaining: {resetResponse.ResetRemaining}");
                #endif

                if (!resetResponse.Success)
                {
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"❌ [解绑设备] 服务器返回失败: {resetResponse.Message}");
                    #endif
                    return (false, resetResponse.Message, resetResponse.ResetCount);
                }

                // 更新本地重置次数
                _resetDeviceCount = resetResponse.ResetRemaining;

                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"✅ [解绑设备] 设备重置成功，剩余{_resetDeviceCount}次");
                System.Diagnostics.Debug.WriteLine($"✅ [解绑设备] 本地_resetDeviceCount已更新为: {_resetDeviceCount}");
                #endif

                return (true, resetResponse.Message, _resetDeviceCount);
            }
            catch (HttpRequestException ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [解绑设备] HTTP请求异常: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ [解绑设备] 异常堆栈: {ex.StackTrace}");
                #endif
                return (false, $"网络请求失败: {ex.Message}", 0);
            }
            catch (JsonException ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [解绑设备] JSON解析异常: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ [解绑设备] 异常堆栈: {ex.StackTrace}");
                #endif
                return (false, $"响应解析失败: {ex.Message}", 0);
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [解绑设备] 未知异常: {ex.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"❌ [解绑设备] 异常消息: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ [解绑设备] 异常堆栈: {ex.StackTrace}");
                #endif
                return (false, $"重置失败: {ex.Message}", 0);
            }
        }

        /// <summary>
        /// 重置设备响应
        /// </summary>
        private class ResetDeviceResponse
        {
            [JsonPropertyName("success")]
            public bool Success { get; set; }
            
            [JsonPropertyName("message")]
            public string Message { get; set; }
            
            [JsonPropertyName("reset_count")]
            public int ResetCount { get; set; }
            
            [JsonPropertyName("reset_remaining")]
            public int ResetRemaining { get; set; }
        }

        #region 登录状态持久化

        /// <summary>
        /// 保存登录状态到本地文件（带签名防篡改）
        /// </summary>
        private async Task SaveAuthDataAsync()
        {
            try
            {
                // 🔒 生成随机nonce（防止文件复制后重放）
                var nonce = Guid.NewGuid().ToString("N");
                var saveTime = DateTime.Now.Ticks;
                
                // 🔒 生成文件版本号（单调递增，防止旧文件回滚）
                var maxVersion = GetMaxFileVersionFromRegistry();
                var newVersion = Math.Max(saveTime, maxVersion + 1);
                _currentFileVersion = newVersion;
                
                var authData = new
                {
                    username = _username,
                    token = _token,
                    expires_at = _expiresAt?.ToString("O"),
                    remaining_days = _remainingDays,
                    last_server_time = _lastServerTime?.ToString("O"),
                    last_local_time = _lastLocalTime?.ToString("O"),
                    last_tick_count = _lastTickCount,
                    reset_device_count = _resetDeviceCount,
                    last_successful_heartbeat = _lastSuccessfulHeartbeat?.ToString("O"),  // 🔒 保存心跳时间
                    nonce = nonce,  // 🔒 随机数（每次保存都不同）
                    save_time = saveTime,  // 🔒 保存时间戳
                    file_version = newVersion,  // 🔒 文件版本号（防止回滚攻击）
                    device_info = _deviceInfo != null ? new
                    {
                        bound_devices = _deviceInfo.BoundDevices,
                        max_devices = _deviceInfo.MaxDevices,
                        remaining_slots = _deviceInfo.RemainingSlots,
                        is_new_device = _deviceInfo.IsNewDevice
                    } : null
                };

                var json = JsonSerializer.Serialize(authData);
                
                // 生成HMAC签名（使用硬件ID作为密钥）
                var signature = GenerateSignature(json);
                
                // 组合数据和签名
                var signedData = new
                {
                    data = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)),
                    signature = signature
                };
                
                var signedJson = JsonSerializer.Serialize(signedData);
                var encrypted = Convert.ToBase64String(Encoding.UTF8.GetBytes(signedJson));

                // 确保目录存在
                var directory = System.IO.Path.GetDirectoryName(AUTH_DATA_FILE);
                if (!System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"💾 [AuthService] 已创建目录: {directory}");
                    #endif
                }

                // 写入文件
                await System.IO.File.WriteAllTextAsync(AUTH_DATA_FILE, encrypted);
                
                // 🔒 保存文件版本号到注册表（防止回滚攻击）
                SaveMaxFileVersionToRegistry(newVersion);

                // 调试信息已注释
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"💾 [AuthService] 登录状态已保存到: {AUTH_DATA_FILE}");
                //System.Diagnostics.Debug.WriteLine($"💾 [AuthService] 文件大小: {encrypted.Length} 字节");
                //System.Diagnostics.Debug.WriteLine($"🔐 [AuthService] 数据已签名保护");
                //System.Diagnostics.Debug.WriteLine($"🔒 [AuthService] 文件版本: {newVersion}");
                //System.Diagnostics.Debug.WriteLine($"💾 [AuthService] 保存的解绑次数: {_resetDeviceCount}");
                //if (_deviceInfo != null)
                //{
                //    System.Diagnostics.Debug.WriteLine($"💾 [AuthService] 保存的设备信息: 已绑定{_deviceInfo.BoundDevices}/{_deviceInfo.MaxDevices}, 剩余{_deviceInfo.RemainingSlots}");
                //}
                //#endif
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 保存登录状态失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 目标路径: {AUTH_DATA_FILE}");
                System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 异常详情: {ex}");
                #else
                _ = ex; // 避免未使用变量警告
                #endif
            }
        }

        /// <summary>
        /// 从本地文件加载登录状态
        /// </summary>
        private async Task TryLoadAuthDataAsync()
        {
            try
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"💾 [AuthService] 尝试加载登录状态: {AUTH_DATA_FILE}");
                #endif

                if (!System.IO.File.Exists(AUTH_DATA_FILE))
                {
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"💾 [AuthService] 无本地登录状态文件");
                    #endif
                    return;
                }

                // 读取文件
                var encrypted = await System.IO.File.ReadAllTextAsync(AUTH_DATA_FILE);
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"💾 [AuthService] 已读取文件，大小: {encrypted.Length} 字节");
                #endif
                
                // 第一层解密
                var bytes = Convert.FromBase64String(encrypted);
                var signedJson = Encoding.UTF8.GetString(bytes);
                
                // 解析带签名的数据
                var signedData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(signedJson);
                
                if (signedData == null || !signedData.ContainsKey("data") || !signedData.ContainsKey("signature"))
                {
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 数据格式错误，缺少签名");
                    #endif
                    DeleteAuthData();
                    return;
                }
                
                // 提取数据和签名
                var dataBase64 = signedData["data"].GetString();
                var storedSignature = signedData["signature"].GetString();
                var dataBytes = Convert.FromBase64String(dataBase64);
                var json = Encoding.UTF8.GetString(dataBytes);
                
                // 验证签名
                var expectedSignature = GenerateSignature(json);
                if (storedSignature != expectedSignature)
                {
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 签名验证失败，数据可能被篡改");
                    System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 存储签名: {storedSignature}");
                    System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 期望签名: {expectedSignature}");
                    #endif
                    DeleteAuthData();
                    return;
                }
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"🔐 [AuthService] 签名验证成功");
                #endif

                // 解析数据
                var authData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

                if (authData != null)
                {
                    _username = authData["username"].GetString();
                    _token = authData["token"].GetString();
                    
                    if (authData.TryGetValue("expires_at", out var expiresAt) && !string.IsNullOrEmpty(expiresAt.GetString()))
                    {
                        _expiresAt = DateTime.Parse(expiresAt.GetString());
                    }
                    
                    _remainingDays = authData["remaining_days"].GetInt32();
                    
                    if (authData.TryGetValue("last_server_time", out var lastServerTime) && !string.IsNullOrEmpty(lastServerTime.GetString()))
                    {
                        _lastServerTime = DateTime.Parse(lastServerTime.GetString());
                    }
                    
                    if (authData.TryGetValue("last_local_time", out var lastLocalTime) && !string.IsNullOrEmpty(lastLocalTime.GetString()))
                    {
                        _lastLocalTime = DateTime.Parse(lastLocalTime.GetString());
                    }
                    
                    if (authData.TryGetValue("last_tick_count", out var lastTickCount))
                    {
                        _lastTickCount = lastTickCount.GetInt64();
                    }
                    
                    // 恢复解绑次数
                    if (authData.TryGetValue("reset_device_count", out var resetDeviceCount))
                    {
                        _resetDeviceCount = resetDeviceCount.GetInt32();
                    }
                    
                    // 🔒 恢复心跳时间
                    if (authData.TryGetValue("last_successful_heartbeat", out var lastHeartbeat) && !string.IsNullOrEmpty(lastHeartbeat.GetString()))
                    {
                        _lastSuccessfulHeartbeat = DateTime.Parse(lastHeartbeat.GetString());
                    }
                    
                    // 🔒 检查文件版本号（防止旧文件回滚）
                    long fileVersion = 0;
                    if (authData.TryGetValue("file_version", out var fileVersionJson))
                    {
                        fileVersion = fileVersionJson.GetInt64();
                    }
                    
                    var maxVersion = GetMaxFileVersionFromRegistry();
                    
                    if (fileVersion > 0 && fileVersion < maxVersion)
                    {
                        // 文件版本号低于注册表记录，说明是旧文件回滚
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"🔒 [AuthService] 检测到文件回滚攻击！");
                        System.Diagnostics.Debug.WriteLine($"🔒 [AuthService] 文件版本: {fileVersion}");
                        System.Diagnostics.Debug.WriteLine($"🔒 [AuthService] 最大版本: {maxVersion}");
                        #endif
                        
                        DeleteAuthData();
                        
                        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            System.Windows.MessageBox.Show(
                                "检测到凭证文件异常，请重新登录。",
                                "安全警告",
                                System.Windows.MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Warning);
                        });
                        return;
                    }
                    
                    // 更新当前版本号
                    if (fileVersion > maxVersion)
                    {
                        SaveMaxFileVersionToRegistry(fileVersion);
                    }
                    
                    // 恢复设备信息
                    if (authData.TryGetValue("device_info", out var deviceInfoJson) && deviceInfoJson.ValueKind != JsonValueKind.Null)
                    {
                        _deviceInfo = new DeviceInfo
                        {
                            BoundDevices = deviceInfoJson.GetProperty("bound_devices").GetInt32(),
                            MaxDevices = deviceInfoJson.GetProperty("max_devices").GetInt32(),
                            RemainingSlots = deviceInfoJson.GetProperty("remaining_slots").GetInt32(),
                            IsNewDevice = deviceInfoJson.GetProperty("is_new_device").GetBoolean()
                        };
                    }

                    // 调试信息已注释
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"💾 [AuthService] 已解析登录数据: {_username}");
                    //System.Diagnostics.Debug.WriteLine($"💾 [AuthService] 过期时间: {_expiresAt}");
                    //System.Diagnostics.Debug.WriteLine($"💾 [AuthService] 剩余天数: {_remainingDays}");
                    //System.Diagnostics.Debug.WriteLine($"💾 [AuthService] TickCount: {_lastTickCount}");
                    //System.Diagnostics.Debug.WriteLine($"💾 [AuthService] 解绑次数: {_resetDeviceCount}");
                    //System.Diagnostics.Debug.WriteLine($"💾 [AuthService] 最后心跳: {_lastSuccessfulHeartbeat}");
                    //if (_deviceInfo != null)
                    //{
                    //    System.Diagnostics.Debug.WriteLine($"💾 [AuthService] 设备信息: 已绑定{_deviceInfo.BoundDevices}/{_deviceInfo.MaxDevices}, 剩余{_deviceInfo.RemainingSlots}");
                    //}
                    //#endif

                    // 🔒 检查离线时长（启动时检测）
                    if (_lastSuccessfulHeartbeat != null)
                    {
                        var offlineDays = (DateTime.Now - _lastSuccessfulHeartbeat.Value).TotalDays;
                        
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"🔒 [AuthService] 启动时离线时长检测: {offlineDays:F1} 天");
                        #endif
                        
                        if (offlineDays > MAX_OFFLINE_DAYS)
                        {
                            #if DEBUG
                            System.Diagnostics.Debug.WriteLine($"🔒 [AuthService] 离线时间超过 {MAX_OFFLINE_DAYS} 天，清除登录状态");
                            #endif
                            
                            DeleteAuthData();
                            
                            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                            {
                                System.Windows.MessageBox.Show(
                                    $"账号已离线超过 {MAX_OFFLINE_DAYS} 天，请重新联网登录验证。",
                                    "离线时间过长",
                                    System.Windows.MessageBoxButton.OK,
                                    System.Windows.MessageBoxImage.Warning);
                            });
                            return;
                        }
                    }

                    // 先设置为已认证状态，再检查是否过期
                    _isAuthenticated = true;
                    
                    // 🔒 生成验证令牌（自动登录）
                    GenerateAuthTokens();
                    
                    // 检查账号是否仍然有效
                    if (CanUseProjection())
                    {
                        // 启动心跳
                        StartHeartbeat();
                        
                        // 触发事件（自动登录，不弹窗）
                        AuthenticationChanged?.Invoke(this, new AuthenticationChangedEventArgs 
                        { 
                            IsAuthenticated = true, 
                            IsAutoLogin = true 
                        });

                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"💾 [AuthService] 自动登录成功: {_username}, 剩余{_remainingDays}天");
                        #endif
                    }
                    else
                    {
                        // 账号已过期，清除认证状态并删除本地文件
                        _isAuthenticated = false;
                        _username = null;
                        _token = null;
                        DeleteAuthData();
                        
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"💾 [AuthService] 本地登录已过期");
                        #endif
                    }
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 加载登录状态失败: {ex.Message}");
                #else
                _ = ex; // 避免未使用变量警告
                #endif
                
                // 加载失败，删除损坏的文件
                DeleteAuthData();
            }
        }

        /// <summary>
        /// 删除本地保存的登录状态
        /// </summary>
        private void DeleteAuthData()
        {
            try
            {
                if (System.IO.File.Exists(AUTH_DATA_FILE))
                {
                    System.IO.File.Delete(AUTH_DATA_FILE);
                    
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"💾 [AuthService] 本地登录状态已删除");
                    #endif
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 删除登录状态失败: {ex.Message}");
                #else
                _ = ex; // 避免未使用变量警告
                #endif
            }
        }

        /// <summary>
        /// 生成数据签名（使用HMAC-SHA256 + 硬件ID + 文件路径作为密钥）
        /// 防止数据被篡改和复制
        /// </summary>
        private string GenerateSignature(string data)
        {
            try
            {
                // 使用硬件ID作为密钥
                var hardwareId = GetHardwareId();
                
                // 🔒 获取凭证文件的绝对路径（防止复制到其他位置）
                var filePath = System.IO.Path.GetFullPath(AUTH_DATA_FILE);
                
                // 🔒 组合多重密钥：硬件ID + 文件路径 + 固定盐值
                // 这样即使在同一台机器上复制到不同位置也会失败
                var key = $"{hardwareId}_{filePath}_CANVAS_CAST_SECRET_2024";
                var keyBytes = Encoding.UTF8.GetBytes(key);
                var dataBytes = Encoding.UTF8.GetBytes(data);
                
                // 使用HMAC-SHA256生成签名
                using (var hmac = new System.Security.Cryptography.HMACSHA256(keyBytes))
                {
                    var hashBytes = hmac.ComputeHash(dataBytes);
                    return Convert.ToBase64String(hashBytes);
                }
            }
            catch
            {
                // 降级方案：使用简单的哈希
                var filePath = System.IO.Path.GetFullPath(AUTH_DATA_FILE);
                using (var sha256 = SHA256.Create())
                {
                    var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(data + GetHardwareId() + filePath));
                    return Convert.ToBase64String(hashBytes);
                }
            }
        }

        #endregion

        #region 试用投影验证（防止破解随机时间限制）

        // 🔒 试用投影配置常量（分散定义,增加破解难度）
        private const int TRIAL_MIN_SECONDS = 30;
        private const int TRIAL_MAX_SECONDS = 60;

        /// <summary>
        /// 开始试用投影（未登录状态）
        /// 每次点击都生成新的随机时长和加密令牌
        /// </summary>
        public void StartTrialProjection()
        {
            // 已登录用户不需要试用
            if (_isAuthenticated)
            {
                _trialProjectionStartTick = 0;
                _trialDurationSeconds = 0;
                _trialProjectionToken = null;
                return;
            }

            // 🔒 每次都生成新的随机试用时长
            // 不使用固定种子,确保每次点击都是真随机
            var random = new Random();
            var randomDuration = random.Next(TRIAL_MIN_SECONDS, TRIAL_MAX_SECONDS + 1);
            
            // 🔒 强制限制上限（防止内存修改）
            _trialDurationSeconds = Math.Min(randomDuration, TRIAL_MAX_SECONDS);

            // 记录开始时刻
            _trialProjectionStartTick = Environment.TickCount64;

            // 生成加密令牌（防止篡改）
            _trialProjectionToken = GenerateTrialProjectionToken();

            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"🔒 [试用投影] 已启动，时长: {_trialDurationSeconds}秒");
            #endif
        }

        /// <summary>
        /// 检查试用投影是否已过期
        /// 🔒 返回验证码而非简单bool，防止直接跳过逻辑判断
        /// </summary>
        public bool IsTrialProjectionExpired()
        {
            return GetTrialProjectionStatus() != 0x1A2B3C4D;
        }
        
        /// <summary>
        /// 🔒 获取试用投影状态验证码（内部方法，增加破解难度）
        /// 返回: 0x1A2B3C4D = 有效, 其他值 = 已过期
        /// </summary>
        private int GetTrialProjectionStatus()
        {
            // 已登录用户无限制
            if (_isAuthenticated)
            {
                return 0x1A2B3C4D; // 魔数：有效 (439041101)
            }

            // 未启动试用投影
            if (_trialProjectionStartTick == 0)
            {
                return 0x1A2B3C4D; // 魔数：有效
            }

            // 验证令牌完整性
            if (!ValidateTrialProjectionToken())
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [试用投影] 令牌验证失败，可能被篡改");
                #endif
                return unchecked((int)0xDEADBEEF); // 魔数：令牌无效
            }

            // 🔒 强制限制最大试用时长（防止内存修改）
            int effectiveDuration = Math.Min(_trialDurationSeconds, TRIAL_MAX_SECONDS);

            // 计算已流逝时间
            var elapsedMs = Environment.TickCount64 - _trialProjectionStartTick;
            var elapsedSeconds = elapsedMs / 1000;

            // 🔒 隐藏验证：多重检查
            if (elapsedSeconds >= effectiveDuration)
            {
                return unchecked((int)0xBADC0DE0); // 魔数：已过期
            }
            
            // 🔒 额外验证：检查是否被异常重置
            if (_trialProjectionStartTick > Environment.TickCount64)
            {
                return unchecked((int)0xBADC0DE1); // 魔数：时间异常
            }

            return 0x1A2B3C4D; // 魔数：有效
        }

        /// <summary>
        /// 获取试用投影剩余时间（秒）
        /// 🔒 内部也进行验证，防止破解者只修改 IsTrialProjectionExpired()
        /// </summary>
        public int GetTrialProjectionRemainingSeconds()
        {
            if (_isAuthenticated || _trialProjectionStartTick == 0)
            {
                return -1; // 无限制
            }

            // 🔒 隐藏验证：检查状态码
            if (GetTrialProjectionStatus() != 0x1A2B3C4D)
            {
                return 0; // 已过期或异常，返回0
            }

            var elapsedMs = Environment.TickCount64 - _trialProjectionStartTick;
            var elapsedSeconds = (int)(elapsedMs / 1000);
            
            // 🔒 强制限制最大时长
            int effectiveDuration = Math.Min(_trialDurationSeconds, TRIAL_MAX_SECONDS);
            var remaining = effectiveDuration - elapsedSeconds;

            return Math.Max(0, remaining);
        }

        /// <summary>
        /// 重置试用投影状态
        /// </summary>
        public void ResetTrialProjection()
        {
            _trialProjectionStartTick = 0;
            _trialDurationSeconds = 0;
            _trialProjectionToken = null;

            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"🔒 [试用投影] 已重置");
            #endif
        }

        /// <summary>
        /// 生成试用投影令牌（SHA256加密 + 动态密钥）
        /// 关键：令牌中包含时长的哈希，修改时长会导致令牌失效
        /// </summary>
        private string GenerateTrialProjectionToken()
        {
            try
            {
                // 🔒 动态密钥（混淆在代码中，增加破解难度）
                const string SECRET_SALT_1 = "CanvasCast_Trial_Projection_Key_2024";
                const string SECRET_SALT_2 = "AntiCrack_Protection_Layer_SHA256";
                
                // 🔒 关键：计算时长的限制值（硬编码在令牌生成中）
                // 即使破解者修改 _trialDurationSeconds，令牌验证也会失败
                int validDuration = Math.Min(_trialDurationSeconds, TRIAL_MAX_SECONDS);
                
                // 组合多个参数生成令牌（增加破解难度）
                var data = $"{SECRET_SALT_1}:{_trialProjectionStartTick}:{validDuration}:{Environment.MachineName}:{Environment.UserName}:{SECRET_SALT_2}";
                
                using (var sha256 = SHA256.Create())
                {
                    var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
                    return Convert.ToBase64String(hashBytes);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 验证试用投影令牌
        /// </summary>
        private bool ValidateTrialProjectionToken()
        {
            if (string.IsNullOrEmpty(_trialProjectionToken))
            {
                return false;
            }

            var expectedToken = GenerateTrialProjectionToken();
            return _trialProjectionToken == expectedToken;
        }

        #endregion
    }
}


