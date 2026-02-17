using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Management;

namespace ImageColorChanger.Services.Auth
{
    internal sealed class AuthDeviceFingerprint
    {
        internal sealed class Components
        {
            public string CpuId { get; set; }
            public string BoardSerial { get; set; }
            public string DiskSerial { get; set; }
            public string BiosUuid { get; set; }
            public string WindowsInstallId { get; set; }
        }

        public string GetHardwareId()
        {
            try
            {
                var c = GetComponents();
                var combined = $"{c.CpuId}|{c.BoardSerial}|{c.DiskSerial}|{c.BiosUuid}|{c.WindowsInstallId}";

                using (var sha256 = SHA256.Create())
                {
                    var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                }
            }
            catch
            {
                using (var sha256 = SHA256.Create())
                {
                    var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(Environment.MachineName));
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                }
            }
        }

        public Components GetComponents()
        {
            return new Components
            {
                CpuId = GetCpuId(),
                BoardSerial = GetBoardSerial(),
                DiskSerial = GetDiskSerial(),
                BiosUuid = GetBiosUuid(),
                WindowsInstallId = GetWindowsInstallId()
            };
        }

        private string GetCpuId()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        var cpuId = obj["ProcessorId"]?.ToString();
                        if (!string.IsNullOrEmpty(cpuId) && cpuId != "UNKNOWN")
                        {
                            return cpuId;
                        }
                    }
                }
            }
            catch
            {
            }
            return "CPU_UNKNOWN";
        }

        private string GetBoardSerial()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        var serial = obj["SerialNumber"]?.ToString();
                        if (!string.IsNullOrEmpty(serial) && serial != "UNKNOWN")
                        {
                            return serial;
                        }
                    }
                }
            }
            catch
            {
            }
            return "BOARD_UNKNOWN";
        }

        private string GetDiskSerial()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_PhysicalMedia"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        var serial = obj["SerialNumber"]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(serial))
                        {
                            return serial;
                        }
                    }
                }

                using (var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        var serial = obj["SerialNumber"]?.ToString()?.Trim();
                        if (!string.IsNullOrEmpty(serial))
                        {
                            return serial;
                        }
                    }
                }
            }
            catch
            {
            }
            return "DISK_UNKNOWN";
        }

        private string GetBiosUuid()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        var uuid = obj["UUID"]?.ToString();
                        if (!string.IsNullOrEmpty(uuid) && uuid != "UNKNOWN")
                        {
                            return uuid;
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private string GetWindowsInstallId()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    if (key != null)
                    {
                        var guid = key.GetValue("MachineGuid")?.ToString();
                        if (!string.IsNullOrEmpty(guid))
                        {
                            return guid;
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
        }
    }
}
