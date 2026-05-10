namespace ImageColorChanger.Services.Ndi.Audio
{
    public sealed class NdiAudioDeviceInfo
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public bool IsDefault { get; init; }
    }
}
