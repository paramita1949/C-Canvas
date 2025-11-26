using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// VLC 自定义渲染器 - 使用 WriteableBitmap 实现视频渲染
    /// 解决 VideoView 的 Airspace 问题，支持 WPF 控件叠加
    /// </summary>
    public class VlcD3D11Renderer : IDisposable
    {
        #region 字段

        private LibVLCSharp.Shared.MediaPlayer _mediaPlayer;
        private WriteableBitmap _writeableBitmap;
        private Dispatcher _dispatcher;

        // 视频缓冲区
        private IntPtr _videoBuffer;
        
        // 视频参数
        private int _width;
        private int _height;
        private int _pitch;
        
        // 🚀 帧率限制（减少 CPU 占用）
        private DateTime _lastFrameTime = DateTime.MinValue;
        private readonly TimeSpan _minFrameInterval = TimeSpan.FromMilliseconds(33); // ~30fps
        
        // 同步对象
        private readonly object _lockObject = new object();
        private bool _isDisposed = false;

        #endregion

        #region 属性

        /// <summary>
        /// 获取 WriteableBitmap（绑定到 WPF Image 控件）
        /// </summary>
        public WriteableBitmap WriteableBitmap => _writeableBitmap;

        #endregion

        #region 构造函数

        /// <summary>
        /// 初始化自定义渲染器
        /// </summary>
        public VlcD3D11Renderer(LibVLCSharp.Shared.MediaPlayer mediaPlayer, int width, int height, Dispatcher dispatcher)
        {
            _mediaPlayer = mediaPlayer;
            _width = width;
            _height = height;
            _pitch = width * 4; // BGRA32 = 4 bytes per pixel
            _dispatcher = dispatcher;

            InitializeWriteableBitmap();
            SetupVlcCallbacks();

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"✅ [VlcCustomRenderer] 初始化完成: {width}x{height}");
#endif
        }

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化 WriteableBitmap
        /// </summary>
        private void InitializeWriteableBitmap()
        {
            // 在 UI 线程创建 WriteableBitmap
            _dispatcher.Invoke(() =>
            {
                _writeableBitmap = new WriteableBitmap(
                    _width,
                    _height,
                    96, 96,
                    PixelFormats.Bgra32,
                    null);
            });

            // 分配视频缓冲区
            _videoBuffer = Marshal.AllocHGlobal(_pitch * _height);

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"✅ [WriteableBitmap] 初始化成功: {_width}x{_height}");
#endif
        }

        /// <summary>
        /// 设置 VLC 视频回调
        /// </summary>
        private void SetupVlcCallbacks()
        {
            // 设置视频格式（BGRA32）
            _mediaPlayer.SetVideoFormat("RV32", (uint)_width, (uint)_height, (uint)_pitch);

            // 设置视频回调
            _mediaPlayer.SetVideoCallbacks(Lock, Unlock, Display);

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"✅ [VLC] 回调设置完成");
#endif
        }

        #endregion

        #region VLC 回调

        /// <summary>
        /// Lock 回调 - VLC 请求锁定视频缓冲区
        /// </summary>
        private IntPtr Lock(IntPtr opaque, IntPtr planes)
        {
            if (_isDisposed || _videoBuffer == IntPtr.Zero)
                return IntPtr.Zero;

            // 返回视频缓冲区指针给 VLC
            Marshal.WriteIntPtr(planes, _videoBuffer);
            return IntPtr.Zero;
        }

        /// <summary>
        /// Unlock 回调 - VLC 释放视频缓冲区
        /// </summary>
        private void Unlock(IntPtr opaque, IntPtr picture, IntPtr planes)
        {
            // 不需要做任何事情
        }

        /// <summary>
        /// Display 回调 - VLC 请求显示视频帧
        /// </summary>
        private void Display(IntPtr opaque, IntPtr picture)
        {
            if (_isDisposed)
                return;

            // 🚀 帧率限制：跳过过于频繁的帧（减少 CPU 占用）
            var now = DateTime.Now;
            if (now - _lastFrameTime < _minFrameInterval)
            {
                return; // 跳过此帧
            }
            _lastFrameTime = now;

            // 在 UI 线程更新 WriteableBitmap
            _dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isDisposed || _writeableBitmap == null)
                    return;

                try
                {
                    _writeableBitmap.Lock();

                    // 从视频缓冲区复制到 WriteableBitmap
                    unsafe
                    {
                        Buffer.MemoryCopy(
                            _videoBuffer.ToPointer(),
                            _writeableBitmap.BackBuffer.ToPointer(),
                            _pitch * _height,
                            _pitch * _height
                        );
                    }

                    // 标记整个区域为脏区域
                    _writeableBitmap.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
                    _writeableBitmap.Unlock();
                }
                catch (Exception ex)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"❌ [Display] WriteableBitmap更新错误: {ex.Message}");
#endif
                }
            }), DispatcherPriority.Render);
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 更新视频尺寸
        /// </summary>
        public void UpdateSize(int width, int height)
        {
            if (_width == width && _height == height)
                return;

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"🔄 [VlcCustomRenderer] 更新尺寸: {_width}x{_height} → {width}x{height}");
#endif

            _width = width;
            _height = height;
            _pitch = width * 4;

            // 重新分配缓冲区
            if (_videoBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_videoBuffer);
            }
            _videoBuffer = Marshal.AllocHGlobal(_pitch * _height);

            // 重新创建 WriteableBitmap
            _dispatcher.Invoke(() =>
            {
                _writeableBitmap = new WriteableBitmap(
                    width,
                    height,
                    96, 96,
                    PixelFormats.Bgra32,
                    null);
            });

            // 重新设置 VLC 格式
            _mediaPlayer.SetVideoFormat("RV32", (uint)_width, (uint)_height, (uint)_pitch);
        }

        #endregion

        #region 资源清理

        /// <summary>
        /// 释放所有资源
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            if (_videoBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_videoBuffer);
                _videoBuffer = IntPtr.Zero;
            }

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"✅ [VlcCustomRenderer] 资源已释放");
#endif
        }

        #endregion
    }
}

