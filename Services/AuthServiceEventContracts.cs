using System;
using System.Collections.Generic;

namespace ImageColorChanger.Services
{
    /// <summary>
    /// 认证状态变化事件参数。
    /// </summary>
    public class AuthenticationChangedEventArgs : EventArgs
    {
        public bool IsAuthenticated { get; set; }
        public bool IsAutoLogin { get; set; }
    }

    public enum UiMessageLevel
    {
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// 认证模块请求 UI 显示消息。
    /// </summary>
    public class UiMessageEventArgs : EventArgs
    {
        public string Title { get; set; }
        public string Message { get; set; }
        public UiMessageLevel Level { get; set; } = UiMessageLevel.Info;
    }

    public class ClientNoticeDisplayItem
    {
        public string Title { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// 客户端通知集合。
    /// </summary>
    public class ClientNoticesEventArgs : EventArgs
    {
        public List<ClientNoticeDisplayItem> Items { get; set; } = new List<ClientNoticeDisplayItem>();
    }

    /// <summary>
    /// API 多地址切换进度。
    /// </summary>
    public class ServerSwitchEventArgs : EventArgs
    {
        public string ServerUrl { get; set; }
        public int AttemptNumber { get; set; }
        public int TotalServers { get; set; }
        public string Message { get; set; }
    }
}
