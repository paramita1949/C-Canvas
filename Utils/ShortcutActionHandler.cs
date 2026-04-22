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
                await _mainWindow.ProcessPinyinEscapeKeyAsync();
                return true;
            }
            return false;
        }

        #endregion

        #region 优先级2: 通用快捷键（不分模式）

        /// <summary>
        /// 处理ESC键 - 通用功能
        /// 优先级：拼音输入 > 编辑框选中 > 关闭投影 > 停止视频 > 清空图片
        /// </summary>
        /// <returns>是否处理了该按键</returns>
        public async Task<bool> HandleEscapeAsync()
        {
            // 投影中的“插入经文预览”优先：Esc 只取消预览，不关闭投影
            if (_mainWindow.TryCancelProjectionVersePreview())
            {
                return true;
            }

            // 优先检查拼音输入
            if (await HandlePinyinEscapeAsync())
            {
                return true;
            }

            // 优先级2: 检查是否有选中的编辑框
            if (_mainWindow.HasSelectedTextBox())
            {
                //System.Diagnostics.Debug.WriteLine($" [全局ESC] 取消编辑框选中状态");
                _mainWindow.DeselectAllTextBoxes(true); // 关闭浮动工具栏
                return true;
            }

            // 尝试关闭投影（仅当主窗口在前台时）
            var projectionManager = _mainWindow.GetProjectionManager();
            if (projectionManager != null)
            {
                //  安全检查：只有主窗口激活（在前台）时才允许ESC关闭投影
                if (_mainWindow.IsActive)
                {
                    bool wasClosed = projectionManager.CloseProjection();
                    if (wasClosed)
                    {
                        return true;
                    }
                }
                else
                {
                    // 主窗口不在前台，忽略ESC键（防止误触关闭投影）
                    //System.Diagnostics.Debug.WriteLine($" [ESC键] 主窗口不在前台，忽略关闭投影请求");
                    return false;
                }
            }

            // 尝试停止视频
            var videoManager = _mainWindow.GetVideoPlayerManager();
            if (videoManager != null && videoManager.IsPlaying)
            {
                _mainWindow.SwitchToImageMode();
                return true;
            }

            // 尝试清空图片
            var imageProcessor = _mainWindow.GetImageProcessor();
            if (imageProcessor != null && imageProcessor.CurrentImage != null)
            {
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
        public async Task HandleF2KeyAsync()
        {
            // 合成播放中：F2 = 暂停/继续（滚动与倒计时）
            if (await _mainWindow.ToggleCompositePauseResumeByHotkeyAsync())
            {
                return;
            }

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
        /// 处理F3键 - 圣经清屏 / 合成播放（直接重置播放）
        /// </summary>
        public async Task HandleF3KeyAsync()
        {
            // 圣经模式优先：作为清屏快捷键使用
            if (await _mainWindow.TryClearBibleScreenByHotkeyAsync())
            {
                return;
            }

            // 获取合成播放服务
            var compositeService = _mainWindow.GetCompositePlaybackService();

            // 如果正在播放，先停止
            if (compositeService != null && compositeService.IsPlaying)
            {
                await compositeService.StopPlaybackAsync();
                await System.Threading.Tasks.Task.Delay(100); // 短暂延迟确保停止完成
            }

            // 如果正在脚本播放，停止脚本播放
            if (_mainWindow.IsScriptPlaying())
            {
                _mainWindow.InvokePlayClick();
                await System.Threading.Tasks.Task.Delay(100); // 短暂延迟确保停止完成
            }

            // 直接开始合成播放（重置播放）
            _mainWindow.InvokeCompositePlayClick();
        }

        /// <summary>
        /// 处理Up键 - 圣经经文上一节（变色高亮）
        /// </summary>
        public void HandleUpKey()
        {
            // 只在圣经模式下生效
            if (_mainWindow.IsBibleMode())
            {
                _mainWindow.NavigateHighlightedVerse(-1);
            }
        }

        /// <summary>
        /// 处理Down键 - 圣经经文下一节（变色高亮）
        /// </summary>
        public void HandleDownKey()
        {
            // 只在圣经模式下生效
            if (_mainWindow.IsBibleMode())
            {
                _mainWindow.NavigateHighlightedVerse(1);
            }
        }

        /// <summary>
        /// 处理Ctrl+S键 - 保存（歌词/幻灯片）
        /// </summary>
        public void HandleSaveKey()
        {
            // 歌词模式
            if (_mainWindow.IsLyricsMode())
            {
                _mainWindow.InvokeSaveLyrics();
                return;
            }

            // 幻灯片模式
            if (_mainWindow.IsTextEditorActive())
            {
                _mainWindow.InvokeSaveTextProject();
                return;
            }
        }

        #endregion
    }
}


