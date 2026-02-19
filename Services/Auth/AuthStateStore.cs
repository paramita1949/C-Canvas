using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ImageColorChanger.Services.Auth
{
    internal sealed class AuthStateStore
    {
        internal sealed class LoadResult
        {
            public bool Exists { get; init; }
            public bool IsValid { get; init; }
            public string Error { get; init; }
            public AuthStateSnapshot Snapshot { get; init; }
        }

        private sealed class SignedPayload
        {
            public string Data { get; set; }
            public string Signature { get; set; }
        }

        private const string ProtectedPrefix = "v2:";
        private static readonly byte[] ProtectedEntropy = Encoding.UTF8.GetBytes("CanvasCast.AuthStateStore.v2");
        private const int MaxQuarantineFiles = 20;

        public async Task SaveSnapshotAsync(string authFilePath, AuthStateSnapshot snapshot, Func<string, string> signFunc)
        {
            string json = JsonSerializer.Serialize(snapshot);
            var payload = new SignedPayload
            {
                Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)),
                Signature = signFunc(json)
            };

            string signedJson = JsonSerializer.Serialize(payload);
            string protectedPayload = ProtectPayload(signedJson);

            string directory = Path.GetDirectoryName(authFilePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException($"无效的认证文件路径: {authFilePath}");
            }

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 原子写入：先写临时文件，再覆盖主文件，避免中途崩溃造成半文件。
            string tempPath = Path.Combine(directory, $"{Path.GetFileName(authFilePath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllTextAsync(tempPath, protectedPayload, Encoding.UTF8);
                File.Move(tempPath, authFilePath, true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        public async Task<LoadResult> LoadSnapshotAsync(string authFilePath, Func<string, string> signFunc)
        {
            if (!File.Exists(authFilePath))
            {
                return new LoadResult { Exists = false, IsValid = false, Error = "missing_file" };
            }

            try
            {
                string raw = await File.ReadAllTextAsync(authFilePath, Encoding.UTF8);
                string signedJson = UnprotectPayload(raw);
                var payload = JsonSerializer.Deserialize<SignedPayload>(signedJson);

                if (payload == null || string.IsNullOrEmpty(payload.Data) || string.IsNullOrEmpty(payload.Signature))
                {
                    return new LoadResult { Exists = true, IsValid = false, Error = "invalid_signed_payload" };
                }

                var dataBytes = Convert.FromBase64String(payload.Data);
                var json = Encoding.UTF8.GetString(dataBytes);

                string expected = signFunc(json);
                if (!string.Equals(payload.Signature, expected, StringComparison.Ordinal))
                {
                    return new LoadResult { Exists = true, IsValid = false, Error = "signature_mismatch" };
                }

                var snapshot = JsonSerializer.Deserialize<AuthStateSnapshot>(json);
                if (snapshot == null)
                {
                    return new LoadResult { Exists = true, IsValid = false, Error = "invalid_snapshot" };
                }

                return new LoadResult
                {
                    Exists = true,
                    IsValid = true,
                    Snapshot = snapshot
                };
            }
            catch (Exception ex)
            {
                return new LoadResult
                {
                    Exists = true,
                    IsValid = false,
                    Error = ex.Message
                };
            }
        }

        public string QuarantineSnapshot(string authFilePath)
        {
            if (!File.Exists(authFilePath))
            {
                return null;
            }

            string directory = Path.GetDirectoryName(authFilePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            string badDir = Path.Combine(directory, "bad");
            Directory.CreateDirectory(badDir);
            string badPath = Path.Combine(
                badDir,
                $"{Path.GetFileName(authFilePath)}.{DateTime.Now:yyyyMMdd_HHmmss_fff}.bad");

            File.Move(authFilePath, badPath, true);
            CleanupOldQuarantineFiles(badDir, Path.GetFileName(authFilePath));
            return badPath;
        }

        public void DeleteSnapshot(string authFilePath)
        {
            if (File.Exists(authFilePath))
            {
                File.Delete(authFilePath);
            }
        }

        private static string ProtectPayload(string signedJson)
        {
            var bytes = Encoding.UTF8.GetBytes(signedJson);
            var protectedBytes = ProtectedData.Protect(bytes, ProtectedEntropy, DataProtectionScope.CurrentUser);
            return ProtectedPrefix + Convert.ToBase64String(protectedBytes);
        }

        private static string UnprotectPayload(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidDataException("empty_payload");
            }

            // 新格式（DPAPI）
            if (raw.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
            {
                string body = raw.Substring(ProtectedPrefix.Length);
                var protectedBytes = Convert.FromBase64String(body);
                var bytes = ProtectedData.Unprotect(protectedBytes, ProtectedEntropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }

            // 兼容旧格式（Base64明文包装）
            var legacyBytes = Convert.FromBase64String(raw);
            return Encoding.UTF8.GetString(legacyBytes);
        }

        private static void CleanupOldQuarantineFiles(string badDir, string authFileName)
        {
            try
            {
                var files = new DirectoryInfo(badDir)
                    .GetFiles($"{authFileName}.*.bad")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToList();

                for (int i = MaxQuarantineFiles; i < files.Count; i++)
                {
                    files[i].Delete();
                }
            }
            catch
            {
            }
        }
    }
}
