using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ImageColorChanger.Services.Licensing
{
    public sealed class ProtectedLicenseStore : ILicenseStore
    {
        private readonly string _path;

        public ProtectedLicenseStore()
            : this(LicenseStoragePaths.DefaultLicensePath)
        {
        }

        public ProtectedLicenseStore(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public SignedLicenseEnvelope Load()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return null;
                }

                string json = File.ReadAllText(_path, Encoding.UTF8);
                return JsonSerializer.Deserialize<SignedLicenseEnvelope>(json);
            }
            catch
            {
                return null;
            }
        }

        public void Save(SignedLicenseEnvelope license)
        {
            if (license == null)
            {
                throw new ArgumentNullException(nameof(license));
            }

            string directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(license);
            string tempPath = $"{_path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(tempPath, json, Encoding.UTF8);
            File.Move(tempPath, _path, true);
        }

        public void Clear()
        {
            try
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }
            }
            catch
            {
            }
        }
    }
}
