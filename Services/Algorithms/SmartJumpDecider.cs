using System;
using ImageColorChanger.Utils;

namespace ImageColorChanger.Services.Algorithms
{
    /// <summary>
    /// 智能跳转判断算法
    /// 参考Python版本：LOGIC_ANALYSIS_03 行398-459
    /// </summary>
    public static class SmartJumpDecider
    {
        /// <summary>
        /// 跳转决策结果
        /// </summary>
        public enum JumpDecision
        {
            /// <summary>正常播放下一帧</summary>
            PlayNext,

            /// <summary>回跳到循环开始</summary>
            JumpToLoopStart,

            /// <summary>时间不足，继续等待</summary>
            WaitForTime,

            /// <summary>播放结束</summary>
            EndPlayback
        }

        /// <summary>
        /// 判断是否需要回跳
        /// </summary>
        /// <param name="currentIndex">当前关键帧索引</param>
        /// <param name="totalCount">总关键帧数量</param>
        /// <param name="loopStartIndex">循环开始索引（如果有循环标记）</param>
        /// <returns>跳转决策</returns>
        public static JumpDecision DecideJump(int currentIndex, int totalCount, int? loopStartIndex = null)
        {
            // 检测是否到达最后一帧
            if (currentIndex >= totalCount - 1)
            {
                // 如果有循环标记，回跳到循环开始
                if (loopStartIndex.HasValue && loopStartIndex.Value >= 0)
                {
                    Logger.Debug("检测到循环，回跳: currentIndex={Current}, loopStart={LoopStart}",
                        currentIndex, loopStartIndex.Value);
                    return JumpDecision.JumpToLoopStart;
                }

                // 否则结束播放
                Logger.Debug("到达最后一帧，准备结束播放");
                return JumpDecision.EndPlayback;
            }

            // 正常播放下一帧
            return JumpDecision.PlayNext;
        }

        /// <summary>
        /// 判断时间是否充足（用于决定是否可以跳转）
        /// </summary>
        /// <param name="elapsedTime">已过去的时间（秒）</param>
        /// <param name="requiredTime">需要的时间（秒）</param>
        /// <param name="tolerance">容差（秒，默认0.05秒）</param>
        /// <returns>时间是否充足</returns>
        public static bool IsTimeEnough(double elapsedTime, double requiredTime, double tolerance = 0.05)
        {
            // 如果已过去的时间 >= 需要的时间 - 容差，则认为时间充足
            return elapsedTime >= (requiredTime - tolerance);
        }

        /// <summary>
        /// 计算剩余等待时间
        /// </summary>
        /// <param name="elapsedTime">已过去的时间（秒）</param>
        /// <param name="requiredTime">需要的时间（秒）</param>
        /// <returns>剩余等待时间（秒）</returns>
        public static double CalculateRemainingTime(double elapsedTime, double requiredTime)
        {
            var remaining = requiredTime - elapsedTime;
            return remaining > 0 ? remaining : 0;
        }
    }
}

