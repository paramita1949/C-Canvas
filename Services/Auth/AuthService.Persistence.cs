using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ImageColorChanger.Services
{
    public partial class AuthService
    {
        #region 登录状态持久化

        /// <summary>
        /// 保存登录状态到本地文件（带签名防篡改）
        /// </summary>
        private async Task SaveAuthDataAsync()
        {
            try
            {
                var nonce = Guid.NewGuid().ToString("N");
                var saveTime = DateTime.Now.Ticks;

                var maxVersion = GetMaxFileVersionFromRegistry();
                var newVersion = Math.Max(saveTime, maxVersion + 1);
                _currentFileVersion = newVersion;

                var snapshot = new Auth.AuthStateSnapshot
                {
                    Username = _username,
                    Token = _token,
                    ExpiresAt = _expiresAt?.ToString("O"),
                    RemainingDays = _remainingDays,
                    LastServerTime = _lastServerTime?.ToString("O"),
                    LastLocalTime = _lastLocalTime?.ToString("O"),
                    LastTickCount = _lastTickCount,
                    ResetDeviceCount = _resetDeviceCount,
                    LastSuccessfulHeartbeat = _lastSuccessfulHeartbeat?.ToString("O"),
                    Nonce = nonce,
                    SaveTime = saveTime,
                    FileVersion = newVersion,
                    ShownClientNoticeKeys = _shownClientNoticeKeys.ToList(),
                    DeviceInfo = _deviceInfo == null
                        ? null
                        : new Auth.DeviceInfoSnapshot
                        {
                            BoundDevices = _deviceInfo.BoundDevices,
                            MaxDevices = _deviceInfo.MaxDevices,
                            RemainingSlots = _deviceInfo.RemainingSlots,
                            IsNewDevice = _deviceInfo.IsNewDevice
                        }
                };

                await _authStateStore.SaveSnapshotAsync(AUTH_DATA_FILE, snapshot, GenerateSignature);

                SaveMaxFileVersionToRegistry(newVersion);
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 保存登录状态失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 目标路径: {AUTH_DATA_FILE}");
                System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 异常详情: {ex}");
#else
                _ = ex;
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

                var loadResult = await _authStateStore.LoadSnapshotAsync(AUTH_DATA_FILE, GenerateSignature);
                if (!loadResult.IsValid || loadResult.Snapshot == null)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 本地凭证无效: {loadResult.Error}");
#endif
                    DeleteAuthData();
                    return;
                }

                var authData = loadResult.Snapshot;
                ApplyPersistedSnapshot(authData);
                RebuildTokensForLoadedState();

                if (!ValidateAndTrackLoadedFileVersion(authData.FileVersion))
                {
                    return;
                }

                if (!ValidateStartupOfflineWindow())
                {
                    return;
                }

                _isAuthenticated = true;
                GenerateAuthTokens();

                if (CanUseProjection())
                {
                    CompleteAutoLoginSuccess();
                }
                else
                {
                    HandleAutoLoginExpired();
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 加载登录状态失败: {ex.Message}");
#else
                _ = ex;
#endif
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
                _authStateStore.DeleteSnapshot(AUTH_DATA_FILE);
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"💾 [AuthService] 本地登录状态已删除");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"⚠️ [AuthService] 删除登录状态失败: {ex.Message}");
#else
                _ = ex;
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
                var hardwareId = _authDeviceFingerprint.GetHardwareId();
                var filePath = System.IO.Path.GetFullPath(AUTH_DATA_FILE);
                var key = $"{hardwareId}_{filePath}_CANVAS_CAST_SECRET_2024";
                var keyBytes = Encoding.UTF8.GetBytes(key);
                var dataBytes = Encoding.UTF8.GetBytes(data);

                using (var hmac = new HMACSHA256(keyBytes))
                {
                    var hashBytes = hmac.ComputeHash(dataBytes);
                    return Convert.ToBase64String(hashBytes);
                }
            }
            catch
            {
                var filePath = System.IO.Path.GetFullPath(AUTH_DATA_FILE);
                using (var sha256 = SHA256.Create())
                {
                    var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(data + _authDeviceFingerprint.GetHardwareId() + filePath));
                    return Convert.ToBase64String(hashBytes);
                }
            }
        }

        #endregion
    }
}
