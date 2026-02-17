using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ImageColorChanger.Services.Auth
{
    /// <summary>
    /// 本地认证状态快照（用于持久化）。
    /// </summary>
    internal sealed class AuthStateSnapshot
    {
        [JsonPropertyName("username")]
        public string Username { get; set; }

        [JsonPropertyName("token")]
        public string Token { get; set; }

        [JsonPropertyName("expires_at")]
        public string ExpiresAt { get; set; }

        [JsonPropertyName("remaining_days")]
        public int RemainingDays { get; set; }

        [JsonPropertyName("last_server_time")]
        public string LastServerTime { get; set; }

        [JsonPropertyName("last_local_time")]
        public string LastLocalTime { get; set; }

        [JsonPropertyName("last_tick_count")]
        public long LastTickCount { get; set; }

        [JsonPropertyName("reset_device_count")]
        public int ResetDeviceCount { get; set; }

        [JsonPropertyName("last_successful_heartbeat")]
        public string LastSuccessfulHeartbeat { get; set; }

        [JsonPropertyName("nonce")]
        public string Nonce { get; set; }

        [JsonPropertyName("save_time")]
        public long SaveTime { get; set; }

        [JsonPropertyName("file_version")]
        public long FileVersion { get; set; }

        [JsonPropertyName("shown_client_notice_keys")]
        public List<string> ShownClientNoticeKeys { get; set; }

        [JsonPropertyName("device_info")]
        public DeviceInfoSnapshot DeviceInfo { get; set; }
    }

    internal sealed class DeviceInfoSnapshot
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
