using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ImageColorChanger.Utils
{
    /// <summary>
    /// 动画辅助类
    /// 提供WPF动画相关的辅助方法
    /// </summary>
    public static class AnimationHelper
    {
        // 用于ScrollViewer动画的附加依赖属性
        private static readonly DependencyProperty AnimatedVerticalOffsetProperty =
            DependencyProperty.RegisterAttached(
                "AnimatedVerticalOffset",
                typeof(double),
                typeof(AnimationHelper),
                new PropertyMetadata(0.0, OnAnimatedVerticalOffsetChanged));

        /// <summary>
        /// 获取动画垂直偏移属性（用于清除动画）
        /// </summary>
        public static DependencyProperty GetAnimatedVerticalOffsetProperty()
        {
            return AnimatedVerticalOffsetProperty;
        }

        /// <summary>
        /// 设置动画垂直偏移
        /// </summary>
        public static void SetAnimatedVerticalOffset(UIElement element, double value)
        {
            element.SetValue(AnimatedVerticalOffsetProperty, value);
        }

        /// <summary>
        /// 获取动画垂直偏移
        /// </summary>
        public static double GetAnimatedVerticalOffset(UIElement element)
        {
            return (double)element.GetValue(AnimatedVerticalOffsetProperty);
        }

        /// <summary>
        /// 动画垂直偏移属性改变时的回调
        /// </summary>
        private static void OnAnimatedVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToVerticalOffset((double)e.NewValue);
            }
        }

        /// <summary>
        /// 为ScrollViewer添加平滑滚动动画
        /// </summary>
        /// <param name="scrollViewer">滚动视图控件</param>
        /// <param name="fromOffset">起始偏移量</param>
        /// <param name="toOffset">目标偏移量</param>
        /// <param name="duration">动画持续时间</param>
        /// <param name="onCompleted">动画完成回调</param>
        /// <param name="easingType">缓动类型</param>
        /// <param name="isLinear">是否线性滚动</param>
        /// <param name="speedRatio">动画速度倍率（默认1.0，用于直接加速滚动动画）</param>
        /// <returns>创建的动画故事板</returns>
        public static Storyboard AnimateScroll(
            ScrollViewer scrollViewer,
            double fromOffset,
            double toOffset,
            TimeSpan duration,
            Action onCompleted = null,
            string easingType = "Bezier",
            bool isLinear = false,
            double speedRatio = 1.0,
            Action onFrame = null)
        {
            if (scrollViewer == null)
                throw new ArgumentNullException(nameof(scrollViewer));

            // 创建双精度动画
            var animation = new DoubleAnimation
            {
                From = fromOffset,
                To = toOffset,
                Duration = new Duration(duration)
            };
            
            // 如果不是线性滚动，则使用缓动函数
            if (!isLinear)
            {
                // 计算滚动距离（用于自适应缓动）
                double scrollDistance = Math.Abs(toOffset - fromOffset);
                animation.EasingFunction = GetEasingFunction(easingType, scrollDistance);
            }
            
            //  极致性能优化：明确指定120 FPS帧率（与Python版本一致）
            // WPF默认60fps，提升到120fps可以获得更丝滑的滚动体验
            Timeline.SetDesiredFrameRate(animation, 120);

            // 创建故事板
            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            
            //  设置动画速度倍率（直接加速滚动动画）
            storyboard.SpeedRatio = speedRatio;

            // 设置动画目标
            Storyboard.SetTarget(animation, scrollViewer);
            Storyboard.SetTargetProperty(animation, new PropertyPath(AnimatedVerticalOffsetProperty));

            // 动画完成事件
            if (onCompleted != null)
            {
                storyboard.Completed += (s, e) => onCompleted?.Invoke();
            }

            if (onFrame != null)
            {
                storyboard.CurrentTimeInvalidated += (s, e) => onFrame?.Invoke();
            }

            // 开始动画
            storyboard.Begin();

            return storyboard;
        }

        /// <summary>
        /// 根据类型获取缓动函数（基于Python版本 + 优化扩展）
        /// </summary>
        /// <param name="easingType">缓动类型</param>
        /// <param name="scrollDistance">滚动距离（用于自适应缓动）</param>
        /// <returns>缓动函数实例</returns>
        private static IEasingFunction GetEasingFunction(string easingType, double scrollDistance = 1000)
        {
            return easingType switch
            {
                "Bezier" => new BezierEase 
                { 
                    P1X = 0.25, 
                    P1Y = 0.1, 
                    P2X = 0.25, 
                    P2Y = 1.0 
                },
                "CssEaseInOut" => new CssEaseInOut(),
                "UltraSmooth" => new UltraSmoothEase(),
                _ => new BezierEase() // 默认贝塞尔曲线
            };
        }

        /// <summary>
        /// 创建渐显动画
        /// </summary>
        /// <param name="element">目标元素</param>
        /// <param name="duration">持续时间</param>
        /// <param name="fromOpacity">起始透明度</param>
        /// <param name="toOpacity">目标透明度</param>
        public static void FadeIn(UIElement element, TimeSpan duration, double fromOpacity = 0.0, double toOpacity = 1.0)
        {
            var animation = new DoubleAnimation
            {
                From = fromOpacity,
                To = toOpacity,
                Duration = new Duration(duration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            element.BeginAnimation(UIElement.OpacityProperty, animation);
        }

        /// <summary>
        /// 创建渐隐动画
        /// </summary>
        /// <param name="element">目标元素</param>
        /// <param name="duration">持续时间</param>
        /// <param name="fromOpacity">起始透明度</param>
        /// <param name="toOpacity">目标透明度</param>
        public static void FadeOut(UIElement element, TimeSpan duration, double fromOpacity = 1.0, double toOpacity = 0.0)
        {
            var animation = new DoubleAnimation
            {
                From = fromOpacity,
                To = toOpacity,
                Duration = new Duration(duration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            element.BeginAnimation(UIElement.OpacityProperty, animation);
        }
    }

    // =====================================================================
    // 滚动缓动函数集合
    // =====================================================================

    /// <summary>
    /// 贝塞尔曲线缓动 - 精确的三次贝塞尔实现
    /// 控制点: (0,0), (0.25,0.1), (0.25,1.0), (1,1)
    /// </summary>
    public class BezierEase : EasingFunctionBase
    {
        public double P1X { get; set; } = 0.25;
        public double P1Y { get; set; } = 0.1;
        public double P2X { get; set; } = 0.25;
        public double P2Y { get; set; } = 1.0;

        protected override double EaseInCore(double normalizedTime)
        {
            double t = normalizedTime;
            
            // 边界处理
            if (t <= 0.0)
                return 0.0;
            if (t >= 1.0)
                return 1.0;

            // 精确的三次贝塞尔曲线实现
            // 控制点: (0,0), (p1x,p1y), (p2x,p2y), (1,1)
            double u = 1 - t;
            return 3 * u * u * t * P1Y +
                   3 * u * t * t * P2Y +
                   t * t * t;
        }

        protected override Freezable CreateInstanceCore()
        {
            return new BezierEase
            {
                P1X = this.P1X,
                P1Y = this.P1Y,
                P2X = this.P2X,
                P2Y = this.P2Y
            };
        }
    }

    /// <summary>
    /// CSS ease-in-out 等价函数
    /// cubic-bezier(0.42, 0, 0.58, 1)
    /// </summary>
    public class CssEaseInOut : EasingFunctionBase
    {
        protected override double EaseInCore(double normalizedTime)
        {
            double t = normalizedTime;
            
            // 边界处理
            if (t <= 0.0)
                return 0.0;
            if (t >= 1.0)
                return 1.0;

            // CSS ease-in-out: cubic-bezier(0.42, 0, 0.58, 1)
            double u = 1 - t;
            double p1y = 0.0;
            double p2y = 1.0;
            return 3 * u * u * t * p1y +
                   3 * u * t * t * p2y +
                   t * t * t;
        }

        protected override Freezable CreateInstanceCore()
        {
            return new CssEaseInOut();
        }
    }

    /// <summary>
    /// 超级平滑缓动（5次多项式，C2连续）
    /// 特点：启动和结束都极其平滑，二阶导数连续
    /// 适合：追求极致丝滑的滚动体验
    /// </summary>
    public class UltraSmoothEase : EasingFunctionBase
    {
        #if DEBUG
        private bool _isFirstCall = true;
        #endif
        
        protected override double EaseInCore(double normalizedTime)
        {
            #if DEBUG
            if (_isFirstCall)
            {
                System.Diagnostics.Debug.WriteLine($"✨ [UltraSmoothEase] 超级平滑缓动已启用（5次多项式）");
                _isFirstCall = false;
            }
            #endif
            
            double t = normalizedTime;
            
            // 边界处理
            if (t <= 0.0)
                return 0.0;
            if (t >= 1.0)
                return 1.0;

            // 5次多项式：6t^5 - 15t^4 + 10t^3
            // 这个函数的特点：
            // - 在t=0和t=1处，一阶导数和二阶导数都为0
            // - 保证了启动和结束时的极致平滑
            return t * t * t * (t * (t * 6 - 15) + 10);
        }

        protected override Freezable CreateInstanceCore()
        {
            return new UltraSmoothEase();
        }
    }
}


