using System;

namespace ImageColorChanger.Services
{
    public partial class AuthService
    {
        private readonly struct ClientEnvironmentPayload
        {
            public ClientEnvironmentPayload(string deviceName, string osVersion, string appVersion)
            {
                DeviceName = deviceName;
                OsVersion = osVersion;
                AppVersion = appVersion;
            }

            public string DeviceName { get; }
            public string OsVersion { get; }
            public string AppVersion { get; }
        }

        private string GetClientAppVersion()
        {
            try
            {
                return UpdateService.GetCurrentVersion();
            }
            catch
            {
                return "0.0.0";
            }
        }

        private ClientEnvironmentPayload BuildClientEnvironmentPayload()
        {
            return new ClientEnvironmentPayload(
                Environment.MachineName,
                Environment.OSVersion.ToString(),
                GetClientAppVersion());
        }
    }
}
