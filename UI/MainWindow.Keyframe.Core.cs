using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Managers.Keyframes;
using MessageBox = System.Windows.MessageBox;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow Keyframe Core (Fields, State, Initialization)
    /// </summary>
    public partial class MainWindow
    {
        #region 关键帧和播放字段

        private KeyframeManager _keyframeManager;
        
        // 滚动速度设置（默认9秒）
        private double _scrollDuration = 9.0;
        
        // 滚动缓动类型（默认线性）
        private string _scrollEasingType = "Linear";
        
        // 是否使用线性滚动（无缓动）
        private bool _isLinearScrolling = true;
        
        // 合成播放的Storyboard引用（用于停止时清除）
        private System.Windows.Media.Animation.Storyboard _compositeScrollStoryboard = null;

        #endregion

        #region 关键帧状态管理

        /// <summary>
        /// 重置关键帧索引到-1（图片加载时调用）
        /// 参考Python版本：image_processor.py 第341行
        /// </summary>
        public void ResetKeyframeIndex()
        {
            if (_keyframeManager != null)
            {
                _keyframeManager.CurrentKeyframeIndex = -1;
                // System.Diagnostics.Debug.WriteLine(" [图片加载] 重置关键帧索引为-1");
                
                // 更新关键帧预览线和指示块
                _keyframeManager?.UpdatePreviewLines();
            }
        }

        #endregion

        #region 关键帧初始化

        /// <summary>
        /// 初始化关键帧和播放系统
        /// </summary>
        private void InitializeKeyframeSystem()
        {
            try
            {
                // 获取数据库上下文
                var dbContext = _dbContext;
                if (dbContext == null)
                {
                    // Console.WriteLine(" 数据库上下文未就绪");
                    return;
                }

                // 获取关键帧存储抽象（由 DI 装配）
                var keyframeStore = _mainWindowServices.GetRequired<IKeyframeStore>();

                // 获取MediaFileRepository
                _mediaFileRepository ??= _mainWindowServices.GetRequired<Repositories.Interfaces.IMediaFileRepository>();

                // 创建关键帧管理器
                _keyframeManager = new KeyframeManager(keyframeStore, this, _mediaFileRepository);
                
                // 从数据库加载滚动速度和缓动函数设置
                LoadScrollSpeedSettings();
                LoadScrollEasingSettings();
                
                // 初始化菜单选中状态
                Dispatcher.InvokeAsync(() =>
                {
                    UpdateScrollSpeedMenuCheck(_scrollDuration);
                    string easingName = _isLinearScrolling ? "Linear" : _scrollEasingType;
                    UpdateScrollEasingMenuCheck(easingName);
                }, System.Windows.Threading.DispatcherPriority.Loaded);

                // System.Diagnostics.Debug.WriteLine(" 关键帧和播放系统初始化完成");
            }
            catch (Exception ex)
            {
                // System.Diagnostics.Debug.WriteLine($" 关键帧系统初始化异常: {ex.Message}");
                MessageBox.Show($"关键帧系统初始化失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

    }
}

