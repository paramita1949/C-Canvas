using System;
using System.Collections.Generic;
using System.Text.Json;
using ImageColorChanger.Services.Licensing;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Licensing
{
    public sealed class FeatureGateTests
    {
        [Fact]
        public void Check_WhenNoLocalLicense_DeniesFeature()
        {
            var gate = new FeatureGate(new FakeLicenseStore(null), new FakeLicenseVerifier(null));

            FeatureGateResult result = gate.Check(PremiumFeature.SplitImage);

            Assert.False(result.IsAllowed);
            Assert.Equal("no_license", result.ReasonCode);
            Assert.Equal("创建分割图", result.FeatureName);
            Assert.Equal("请先登录", result.UserMessage);
        }

        [Fact]
        public void Check_WhenSignedLicenseContainsFeature_AllowsFeature()
        {
            var payload = new SignedLicensePayload
            {
                Features = new[] { "premium.split_image", "premium.slides" },
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(10),
            };
            var envelope = new SignedLicenseEnvelope("{}", "sig", "test-key", "ES256");
            var gate = new FeatureGate(
                new FakeLicenseStore(envelope),
                new FakeLicenseVerifier(LicenseVerificationResult.Valid(payload)));

            FeatureGateResult result = gate.Check(PremiumFeature.SplitImage);

            Assert.True(result.IsAllowed);
            Assert.Equal("allowed", result.ReasonCode);
            Assert.False(result.IsGrace);

            FeatureGateResult slidesResult = gate.Check(PremiumFeature.Slides);
            Assert.True(slidesResult.IsAllowed);
            Assert.Equal("幻灯片", slidesResult.FeatureName);
        }

        [Fact]
        public void Check_WhenSignedLicenseDoesNotContainFeature_DeniesOnlyThatFeature()
        {
            var payload = new SignedLicensePayload
            {
                Features = new[] { "premium.lyrics" },
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(10),
            };
            var envelope = new SignedLicenseEnvelope("{}", "sig", "test-key", "ES256");
            var gate = new FeatureGate(
                new FakeLicenseStore(envelope),
                new FakeLicenseVerifier(LicenseVerificationResult.Valid(payload)));

            FeatureGateResult result = gate.Check(PremiumFeature.SplitImage);

            Assert.False(result.IsAllowed);
            Assert.Equal("feature_missing", result.ReasonCode);
            Assert.Equal("功能未开通", result.UserMessage);
        }

        [Fact]
        public void Check_WhenLicenseExpired_ReturnsShortExpiredMessage()
        {
            var payload = new SignedLicensePayload
            {
                Features = new[] { "premium.split_image" },
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(-3),
            };
            var envelope = new SignedLicenseEnvelope("{}", "sig", "test-key", "ES256");
            var gate = new FeatureGate(
                new FakeLicenseStore(envelope),
                new FakeLicenseVerifier(LicenseVerificationResult.Expired(payload)));

            FeatureGateResult result = gate.Check(PremiumFeature.SplitImage);

            Assert.False(result.IsAllowed);
            Assert.Equal("expired", result.ReasonCode);
            Assert.Equal("授权到期", result.UserMessage);
        }

        [Fact]
        public void Check_WhenLicenseIsInGrace_AllowsFeatureAndMarksGrace()
        {
            var payload = new SignedLicensePayload
            {
                Features = new[] { "premium.live_caption" },
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
                GraceUntil = DateTimeOffset.UtcNow.AddDays(6),
            };
            var envelope = new SignedLicenseEnvelope("{}", "sig", "test-key", "ES256");
            var gate = new FeatureGate(
                new FakeLicenseStore(envelope),
                new FakeLicenseVerifier(LicenseVerificationResult.Grace(payload)));

            FeatureGateResult result = gate.Check(PremiumFeature.LiveCaption);

            Assert.True(result.IsAllowed);
            Assert.True(result.IsGrace);
            Assert.Equal("grace", result.ReasonCode);
        }

        [Fact]
        public void Check_WhenLicenseIsTampered_DeniesFeature()
        {
            var envelope = new SignedLicenseEnvelope("{}", "sig", "test-key", "ES256");
            var gate = new FeatureGate(
                new FakeLicenseStore(envelope),
                new FakeLicenseVerifier(LicenseVerificationResult.Tampered("signature mismatch")));

            FeatureGateResult result = gate.Check(PremiumFeature.Ndi);

            Assert.False(result.IsAllowed);
            Assert.Equal("tampered", result.ReasonCode);
        }

        [Fact]
        public void Require_WhenFeatureDenied_ReturnsSameDecisionForUiAndExecutionLayers()
        {
            var gate = new FeatureGate(new FakeLicenseStore(null), new FakeLicenseVerifier(null));

            FeatureGateResult result = gate.Require(PremiumFeature.SplitImage);

            Assert.False(result.IsAllowed);
            Assert.Equal("no_license", result.ReasonCode);
            Assert.Equal("请先登录", result.UserMessage);
        }

        [Fact]
        public void SignedLicensePayload_Should_ParseSupabaseSnakeCaseContract()
        {
            const string json = """
            {
              "license_id": "lic-7",
              "subject_id": "user-9",
              "device_hash": "hw-1",
              "plan": "pro",
              "features": ["premium.ai_panel", "premium.live_caption"],
              "issued_at": "2026-06-05T00:00:00Z",
              "expires_at": "2026-06-12T00:00:00Z",
              "grace_until": "2026-06-19T00:00:00Z",
              "license_version": 12
            }
            """;

            var payload = JsonSerializer.Deserialize<SignedLicensePayload>(json);

            Assert.NotNull(payload);
            Assert.Equal("lic-7", payload.LicenseId);
            Assert.Equal("user-9", payload.SubjectId);
            Assert.Equal("hw-1", payload.DeviceHash);
            Assert.Equal("pro", payload.Plan);
            Assert.Contains("premium.ai_panel", payload.Features);
            Assert.Equal(12, payload.LicenseVersion);
            Assert.Equal(DateTimeOffset.Parse("2026-06-12T00:00:00Z"), payload.ExpiresAt);
            Assert.Equal(DateTimeOffset.Parse("2026-06-19T00:00:00Z"), payload.GraceUntil);
        }

        [Fact]
        public void DefaultSignedLicenseVerifier_Should_KnowCurrentSupabaseKeyId()
        {
            const string payloadJson = "{\"features\":[\"premium.ndi\"],\"license_version\":1}";
            var envelope = new SignedLicenseEnvelope(
                payloadJson,
                "invalid-signature",
                "canvas-es256-2026-06",
                "ES256");
            var verifier = new SignedLicenseVerifier();

            LicenseVerificationResult result = verifier.Verify(envelope);

            Assert.Equal(LicenseVerificationStatus.Tampered, result.Status);
            Assert.Contains("signature", result.Reason);
        }

        private sealed class FakeLicenseStore : ILicenseStore
        {
            private readonly SignedLicenseEnvelope _envelope;

            public FakeLicenseStore(SignedLicenseEnvelope envelope)
            {
                _envelope = envelope;
            }

            public SignedLicenseEnvelope Load()
            {
                return _envelope;
            }

            public void Save(SignedLicenseEnvelope license)
            {
                throw new NotSupportedException();
            }

            public void Clear()
            {
                throw new NotSupportedException();
            }
        }

        private sealed class FakeLicenseVerifier : ISignedLicenseVerifier
        {
            private readonly LicenseVerificationResult _result;

            public FakeLicenseVerifier(LicenseVerificationResult result)
            {
                _result = result;
            }

            public LicenseVerificationResult Verify(SignedLicenseEnvelope envelope)
            {
                return _result ?? LicenseVerificationResult.NoLicense();
            }
        }
    }
}
