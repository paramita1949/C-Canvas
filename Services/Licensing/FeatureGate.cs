using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ImageColorChanger.Services.Licensing
{
    public sealed class FeatureGate : IFeatureGate
    {
        private readonly ILicenseStore _licenseStore;
        private readonly ISignedLicenseVerifier _licenseVerifier;

        public FeatureGate(ILicenseStore licenseStore, ISignedLicenseVerifier licenseVerifier)
        {
            _licenseStore = licenseStore ?? throw new ArgumentNullException(nameof(licenseStore));
            _licenseVerifier = licenseVerifier ?? throw new ArgumentNullException(nameof(licenseVerifier));
        }

        public FeatureGateResult Check(PremiumFeature feature)
        {
            string featureName = FeatureClaimMapper.ToDisplayName(feature);
            SignedLicenseEnvelope envelope = _licenseStore.Load();
            if (envelope == null)
            {
                return Deny(featureName, "no_license", "请先登录", null);
            }

            LicenseVerificationResult verification = _licenseVerifier.Verify(envelope);
            if (verification == null || verification.Status == LicenseVerificationStatus.NoLicense)
            {
                return Deny(featureName, "no_license", "请先登录", null);
            }

            if (verification.Status == LicenseVerificationStatus.Tampered)
            {
                return Deny(featureName, "tampered", "本机授权文件校验失败，请重新登录刷新授权。", null);
            }

            if (verification.Status == LicenseVerificationStatus.WrongDevice)
            {
                return Deny(featureName, "wrong_device", "授权不属于当前设备，请重新绑定或联系管理员。", null);
            }

            if (verification.Status == LicenseVerificationStatus.ClockRollbackSuspected)
            {
                return Deny(featureName, "clock_rollback", "检测到本机时间异常，请校准时间后重试。", null);
            }

            if (verification.Status == LicenseVerificationStatus.UnsupportedKey)
            {
                return Deny(featureName, "unsupported_key", "授权密钥版本不受支持，请更新软件或刷新授权。", null);
            }

            if (verification.Status == LicenseVerificationStatus.Expired)
            {
                return Deny(featureName, "expired", "授权到期", verification.Payload?.ExpiresAt);
            }

            SignedLicensePayload payload = verification.Payload;
            if (payload == null)
            {
                return Deny(featureName, "invalid_payload", "授权内容无效，请重新登录刷新授权。", null);
            }

            string claim = FeatureClaimMapper.ToClaim(feature);
            bool hasClaim = payload.Features?.Any(x => string.Equals(x, claim, StringComparison.Ordinal)) == true;
            if (!hasClaim)
            {
                return Deny(featureName, "feature_missing", "功能未开通", payload.ExpiresAt);
            }

            bool isGrace = verification.Status == LicenseVerificationStatus.Grace;
            return new FeatureGateResult(
                true,
                featureName,
                isGrace ? "grace" : "allowed",
                isGrace ? $"{featureName}处于离线宽限期内。" : string.Empty,
                payload.ExpiresAt,
                isGrace);
        }

        public FeatureGateResult Require(PremiumFeature feature)
        {
            FeatureGateResult result = Check(feature);
            if (result.IsAllowed)
            {
                return result;
            }

            string message = string.IsNullOrWhiteSpace(result.UserMessage)
                ? "功能未开通"
                : result.UserMessage;

            return result with { UserMessage = message };
        }

        public Task<FeatureGateResult> CheckAsync(PremiumFeature feature, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Check(feature));
        }

        private static FeatureGateResult Deny(string featureName, string reasonCode, string userMessage, DateTimeOffset? expiresAt)
        {
            return new FeatureGateResult(false, featureName, reasonCode, userMessage, expiresAt, false);
        }
    }
}
