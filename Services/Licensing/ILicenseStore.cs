namespace ImageColorChanger.Services.Licensing
{
    public interface ILicenseStore
    {
        SignedLicenseEnvelope Load();
        void Save(SignedLicenseEnvelope license);
        void Clear();
    }
}
