using System.Collections.Generic;

namespace ImageColorChanger.Services.Licensing
{
    public static class SignedLicensePublicKeys
    {
        public const string CurrentKeyId = "canvas-es256-2026-06";

        private const string CurrentPublicKeyPem =
            "-----BEGIN PUBLIC KEY-----\n" +
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEaVUbGgu7dtijKXkrZ0AGfQpWRYwQ\n" +
            "/mHyXiE5OMPCkCsmLnptJ5UnVT1TRM1fI2iHrInsj9SLLWnahIacov7Bzw==\n" +
            "-----END PUBLIC KEY-----";

        public static IReadOnlyDictionary<string, string> All { get; } =
            new Dictionary<string, string>
            {
                [CurrentKeyId] = CurrentPublicKeyPem
            };
    }
}
