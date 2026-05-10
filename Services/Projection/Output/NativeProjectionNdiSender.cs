using System;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;

namespace ImageColorChanger.Services.Projection.Output
{
    /// <summary>
    /// 基于 NDI Runtime 原生库的真实发送器。
    /// 依赖目标机已安装 NewTek NDI Runtime（提供 Processing.NDI.Lib.x64.dll）。
    /// </summary>
    public sealed class NativeProjectionNdiSender : IProjectionNdiSender
    {
        private static readonly object InitLock = new object();
        private static int _ndiInitRefCount;
        private static bool _ndiAvailable;
        private static bool _audioFormatLogged;
        private const int AsyncBufferCount = 3;

        private IntPtr _senderHandle = IntPtr.Zero;
        private readonly object _sync = new object();
        private readonly IntPtr[] _asyncBuffers = new IntPtr[AsyncBufferCount];
        private int _asyncBufferSize;
        private int _asyncBufferIndex;
        private short[] _audioInterleaved16Buffer = Array.Empty<short>();
        private int _lastConnectionCount = -1;
        private ProjectionNdiOutputOptions _options;

        public bool IsRunning => _senderHandle != IntPtr.Zero;

        public bool Start(ProjectionNdiOutputOptions options)
        {
            if (IsRunning)
            {
                return true;
            }

            _options = options ?? new ProjectionNdiOutputOptions();
            ProjectionNdiDiagnostics.Log($"Sender start requested: name={_options.SenderName}, fps={_options.Fps}, size={_options.Width}x{_options.Height}, preferAlpha={_options.PreferAlpha}");

            if (!TryInitializeNdi())
            {
                ProjectionNdiDiagnostics.Log("Sender start failed: NDI runtime initialization failed.");
                return false;
            }

            try
            {
                string senderName = string.IsNullOrWhiteSpace(_options.SenderName) ? "YongMu-NDI" : _options.SenderName;
                IntPtr senderNamePtr = AllocUtf8(senderName);
                var create = new NativeNdiInterop.NDIlib_send_create_t
                {
                    p_ndi_name = senderNamePtr,
                    p_groups = IntPtr.Zero,
                    clock_video = true,
                    clock_audio = true
                };

                try
                {
                    _senderHandle = NativeNdiInterop.SendCreate(ref create);
                }
                finally
                {
                    if (senderNamePtr != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(senderNamePtr);
                    }
                }
                if (_senderHandle == IntPtr.Zero)
                {
                    ProjectionNdiDiagnostics.Log("Sender start failed: NDIlib_send_create returned null.");
                    ReleaseNdi();
                    return false;
                }

                ProjectionNdiDiagnostics.Log("Sender started successfully.");
                return true;
            }
            catch (DllNotFoundException)
            {
                ProjectionNdiDiagnostics.Log("Sender start failed: NDI DLL not found.");
                ReleaseNdi();
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                ProjectionNdiDiagnostics.Log("Sender start failed: NDI entry point missing.");
                ReleaseNdi();
                return false;
            }
            catch (BadImageFormatException)
            {
                ProjectionNdiDiagnostics.Log("Sender start failed: bad image format (x64/x86 mismatch).");
                ReleaseNdi();
                return false;
            }
            catch (Exception ex)
            {
                ProjectionNdiDiagnostics.LogException("Sender start exception", ex);
                ReleaseNdi();
                return false;
            }
        }

