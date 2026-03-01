using System;
using System.Runtime.InteropServices;

namespace ImageColorChanger.Services.Projection.Output
{
    public static class ProjectionNdiRuntimeProbe
    {
        private const string NdiLibrary = "Processing.NDI.Lib.x64.dll";

        public static bool IsRuntimeAvailable()
        {
            try
            {
                if (NdiLibraryLocator.TryLoad(out var _))
                {
                    string loadedFrom = NdiLibraryLocator.GetLoadedPathOrEmpty();
                    ProjectionNdiDiagnostics.Log($"Runtime probe success: loaded {NdiLibrary} from {loadedFrom}");
                    return true;
                }

                ProjectionNdiDiagnostics.Log($"Runtime probe failed: cannot load {NdiLibrary}");
                return false;
            }
            catch (Exception ex)
            {
                ProjectionNdiDiagnostics.LogException("Runtime probe exception", ex);
                return false;
            }
        }
    }
}
