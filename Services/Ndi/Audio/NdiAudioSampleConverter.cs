using System;
using ImageColorChanger.Services.Projection.Output;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ImageColorChanger.Services.Ndi.Audio
{
    internal static class NdiAudioSampleConverter
    {
        public static bool TryConvertToPlanarFloat(
            byte[] buffer,
            int bytesRecorded,
            WaveFormat sourceFormat,
            out ProjectionNdiAudioFrame frame)
        {
            frame = null;
            if (buffer == null || bytesRecorded <= 0 || sourceFormat == null)
            {
                return false;
            }

            try
            {
                using var sourceStream = new RawSourceWaveStream(buffer, 0, bytesRecorded, sourceFormat);
                ISampleProvider sampleProvider = sourceStream.ToSampleProvider();
                int channels = Math.Max(1, sampleProvider.WaveFormat.Channels);
                int sampleRate = Math.Max(1, sampleProvider.WaveFormat.SampleRate);
                int bytesPerFrame = Math.Max(1, sourceFormat.BlockAlign);
                int frameCount = Math.Max(1, bytesRecorded / bytesPerFrame);

                float[] interleaved = new float[frameCount * channels];
                int samplesRead = sampleProvider.Read(interleaved, 0, interleaved.Length);
                if (samplesRead <= 0)
                {
                    return false;
                }

                int framesRead = samplesRead / channels;
                if (framesRead <= 0)
                {
                    return false;
                }

                float[] planar = new float[framesRead * channels];
                for (int sampleIndex = 0; sampleIndex < framesRead; sampleIndex++)
                {
                    int interleavedOffset = sampleIndex * channels;
                    for (int channelIndex = 0; channelIndex < channels; channelIndex++)
                    {
                        planar[channelIndex * framesRead + sampleIndex] = interleaved[interleavedOffset + channelIndex];
                    }
                }

                frame = new ProjectionNdiAudioFrame(planar, sampleRate, channels, framesRead);
                return true;
            }
            catch
            {
                frame = null;
                return false;
            }
        }
    }
}
