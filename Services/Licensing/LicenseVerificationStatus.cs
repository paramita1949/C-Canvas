namespace ImageColorChanger.Services.Licensing
{
    public enum LicenseVerificationStatus
    {
        NoLicense,
        Valid,
        Grace,
        Expired,
        Tampered,
        WrongDevice,
        ClockRollbackSuspected,
        UnsupportedKey
    }
}
