using System;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Services.Implementations;
using ImageColorChanger.Services.Interfaces;

namespace ImageColorChanger.Services
{
    /// <summary>
    /// 播放服务工厂
    /// 根据播放模式返回对应的录制/播放服务
    /// </summary>
    public class PlaybackServiceFactory
    {
        private readonly KeyframeRecordingService _keyframeRecording;
        private readonly KeyframePlaybackService _keyframePlayback;
        private readonly OriginalRecordingService _originalRecording;
        private readonly OriginalPlaybackService _originalPlayback;

        public PlaybackServiceFactory(
            KeyframeRecordingService keyframeRecording,
            KeyframePlaybackService keyframePlayback,
            OriginalRecordingService originalRecording,
            OriginalPlaybackService originalPlayback)
        {
            _keyframeRecording = keyframeRecording ?? throw new ArgumentNullException(nameof(keyframeRecording));
            _keyframePlayback = keyframePlayback ?? throw new ArgumentNullException(nameof(keyframePlayback));
            _originalRecording = originalRecording ?? throw new ArgumentNullException(nameof(originalRecording));
            _originalPlayback = originalPlayback ?? throw new ArgumentNullException(nameof(originalPlayback));
        }

        /// <summary>
        /// 获取录制服务
        /// </summary>
        public IRecordingService GetRecordingService(PlaybackMode mode)
        {
            return mode switch
            {
                PlaybackMode.Keyframe => _keyframeRecording,
                PlaybackMode.Original => _originalRecording,
                _ => throw new ArgumentException($"不支持的播放模式: {mode}", nameof(mode))
            };
        }

        /// <summary>
        /// 获取播放服务
        /// </summary>
        public IPlaybackService GetPlaybackService(PlaybackMode mode)
        {
            return mode switch
            {
                PlaybackMode.Keyframe => _keyframePlayback,
                PlaybackMode.Original => _originalPlayback,
                _ => throw new ArgumentException($"不支持的播放模式: {mode}", nameof(mode))
            };
        }
    }
}

