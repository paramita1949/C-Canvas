using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace ImageColorChanger.Utils
{
    /// <summary>
    /// 快捷键业务逻辑处理器
    /// 统一管理所有快捷键的业务逻辑，投影和非投影模式共享
    /// </summary>
    public class ShortcutActionHandler
    {
        private readonly UI.MainWindow _mainWindow;

        public ShortcutActionHandler(UI.MainWindow mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        }

        #region 优先级1: 拼音输入模式（最高优先级）

        /// <summary>
        /// 处理ESC键 - 拼音输入模式
        /// </summary>
        /// <returns>是否处理了该按键</returns>
        public async Task<bool> HandlePinyinEscapeAsync()
        {
            if (_mainWindow.IsPinyinInputActive)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine("⌨️ [ActionHandler] ESC键 - 关闭拼音输入框");
                #endif
                await _mainWindow.ProcessPinyinEscapeKeyAsync();
                return true;
            }
            return false;
        }

        #endregion

        #region 优先级2: 通用快捷键（不分模式）

        /// <summary>
        /// 处理ESC键 - 通用功能
        /// 优先级：关闭投影 > 停止视频 > 清空图片
        /// </summary>
        /// <returns>是否处理了该按键</returns>
        public async Task<bool> HandleEscapeAsync()
        {
            // 优先检查拼音输入
            if (await HandlePinyinEscapeAsync())
            {
                return true;
            }

            #if DEBUG
            System.Diagnostics.Debug.WriteLine("⌨️ [ActionHandler] ESC键 - 通用处理");
            #endif

            // 尝试关闭投影
            var projectionManager = _mainWindow.GetProjectionManager();
            if (projectionManager != null)
            {
                bool wasClosed = projectionManager.CloseProjection();
                if (wasClosed)
                {
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine("⌨️ [ActionHandler] ESC键 - 已关闭投影");
                    #endif
                    return true;
                }
            }

            // 尝试停止视频
            var videoManager = _mainWindow.GetVideoPlayerManager();
            if (videoManager != null && videoManager.IsPlaying)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine("⌨️ [ActionHandler] ESC键 - 停止视频播放");
                #endif
                _mainWindow.SwitchToImageMode();
                return true;
            }

            // 尝试清空图片
            var imageProcessor = _mainWindow.GetImageProcessor();
            if (imageProcessor != null && imageProcessor.CurrentImage != null)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine("⌨️ [ActionHandler] ESC键 - 清空图片显示");
                #endif
                _mainWindow.ClearImageDisplay();
                return true;
            }

            return false;
        }

        #endregion

        #region 优先级3: 功能快捷键（根据模式判断）

        /// <summary>
        /// 处理Left键 - 上一个（文本编辑器/视频/关键帧）
        /// </summary>
        public async Task HandleLeftKeyAsync()
        {
            #if DEBUG
            System.Diagnostics.Debug.WriteLine("⌨️ [ActionHandler] Left键 - 上一个");
            #endif

            // 文本编辑器模式
            if (_mainWindow.IsTextEditorActive())
            {
                _mainWindow.NavigateToPreviousSlide();
                return;
            }

            // 视频播放模式
            if (_mainWindow.IsMediaPlaybackMode())
            {
                await _mainWindow.SwitchToPreviousMediaFile();
                return;
            }

            // 关键帧模式
            _mainWindow.InvokePrevKeyframe();
        }

        /// <summary>
        /// 处理Right键 - 下一个（文本编辑器/视频/关键帧）
        /// </summary>
        public async Task HandleRightKeyAsync()
        {
            #if DEBUG
            System.Diagnostics.Debug.WriteLine("⌨️ [ActionHandler] Right键 - 下一个");
            #endif

            // 文本编辑器模式
            if (_mainWindow.IsTextEditorActive())
            {
                _mainWindow.NavigateToNextSlide();
                return;
            }

            // 视频播放模式
            if (_mainWindow.IsMediaPlaybackMode())
            {
                await _mainWindow.SwitchToNextMediaFile();
                return;
            }

            // 关键帧模式
            _mainWindow.InvokeNextKeyframe();
        }

        /// <summary>
        /// 处理PageUp键 - 上一页（文本编辑器/原图/关键帧）
        /// </summary>
        public void HandlePageUpKey()
        {
            #if DEBUG
            System.Diagnostics.Debug.WriteLine("⌨️ [ActionHandler] PageUp键 - 上一页");
            #endif

            // 文本编辑器模式
            if (_mainWindow.IsTextEditorActive())
            {
                _mainWindow.NavigateToPreviousSlide();
                return;
            }

            // 原图模式
            if (_mainWindow.IsOriginalMode())
            {
                _mainWindow.SwitchSimilarImagePrevious();
                return;
            }

            // 关键帧模式
            _mainWindow.InvokePrevKeyframe();
        }

        /// <summary>
        /// 处理PageDown键 - 下一页（文本编辑器/原图/关键帧）
        /// </summary>
        public void HandlePageDownKey()
        {
            #if DEBUG
            System.Diagnostics.Debug.WriteLine("⌨️ [ActionHandler] PageDown键 - 下一页");
            #endif

            // 文本编辑器模式
            if (_mainWindow.IsTextEditorActive())
            {
                _mainWindow.NavigateToNextSlide();
                return;
            }

            // 原图模式
            if (_mainWindow.IsOriginalMode())
            {
                _mainWindow.SwitchSimilarImageNext();
                return;
            }

            // 关键帧模式
            _mainWindow.InvokeNextKeyframe();
        }

        /// <summary>
        /// 处理F2键 - 播放/暂停（脚本播放/视频播放）
        /// </summary>
        public void HandleF2Key()
        {
            #if DEBUG
            System.Diagnostics.Debug.WriteLine("⌨️ [ActionHandler] F2键 - 播放/暂停");
            #endif

            // 视频播放模式
            if (_mainWindow.IsMediaPlaybackMode())
            {
                var videoManager = _mainWindow.GetVideoPlayerManager();
                if (videoManager != null)
                {
                    if (videoManager.IsPaused)
                    {
                        videoManager.Play();
                    }
                    else
                    {
                        videoManager.Pause();
                    }
                }
                return;
            }

            // 脚本播放模式（关键帧/原图）
            _mainWindow.InvokePlayClick();
        }

        /// <summary>
        /// 处理F3键 - 合成播放（开始/停止）
        /// </summary>
        public void HandleF3Key()
        {
            #if DEBUG
            System.Diagnostics.Debug.WriteLine("⌨️ [ActionHandler] F3键 - 合成播放");
            #endif

            // 检查是否正在合成播放
            var compositeService = App.GetService<Services.Implementations.CompositePlaybackService>();
            if (compositeService != null && compositeService.IsPlaying)
            {
                // 停止合成播放
                _mainWindow.InvokeCompositePlayClick();
                return;
            }

            // 检查是否正在脚本播放
            if (_mainWindow.IsScriptPlaying())
            {
                // 停止脚本播放
                _mainWindow.InvokePlayClick();
                return;
            }

            // 开始合成播放
            _mainWindow.InvokeCompositePlayClick();
        }

        #endregion
    }
}

