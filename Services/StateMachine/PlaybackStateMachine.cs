using System;
using System.Collections.Generic;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Utils;

namespace ImageColorChanger.Services.StateMachine
{
    /// <summary>
    /// 播放状态机
    /// 管理播放状态转换，确保状态转换的合法性
    /// </summary>
    public class PlaybackStateMachine
    {
        /// <summary>
        /// 当前播放状态
        /// </summary>
        public PlaybackStatus CurrentStatus { get; private set; } = PlaybackStatus.Idle;

        /// <summary>
        /// 状态变化事件
        /// </summary>
        public event EventHandler<PlaybackStatusChangedEventArgs> StatusChanged;

        /// <summary>
        /// 定义合法的状态转换规则
        /// </summary>
        private readonly Dictionary<PlaybackStatus, HashSet<PlaybackStatus>> _validTransitions = new()
        {
            // Idle可以转换到：Recording（开始录制）、Playing（开始播放）
            [PlaybackStatus.Idle] = new HashSet<PlaybackStatus> 
            { 
                PlaybackStatus.Recording, 
                PlaybackStatus.Playing 
            },

            // Recording可以转换到：Idle（停止录制）
            [PlaybackStatus.Recording] = new HashSet<PlaybackStatus> 
            { 
                PlaybackStatus.Idle 
            },

            // Playing可以转换到：Paused（暂停）、Stopped（停止）、Idle（播放完成）
            [PlaybackStatus.Playing] = new HashSet<PlaybackStatus> 
            { 
                PlaybackStatus.Paused, 
                PlaybackStatus.Stopped,
                PlaybackStatus.Idle 
            },

            // Paused可以转换到：Playing（继续播放）、Stopped（停止）
            [PlaybackStatus.Paused] = new HashSet<PlaybackStatus> 
            { 
                PlaybackStatus.Playing, 
                PlaybackStatus.Stopped 
            },

            // Stopped可以转换到：Idle（回到空闲）、Playing（重新播放）
            [PlaybackStatus.Stopped] = new HashSet<PlaybackStatus> 
            { 
                PlaybackStatus.Idle,
                PlaybackStatus.Playing 
            }
        };

        /// <summary>
        /// 尝试转换状态
        /// </summary>
        /// <param name="newStatus">目标状态</param>
        /// <returns>是否转换成功</returns>
        public bool TryTransition(PlaybackStatus newStatus)
        {
            if (!CanTransition(newStatus))
            {
                Logger.Warning("非法的状态转换: {CurrentStatus} -> {NewStatus}", CurrentStatus, newStatus);
                return false;
            }

            var oldStatus = CurrentStatus;
            CurrentStatus = newStatus;

            Logger.Info("状态转换: {OldStatus} -> {NewStatus}", oldStatus, newStatus);

            // 触发状态变化事件
            StatusChanged?.Invoke(this, new PlaybackStatusChangedEventArgs(oldStatus, newStatus));

            return true;
        }

        /// <summary>
        /// 检查是否可以转换到目标状态
        /// </summary>
        /// <param name="newStatus">目标状态</param>
        /// <returns>是否可以转换</returns>
        public bool CanTransition(PlaybackStatus newStatus)
        {
            // 相同状态视为允许（幂等操作）
            if (CurrentStatus == newStatus)
                return true;

            // 检查转换规则
            if (_validTransitions.TryGetValue(CurrentStatus, out var allowedTransitions))
            {
                return allowedTransitions.Contains(newStatus);
            }

            return false;
        }

        /// <summary>
        /// 强制设置状态（仅用于紧急情况，不触发事件）
        /// </summary>
        /// <param name="status">要设置的状态</param>
        public void ForceSetStatus(PlaybackStatus status)
        {
            Logger.Warning("强制设置状态: {OldStatus} -> {NewStatus}", CurrentStatus, status);
            CurrentStatus = status;
        }

        /// <summary>
        /// 重置到空闲状态
        /// </summary>
        public void Reset()
        {
            if (CurrentStatus != PlaybackStatus.Idle)
            {
                TryTransition(PlaybackStatus.Idle);
            }
        }
    }

    /// <summary>
    /// 播放状态变化事件参数
    /// </summary>
    public class PlaybackStatusChangedEventArgs : EventArgs
    {
        public PlaybackStatus OldStatus { get; }
        public PlaybackStatus NewStatus { get; }

        public PlaybackStatusChangedEventArgs(PlaybackStatus oldStatus, PlaybackStatus newStatus)
        {
            OldStatus = oldStatus;
            NewStatus = newStatus;
        }
    }
}

