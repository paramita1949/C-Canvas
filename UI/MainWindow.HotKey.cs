using System;
using System.Windows;
using System.Windows.Input;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        #region 全局热键管理

        private void InitializeGlobalHotKeys()
        {
            try
            {
                // 创建全局热键管理器，但不立即注册热键
                _globalHotKeyManager = new Utils.GlobalHotKeyManager(this);
                
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 全局热键管理器初始化失败: {ex.Message}");
                System.Windows.MessageBox.Show($"全局热键管理器初始化失败: {ex.Message}", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 启用全局热键（仅在投影模式下调用）
        /// </summary>
        private void EnableGlobalHotKeys()
        {
            if (_globalHotKeyManager == null)
            {
                //System.Diagnostics.Debug.WriteLine("❌ 全局热键管理器未初始化");
                return;
            }

            try
            {
                // 注册热键（使用原来的按键功能）
                
                // 左方向键: 上一个媒体/关键帧/幻灯片
                _globalHotKeyManager.RegisterHotKey(
                    Key.Left,
                    ModifierKeys.None,
                    () =>
                    {
                        //System.Diagnostics.Debug.WriteLine("🎯 全局热键触发: Left");
                        Dispatcher.InvokeAsync(async () =>
                        {
                            // 🆕 如果在文本编辑器模式，切换到上一张幻灯片
                            if (TextEditorPanel.Visibility == Visibility.Visible)
                            {
                                //System.Diagnostics.Debug.WriteLine("📖 文本编辑器模式，切换到上一张幻灯片");
                                NavigateToPreviousSlide();
                                return;
                            }
                            
                            if (IsMediaPlaybackMode())
                            {
                                await SwitchToPreviousMediaFile();
                            }
                            else
                            {
                                // 关键帧模式
                                BtnPrevKeyframe_Click(null, null);
                            }
                        });
                    });
                
                // 右方向键: 下一个媒体/关键帧/幻灯片
                _globalHotKeyManager.RegisterHotKey(
                    Key.Right,
                    ModifierKeys.None,
                    () =>
                    {
                        //System.Diagnostics.Debug.WriteLine("🎯 全局热键触发: Right");
                        Dispatcher.InvokeAsync(async () =>
                        {
                            // 🆕 如果在文本编辑器模式，切换到下一张幻灯片
                            if (TextEditorPanel.Visibility == Visibility.Visible)
                            {
                                //System.Diagnostics.Debug.WriteLine("📖 文本编辑器模式，切换到下一张幻灯片");
                                NavigateToNextSlide();
                                return;
                            }
                            
                            if (IsMediaPlaybackMode())
                            {
                                await SwitchToNextMediaFile();
                            }
                            else
                            {
                                // 关键帧模式
                                BtnNextKeyframe_Click(null, null);
                            }
                        });
                    });
                
                // PageUp: 上一个相似图片（原图模式）/ 上一个关键帧（关键帧模式）/ 上一张幻灯片（文本编辑器模式）
                _globalHotKeyManager.RegisterHotKey(
                    Key.PageUp,
                    ModifierKeys.None,
                    () =>
                    {
                        //System.Diagnostics.Debug.WriteLine("🎯 全局热键触发: PageUp");
                        Dispatcher.InvokeAsync(() =>
                        {
                            // 🆕 如果在文本编辑器模式，切换到上一张幻灯片
                            if (TextEditorPanel.Visibility == Visibility.Visible)
                            {
                                //System.Diagnostics.Debug.WriteLine("📖 文本编辑器模式，切换到上一张幻灯片");
                                NavigateToPreviousSlide();
                                return;
                            }

                            if (_originalMode)
                            {
                                // 原图模式：切换到上一张相似图片
                                SwitchSimilarImage(false);
                            }
                            else
                            {
                                // 关键帧模式：上一个关键帧
                                BtnPrevKeyframe_Click(null, null);
                            }
                        });
                    });
                
                // PageDown: 下一个相似图片（原图模式）/ 下一个关键帧（关键帧模式）/ 下一张幻灯片（文本编辑器模式）
                _globalHotKeyManager.RegisterHotKey(
                    Key.PageDown,
                    ModifierKeys.None,
                    () =>
                    {
                        //System.Diagnostics.Debug.WriteLine("🎯 全局热键触发: PageDown");
                        Dispatcher.InvokeAsync(() =>
                        {
                            // 🆕 如果在文本编辑器模式，切换到下一张幻灯片
                            if (TextEditorPanel.Visibility == Visibility.Visible)
                            {
                                //System.Diagnostics.Debug.WriteLine("📖 文本编辑器模式，切换到下一张幻灯片");
                                NavigateToNextSlide();
                                return;
                            }

                            if (_originalMode)
                            {
                                // 原图模式：切换到下一张相似图片
                                SwitchSimilarImage(true);
                            }
                            else
                            {
                                // 关键帧模式：下一个关键帧
                                BtnNextKeyframe_Click(null, null);
                            }
                        });
                    });
                
                // F2键: 播放/暂停
                _globalHotKeyManager.RegisterHotKey(
                    Key.F2,
                    ModifierKeys.None,
                    () =>
                    {
                        //System.Diagnostics.Debug.WriteLine("🎯 全局热键触发: F2");
                        Dispatcher.InvokeAsync(() =>
                        {
                            if (IsMediaPlaybackMode())
                            {
                                // 视频播放/暂停
                                if (_videoPlayerManager.IsPaused)
                                {
                                    _videoPlayerManager.Play();
                                }
                                else
                                {
                                    _videoPlayerManager.Pause();
                                }
                            }
                            else
                            {
                                // 关键帧/原图模式的播放/暂停
                                BtnPlay_Click(null, null);
                            }
                        });
                    });
                
                // 🆕 F3键: 停止播放（脚本录制播放或合成播放）
                _globalHotKeyManager.RegisterHotKey(
                    Key.F3,
                    ModifierKeys.None,
                    () =>
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine("🎯 全局热键触发: F3");
                        //#endif
                        Dispatcher.InvokeAsync(() =>
                        {
                            // 检查是否正在合成播放
                            var compositeService = App.GetService<Services.Implementations.CompositePlaybackService>();
                            if (compositeService != null && compositeService.IsPlaying)
                            {
                                //#if DEBUG
                                //System.Diagnostics.Debug.WriteLine("⌨️ [投影] F3键: 停止合成播放");
                                //#endif
                                // 触发合成播放按钮点击事件（停止播放）
                                BtnFloatingCompositePlay.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            }
                            // 检查是否正在脚本播放（关键帧模式或原图模式）
                            else if (_playbackViewModel != null && _playbackViewModel.IsPlaying)
                            {
                                //#if DEBUG
                                //System.Diagnostics.Debug.WriteLine("⌨️ [投影] F3键: 停止脚本播放");
                                //#endif
                                // 停止播放
                                BtnPlay_Click(null, null);
                            }
                        });
                    });
                
                // ESC键: 取消投影/停止播放视频
                _globalHotKeyManager.RegisterHotKey(
                    Key.Escape,
                    ModifierKeys.None,
                    () =>
                    {
                        Dispatcher.InvokeAsync(async () =>
                        {
                            // 🔧 如果拼音输入框激活，直接调用拼音输入处理ESC
                            if (IsPinyinInputActive)
                            {
                                //#if DEBUG
                                //System.Diagnostics.Debug.WriteLine("[全局热键] ESC键 - 拼音输入激活，关闭输入框");
                                //#endif
                                
                                // 直接调用拼音输入管理器处理ESC键
                                await ProcessPinyinEscapeKeyAsync();
                                return;
                            }
                            
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine("[全局热键] ESC键 - 开始处理投影关闭");
                            //#endif
                            
                            // 如果正在播放视频，先停止播放并重置界面
                            if (_videoPlayerManager != null && _videoPlayerManager.IsPlaying)
                            {
                                SwitchToImageMode();
                            }
                            
                            // 关闭投影
                            if (_projectionManager != null)
                            {
                                bool wasClosed = _projectionManager.CloseProjection();
                                //#if DEBUG
                                //if (wasClosed)
                                //{
                                //    System.Diagnostics.Debug.WriteLine("[全局热键] ESC键 - 已关闭投影");
                                //}
                                //#endif
                            }
                        });
                    });
                
                //System.Diagnostics.Debug.WriteLine("✅ 全局热键已启用（投影模式）- 使用原来的按键");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 启用全局热键失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 禁用全局热键（退出投影模式时调用）
        /// </summary>
        private void DisableGlobalHotKeys()
        {
            if (_globalHotKeyManager == null)
                return;

            try
            {
                _globalHotKeyManager.UnregisterAllHotKeys();
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 禁用全局热键失败: {ex.Message}");
            }
        }

        #endregion
    }
}

