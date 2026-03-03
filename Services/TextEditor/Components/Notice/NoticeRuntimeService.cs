using System;
using System.Collections.Generic;

namespace ImageColorChanger.Services.TextEditor.Components.Notice
{
    public sealed class NoticeRuntimeService
    {
        private readonly object _syncRoot = new();
        private readonly Dictionary<int, NoticeRuntimeState> _states = new();

        public NoticeRuntimeState GetOrCreateState(int textElementId, long nowMs)
        {
            if (textElementId <= 0)
            {
                return new NoticeRuntimeState { StartTimestampMs = nowMs };
            }

            lock (_syncRoot)
            {
                if (_states.TryGetValue(textElementId, out var existing))
                {
                    return existing;
                }

                var created = new NoticeRuntimeState
                {
                    StartTimestampMs = nowMs,
                    IsManuallyClosed = false,
                    IsAutoPausedByTimeout = false,
                    HasLastOffset = false,
                    LastOffsetX = 0,
                    LastDebugLogTimestampMs = 0
                };
                _states[textElementId] = created;
                return created;
            }
        }

        public void MarkManuallyClosed(int textElementId)
        {
            if (textElementId <= 0)
            {
                return;
            }

            lock (_syncRoot)
            {
                Pause(textElementId, GetNowMsUnsafe());
            }
        }

        public void Pause(int textElementId, long nowMs)
        {
            if (textElementId <= 0)
            {
                return;
            }

            lock (_syncRoot)
            {
                var state = GetOrCreateState(textElementId, nowMs);
                state.PausedElapsedMs = Math.Max(0, nowMs - state.StartTimestampMs);
                state.IsManuallyClosed = true;
                state.IsAutoPausedByTimeout = false;
                state.HasLastOffset = false;
            }
        }

        public void Resume(int textElementId, long nowMs)
        {
            if (textElementId <= 0)
            {
                return;
            }

            lock (_syncRoot)
            {
                var state = GetOrCreateState(textElementId, nowMs);
                long elapsed = Math.Max(0, state.PausedElapsedMs);
                state.StartTimestampMs = Math.Max(0, nowMs - elapsed);
                state.IsManuallyClosed = false;
                state.IsAutoPausedByTimeout = false;
                state.HasLastOffset = false;
            }
        }

        public void Reopen(int textElementId, long nowMs)
        {
            if (textElementId <= 0)
            {
                return;
            }

            lock (_syncRoot)
            {
                var state = GetOrCreateState(textElementId, nowMs);
                state.IsManuallyClosed = false;
                state.IsAutoPausedByTimeout = false;
                state.StartTimestampMs = nowMs;
                state.PausedElapsedMs = 0;
                state.HasLastOffset = false;
                state.LastOffsetX = 0;
                state.LastDebugLogTimestampMs = 0;
            }
        }

        public void RemoveState(int textElementId)
        {
            if (textElementId <= 0)
            {
                return;
            }

            lock (_syncRoot)
            {
                _states.Remove(textElementId);
            }
        }

        public void Clear()
        {
            lock (_syncRoot)
            {
                _states.Clear();
            }
        }

        public double GetOffset(long elapsedMs, int speed, NoticeDirection direction)
        {
            int clampedSpeed = Math.Clamp(speed, 0, 100);
            double pixelsPerSecond = 10 + (clampedSpeed * 1.2);
            double delta = pixelsPerSecond * (elapsedMs / 1000.0);
            return direction == NoticeDirection.RightToLeft ? -delta : delta;
        }

        public double GetLoopingOffset(long elapsedMs, int speed, NoticeDirection direction, double cycleWidth)
        {
            return GetLoopingOffset(
                elapsedMs,
                speed,
                direction,
                viewportWidth: cycleWidth,
                contentWidth: cycleWidth,
                laneStartX: 0,
                laneEndX: 0,
                initialContentX: 0);
        }

        public double GetLoopingOffset(long elapsedMs, int speed, NoticeDirection direction, double viewportWidth, double contentWidth)
        {
            return GetLoopingOffset(
                elapsedMs,
                speed,
                direction,
                viewportWidth,
                contentWidth,
                laneStartX: 0,
                laneEndX: 0,
                initialContentX: 0);
        }

