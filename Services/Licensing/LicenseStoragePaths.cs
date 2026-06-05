using System;
using System.IO;

namespace ImageColorChanger.Services.Licensing
{
    public static class LicenseStoragePaths
    {
        public static string DefaultLicensePath
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(appData, "CanvasCast", "license.json");
            }
        }
    }
}
