using System;
using System.IO;
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

        public async Task SaveSnapshotAsync(string authFilePath, AuthStateSnapshot snapshot, Func<string, string> signFunc)
        {
            string json = JsonSerializer.Serialize(snapshot);
            var payload = new SignedPayload
            {
                Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)),
                Signature = signFunc(json)
            };

            string signedJson = JsonSerializer.Serialize(payload);
            string encrypted = Convert.ToBase64String(Encoding.UTF8.GetBytes(signedJson));

            string directory = Path.GetDirectoryName(authFilePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(authFilePath, encrypted);
        }

        public async Task<LoadResult> LoadSnapshotAsync(string authFilePath, Func<string, string> signFunc)
        {
            if (!File.Exists(authFilePath))
            {
                return new LoadResult { Exists = false, IsValid = false, Error = "missing_file" };
            }

            try
            {
                string encrypted = await File.ReadAllTextAsync(authFilePath);
                var bytes = Convert.FromBase64String(encrypted);
                var signedJson = Encoding.UTF8.GetString(bytes);
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

        public void DeleteSnapshot(string authFilePath)
        {
            if (File.Exists(authFilePath))
            {
                File.Delete(authFilePath);
            }
        }
    }
}