        public double GetLoopingOffset(
            long elapsedMs,
            int speed,
            NoticeDirection direction,
            double viewportWidth,
            double contentWidth,
            double contentStartX)
        {
            return GetLoopingOffset(
                elapsedMs,
                speed,
                direction,
                viewportWidth,
                contentWidth,
                laneStartX: 0,
                laneEndX: 0,
                initialContentX: contentStartX);
        }

        public double GetLoopingOffset(
            long elapsedMs,
            int speed,
            NoticeDirection direction,
            double viewportWidth,
            double contentWidth,
            double contentStartX,
            double contentEndX)
        {
            // 兼容旧调用：contentStartX 视作初始位置；仅传入右侧轨道内边距。
            return GetLoopingOffset(
                elapsedMs,
                speed,
                direction,
                viewportWidth,
                contentWidth,
                laneStartX: 0,
                laneEndX: contentEndX,
                initialContentX: contentStartX);
        }

        public double GetLoopingOffset(
            long elapsedMs,
            int speed,
            NoticeDirection direction,
            double viewportWidth,
            double contentWidth,
            double laneStartX,
            double laneEndX,
            double initialContentX)
        {
            double viewport = Math.Max(1, viewportWidth);
            double content = Math.Max(1, contentWidth);
            double laneStart = Math.Max(0, laneStartX);
            double laneEnd = Math.Max(0, laneEndX);
            double laneRight = Math.Max(laneStart + 1, viewport - laneEnd);
            double laneWidth = Math.Max(1, laneRight - laneStart);
            double startX = double.IsNaN(initialContentX) || double.IsInfinity(initialContentX)
                ? laneStart
                : initialContentX;
            double delta = Math.Abs(GetOffset(elapsedMs, speed, direction));

            if (direction == NoticeDirection.PingPong)
            {
                if (content <= laneWidth)
                {
                    // 文本不超过轨道：左边界与右边界间往返。
                    double minX = laneStart;
                    double maxX = laneRight - content;
                    double travel = Math.Max(1, maxX - minX);
                    double positionX = minX + ComputeTriangleStep(delta, travel);
                    return positionX - startX;
                }

                // 文本宽于轨道：在 [laneRight-content, laneStart] 区间往返（扫描式）。
                double minWideX = laneRight - content;
                double maxWideX = laneStart;
                double wideTravel = Math.Max(1, maxWideX - minWideX);
                double wideStep = ComputeTriangleStep(delta, wideTravel);
                double widePositionX = maxWideX - wideStep;
                return widePositionX - startX;
            }

            if (direction == NoticeDirection.RightToLeft)
            {
                // 文本右边缘触达轨道左边界后即回卷，无等待空白。
                double leaveLeftX = laneStart - content;
                double travel = Math.Max(1, startX - leaveLeftX);
                double positionX = startX - (delta % travel);
                return positionX - startX;
            }

            // L->R：文本尾部触达轨道右边界后即回卷，无等待空白。
            double rightWallX = laneRight - content;
            double ltrTravel = rightWallX - startX;
            if (ltrTravel <= 1)
            {
                // 长文本兜底：保持移动，不锁死在 0~几像素。
                ltrTravel = Math.Max(1, laneWidth);
            }

            double ltrPositionX = startX + (delta % ltrTravel);
            return ltrPositionX - startX;
        }

        public bool IsExpired(long elapsedMs, int durationMinutes, bool autoClose)
        {
            if (!autoClose)
            {
                return false;
            }

            int clampedMinutes = Math.Clamp(durationMinutes, 1, 10);
            long maxDurationMs = clampedMinutes * 60_000L;
            return elapsedMs >= maxDurationMs;
        }

        private static long GetNowMsUnsafe()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private static double ComputeTriangleStep(double delta, double travel)
        {
            if (travel <= 1)
            {
                return 0;
            }

            double cycle = travel * 2.0;
            double phase = delta % cycle;
            return phase <= travel ? phase : (cycle - phase);
        }
    }
}
