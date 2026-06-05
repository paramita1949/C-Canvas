using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ImageColorChanger.Services.Licensing
{
    public sealed class SignedLicenseVerifier : ISignedLicenseVerifier
    {
        private readonly IReadOnlyDictionary<string, string> _publicKeysByKeyId;
        private readonly Func<DateTimeOffset> _nowProvider;

        public SignedLicenseVerifier()
            : this(SignedLicensePublicKeys.All, () => DateTimeOffset.UtcNow)
        {
        }

        public SignedLicenseVerifier(IReadOnlyDictionary<string, string> publicKeysByKeyId, Func<DateTimeOffset> nowProvider = null)
        {
            _publicKeysByKeyId = publicKeysByKeyId ?? throw new ArgumentNullException(nameof(publicKeysByKeyId));
            _nowProvider = nowProvider ?? (() => DateTimeOffset.UtcNow);
        }

        public LicenseVerificationResult Verify(SignedLicenseEnvelope envelope)
        {
            if (envelope == null)
            {
                return LicenseVerificationResult.NoLicense();
            }

            if (string.IsNullOrWhiteSpace(envelope.PayloadJson) ||
                string.IsNullOrWhiteSpace(envelope.Signature) ||
                string.IsNullOrWhiteSpace(envelope.KeyId) ||
                string.IsNullOrWhiteSpace(envelope.Algorithm))
            {
                return LicenseVerificationResult.Tampered("missing envelope fields");
            }

            if (!string.Equals(envelope.Algorithm, "ES256", StringComparison.OrdinalIgnoreCase))
            {
                return LicenseVerificationResult.UnsupportedKey($"unsupported algorithm: {envelope.Algorithm}");
            }

            if (!_publicKeysByKeyId.TryGetValue(envelope.KeyId, out string publicKeyPem) ||
                string.IsNullOrWhiteSpace(publicKeyPem))
            {
                return LicenseVerificationResult.UnsupportedKey($"unknown key id: {envelope.KeyId}");
            }

            if (!TryVerifyEs256(envelope.PayloadJson, envelope.Signature, publicKeyPem))
            {
                return LicenseVerificationResult.Tampered("signature mismatch");
            }

            SignedLicensePayload payload;
            try
            {
                payload = JsonSerializer.Deserialize<SignedLicensePayload>(
                    envelope.PayloadJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                return LicenseVerificationResult.Tampered($"invalid payload json: {ex.Message}");
            }

            if (payload == null)
            {
                return LicenseVerificationResult.Tampered("empty payload");
            }

            DateTimeOffset now = _nowProvider();
            if (payload.ExpiresAt.HasValue && now > payload.ExpiresAt.Value)
            {
                if (payload.GraceUntil.HasValue && now <= payload.GraceUntil.Value)
                {
                    return LicenseVerificationResult.Grace(payload);
                }

                return LicenseVerificationResult.Expired(payload);
            }

            return LicenseVerificationResult.Valid(payload);
        }

        private static bool TryVerifyEs256(string payloadJson, string signatureBase64Url, string publicKeyPem)
        {
            try
            {
                byte[] signature = DecodeBase64Url(signatureBase64Url);
                byte[] payload = Encoding.UTF8.GetBytes(payloadJson);
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(publicKeyPem);
                return ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            }
            catch
            {
                return false;
            }
        }

        private static byte[] DecodeBase64Url(string value)
        {
            string normalized = value.Replace('-', '+').Replace('_', '/');
            switch (normalized.Length % 4)
            {
                case 2:
                    normalized += "==";
                    break;
                case 3:
                    normalized += "=";
                    break;
            }

            return Convert.FromBase64String(normalized);
        }
    }
}
