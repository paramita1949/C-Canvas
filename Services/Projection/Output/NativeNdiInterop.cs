using System;
using System.Runtime.InteropServices;
using System.Reflection;

namespace ImageColorChanger.Services.Projection.Output
{
    internal static class NativeNdiInterop
    {
        // NDI 5/6 Windows Runtime 默认导出库名。
        private const string NdiLibrary = "Processing.NDI.Lib.x64.dll";

        internal const long NDIlib_send_timecode_synthesize = long.MaxValue;

        static NativeNdiInterop()
        {
            NativeLibrary.SetDllImportResolver(typeof(NativeNdiInterop).Assembly, ResolveImport);
        }

        private static IntPtr ResolveImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            _ = assembly;
            _ = searchPath;
            if (!string.Equals(libraryName, NdiLibrary, StringComparison.OrdinalIgnoreCase))
            {
                return IntPtr.Zero;
            }

            return NdiLibraryLocator.TryLoad(out var handle) ? handle : IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct NDIlib_send_create_t
        {
            public IntPtr p_ndi_name;
            public IntPtr p_groups;
            [MarshalAs(UnmanagedType.I1)]
            public bool clock_video;
            [MarshalAs(UnmanagedType.I1)]
            public bool clock_audio;
        }

        internal enum NDIlib_FourCC_video_type_e : uint
        {
            NDIlib_FourCC_video_type_BGRA = 0x41524742 // 'BGRA'
        }

        internal enum NDIlib_frame_format_type_e
        {
            NDIlib_frame_format_type_progressive = 1
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct NDIlib_video_frame_v2_t
        {
            public int xres;
            public int yres;
            public NDIlib_FourCC_video_type_e FourCC;
            public int frame_rate_N;
            public int frame_rate_D;
            public float picture_aspect_ratio;
            public NDIlib_frame_format_type_e frame_format_type;
            public long timecode;
            public IntPtr p_data;
            public int line_stride_in_bytes;
            public IntPtr p_metadata;
            public long timestamp;
        }

        [DllImport(NdiLibrary, EntryPoint = "NDIlib_initialize", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool Initialize();

        [DllImport(NdiLibrary, EntryPoint = "NDIlib_destroy", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Destroy();

        [DllImport(NdiLibrary, EntryPoint = "NDIlib_send_create", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr SendCreate(ref NDIlib_send_create_t p_create_settings);

        [DllImport(NdiLibrary, EntryPoint = "NDIlib_send_destroy", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SendDestroy(IntPtr p_instance);

        [DllImport(NdiLibrary, EntryPoint = "NDIlib_send_send_video_v2", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SendSendVideoV2(IntPtr p_instance, ref NDIlib_video_frame_v2_t p_video_data);

        [DllImport(NdiLibrary, EntryPoint = "NDIlib_send_send_video_async_v2", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void SendSendVideoAsyncV2(IntPtr p_instance, ref NDIlib_video_frame_v2_t p_video_data);

        [DllImport(NdiLibrary, EntryPoint = "NDIlib_send_get_no_connections", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int SendGetNoConnections(IntPtr p_instance, uint timeout_in_ms);
    }
}
