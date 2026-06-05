namespace ImageColorChanger.Services.Licensing
{
    public sealed record LicenseVerificationResult(
        LicenseVerificationStatus Status,
        SignedLicensePayload Payload,
        string Reason)
    {
        public static LicenseVerificationResult NoLicense()
            => new(LicenseVerificationStatus.NoLicense, null, "no local license");

        public static LicenseVerificationResult Valid(SignedLicensePayload payload)
            => new(LicenseVerificationStatus.Valid, payload, null);

        public static LicenseVerificationResult Grace(SignedLicensePayload payload)
            => new(LicenseVerificationStatus.Grace, payload, null);

        public static LicenseVerificationResult Expired(SignedLicensePayload payload)
            => new(LicenseVerificationStatus.Expired, payload, "expired");

        public static LicenseVerificationResult Tampered(string reason)
            => new(LicenseVerificationStatus.Tampered, null, reason);

        public static LicenseVerificationResult WrongDevice(string reason)
            => new(LicenseVerificationStatus.WrongDevice, null, reason);

        public static LicenseVerificationResult ClockRollbackSuspected(string reason)
            => new(LicenseVerificationStatus.ClockRollbackSuspected, null, reason);

        public static LicenseVerificationResult UnsupportedKey(string reason)
            => new(LicenseVerificationStatus.UnsupportedKey, null, reason);
    }
}
