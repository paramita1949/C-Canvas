using System;

namespace ImageColorChanger.Services.Auth
{
    /// <summary>
    /// 认证时钟：基于服务器时间 + TickCount64 估算当前服务器时间，
    /// 并在系统重启导致 TickCount 重置时提供自愈基准。
    /// </summary>
    internal sealed class AuthClock
    {
        internal readonly struct EstimateResult
        {
            public EstimateResult(
                DateTime estimatedServerTime,
                DateTime? updatedLastServerTime,
                DateTime? updatedLastLocalTime,
                long updatedLastTickCount,
                bool tickResetRecovered,
                bool localTimeRolledBack,
                bool localTickSkewDetected,
                TimeSpan elapsedByTick,
                double localTickSkewSeconds)
            {
                EstimatedServerTime = estimatedServerTime;
                UpdatedLastServerTime = updatedLastServerTime;
                UpdatedLastLocalTime = updatedLastLocalTime;
                UpdatedLastTickCount = updatedLastTickCount;
                TickResetRecovered = tickResetRecovered;
                LocalTimeRolledBack = localTimeRolledBack;
                LocalTickSkewDetected = localTickSkewDetected;
                ElapsedByTick = elapsedByTick;
                LocalTickSkewSeconds = localTickSkewSeconds;
            }

            public DateTime EstimatedServerTime { get; }
            public DateTime? UpdatedLastServerTime { get; }
            public DateTime? UpdatedLastLocalTime { get; }
            public long UpdatedLastTickCount { get; }
            public bool TickResetRecovered { get; }
            public bool LocalTimeRolledBack { get; }
            public bool LocalTickSkewDetected { get; }
            public TimeSpan ElapsedByTick { get; }
            public double LocalTickSkewSeconds { get; }
        }

        public EstimateResult Estimate(
            DateTime? lastServerTime,
            DateTime? lastLocalTime,
            long lastTickCount,
            long currentTick,
            DateTime now,
            double localTickSkewThresholdSeconds = 600)
        {
            if (lastServerTime == null)
            {
                return new EstimateResult(
                    estimatedServerTime: now,
                    updatedLastServerTime: null,
                    updatedLastLocalTime: lastLocalTime,
                    updatedLastTickCount: lastTickCount,
                    tickResetRecovered: false,
                    localTimeRolledBack: false,
                    localTickSkewDetected: false,
                    elapsedByTick: TimeSpan.Zero,
                    localTickSkewSeconds: 0);
            }

            long elapsedMilliseconds = currentTick - lastTickCount;
            if (elapsedMilliseconds < 0)
            {
                bool localTimeRolledBack = false;
                DateTime recoveredServerTime;
                if (lastLocalTime != null)
                {
                    var localElapsed = now - lastLocalTime.Value;
                    if (localElapsed.TotalSeconds < 0)
                    {
                        localElapsed = TimeSpan.Zero;
                        localTimeRolledBack = true;
                    }
                    recoveredServerTime = lastServerTime.Value + localElapsed;
                }
                else
                {
                    recoveredServerTime = now;
                }

                return new EstimateResult(
                    estimatedServerTime: recoveredServerTime,
                    updatedLastServerTime: recoveredServerTime,
                    updatedLastLocalTime: now,
                    updatedLastTickCount: currentTick,
                    tickResetRecovered: true,
                    localTimeRolledBack: localTimeRolledBack,
                    localTickSkewDetected: false,
                    elapsedByTick: TimeSpan.Zero,
                    localTickSkewSeconds: 0);
            }

            var elapsedByTick = TimeSpan.FromMilliseconds(elapsedMilliseconds);
            var estimated = lastServerTime.Value + elapsedByTick;
            bool skewDetected = false;
            double skewSeconds = 0;

            if (lastLocalTime != null)
            {
                var localElapsed = now - lastLocalTime.Value;
                skewSeconds = Math.Abs((localElapsed - elapsedByTick).TotalSeconds);
                skewDetected = skewSeconds > localTickSkewThresholdSeconds;
            }

            return new EstimateResult(
                estimatedServerTime: estimated,
                updatedLastServerTime: lastServerTime,
                updatedLastLocalTime: lastLocalTime,
                updatedLastTickCount: lastTickCount,
                tickResetRecovered: false,
                localTimeRolledBack: false,
                localTickSkewDetected: skewDetected,
                elapsedByTick: elapsedByTick,
                localTickSkewSeconds: skewSeconds);
        }
    }
}
