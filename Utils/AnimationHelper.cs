using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using static ImageColorChanger.Utils.EasingFunctions;

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
        /// <returns>创建的动画故事板</returns>
        public static Storyboard AnimateScroll(
            ScrollViewer scrollViewer,
            double fromOffset,
            double toOffset,
            TimeSpan duration,
            Action onCompleted = null,
            string easingType = "Bezier",
            bool isLinear = false)
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
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"🎬 [滚动动画] 缓动函数: {easingType}, 距离: {scrollDistance:F1}px, 时长: {duration.TotalSeconds:F1}秒");
                #endif
            }
            else
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"🎬 [滚动动画] 线性滚动, 距离: {Math.Abs(toOffset - fromOffset):F1}px, 时长: {duration.TotalSeconds:F1}秒");
                #endif
            }
            
            // 🎯 性能优化：明确指定60 FPS帧率
            Timeline.SetDesiredFrameRate(animation, 60);

            // 创建故事板
            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);

            // 设置动画目标
            Storyboard.SetTarget(animation, scrollViewer);
            Storyboard.SetTargetProperty(animation, new PropertyPath(AnimatedVerticalOffsetProperty));

            // 动画完成事件
            if (onCompleted != null)
            {
                storyboard.Completed += (s, e) => 
                {
                    // 停止性能监控
                    PerformanceMonitor.Instance.StopScrollMonitoring();
                    onCompleted();
                };
            }
            else
            {
                storyboard.Completed += (s, e) => 
                {
                    // 停止性能监控
                    PerformanceMonitor.Instance.StopScrollMonitoring();
                };
            }

            // 开始性能监控（所有滚动都监控，包括线性）
            string monitoringType = isLinear ? "Linear" : easingType;
            PerformanceMonitor.Instance.StartScrollMonitoring(monitoringType);

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
                "OptimizedCubic" => new OptimizedCubicEase(),
                "EaseOutExpo" => new ExponentialEaseOut(),
                "Bezier" => new BezierEase 
                { 
                    P1X = 0.25, 
                    P1Y = 0.1, 
                    P2X = 0.25, 
                    P2Y = 1.0 
                },
                "CssEaseInOut" => new CssEaseInOut(),
                // 🆕 新增优化的缓动函数
                "UltraSmooth" => new UltraSmoothEase(),
                "Physics" => new PhysicsEase { InitialVelocity = 2.0, Friction = -2.0 },
                "Adaptive" => new AdaptiveEase { ScrollDistance = scrollDistance },
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
}

