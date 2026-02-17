using System;

namespace ImageColorChanger.Services.Auth
{
    internal sealed class AuthHeartbeatScheduler : IDisposable
    {
        private System.Threading.Timer _heartbeatTimer;
        private System.Threading.Timer _noticeHeartbeatTimer;

        public void Start(
            System.Threading.TimerCallback heartbeatCallback,
            System.Threading.TimerCallback noticeHeartbeatCallback,
            TimeSpan heartbeatInterval,
            TimeSpan noticeInterval)
        {
            StopAll();

            _heartbeatTimer = new System.Threading.Timer(
                heartbeatCallback,
                null,
                heartbeatInterval,
                heartbeatInterval);

            _noticeHeartbeatTimer = new System.Threading.Timer(
                noticeHeartbeatCallback,
                null,
                TimeSpan.Zero,
                noticeInterval);
        }

        public void StopNoticeHeartbeat()
        {
            _noticeHeartbeatTimer?.Dispose();
            _noticeHeartbeatTimer = null;
        }

        public void StopAll()
        {
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
            StopNoticeHeartbeat();
        }

        public void Dispose()
        {
            StopAll();
        }
    }
}
