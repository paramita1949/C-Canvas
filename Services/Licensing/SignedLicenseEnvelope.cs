using System.Text.Json.Serialization;

namespace ImageColorChanger.Services.Licensing
{
    public sealed record SignedLicenseEnvelope(
        [property: JsonPropertyName("payload_json")]
        string PayloadJson,
        [property: JsonPropertyName("signature")]
        string Signature,
        [property: JsonPropertyName("key_id")]
        string KeyId,
        [property: JsonPropertyName("algorithm")]
        string Algorithm);
}
