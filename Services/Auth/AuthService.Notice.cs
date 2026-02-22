using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ImageColorChanger.Services
{
    public partial class AuthService
    {
        private void TryShowHolidayBonusNotification(HolidayBonusInfo holidayBonus)
        {
            if (holidayBonus == null || string.IsNullOrWhiteSpace(holidayBonus.HolidayKey))
            {
                return;
            }

            if (string.Equals(_lastHolidayBonusKey, holidayBonus.HolidayKey, StringComparison.Ordinal))
            {
                return;
            }

            _lastHolidayBonusKey = holidayBonus.HolidayKey;

            var title = string.IsNullOrWhiteSpace(holidayBonus.HolidayName)
                ? "有效期已自动延长"
                : $"{holidayBonus.HolidayName}活动";

            var content = !string.IsNullOrWhiteSpace(holidayBonus.Message)
                ? holidayBonus.Message
                : $"账号有效期已自动增加 {holidayBonus.BonusDays} 天。";

            if (holidayBonus.NewExpiresAt.HasValue)
            {
                var newExpiresAt = DateTimeOffset.FromUnixTimeSeconds(holidayBonus.NewExpiresAt.Value).LocalDateTime;
                content += $"\n当前到期时间：{newExpiresAt:yyyy-MM-dd HH:mm:ss}";
            }

            RaiseUiMessage(title, content, UiMessageLevel.Info);
        }

        private void TryShowClientNotices(AuthData authData, string source)
        {
            var incoming = new List<ClientNoticeInfo>();
            if (authData?.ClientNotices != null && authData.ClientNotices.Count > 0)
            {
                incoming.AddRange(authData.ClientNotices.Where(n => n != null));
            }

            if (authData?.ClientNotice != null)
            {
                var fallback = authData.ClientNotice;
                var exists = incoming.Any(n =>
                    string.Equals((n.NoticeKey ?? string.Empty).Trim(), (fallback.NoticeKey ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((n.Message ?? string.Empty).Trim(), (fallback.Message ?? string.Empty).Trim(), StringComparison.Ordinal));
                if (!exists)
                {
                    incoming.Add(fallback);
                }
            }

            if (incoming.Count == 0)
            {
                return;
            }

            var displayItems = new List<ClientNoticeDisplayItem>();
            var ackKeys = new List<string>();
            var changedShownSet = false;

            foreach (var notice in incoming)
            {
                if (notice == null || string.IsNullOrWhiteSpace(notice.Message))
                {
                    continue;
                }

                if (notice.ExpiresAt.HasValue && notice.ExpiresAt.Value > 0)
                {
                    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    if (notice.ExpiresAt.Value <= now)
                    {
                        continue;
                    }
                }

                var showMode = string.Equals(notice.ShowMode, "always", StringComparison.OrdinalIgnoreCase)
                    ? "always"
                    : "once";

                var title = string.IsNullOrWhiteSpace(notice.Title)
                    ? "系统通知"
                    : notice.Title.Trim();

                var message = notice.Message.Trim();
                var rawKey = string.IsNullOrWhiteSpace(notice.NoticeKey)
                    ? $"{title}|{message}"
                    : notice.NoticeKey.Trim();

                var scopedKey = string.IsNullOrWhiteSpace(_username)
                    ? rawKey
                    : $"{_username}:{rawKey}";

                if (showMode == "once")
                {
                    if (_shownClientNoticeKeys.Contains(scopedKey))
                    {
                        continue;
                    }

                    _shownClientNoticeKeys.Add(scopedKey);
                    changedShownSet = true;
                }

                displayItems.Add(new ClientNoticeDisplayItem
                {
                    Title = title,
                    Message = message
                });

                if (!string.IsNullOrWhiteSpace(rawKey))
                {
                    ackKeys.Add(rawKey);
                }
            }

            if (displayItems.Count == 0)
            {
                return;
            }

            if (_isAuthenticated && changedShownSet)
            {
                RequestPersistAuthData();
            }

            ClientNoticesRequested?.Invoke(this, new ClientNoticesEventArgs
            {
                Items = displayItems
            });

            foreach (var key in ackKeys.Distinct(StringComparer.Ordinal))
            {
                _ = ReportNoticeReceiptAsync(key, source);
            }
        }

        private async Task ReportNoticeReceiptAsync(string noticeKey, string source)
        {
            if (!_isAuthenticated || string.IsNullOrWhiteSpace(_token) || string.IsNullOrWhiteSpace(noticeKey))
            {
                return;
            }

            try
            {
                var requestData = new
                {
                    token = _token,
                    notice_key = noticeKey,
                    channel = string.IsNullOrWhiteSpace(source) ? "client" : source
                };

                var jsonContent = JsonSerializer.Serialize(requestData);
                var response = await TryMultipleApiUrlsAsync(async (apiUrl) =>
                {
                    var requestContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                    return await _httpClient.PostAsync(apiUrl + NOTICE_ACK_ENDPOINT, requestContent);
                }, timeoutSeconds: 8);

                if (response != null)
                {
                    _ = response.IsSuccessStatusCode;
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Trace.WriteLine($" [AuthService] 通知回执失败: {ex.Message}");
#else
                _ = ex;
#endif
            }
        }
    }
}


