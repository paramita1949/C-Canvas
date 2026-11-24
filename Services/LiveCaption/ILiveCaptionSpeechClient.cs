using System;
using System.Threading;
using System.Threading.Tasks;

namespace ImageColorChanger.Services.LiveCaption
{
    /// <summary>实时/分段ASR传输边界。实现负责平台协议，消费方只接收文字。</summary>
    internal interface ILiveCaptionSpeechClient : IDisposable
    {
        string AsrProvider { get; }
        string AsrModel { get; }
        bool IsReady { get; }
        bool SupportsRealtimeSession { get; }
        bool IsRealtimeConnected { get; }
        string LastError { get; }
        string LastTranscribeUrl { get; }
        int LastTranscribeStatusCode { get; }
        long LastTranscribeElapsedMs { get; }
        LiveCaptionSpeechAudioProfile AudioProfile { get; }
        long ReceiveIdleMilliseconds { get; }
        bool IsReceiveStalled(TimeSpan threshold);
        Task<string> TranscribeAudioAsync(byte[] wavBytes, CancellationToken cancellationToken);
        Task<bool> StartRealtimeSessionAsync(Action<LiveCaptionAsrText> onText, Action<string> onStatus, CancellationToken cancellationToken);
        Task<bool> SendRealtimeAudioAsync(byte[] pcm16kMono, CancellationToken cancellationToken);
        Task StopRealtimeSessionAsync(CancellationToken cancellationToken);
    }

    internal sealed record LiveCaptionSpeechAudioProfile(int TickMilliseconds, int PacketBytes, int MaxQueueBytes,
        int KeepAliveMilliseconds = 0, bool EnableStallRecovery = false)
    {
        private static readonly LiveCaptionSpeechAudioProfile Baidu = new(100, 3200, 3200 * 40, 800);
        private static readonly LiveCaptionSpeechAudioProfile Doubao = new(200, 6400, 6400 * 30, 300);
        private static readonly LiveCaptionSpeechAudioProfile FunAsr = new(100, 3200, 3200 * 40);
        private static readonly LiveCaptionSpeechAudioProfile Xfyun = new(40, 1280, 1280 * 100, 800, true);
        private static readonly LiveCaptionSpeechAudioProfile Default = new(40, 1280, 1280 * 100);

        public static LiveCaptionSpeechAudioProfile ForProvider(string provider) => provider?.Trim().ToLowerInvariant() switch
        {
            "baidu" => Baidu, "doubao" => Doubao, "funasr" => FunAsr, "xfyun" => Xfyun, _ => Default
        };
    }
}
