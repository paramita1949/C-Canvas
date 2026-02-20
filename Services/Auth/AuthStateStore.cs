using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

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

        private const string CurrentPrefix = "v3:";
        private const string PreviousPrefix = "v2:";
        private const string LegacyPrefix = "v1:";
        private const string EncryptionAlgorithmAesGcm = "A256GCM";
        private const string CurrentKeyId = "k3";
        private static readonly string[] DecryptWindowKeyIds = { "k3" };
        private const int LegacyKeyDecryptWindowDays = 90;
        private const int AesKeySizeBytes = 32;
        private const int AesNonceSizeBytes = 12;
        private const int AesTagSizeBytes = 16;
        private static readonly byte[] KeyDerivationSalt = Encoding.UTF8.GetBytes("CanvasCast.AuthStateStore.v2");

        public async Task SaveSnapshotAsync(string authFilePath, AuthStateSnapshot snapshot, Func<string, string> signFunc)
        {
            LogDebug($"[AuthStateStore.Save] Begin: Path={authFilePath}");
            string json = JsonSerializer.Serialize(snapshot);
            var payloadSignature = signFunc(json);
            var encrypted = EncryptSnapshotJson(json, CurrentKeyId);
            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var maxSeenCounter = ReadMaxSeenCounter(authFilePath);
            var counter = checked(maxSeenCounter + 1);
            LogDebug(
                $"[AuthStateStore.Save] SnapshotSerialized: JsonLen={json.Length}, CipherLen={encrypted.CiphertextBase64.Length}, SigLen={payloadSignature?.Length ?? 0}, Counter={counter}");

            // 使用固定字面键，避免壳/混淆改名导致反序列化失效。
            var envelope = new Dictionary<string, object>
            {
                ["Version"] = 3,
                ["KeyId"] = CurrentKeyId,
                ["Counter"] = counter,
                ["CreatedAt"] = nowUnix,
                ["UpdatedAt"] = nowUnix,
                ["Enc"] = EncryptionAlgorithmAesGcm,
                ["Nonce"] = encrypted.NonceBase64,
                ["Tag"] = encrypted.TagBase64,
                ["Data"] = encrypted.CiphertextBase64,
                ["Signature"] = payloadSignature
            };
            envelope["MetaMac"] = ComputeMetaMac(envelope, authFilePath);

            string signedJson = JsonSerializer.Serialize(envelope);
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
                WriteMaxSeenCounter(authFilePath, counter);
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
                if (!TryReadEnvelope(
                        authFilePath,
                        signedJson,
                        out var payloadData,
                        out var payloadSignature,
                        out var payloadEnc,
                        out var payloadNonce,
                        out var payloadTag,
                        out var payloadCounter,
                        out var payloadKeyId,
                        out var payloadUpdatedAtUnix))
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

                string json;
                if (string.Equals(payloadEnc, EncryptionAlgorithmAesGcm, StringComparison.Ordinal))
                {
                    if (IsLegacyKeyExpired(payloadKeyId, payloadUpdatedAtUnix))
                    {
                        LogDebug($"[AuthStateStore.Load] LegacyKeyExpired: KeyId={payloadKeyId}, UpdatedAt={payloadUpdatedAtUnix}, WindowDays={LegacyKeyDecryptWindowDays}");
                        return new LoadResult { Exists = true, IsValid = false, Error = "legacy_key_expired" };
                    }

                    try
                    {
                        json = DecryptSnapshotJson(payloadData, payloadNonce, payloadTag, payloadKeyId, out var decryptKeyId);
                        LogDebug($"[AuthStateStore.Load] DecryptKeyResolved: EnvelopeKeyId={payloadKeyId ?? "<null>"}, UsedKeyId={decryptKeyId}");
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"[AuthStateStore.Load] DecryptFail: {ex.GetType().Name}: {ex.Message}");
                        return new LoadResult { Exists = true, IsValid = false, Error = "decrypt_failed" };
                    }
                }
                else
                {
                    // 兼容：历史 v2 明文 Data(Base64(JSON))
                    byte[] dataBytes;
                    try
                    {
                        dataBytes = Convert.FromBase64String(payloadData);
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"[AuthStateStore.Load] DataBase64DecodeFail: {ex.GetType().Name}: {ex.Message}");
                        return new LoadResult { Exists = true, IsValid = false, Error = "invalid_data_base64" };
                    }

                    json = Encoding.UTF8.GetString(dataBytes);
                }
                LogDebug($"[AuthStateStore.Load] DataDecoded: JsonLen={json.Length}, SigLen={payloadSignature.Length}, Enc={payloadEnc ?? "<legacy_plain>"}, Counter={payloadCounter}, KeyId={payloadKeyId ?? "<legacy>"}");

                string expected = signFunc(json);
                if (!string.Equals(payloadSignature, expected, StringComparison.Ordinal))
                {
                    LogDebug(
                        $"[AuthStateStore.Load] SignatureMismatch: FileSigLen={payloadSignature.Length}, ExpectedSigLen={expected?.Length ?? 0}");
                    return new LoadResult { Exists = true, IsValid = false, Error = "signature_mismatch" };
                }

                if (payloadCounter > 0)
                {
                    var maxSeenCounter = ReadMaxSeenCounter(authFilePath);
                    if (payloadCounter < maxSeenCounter)
                    {
                        LogDebug($"[AuthStateStore.Load] RollbackDetected: PayloadCounter={payloadCounter}, MaxSeen={maxSeenCounter}");
                        return new LoadResult { Exists = true, IsValid = false, Error = "rollback_detected" };
                    }

                    WriteMaxSeenCounter(authFilePath, payloadCounter);
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
            return CurrentPrefix + Convert.ToBase64String(bytes);
        }

        private static string UnprotectPayload(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidDataException("empty_payload");
            }

            // 优先新格式 v3，兼容旧 v2/v1。
            if (raw.StartsWith(CurrentPrefix, StringComparison.Ordinal))
            {
                string bodyV3 = raw.Substring(CurrentPrefix.Length);
                return DecodeBase64Payload(bodyV3);
            }

            if (raw.StartsWith(PreviousPrefix, StringComparison.Ordinal))
            {
                string bodyV2 = raw.Substring(PreviousPrefix.Length);
                return DecodeBase64Payload(bodyV2);
            }

            if (raw.StartsWith(LegacyPrefix, StringComparison.Ordinal))
            {
                string bodyV1 = raw.Substring(LegacyPrefix.Length);
                return DecodeBase64Payload(bodyV1);
            }

            LogDebug($"[AuthStateStore.Unprotect] UnsupportedPrefix: Prefix={GetPrefixForLog(raw)}");
            throw new InvalidDataException("unsupported_auth_payload_version");
        }

        private static string DecodeBase64Payload(string body)
        {
            try
            {
                var bytes = Convert.FromBase64String(body);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex)
            {
                LogDebug($"[AuthStateStore.Unprotect] Base64DecodeFail: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }

        private static bool TryReadEnvelope(
            string authFilePath,
            string signedJson,
            out string data,
            out string signature,
            out string enc,
            out string nonce,
            out string tag,
            out long counter,
            out string keyId,
            out long updatedAtUnix)
        {
            data = null;
            signature = null;
            enc = null;
            nonce = null;
            tag = null;
            counter = 0;
            keyId = null;
            updatedAtUnix = 0;

            try
            {
                using (var doc = JsonDocument.Parse(signedJson))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        return false;
                    }

                    if (!doc.RootElement.TryGetProperty("Data", out var dataElem) ||
                        dataElem.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }

                    if (!doc.RootElement.TryGetProperty("Signature", out var sigElem) ||
                        sigElem.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }

                    data = dataElem.GetString();
                    signature = sigElem.GetString();

                    if (doc.RootElement.TryGetProperty("Enc", out var encElem) &&
                        encElem.ValueKind == JsonValueKind.String)
                    {
                        enc = encElem.GetString();
                    }

                    if (doc.RootElement.TryGetProperty("Nonce", out var nonceElem) &&
                        nonceElem.ValueKind == JsonValueKind.String)
                    {
                        nonce = nonceElem.GetString();
                    }

                    if (doc.RootElement.TryGetProperty("Tag", out var tagElem) &&
                        tagElem.ValueKind == JsonValueKind.String)
                    {
                        tag = tagElem.GetString();
                    }

                    if (doc.RootElement.TryGetProperty("Counter", out var counterElem) &&
                        counterElem.ValueKind == JsonValueKind.Number &&
                        counterElem.TryGetInt64(out var parsedCounter))
                    {
                        counter = parsedCounter;
                    }

                    if (doc.RootElement.TryGetProperty("KeyId", out var keyIdElem) &&
                        keyIdElem.ValueKind == JsonValueKind.String)
                    {
                        keyId = keyIdElem.GetString();
                    }

                    if (doc.RootElement.TryGetProperty("UpdatedAt", out var updatedAtElem) &&
                        updatedAtElem.ValueKind == JsonValueKind.Number &&
                        updatedAtElem.TryGetInt64(out var parsedUpdatedAt))
                    {
                        updatedAtUnix = parsedUpdatedAt;
                    }

                    if (doc.RootElement.TryGetProperty("Version", out var versionElem) &&
                        versionElem.ValueKind == JsonValueKind.Number &&
                        versionElem.TryGetInt32(out var version) &&
                        version >= 3)
                    {
                        if (!doc.RootElement.TryGetProperty("MetaMac", out var macElem) ||
                            macElem.ValueKind != JsonValueKind.String)
                        {
                            return false;
                        }

                        var macKeyId = string.IsNullOrWhiteSpace(keyId) ? CurrentKeyId : keyId;

                        var fileMac = macElem.GetString();
                        var expectMac = ComputeMetaMac(doc.RootElement, macKeyId, authFilePath);
                        if (!FixedTimeEquals(fileMac, expectMac))
                        {
                            LogDebug("[AuthStateStore.Load] MetaMacMismatch");
                            return false;
                        }
                    }

                    if (string.Equals(enc, EncryptionAlgorithmAesGcm, StringComparison.Ordinal) &&
                        (string.IsNullOrWhiteSpace(nonce) || string.IsNullOrWhiteSpace(tag)))
                    {
                        return false;
                    }

                    return !string.IsNullOrWhiteSpace(data) && !string.IsNullOrWhiteSpace(signature);
                }
            }
            catch
            {
                return false;
            }
        }

        private static (string CiphertextBase64, string NonceBase64, string TagBase64) EncryptSnapshotJson(string json, string keyId)
        {
            var key = BuildEncryptionKey(keyId);
            var plain = Encoding.UTF8.GetBytes(json);
            var nonce = new byte[AesNonceSizeBytes];
            var tag = new byte[AesTagSizeBytes];
            var cipher = new byte[plain.Length];

            RandomNumberGenerator.Fill(nonce);

            using (var aes = new AesGcm(key, AesTagSizeBytes))
            {
                aes.Encrypt(nonce, plain, cipher, tag, null);
            }

            return (
                Convert.ToBase64String(cipher),
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(tag)
            );
        }

        private static string DecryptSnapshotJson(
            string cipherBase64,
            string nonceBase64,
            string tagBase64,
            string envelopeKeyId,
            out string usedKeyId)
        {
            var cipher = Convert.FromBase64String(cipherBase64);
            var nonce = Convert.FromBase64String(nonceBase64);
            var tag = Convert.FromBase64String(tagBase64);
            foreach (var candidateKeyId in EnumerateDecryptKeyCandidates(envelopeKeyId))
            {
                try
                {
                    var key = BuildEncryptionKey(candidateKeyId);
                    var plain = new byte[cipher.Length];
                    using (var aes = new AesGcm(key, AesTagSizeBytes))
                    {
                        aes.Decrypt(nonce, cipher, tag, plain, null);
                    }

                    usedKeyId = candidateKeyId;
                    return Encoding.UTF8.GetString(plain);
                }
                catch (CryptographicException)
                {
                    // 继续尝试解密窗口中的其余 key。
                }
            }

            usedKeyId = null;
            throw new CryptographicException("decrypt_failed_all_keys");
        }

        private static byte[] BuildEncryptionKey(string keyId)
        {
            return BuildKeyMaterial(keyId, "enc");
        }

        private static byte[] BuildMetaMacKey(string keyId)
        {
            return BuildKeyMaterial(keyId, "mac");
        }

        private static IEnumerable<string> EnumerateDecryptKeyCandidates(string envelopeKeyId)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(envelopeKeyId) && seen.Add(envelopeKeyId))
            {
                yield return envelopeKeyId;
            }

            foreach (var keyId in DecryptWindowKeyIds)
            {
                if (!string.IsNullOrWhiteSpace(keyId) && seen.Add(keyId))
                {
                    yield return keyId;
                }
            }
        }

        private static bool IsLegacyKeyExpired(string keyId, long updatedAtUnix)
        {
            if (string.IsNullOrWhiteSpace(keyId) ||
                string.Equals(keyId, CurrentKeyId, StringComparison.Ordinal))
            {
                return false;
            }

            if (updatedAtUnix <= 0)
            {
                return true;
            }

            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var ageSeconds = nowUnix - updatedAtUnix;
            var maxAgeSeconds = LegacyKeyDecryptWindowDays * 24L * 60L * 60L;
            return ageSeconds > maxAgeSeconds;
        }

        private static byte[] BuildKeyMaterial(string keyId, string purpose)
        {
            string machineGuid = null;
            try
            {
                using (var key64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                           .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    machineGuid = key64?.GetValue("MachineGuid")?.ToString();
                }
            }
            catch
            {
            }

            if (string.IsNullOrWhiteSpace(machineGuid))
            {
                try
                {
                    using (var key32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
                               .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                    {
                        machineGuid = key32?.GetValue("MachineGuid")?.ToString();
                    }
                }
                catch
                {
                }
            }

            if (string.IsNullOrWhiteSpace(machineGuid))
            {
                machineGuid = Environment.MachineName ?? "unknown_machine";
            }

            var seed = $"{machineGuid}|CanvasCast.AuthStateStore.v2|{keyId}|{purpose}";
            var seedBytes = Encoding.UTF8.GetBytes(seed);
            var mixed = new byte[seedBytes.Length + KeyDerivationSalt.Length];
            Buffer.BlockCopy(seedBytes, 0, mixed, 0, seedBytes.Length);
            Buffer.BlockCopy(KeyDerivationSalt, 0, mixed, seedBytes.Length, KeyDerivationSalt.Length);

            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(mixed);
                var key = new byte[AesKeySizeBytes];
                Buffer.BlockCopy(hash, 0, key, 0, AesKeySizeBytes);
                return key;
            }
        }

        private static string ComputeMetaMac(IDictionary<string, object> envelope, string authFilePath)
        {
            string keyId = envelope.TryGetValue("KeyId", out var keyIdObj) ? keyIdObj?.ToString() : CurrentKeyId;
            string payload = BuildMetaMacPayload(
                envelope.TryGetValue("Version", out var v) ? v?.ToString() : null,
                keyId,
                envelope.TryGetValue("Counter", out var c) ? c?.ToString() : null,
                envelope.TryGetValue("CreatedAt", out var ca) ? ca?.ToString() : null,
                envelope.TryGetValue("UpdatedAt", out var ua) ? ua?.ToString() : null,
                envelope.TryGetValue("Enc", out var e) ? e?.ToString() : null,
                envelope.TryGetValue("Nonce", out var n) ? n?.ToString() : null,
                envelope.TryGetValue("Tag", out var t) ? t?.ToString() : null,
                envelope.TryGetValue("Data", out var d) ? d?.ToString() : null,
                envelope.TryGetValue("Signature", out var s) ? s?.ToString() : null,
                authFilePath);

            var macKey = BuildMetaMacKey(keyId ?? CurrentKeyId);
            using (var hmac = new HMACSHA256(macKey))
            {
                return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
            }
        }

        private static string ComputeMetaMac(JsonElement root, string keyId, string authFilePath)
        {
            string payload = BuildMetaMacPayload(
                root.TryGetProperty("Version", out var v) ? v.ToString() : null,
                keyId,
                root.TryGetProperty("Counter", out var c) ? c.ToString() : null,
                root.TryGetProperty("CreatedAt", out var ca) ? ca.ToString() : null,
                root.TryGetProperty("UpdatedAt", out var ua) ? ua.ToString() : null,
                root.TryGetProperty("Enc", out var e) ? e.GetString() : null,
                root.TryGetProperty("Nonce", out var n) ? n.GetString() : null,
                root.TryGetProperty("Tag", out var t) ? t.GetString() : null,
                root.TryGetProperty("Data", out var d) ? d.GetString() : null,
                root.TryGetProperty("Signature", out var s) ? s.GetString() : null,
                authFilePath);

            var macKey = BuildMetaMacKey(keyId ?? CurrentKeyId);
            using (var hmac = new HMACSHA256(macKey))
            {
                return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
            }
        }

        private static string BuildMetaMacPayload(
            string version,
            string keyId,
            string counter,
            string createdAt,
            string updatedAt,
            string enc,
            string nonce,
            string tag,
            string data,
            string signature,
            string authFilePath)
        {
            return string.Join("|",
                version ?? string.Empty,
                keyId ?? string.Empty,
                counter ?? string.Empty,
                createdAt ?? string.Empty,
                updatedAt ?? string.Empty,
                enc ?? string.Empty,
                nonce ?? string.Empty,
                tag ?? string.Empty,
                data ?? string.Empty,
                signature ?? string.Empty,
                authFilePath ?? string.Empty);
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return false;
            }

            var left = Encoding.UTF8.GetBytes(a);
            var right = Encoding.UTF8.GetBytes(b);
            return CryptographicOperations.FixedTimeEquals(left, right);
        }

        private static string GetCounterStorePath(string authFilePath)
        {
            return authFilePath + ".ctr";
        }

        private static long ReadMaxSeenCounter(string authFilePath)
        {
            try
            {
                var path = GetCounterStorePath(authFilePath);
                if (!File.Exists(path))
                {
                    return 0;
                }

                var raw = File.ReadAllText(path, Encoding.UTF8);
                var parts = raw.Split('|');
                if (parts.Length != 2 || !long.TryParse(parts[0], out var value))
                {
                    return 0;
                }

                if (!IsCounterMacValid(parts[1], value, authFilePath))
                {
                    return 0;
                }

                return value;
            }
            catch
            {
                return 0;
            }
        }

        private static void WriteMaxSeenCounter(string authFilePath, long value)
        {
            try
            {
                var path = GetCounterStorePath(authFilePath);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var mac = ComputeCounterMac(value, authFilePath, CurrentKeyId);
                File.WriteAllText(path, $"{value}|{mac}", Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static bool IsCounterMacValid(string fileMac, long value, string authFilePath)
        {
            foreach (var keyId in EnumerateDecryptKeyCandidates(CurrentKeyId))
            {
                var expectMac = ComputeCounterMac(value, authFilePath, keyId);
                if (FixedTimeEquals(fileMac, expectMac))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ComputeCounterMac(long value, string authFilePath, string keyId)
        {
            var key = BuildMetaMacKey(keyId);
            var payload = $"{value}|{authFilePath}";
            using (var hmac = new HMACSHA256(key))
            {
                return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
            }
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
            // Trace.WriteLine(message);
        }
    }
}
