using System;
using LibVLCSharp.WPF;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 投影窗口构建工厂，封装具体 UI 组装细节。
    /// </summary>
    public interface IProjectionWindowFactory
    {
        ProjectionWindowLayout CreateProjectionWindow(Action<VideoView> onVideoViewLoaded);
    }
}
