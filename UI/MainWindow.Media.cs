using System;
using System.Windows;
using System.Windows.Controls;
using ImageColorChanger.Managers;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 的媒体播放功能部分
    /// </summary>
    public partial class MainWindow : Window
    {
        #region 媒体播放器事件

        private void BtnMediaPrev_Click(object sender, RoutedEventArgs e)
        {
            if (_videoPlayerManager == null && !EnsureVideoPlayerInitialized("BtnMediaPrev_Click")) return;
            
            // 防抖动：防止重复点击
            var now = DateTime.Now;
            if ((now - _lastMediaPrevClickTime).TotalMilliseconds < BUTTON_DEBOUNCE_MILLISECONDS)
            {
                //System.Diagnostics.Debug.WriteLine(" 上一首按钮防抖动，忽略重复点击");
                return;
            }
            _lastMediaPrevClickTime = now;
            
            _videoPlayerManager.PlayPrevious();
        }

        private void BtnMediaPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_videoPlayerManager == null && !EnsureVideoPlayerInitialized("BtnMediaPlayPause_Click")) return;
            
            if (_videoPlayerManager.IsPlaying && !_videoPlayerManager.IsPaused)
            {
                _videoPlayerManager.Pause();
            }
            else
            {
                _videoPlayerManager.Play();
            }
        }

        private void BtnMediaNext_Click(object sender, RoutedEventArgs e)
        {
            if (_videoPlayerManager == null && !EnsureVideoPlayerInitialized("BtnMediaNext_Click")) return;
            
            // 防抖动：防止重复点击
            var now = DateTime.Now;
            if ((now - _lastMediaNextClickTime).TotalMilliseconds < BUTTON_DEBOUNCE_MILLISECONDS)
            {
                //System.Diagnostics.Debug.WriteLine(" 下一首按钮防抖动，忽略重复点击");
                return;
            }
            _lastMediaNextClickTime = now;
            
            _videoPlayerManager.PlayNext();
        }

        private void MediaProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_videoPlayerManager == null && !EnsureVideoPlayerInitialized("MediaProgressSlider_ValueChanged")) return;
            if (_isUpdatingProgress) return;
            
            float position = (float)(e.NewValue / 100.0);
            _videoPlayerManager.SetPosition(position);
        }

        private void BtnPlayMode_Click(object sender, RoutedEventArgs e)
        {
            if (_videoPlayerManager == null && !EnsureVideoPlayerInitialized("BtnPlayMode_Click")) return;
            
            // 防抖动：防止重复点击
            var now = DateTime.Now;
            if ((now - _lastPlayModeClickTime).TotalMilliseconds < BUTTON_DEBOUNCE_MILLISECONDS)
            {
                //System.Diagnostics.Debug.WriteLine(" 播放模式按钮防抖动，忽略重复点击");
                return;
            }
            _lastPlayModeClickTime = now;
            
            // 只在用户需要的三种模式之间循环。
            var currentMode = _videoPlayerManager.CurrentPlayMode;
            PlayMode nextMode = currentMode switch
            {
                PlayMode.Random => PlayMode.LoopOne,
                PlayMode.LoopOne => PlayMode.LoopAll,
                _ => PlayMode.Random
            };

            ApplyMediaPlayMode(nextMode);
        }

        private void MediaPlayModeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem ||
                !Enum.TryParse(menuItem.Tag?.ToString(), out PlayMode mode))
            {
                return;
            }

            if (_videoPlayerManager == null && !EnsureVideoPlayerInitialized("MediaPlayModeMenuItem_Click")) return;
            ApplyMediaPlayMode(mode);
        }

        private void ApplyMediaPlayMode(PlayMode mode)
        {
            _videoPlayerManager.SetPlayMode(mode);
            SetMediaPlayModeButtonContent(mode);

            string modeName = mode switch
            {
                PlayMode.LoopOne => "单曲循环",
                PlayMode.LoopAll => "歌单循环",
                _ => "随机播放"
            };

            BtnPlayMode.ToolTip = $"播放模式：{modeName}（左键切换，右键选择）";
            if (MenuMediaPlayModeRandom != null) MenuMediaPlayModeRandom.IsChecked = mode == PlayMode.Random;
            if (MenuMediaPlayModeLoopOne != null) MenuMediaPlayModeLoopOne.IsChecked = mode == PlayMode.LoopOne;
            if (MenuMediaPlayModeLoopAll != null) MenuMediaPlayModeLoopAll.IsChecked = mode == PlayMode.LoopAll;
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_videoPlayerManager == null && !EnsureVideoPlayerInitialized("VolumeSlider_ValueChanged")) return;
            
            int volume = (int)e.NewValue;
            _videoPlayerManager.SetVolume(volume);
        }

        #endregion
    }
}


