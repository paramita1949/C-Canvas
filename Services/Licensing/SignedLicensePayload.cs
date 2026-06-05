using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ImageColorChanger.Services.Licensing
{
    public sealed class SignedLicensePayload
    {
        [JsonPropertyName("license_id")]
        public string LicenseId { get; init; }

        [JsonPropertyName("subject_id")]
        public string SubjectId { get; init; }

        [JsonPropertyName("device_hash")]
        public string DeviceHash { get; init; }

        [JsonPropertyName("plan")]
        public string Plan { get; init; }

        [JsonPropertyName("features")]
        public IReadOnlyCollection<string> Features { get; init; } = Array.Empty<string>();

        [JsonPropertyName("issued_at")]
        public DateTimeOffset? IssuedAt { get; init; }

        [JsonPropertyName("expires_at")]
        public DateTimeOffset? ExpiresAt { get; init; }

        [JsonPropertyName("grace_until")]
        public DateTimeOffset? GraceUntil { get; init; }

        [JsonPropertyName("license_version")]
        public long LicenseVersion { get; init; }
    }
}
