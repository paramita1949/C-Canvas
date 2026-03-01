using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace ImageColorChanger.Services.Projection.Output
{
    internal static class NdiLibraryLocator
    {
        private const string NdiDllName = "Processing.NDI.Lib.x64.dll";
        private static readonly object Sync = new object();
        private static IntPtr _cachedHandle = IntPtr.Zero;
        private static string _loadedPath = string.Empty;

        public static bool TryLoad(out IntPtr handle)
        {
            lock (Sync)
            {
                if (_cachedHandle != IntPtr.Zero)
                {
                    handle = _cachedHandle;
                    return true;
                }

                if (NativeLibrary.TryLoad(NdiDllName, out var byName))
                {
                    _cachedHandle = byName;
                    _loadedPath = NdiDllName;
                    ProjectionNdiDiagnostics.Log($"NDI dll loaded by name: {_loadedPath}");
                    handle = _cachedHandle;
                    return true;
                }

                foreach (var path in EnumerateCandidateDllPaths())
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    if (NativeLibrary.TryLoad(path, out var byPath))
                    {
                        _cachedHandle = byPath;
                        _loadedPath = path;
                        ProjectionNdiDiagnostics.Log($"NDI dll loaded by path: {_loadedPath}");
                        handle = _cachedHandle;
                        return true;
                    }
                }

                handle = IntPtr.Zero;
                return false;
            }
        }

        public static string GetLoadedPathOrEmpty()
        {
            lock (Sync)
            {
                return _loadedPath ?? string.Empty;
            }
        }

        private static IEnumerable<string> EnumerateCandidateDllPaths()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            yield return Path.Combine(appDir, NdiDllName);

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                yield return Path.Combine(programFiles, "NDI", "NDI 6 Runtime", "v6", NdiDllName);
                yield return Path.Combine(programFiles, "NDI", "NDI 6 Tools", "Runtime", NdiDllName);
                yield return Path.Combine(programFiles, "NDI", "NDI 5 Runtime", "v5", NdiDllName);
            }

            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(programFilesX86))
            {
                yield return Path.Combine(programFilesX86, "NDI", "NDI 6 Runtime", "v6", NdiDllName);
                yield return Path.Combine(programFilesX86, "NDI", "NDI 6 Tools", "Runtime", NdiDllName);
                yield return Path.Combine(programFilesX86, "NDI", "NDI 5 Runtime", "v5", NdiDllName);
            }
        }
    }
}

