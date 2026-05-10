namespace ImageColorChanger.Services.Projection.Output
{
    public sealed class ProjectionNdiAudioFrame
    {
        public ProjectionNdiAudioFrame(
            float[] planarSamples,
            int sampleRate,
            int channelCount,
            int samplesPerChannel)
        {
            PlanarSamples = planarSamples ?? System.Array.Empty<float>();
            SampleRate = sampleRate;
            ChannelCount = channelCount;
            SamplesPerChannel = samplesPerChannel;
        }

        public float[] PlanarSamples { get; }
        public int SampleRate { get; }
        public int ChannelCount { get; }
        public int SamplesPerChannel { get; }

        public int ChannelStrideInBytes => SamplesPerChannel * sizeof(float);
    }
}
