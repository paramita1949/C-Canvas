using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private bool _onlineScoreSearchBusy;
        private bool _onlineScoreNetworkAnimationRunning;

        private void SetOnlineScoreSearchBusy(bool busy)
        {
            _onlineScoreSearchBusy = busy;
            RefreshOnlineScoreNetworkAnimation();
        }

        private void RefreshOnlineScoreNetworkAnimation()
        {
            if (OnlineScoreNetworkRotation == null || OnlineScoreNetworkIcon == null) return;
            bool animate = _onlineScoreSearchBusy && IsOnlineScoreMode && !_onlineScoreClosed &&
                OnlineScoreNetworkIcon.IsLoaded && OnlineScoreNetworkIcon.IsVisible;
            if (animate == _onlineScoreNetworkAnimationRunning) return;
            _onlineScoreNetworkAnimationRunning = animate;
            OnlineScoreNetworkRotation.BeginAnimation(RotateTransform.AngleProperty, animate
                ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(2.4)) { RepeatBehavior = RepeatBehavior.Forever }
                : null);
            if (!animate) OnlineScoreNetworkRotation.Angle = 0;
        }

        private void OnlineScoreNetworkIcon_Loaded(object sender, RoutedEventArgs e) => RefreshOnlineScoreNetworkAnimation();
        private void OnlineScoreNetworkIcon_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => RefreshOnlineScoreNetworkAnimation();
        private void OnlineScoreNetworkIcon_Unloaded(object sender, RoutedEventArgs e)
        {
            OnlineScoreNetworkRotation?.BeginAnimation(RotateTransform.AngleProperty, null);
            if (OnlineScoreNetworkRotation != null) OnlineScoreNetworkRotation.Angle = 0;
            _onlineScoreNetworkAnimationRunning = false;
        }
    }
}
