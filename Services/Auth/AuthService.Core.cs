using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace ImageColorChanger.Services
{
    public partial class AuthService
    {
        public static bool CheckSingleInstance()
        {
            try
            {
                bool createdNew;
                var testMutex = new System.Threading.Mutex(false, MUTEX_NAME, out createdNew);
                if (!createdNew)
                {
                    testMutex.Close();
                    return false;
                }

                testMutex.Close();
                return true;
            }
            catch
            {
                return true;
            }
        }

        private static long GetMaxFileVersionFromRegistry()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(VERSION_REGISTRY_KEY))
                {
                    var value = key?.GetValue(VERSION_REGISTRY_VALUE);
                    if (value != null && long.TryParse(value.ToString(), out var version))
                    {
                        return version;
                    }
                }
            }
            catch
            {
            }

            return 0;
        }

        private static void SaveMaxFileVersionToRegistry(long version)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(VERSION_REGISTRY_KEY))
                {
                    key?.SetValue(VERSION_REGISTRY_VALUE, version, Microsoft.Win32.RegistryValueKind.QWord);
                }
            }
            catch
            {
            }
        }

        public bool IsAuthenticated => _isAuthenticated;
        public string Username => _username;
        public string LastAuthFailureReason => _lastAuthFailureReason;
        public PaymentInfo LastPaymentInfo => _lastPaymentInfo;
        public DateTime? ExpiresAt => _expiresAt;
        public int RemainingDays => _remainingDays;
        public DeviceInfo DeviceBindingInfo => _deviceInfo;
        public int ResetDeviceCount => _resetDeviceCount;

        public string GetCurrentHardwareId()
        {
            return _authDeviceFingerprint.GetHardwareId();
        }

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

        public class ClientNoticesEventArgs : EventArgs
        {
            public List<ClientNoticeDisplayItem> Items { get; set; } = new List<ClientNoticeDisplayItem>();
        }

        public class ServerSwitchEventArgs : EventArgs
        {
            public string ServerUrl { get; set; }
            public int AttemptNumber { get; set; }
            public int TotalServers { get; set; }
            public string Message { get; set; }
        }

        public event EventHandler<AuthenticationChangedEventArgs> AuthenticationChanged;
        public event EventHandler<UiMessageEventArgs> UiMessageRequested;
        public event EventHandler<ClientNoticesEventArgs> ClientNoticesRequested;
        public event EventHandler<ServerSwitchEventArgs> ServerSwitching;

        private async Task<HttpResponseMessage> TryMultipleApiUrlsAsync(
            Func<string, Task<HttpResponseMessage>> requestFunc,
            int timeoutSeconds = 20,
            bool allowFailoverOnFailure = true)
        {
            return await _authApiClient.ExecuteWithFailoverAsync(
                requestFunc,
                (serverUrl, attemptNumber, totalServers, message) =>
                {
                    ServerSwitching?.Invoke(this, new ServerSwitchEventArgs
                    {
                        ServerUrl = serverUrl,
                        AttemptNumber = attemptNumber,
                        TotalServers = totalServers,
                        Message = message
                    });
                },
                timeoutSeconds,
                allowFailoverOnFailure);
        }
    }
}