        public bool SendFrame(SKBitmap frame)
        {
            if (!IsRunning || frame == null)
            {
                if (!IsRunning)
                {
                    ProjectionNdiDiagnostics.Log("SendFrame skipped: sender not running.");
                }
                return false;
            }

            try
            {
                int width = frame.Width;
                int height = frame.Height;
                int rowBytes = frame.RowBytes;
                int byteCount = rowBytes * height;
                if (width <= 0 || height <= 0 || byteCount <= 0)
                {
                    return false;
                }

                IntPtr pixelPtr = frame.GetPixels();
                if (pixelPtr == IntPtr.Zero)
                {
                    return false;
                }

                lock (_sync)
                {
                    if (_senderHandle == IntPtr.Zero)
                    {
                        return false;
                    }

                    EnsureAsyncBuffers(byteCount);
                    _asyncBufferIndex = (_asyncBufferIndex + 1) % AsyncBufferCount;
                    IntPtr bufferPtr = _asyncBuffers[_asyncBufferIndex];

                    unsafe
                    {
                        Buffer.MemoryCopy((void*)pixelPtr, (void*)bufferPtr, byteCount, byteCount);
                    }

                    int fps = Math.Clamp(_options?.Fps ?? 30, 1, 120);
                    var videoFrame = new NativeNdiInterop.NDIlib_video_frame_v2_t
                    {
                        xres = width,
                        yres = height,
                        FourCC = NativeNdiInterop.NDIlib_FourCC_video_type_e.NDIlib_FourCC_video_type_BGRA,
                        frame_rate_N = fps,
                        frame_rate_D = 1,
                        picture_aspect_ratio = (float)width / height,
                        frame_format_type = NativeNdiInterop.NDIlib_frame_format_type_e.NDIlib_frame_format_type_progressive,
                        timecode = NativeNdiInterop.NDIlib_send_timecode_synthesize,
                        p_data = bufferPtr,
                        line_stride_in_bytes = rowBytes,
                        p_metadata = IntPtr.Zero,
                        timestamp = 0
                    };

                    NativeNdiInterop.SendSendVideoAsyncV2(_senderHandle, ref videoFrame);
                }
                return true;
            }
            catch
            {
                ProjectionNdiDiagnostics.Log("SendFrame failed: unknown exception.");
                return false;
            }
        }

        public bool SendAudio(ProjectionNdiAudioFrame audioFrame)
        {
            if (!IsRunning || audioFrame == null || audioFrame.PlanarSamples == null || audioFrame.PlanarSamples.Length == 0)
            {
                return false;
            }

            try
            {
                int sampleRate = Math.Max(1, audioFrame.SampleRate);
                int channelCount = Math.Max(1, audioFrame.ChannelCount);
                int samplesPerChannel = Math.Max(1, audioFrame.SamplesPerChannel);
                int expectedLength = channelCount * samplesPerChannel;
                if (audioFrame.PlanarSamples.Length < expectedLength)
                {
                    return false;
                }

                lock (_sync)
                {
                    if (_senderHandle == IntPtr.Zero)
                    {
                        return false;
                    }

                    short[] interleavedPcm16 = EnsureInterleavedAudioBuffer(expectedLength);
                    ConvertPlanarFloatToInterleaved16(
                        audioFrame.PlanarSamples,
                        interleavedPcm16,
                        channelCount,
                        samplesPerChannel);

                    var handle = GCHandle.Alloc(interleavedPcm16, GCHandleType.Pinned);
                    try
                    {
                        if (!_audioFormatLogged)
                        {
                            _audioFormatLogged = true;
                            ProjectionNdiDiagnostics.Log("NDI audio send format: interleaved16s, referenceLevel=0");
                        }

                        var audioFrameNative = new NativeNdiInterop.NDIlib_audio_frame_interleaved_16s_t
                        {
                            sample_rate = sampleRate,
                            no_channels = channelCount,
                            no_samples = samplesPerChannel,
                            timecode = NativeNdiInterop.NDIlib_send_timecode_synthesize,
                            reference_level = 0,
                            p_data = handle.AddrOfPinnedObject(),
                        };

                        NativeNdiInterop.SendSendAudioInterleaved16s(_senderHandle, ref audioFrameNative);
                    }
                    finally
                    {
                        handle.Free();
                    }
                }

                return true;
            }
            catch
            {
                ProjectionNdiDiagnostics.Log("SendAudio failed: unknown exception.");
                return false;
            }
        }

        public void Stop()
        {
            lock (_sync)
            {
                FreeAsyncBuffers();
            }

            if (_senderHandle != IntPtr.Zero)
            {
                try
                {
                    NativeNdiInterop.SendDestroy(_senderHandle);
                    ProjectionNdiDiagnostics.Log("Sender stopped.");
                }
                catch
                {
                }
                finally
                {
                    _senderHandle = IntPtr.Zero;
                }
            }

            ReleaseNdi();
        }

