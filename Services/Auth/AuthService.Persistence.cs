using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ImageColorChanger.Services
{
    public partial class AuthService
    {
        #region 登录状态持久化
        private readonly System.Threading.SemaphoreSlim _authStorageWriteLock = new System.Threading.SemaphoreSlim(1, 1);
        private readonly object _authPersistScheduleLock = new object();
        private Task _authPersistWorkerTask;
        private bool _authPersistRequested;
        private static readonly TimeSpan AuthPersistCoalesceWindow = TimeSpan.FromMilliseconds(500);

        private void RequestPersistAuthData()
        {
            lock (_authPersistScheduleLock)
            {
                _authPersistRequested = true;
                if (_authPersistWorkerTask == null || _authPersistWorkerTask.IsCompleted)
                {
                    _authPersistWorkerTask = Task.Run(PersistAuthDataWorkerAsync);
                }
            }
        }

        private async Task PersistAuthDataWorkerAsync()
        {
            while (true)
            {
                lock (_authPersistScheduleLock)
                {
                    if (!_authPersistRequested)
                    {
                        return;
                    }
                    _authPersistRequested = false;
                }

                await Task.Delay(AuthPersistCoalesceWindow).ConfigureAwait(false);
                await SaveAuthDataAsync().ConfigureAwait(false);
            }
        }

        private async Task FlushPersistQueueAsync()
        {
            while (true)
            {
                Task workerTask;
                lock (_authPersistScheduleLock)
                {
                    workerTask = _authPersistWorkerTask;
                }

                if (workerTask == null)
                {
                    return;
                }

                await workerTask.ConfigureAwait(false);

                lock (_authPersistScheduleLock)
                {
                    // worker已结束且没有新请求，刷新完成
                    if ((_authPersistWorkerTask == null || _authPersistWorkerTask.IsCompleted) && !_authPersistRequested)
                    {
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// 保存登录状态到本地文件（带签名防篡改）
        /// </summary>
        private async Task SaveAuthDataAsync()
        {
            bool lockTaken = false;
            try
            {
                await _authStorageWriteLock.WaitAsync().ConfigureAwait(false);
                lockTaken = true;

                // 仅在已登录且凭证完整时持久化。
                if (!_isAuthenticated || string.IsNullOrWhiteSpace(_username) || string.IsNullOrWhiteSpace(_token))
                {
                    return;
                }

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

                bool savedAny = false;
                Exception lastError = null;
                foreach (var authFilePath in GetAuthDataFilePaths())
                {
                    try
                    {
                        await _authStateStore.SaveSnapshotAsync(
                            authFilePath,
                            snapshot,
                            data => GenerateSignature(data, authFilePath));
                        savedAny = true;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
#if DEBUG
                        System.Diagnostics.Trace.WriteLine($"⚠️ [AuthService] 保存登录状态失败: {authFilePath} -> {ex.Message}");
#else
                        _ = ex;
#endif
                    }
                }

                if (!savedAny)
                {
                    throw lastError ?? new InvalidOperationException("所有凭证路径写入失败");
                }

                SaveMaxFileVersionToRegistry(newVersion);
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Trace.WriteLine($"⚠️ [AuthService] 保存登录状态失败: {ex.Message}");
                System.Diagnostics.Trace.WriteLine($"⚠️ [AuthService] 异常详情: {ex}");
#else
                _ = ex;
#endif
            }
            finally
            {
                if (lockTaken)
                {
                    _authStorageWriteLock.Release();
                }
            }
        }

        /// <summary>
        /// 从本地文件加载登录状态
        /// </summary>
        private async Task TryLoadAuthDataAsync()
        {
            try
            {
                var candidatePaths = GetAuthDataFilePaths()
                    .Where(File.Exists)
                    .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                    .ToList();

#if DEBUG
                System.Diagnostics.Trace.WriteLine("💾 [AuthService] 尝试加载登录状态（按时间倒序）:");
                foreach (var path in candidatePaths)
                {
                    System.Diagnostics.Trace.WriteLine($"   - {path}");
                }
#endif

                if (!candidatePaths.Any())
                {
#if DEBUG
                    System.Diagnostics.Trace.WriteLine($"💾 [AuthService] 无本地登录状态文件");
#endif
                    return;
                }

                var validSnapshots = new List<(string Path, Auth.AuthStateSnapshot Snapshot, DateTime LastWriteUtc)>();

                foreach (var authFilePath in candidatePaths)
                {
                    var loadResult = await _authStateStore.LoadSnapshotAsync(
                        authFilePath,
                        data => GenerateSignature(data, authFilePath));

                    // 兼容旧版本签名算法（5.9.9.8及更早版本）
                    if (!loadResult.IsValid &&
                        string.Equals(loadResult.Error, "signature_mismatch", StringComparison.Ordinal))
                    {
                        var legacyResult = await _authStateStore.LoadSnapshotAsync(
                            authFilePath,
                            data => GenerateSignatureLegacy(data, authFilePath));
                        if (legacyResult.IsValid && legacyResult.Snapshot != null)
                        {
                            loadResult = legacyResult;
                        }
                    }

                    if (!loadResult.IsValid || loadResult.Snapshot == null)
                    {
#if DEBUG
                        System.Diagnostics.Trace.WriteLine($"⚠️ [AuthService] 本地凭证无效: {authFilePath} ({loadResult.Error})");
#endif
                        _authStateStore.DeleteSnapshot(authFilePath);
#if DEBUG
                        System.Diagnostics.Trace.WriteLine($"🧹 [AuthService] 已删除损坏凭证: {authFilePath}");
#endif
                        continue;
                    }

                    validSnapshots.Add((authFilePath, loadResult.Snapshot, File.GetLastWriteTimeUtc(authFilePath)));
                }

                if (!validSnapshots.Any())
                {
                    // 所有候选文件均无效（均已隔离/删除），直接返回未登录状态
                    return;
                }

                // 关键修复：按 FileVersion 优先选择最新凭证，避免先命中旧文件触发“版本回滚误判”
                var selected = validSnapshots
                    .OrderByDescending(x => x.Snapshot.FileVersion)
                    .ThenByDescending(x => x.LastWriteUtc)
                    .First();

                var authData = selected.Snapshot;
#if DEBUG
                System.Diagnostics.Trace.WriteLine(
                    $"💾 [AuthService] 选择凭证: Path={selected.Path}, FileVersion={authData.FileVersion}, " +
                    $"Candidates={validSnapshots.Count}");
#endif
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

                // 成功加载后，异步回写到所有凭证路径，修复“某一路径找不到凭证”场景
                RequestPersistAuthData();
                return;
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Trace.WriteLine($"⚠️ [AuthService] 加载登录状态失败: {ex.Message}");
#else
                _ = ex;
#endif
                // 加载异常不主动删除凭证，避免瞬时IO问题导致用户被登出。
            }
        }

        /// <summary>
        /// 删除本地保存的登录状态
        /// </summary>
        private void DeleteAuthData()
        {
            bool lockTaken = false;
            try
            {
                _authStorageWriteLock.Wait();
                lockTaken = true;
                foreach (var authFilePath in GetAuthDataFilePaths())
                {
                    _authStateStore.DeleteSnapshot(authFilePath);
                }
#if DEBUG
                System.Diagnostics.Trace.WriteLine($"💾 [AuthService] 本地登录状态已删除");
#endif
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Trace.WriteLine($"⚠️ [AuthService] 删除登录状态失败: {ex.Message}");
#else
                _ = ex;
#endif
            }
            finally
            {
                if (lockTaken)
                {
                    _authStorageWriteLock.Release();
                }
            }
        }

        /// <summary>
        /// 生成数据签名（稳定算法，绑定 MachineGuid + 文件路径）
        /// 仅用于本地缓存防篡改，避免重启后硬件指纹抖动导致误判失效。
        /// </summary>
        private string GenerateSignature(string data, string authFilePath)
        {
            var machineKey = GetStableMachineSignatureKey();
            return GenerateSignatureCore(data, authFilePath, machineKey);
        }

        /// <summary>
        /// 旧版本签名算法（绑定完整硬件指纹），用于兼容读取历史凭证。
        /// </summary>
        private string GenerateSignatureLegacy(string data, string authFilePath)
        {
            var legacyMachineKey = _authDeviceFingerprint.GetHardwareId();
            return GenerateSignatureCore(data, authFilePath, legacyMachineKey);
        }

        private string GenerateSignatureCore(string data, string authFilePath, string machineKey)
        {
            try
            {
                var filePath = Path.GetFullPath(authFilePath);
                var key = $"{machineKey}_{filePath}_CANVAS_CAST_SECRET_2024";
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
                var filePath = Path.GetFullPath(authFilePath);
                using (var sha256 = SHA256.Create())
                {
                    var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(data + machineKey + filePath));
                    return Convert.ToBase64String(hashBytes);
                }
            }
        }

        private string GetStableMachineSignatureKey()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    var guid = key?.GetValue("MachineGuid")?.ToString();
                    if (!string.IsNullOrWhiteSpace(guid))
                    {
                        return guid;
                    }
                }
            }
            catch
            {
            }

            return Environment.MachineName ?? "unknown_machine";
        }

        private IEnumerable<string> GetAuthDataFilePaths()
        {
            string localPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AUTH_DATA_DIR_NAME,
                AUTH_DATA_FILE_NAME);

            return new[] { localPath }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        #endregion
    }
}

