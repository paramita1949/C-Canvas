using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ImageColorChanger.Services
{
    public partial class AuthService
    {
        /// <summary>
        /// 登录验证
        /// </summary>
        public async Task<(bool success, string message)> LoginAsync(string username, string password)
        {
            try
            {
                _lastAuthFailureReason = null;
                _lastPaymentInfo = null;

#if DEBUG
                System.Diagnostics.Debug.WriteLine($"🔐 [AuthService] 开始登录验证: {username}");
#endif

                var hardwareId = _authDeviceFingerprint.GetHardwareId();
                var envPayload = BuildClientEnvironmentPayload();
                var requestData = new
                {
                    username = username,
                    password = password,
                    hardware_id = hardwareId,
                    device_name = envPayload.DeviceName,
                    os_version = envPayload.OsVersion,
                    app_version = envPayload.AppVersion
                };

                var jsonContent = JsonSerializer.Serialize(requestData);
                var response = await PostJsonWithFailoverAsync(VERIFY_ENDPOINT, jsonContent, timeoutSeconds: 20);

                if (response == null)
                {
                    return (false, "网络连接失败，所有验证服务器均无法访问");
                }

                var responseContent = await response.Content.ReadAsStringAsync();

#if DEBUG
                System.Diagnostics.Debug.WriteLine($"🔐 [AuthService] 服务器响应: {responseContent}");
#endif

                var authResponse = DeserializeAuthResponse(responseContent);
                if (authResponse == null)
                {
                    return (false, "服务器响应解析失败");
                }

                if (!authResponse.Success)
                {
                    return BuildLoginFailure(authResponse, "验证失败");
                }

                if (!authResponse.Valid)
                {
                    return BuildLoginFailure(authResponse, "账号无效");
                }

                ApplyLoginSuccess(username, authResponse);
                return (true, $"登录成功！账号有效期剩余 {_remainingDays} 天");
            }
            catch (HttpRequestException ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [AuthService] 网络请求失败: {ex.Message}");
#else
                _ = ex;
#endif
                return BuildHttpRequestFailureResult(ex, "网络连接失败，请检查网络设置");
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
        /// 发送邮箱验证码（用于密码重置）
        /// </summary>
        public async Task<(bool success, string message)> SendVerificationCodeAsync(string username, string email)
        {
            try
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"📧 [AuthService] 发送验证码: {username}, {email}");
#endif

                var requestData = new { username = username, email = email };
                var jsonContent = JsonSerializer.Serialize(requestData);
                var response = await PostJsonWithFailoverAsync(
                    "/api/user/send-verification-code",
                    jsonContent,
                    timeoutSeconds: 20,
                    allowFailoverOnFailure: false);

                if (response == null)
                {
                    return (false, "网络连接失败，所有验证服务器均无法访问");
                }

                var responseContent = await response.Content.ReadAsStringAsync();

#if DEBUG
                System.Diagnostics.Debug.WriteLine($"📧 [AuthService] 服务器响应: {responseContent}");
#endif

                var result = DeserializeAuthResponse(responseContent);
                if (result == null)
                {
                    return (false, "服务器响应解析失败");
                }

                if (!result.Success)
                {
                    return (false, result.Message ?? "发送失败");
                }

#if DEBUG
                System.Diagnostics.Debug.WriteLine($"✅ [AuthService] 验证码发送成功");
#endif
                return (true, result.Message ?? "验证码已发送");
            }
            catch (HttpRequestException ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [AuthService] 网络请求失败: {ex.Message}");
#else
                _ = ex;
#endif
                return BuildHttpRequestFailureResult(ex, "网络连接失败，请检查网络设置");
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [AuthService] 发送验证码异常: {ex.Message}");
#endif
                return (false, $"发送失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 重置密码（通过邮箱验证码）
        /// </summary>
        public async Task<(bool success, string message)> ResetPasswordAsync(string email, string code, string newPassword)
        {
            try
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"🔑 [AuthService] 重置密码: {email}");
#endif

                var requestData = new { email = email, code = code, new_password = newPassword };
                var jsonContent = JsonSerializer.Serialize(requestData);
                var response = await PostJsonWithFailoverAsync(
                    "/api/user/reset-password",
                    jsonContent,
                    timeoutSeconds: 20,
                    allowFailoverOnFailure: false);

                if (response == null)
                {
                    return (false, "网络连接失败，所有验证服务器均无法访问");
                }

                var responseContent = await response.Content.ReadAsStringAsync();

#if DEBUG
                System.Diagnostics.Debug.WriteLine($"🔑 [AuthService] 服务器响应: {responseContent}");
#endif

                var result = DeserializeAuthResponse(responseContent);
                if (result == null)
                {
                    return (false, "服务器响应解析失败");
                }

                if (!result.Success)
                {
                    return (false, result.Message ?? "重置失败");
                }

#if DEBUG
                System.Diagnostics.Debug.WriteLine($"✅ [AuthService] 密码重置成功");
#endif
                return (true, result.Message ?? "密码重置成功");
            }
            catch (HttpRequestException ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [AuthService] 网络请求失败: {ex.Message}");
#else
                _ = ex;
#endif
                return BuildHttpRequestFailureResult(ex, "网络连接失败，请检查网络设置");
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [AuthService] 重置密码异常: {ex.Message}");
#endif
                return (false, $"重置失败: {ex.Message}");
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

                var fp = _authDeviceFingerprint.GetComponents();
                var requestData = new
                {
                    username = username,
                    password = password,
                    email = email,
                    cpu_id = fp.CpuId,
                    motherboard_serial = fp.BoardSerial,
                    disk_serial = fp.DiskSerial,
                    bios_uuid = fp.BiosUuid,
                    windows_install_id = fp.WindowsInstallId,
                    device_name = Environment.MachineName
                };

                var jsonContent = JsonSerializer.Serialize(requestData);
                var response = await PostJsonWithFailoverAsync(
                    "/api/user/register",
                    jsonContent,
                    timeoutSeconds: 20,
                    allowFailoverOnFailure: false);

                if (response == null)
                {
                    return (false, "网络连接失败，所有验证服务器均无法访问");
                }

                var responseContent = await response.Content.ReadAsStringAsync();

#if DEBUG
                System.Diagnostics.Debug.WriteLine($"📝 [AuthService] 服务器响应: {responseContent}");
#endif

                var registerResponse = DeserializeAuthResponse(responseContent);
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
                _ = ex;
#endif
                return BuildHttpRequestFailureResult(ex, "网络连接失败，请检查网络设置", exposeHttpPrefixMessage: false);
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
            ClearAuthenticatedIdentity();
            _expiresAt = null;
            _remainingDays = 0;
            _lastServerTime = null;
            _lastLocalTime = null;
            _lastHolidayBonusKey = null;

            StopHeartbeat();
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
    }
}
