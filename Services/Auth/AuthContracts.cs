using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ImageColorChanger.Services
{
    /// <summary>
    /// 验证响应数据结构
    /// </summary>
    public class AuthResponse
    {
        public bool Success { get; set; }
        public bool Valid { get; set; }
        public string Message { get; set; }
        public string Reason { get; set; }
        public AuthData Data { get; set; }

        [JsonPropertyName("payment_info")]
        public PaymentInfo PaymentInfo { get; set; }

        [JsonPropertyName("server_time")]
        public string ServerTimeString { get; set; }
    }

    public class AuthData
    {
        public string Username { get; set; }
        public string Email { get; set; }

        [JsonPropertyName("license_type")]
        public string LicenseType { get; set; }

        [JsonPropertyName("expires_at")]
        public long? ExpiresAt { get; set; }

        [JsonPropertyName("remaining_days")]
        public int? RemainingDays { get; set; }

        [JsonPropertyName("remaining_hours")]
        public int? RemainingHours { get; set; }

        public string Token { get; set; }

        [JsonPropertyName("server_time")]
        public string ServerTimeString { get; set; }

        public string Warning { get; set; }

        [JsonPropertyName("device_info")]
        public DeviceInfo DeviceInfo { get; set; }

        [JsonPropertyName("reset_device_count")]
        public int? ResetDeviceCount { get; set; }

        [JsonPropertyName("trial_days")]
        public int? TrialDays { get; set; }

        [JsonPropertyName("max_devices")]
        public int? MaxDevices { get; set; }

        [JsonPropertyName("holiday_bonus")]
        public HolidayBonusInfo HolidayBonus { get; set; }

        [JsonPropertyName("client_notice")]
        public ClientNoticeInfo ClientNotice { get; set; }

        [JsonPropertyName("client_notices")]
        public List<ClientNoticeInfo> ClientNotices { get; set; }
    }

    public class ClientNoticeInfo
    {
        [JsonPropertyName("notice_key")]
        public string NoticeKey { get; set; }

        public string Title { get; set; }
        public string Message { get; set; }

        [JsonPropertyName("show_mode")]
        public string ShowMode { get; set; }

        [JsonPropertyName("expires_at")]
        public long? ExpiresAt { get; set; }
    }

    public class PaymentInfo
    {
        public string Title { get; set; }
        public string Message { get; set; }
        public string Url { get; set; }
    }

    public class HolidayBonusInfo
    {
        [JsonPropertyName("holiday_key")]
        public string HolidayKey { get; set; }

        [JsonPropertyName("holiday_name")]
        public string HolidayName { get; set; }

        [JsonPropertyName("bonus_days")]
        public int BonusDays { get; set; }

        [JsonPropertyName("new_expires_at")]
        public long? NewExpiresAt { get; set; }

        public string Message { get; set; }
    }

    /// <summary>
    /// 设备绑定信息
    /// </summary>
    public class DeviceInfo
    {
        [JsonPropertyName("bound_devices")]
        public int BoundDevices { get; set; }

        [JsonPropertyName("max_devices")]
        public int MaxDevices { get; set; }

        [JsonPropertyName("remaining_slots")]
        public int RemainingSlots { get; set; }

        [JsonPropertyName("is_new_device")]
        public bool IsNewDevice { get; set; }
    }
}
