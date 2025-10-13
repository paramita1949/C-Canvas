using System;
using ImageColorChanger.Utils;

namespace ImageColorChanger.Services.Algorithms
{
    /// <summary>
    /// 暂停时间累加算法
    /// 参考Python版本：LOGIC_ANALYSIS_03 行565-625
    /// </summary>
    public static class PauseTimeAccumulator
    {
        /// <summary>
        /// 计算暂停时长（当前时间 - 暂停开始时间）
        /// </summary>
        /// <param name="pauseStartTime">暂停开始时间（秒）</param>
        /// <param name="currentTime">当前时间（秒）</param>
        /// <returns>暂停时长（秒）</returns>
        public static double CalculatePauseDuration(double pauseStartTime, double currentTime)
        {
            var duration = currentTime - pauseStartTime;

            // 边界值检查
            if (duration < 0)
            {
                Logger.Warning("暂停时长计算异常: pauseStartTime={PauseStart}, currentTime={Current}, duration={Duration}",
                    pauseStartTime, currentTime, duration);
                return 0;
            }

            return duration;
        }

        /// <summary>
        /// 将暂停时间累加到指定的总暂停时间
        /// </summary>
        /// <param name="totalPausedTime">总暂停时间</param>
        /// <param name="pauseDuration">本次暂停时长</param>
        /// <returns>累加后的总暂停时间</returns>
        public static double Accumulate(double totalPausedTime, double pauseDuration)
        {
            if (pauseDuration < 0)
            {
                Logger.Warning("暂停时长为负值: {Duration}，忽略累加", pauseDuration);
                return totalPausedTime;
            }

            return totalPausedTime + pauseDuration;
        }

        /// <summary>
        /// 计算有效时间（总时间 - 暂停时间）
        /// </summary>
        /// <param name="totalTime">总时间</param>
        /// <param name="pausedTime">暂停时间</param>
        /// <returns>有效时间</returns>
        public static double CalculateEffectiveTime(double totalTime, double pausedTime)
        {
            var effectiveTime = totalTime - pausedTime;

            if (effectiveTime < 0)
            {
                Logger.Warning("有效时间为负: totalTime={Total}, pausedTime={Paused}, effective={Effective}",
                    totalTime, pausedTime, effectiveTime);
                return 0;
            }

            return effectiveTime;
        }
    }
}

