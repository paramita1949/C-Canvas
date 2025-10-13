using System;

namespace ImageColorChanger.Services.Algorithms
{
    /// <summary>
    /// 播放次数判断算法
    /// 参考Python版本：LOGIC_ANALYSIS_03 行335-366
    /// </summary>
    public static class PlayCountJudge
    {
        /// <summary>
        /// 播放次数判断结果
        /// </summary>
        public enum JudgeResult
        {
            /// <summary>无限循环（playCount = -1）</summary>
            InfiniteLoop,

            /// <summary>未完成（currentCount < playCount）</summary>
            NotCompleted,

            /// <summary>已完成（currentCount >= playCount）</summary>
            Completed
        }

        /// <summary>
        /// 判断播放次数状态
        /// </summary>
        /// <param name="playCount">设定的播放次数（-1表示无限循环）</param>
        /// <param name="completedCount">已完成的播放次数</param>
        /// <returns>判断结果</returns>
        public static JudgeResult Judge(int playCount, int completedCount)
        {
            // 分支1: 无限循环模式
            if (playCount == -1)
            {
                return JudgeResult.InfiniteLoop;
            }

            // 分支2: 未完成
            if (completedCount < playCount)
            {
                return JudgeResult.NotCompleted;
            }

            // 分支3: 已完成
            return JudgeResult.Completed;
        }

        /// <summary>
        /// 判断是否应该继续播放
        /// </summary>
        /// <param name="playCount">设定的播放次数</param>
        /// <param name="completedCount">已完成的播放次数</param>
        /// <returns>是否应该继续播放</returns>
        public static bool ShouldContinue(int playCount, int completedCount)
        {
            var result = Judge(playCount, completedCount);
            return result == JudgeResult.InfiniteLoop || result == JudgeResult.NotCompleted;
        }
    }
}

