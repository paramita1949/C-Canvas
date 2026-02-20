using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
            [JsonPropertyName("Data")]
            public string Data { get; set; }

            [JsonPropertyName("Signature")]
            public string Signature { get; set; }
        }

        private const string EncryptedPrefix = "v1:";

        public async Task SaveSnapshotAsync(string authFilePath, AuthStateSnapshot snapshot, Func<string, string> signFunc)
        {
            LogDebug($"[AuthStateStore.Save] Begin: Path={authFilePath}");
            string json = JsonSerializer.Serialize(snapshot);
            var payload = new SignedPayload
            {
                Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)),
                Signature = signFunc(json)
            };
            LogDebug(
                $"[AuthStateStore.Save] SnapshotSerialized: JsonLen={json.Length}, DataLen={payload.Data.Length}, SigLen={payload.Signature?.Length ?? 0}");

            string signedJson = JsonSerializer.Serialize(payload);
            string protectedPayload = ProtectPayload(signedJson);
            LogDebug(
                $"[AuthStateStore.Save] PayloadReady: SignedLen={signedJson.Length}, StoredLen={protectedPayload.Length}, Prefix={GetPrefixForLog(protectedPayload)}");

            string directory = Path.GetDirectoryName(authFilePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                LogDebug($"[AuthStateStore.Save] InvalidPath: {authFilePath}");
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
                var fileInfo = new FileInfo(authFilePath);
                LogDebug(
                    $"[AuthStateStore.Save] Success: Path={authFilePath}, Exists={File.Exists(authFilePath)}, Bytes={fileInfo.Length}");
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
                LogDebug($"[AuthStateStore.Load] MissingFile: {authFilePath}");
                return new LoadResult { Exists = false, IsValid = false, Error = "missing_file" };
            }

            try
            {
                var fileInfo = new FileInfo(authFilePath);
                string raw = await File.ReadAllTextAsync(authFilePath, Encoding.UTF8);
                LogDebug(
                    $"[AuthStateStore.Load] Read: Path={authFilePath}, Bytes={fileInfo.Length}, RawLen={raw?.Length ?? 0}, Prefix={GetPrefixForLog(raw)}");
                string signedJson = UnprotectPayload(raw);
                LogDebug($"[AuthStateStore.Load] UnprotectOk: SignedLen={signedJson.Length}");
                LogDebug($"[AuthStateStore.Load] SignedHead={signedJson.Substring(0, Math.Min(120, signedJson.Length))}");
                var payload = JsonSerializer.Deserialize<SignedPayload>(signedJson);

                if (payload == null || string.IsNullOrEmpty(payload.Data) || string.IsNullOrEmpty(payload.Signature))
                {
                    try
                    {
                        using (var doc = JsonDocument.Parse(signedJson))
                        {
                            var keys = string.Join(",", doc.RootElement.EnumerateObject().Select(p => p.Name));
                            LogDebug($"[AuthStateStore.Load] SignedKeys={keys}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"[AuthStateStore.Load] SignedJsonParseFail: {ex.GetType().Name}: {ex.Message}");
                    }
                    LogDebug("[AuthStateStore.Load] InvalidSignedPayload");
                    return new LoadResult { Exists = true, IsValid = false, Error = "invalid_signed_payload" };
                }

                byte[] dataBytes;
                try
                {
                    dataBytes = Convert.FromBase64String(payload.Data);
                }
                catch (Exception ex)
                {
                    LogDebug($"[AuthStateStore.Load] DataBase64DecodeFail: {ex.GetType().Name}: {ex.Message}");
                    return new LoadResult { Exists = true, IsValid = false, Error = "invalid_data_base64" };
                }
                var json = Encoding.UTF8.GetString(dataBytes);
                LogDebug($"[AuthStateStore.Load] DataDecoded: JsonLen={json.Length}, SigLen={payload.Signature.Length}");

                string expected = signFunc(json);
                if (!string.Equals(payload.Signature, expected, StringComparison.Ordinal))
                {
                    LogDebug(
                        $"[AuthStateStore.Load] SignatureMismatch: FileSigLen={payload.Signature.Length}, ExpectedSigLen={expected?.Length ?? 0}");
                    return new LoadResult { Exists = true, IsValid = false, Error = "signature_mismatch" };
                }

                var snapshot = JsonSerializer.Deserialize<AuthStateSnapshot>(json);
                if (snapshot == null)
                {
                    LogDebug("[AuthStateStore.Load] InvalidSnapshot");
                    return new LoadResult { Exists = true, IsValid = false, Error = "invalid_snapshot" };
                }

                LogDebug($"[AuthStateStore.Load] Success: Username={snapshot.Username ?? "<null>"}, FileVersion={snapshot.FileVersion}");
                return new LoadResult
                {
                    Exists = true,
                    IsValid = true,
                    Snapshot = snapshot
                };
            }
            catch (Exception ex)
            {
                LogDebug($"[AuthStateStore.Load] Exception: {ex.GetType().Name}: {ex.Message}");
                return new LoadResult
                {
                    Exists = true,
                    IsValid = false,
                    Error = ex.Message
                };
            }
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
            return EncryptedPrefix + Convert.ToBase64String(bytes);
        }

        private static string UnprotectPayload(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidDataException("empty_payload");
            }

            // 仅支持新格式 v1(AES-GCM)；旧格式不再兼容。
            if (!raw.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
            {
                LogDebug($"[AuthStateStore.Unprotect] UnsupportedPrefix: Prefix={GetPrefixForLog(raw)}");
                throw new InvalidDataException("unsupported_auth_payload_version");
            }

            string body = raw.Substring(EncryptedPrefix.Length);
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(body);
            }
            catch (Exception ex)
            {
                LogDebug($"[AuthStateStore.Unprotect] Base64DecodeFail: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
            return Encoding.UTF8.GetString(bytes);
        }

        private static string GetPrefixForLog(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "<empty>";
            }

            var sampleLength = Math.Min(8, text.Length);
            var sample = text.Substring(0, sampleLength)
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
            return sample;
        }

        [Conditional("DEBUG")]
        private static void LogDebug(string message)
        {
            Trace.WriteLine(message);
        }
    }
}