        public int GetConnectionCount()
        {
            if (_senderHandle == IntPtr.Zero)
            {
                if (_lastConnectionCount != 0)
                {
                    _lastConnectionCount = 0;
                    ProjectionNdiDiagnostics.Log("NDI connections: 0 (sender not running)");
                }
                return 0;
            }

            try
            {
                int count = Math.Max(0, NativeNdiInterop.SendGetNoConnections(_senderHandle, 0));
                if (count != _lastConnectionCount)
                {
                    _lastConnectionCount = count;
                    ProjectionNdiDiagnostics.Log($"NDI connections changed: {count}");
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }

        private void EnsureAsyncBuffers(int requiredSize)
        {
            if (requiredSize <= 0)
            {
                requiredSize = 1;
            }

            if (_asyncBufferSize >= requiredSize && _asyncBuffers[0] != IntPtr.Zero)
            {
                return;
            }

            FreeAsyncBuffers();

            for (int i = 0; i < AsyncBufferCount; i++)
            {
                _asyncBuffers[i] = Marshal.AllocHGlobal(requiredSize);
            }

            _asyncBufferSize = requiredSize;
            _asyncBufferIndex = 0;
        }

        private void FreeAsyncBuffers()
        {
            for (int i = 0; i < AsyncBufferCount; i++)
            {
                if (_asyncBuffers[i] != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_asyncBuffers[i]);
                    _asyncBuffers[i] = IntPtr.Zero;
                }
            }

            _asyncBufferSize = 0;
            _asyncBufferIndex = 0;
        }

        private static bool TryInitializeNdi()
        {
            lock (InitLock)
            {
                if (_ndiInitRefCount > 0)
                {
                    _ndiInitRefCount++;
                    return _ndiAvailable;
                }

                try
                {
                    _ndiAvailable = NativeNdiInterop.Initialize();
                    _ndiInitRefCount = _ndiAvailable ? 1 : 0;
                    ProjectionNdiDiagnostics.Log(_ndiAvailable
                        ? "NDI initialize success."
                        : "NDI initialize returned false.");
                    return _ndiAvailable;
                }
                catch (DllNotFoundException)
                {
                    _ndiAvailable = false;
                    _ndiInitRefCount = 0;
                    ProjectionNdiDiagnostics.Log("NDI initialize failed: DLL not found.");
                    return false;
                }
                catch (EntryPointNotFoundException)
                {
                    _ndiAvailable = false;
                    _ndiInitRefCount = 0;
                    ProjectionNdiDiagnostics.Log("NDI initialize failed: entry point not found.");
                    return false;
                }
                catch (BadImageFormatException)
                {
                    _ndiAvailable = false;
                    _ndiInitRefCount = 0;
                    ProjectionNdiDiagnostics.Log("NDI initialize failed: bad image format.");
                    return false;
                }
                catch (Exception ex)
                {
                    _ndiAvailable = false;
                    _ndiInitRefCount = 0;
                    ProjectionNdiDiagnostics.LogException("NDI initialize exception", ex);
                    return false;
                }
            }
        }

        private static void ReleaseNdi()
        {
            lock (InitLock)
            {
                if (_ndiInitRefCount <= 0)
                {
                    return;
                }

                _ndiInitRefCount--;
                if (_ndiInitRefCount == 0 && _ndiAvailable)
                {
                    try
                    {
                        NativeNdiInterop.Destroy();
                        ProjectionNdiDiagnostics.Log("NDI runtime destroyed.");
                    }
                    catch
                    {
                    }
                    finally
                    {
                        _ndiAvailable = false;
                    }
                }
            }
        }

        private static IntPtr AllocUtf8(string text)
        {
            string value = text ?? string.Empty;
            byte[] bytes = Encoding.UTF8.GetBytes(value + '\0');
            IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            return ptr;
        }

        private short[] EnsureInterleavedAudioBuffer(int requiredLength)
        {
            if (_audioInterleaved16Buffer.Length == requiredLength)
            {
                return _audioInterleaved16Buffer;
            }

            _audioInterleaved16Buffer = new short[Math.Max(1, requiredLength)];
            return _audioInterleaved16Buffer;
        }

        private static void ConvertPlanarFloatToInterleaved16(float[] planar, short[] interleaved, int channels, int samplesPerChannel)
        {
            int writeIndex = 0;
            for (int sample = 0; sample < samplesPerChannel; sample++)
            {
                for (int channel = 0; channel < channels; channel++)
                {
                    float value = planar[channel * samplesPerChannel + sample];
                    value = Math.Clamp(value, -1f, 1f);
                    int pcm = (int)Math.Round(value * 32767f);
                    interleaved[writeIndex++] = (short)Math.Clamp(pcm, short.MinValue, short.MaxValue);
                }
            }
        }
    }
}
