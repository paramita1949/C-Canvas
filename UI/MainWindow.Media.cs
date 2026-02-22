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
                //System.Diagnostics.Debug.WriteLine("⚠️ 上一首按钮防抖动，忽略重复点击");
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
                //System.Diagnostics.Debug.WriteLine("⚠️ 下一首按钮防抖动，忽略重复点击");
                return;
            }
            _lastMediaNextClickTime = now;
            
            _videoPlayerManager.PlayNext();
        }

        private void BtnMediaStop_Click(object sender, RoutedEventArgs e)
        {
            if (_videoPlayerManager == null && !EnsureVideoPlayerInitialized("BtnMediaStop_Click")) return;
            
            _videoPlayerManager.Stop();
            MediaProgressSlider.Value = 0;
            MediaCurrentTime.Text = "00:00";
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
                //System.Diagnostics.Debug.WriteLine("⚠️ 播放模式按钮防抖动，忽略重复点击");
                return;
            }
            _lastPlayModeClickTime = now;
            
            // 循环切换播放模式
            var currentMode = _videoPlayerManager.CurrentPlayMode;
            PlayMode nextMode;
            string modeText;
            
            switch (currentMode)
            {
                case PlayMode.Sequential:
                    nextMode = PlayMode.Random;
                    modeText = "🔀";
                    break;
                case PlayMode.Random:
                    nextMode = PlayMode.LoopOne;
                    modeText = "🔂";
                    break;
                case PlayMode.LoopOne:
                    nextMode = PlayMode.LoopAll;
                    modeText = "🔁";
                    break;
                case PlayMode.LoopAll:
                default:
                    nextMode = PlayMode.Sequential;
                    modeText = "▶";
                    break;
            }
            
            _videoPlayerManager.SetPlayMode(nextMode);
            BtnPlayMode.Content = modeText;
            
            string[] modeNames = { "顺序", "随机", "单曲", "列表" };
            BtnPlayMode.ToolTip = $"播放模式：{modeNames[(int)nextMode]}";
            
            //System.Diagnostics.Debug.WriteLine($"🔄 播放模式已切换: {modeNames[(int)nextMode]}");
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

