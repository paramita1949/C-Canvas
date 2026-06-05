using System.Text.Json;
using ImageColorChanger.Services;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class AuthLicenseContractTests
    {
        [Fact]
        public void AuthResponse_Should_ParseSignedLicenseEnvelopeFromData()
        {
            const string json = """
            {
              "success": true,
              "valid": true,
              "data": {
                "username": "demo",
                "token": "token-1",
                "license": {
                  "payload_json": "{\"features\":[\"premium.ndi\"],\"license_version\":7}",
                  "signature": "sig-1",
                  "key_id": "kid-2026-01",
                  "algorithm": "ES256"
                }
              }
            }
            """;

            var response = JsonSerializer.Deserialize<AuthResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            Assert.NotNull(response);
            Assert.NotNull(response.Data);
            Assert.NotNull(response.Data.License);
            Assert.Equal("sig-1", response.Data.License.Signature);
            Assert.Equal("kid-2026-01", response.Data.License.KeyId);
            Assert.Equal("ES256", response.Data.License.Algorithm);
            Assert.Contains("premium.ndi", response.Data.License.PayloadJson);
        }
    }
}
