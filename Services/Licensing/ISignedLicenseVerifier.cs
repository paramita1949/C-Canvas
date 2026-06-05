namespace ImageColorChanger.Services.Licensing
{
    public interface ISignedLicenseVerifier
    {
        LicenseVerificationResult Verify(SignedLicenseEnvelope envelope);
    }
}
