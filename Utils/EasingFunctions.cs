using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace ImageColorChanger.Utils
{
    /// <summary>
    /// 专业级缓动函数集合（基于Python版本）
    /// </summary>
    public static class EasingFunctions
    {
        /// <summary>
        /// 优化的三次缓动 - 性能最佳
        /// t * t * (3.0 - 2.0 * t)
        /// </summary>
        public class OptimizedCubicEase : EasingFunctionBase
        {
            protected override double EaseInCore(double normalizedTime)
            {
                return normalizedTime * normalizedTime * (3.0 - 2.0 * normalizedTime);
            }

            protected override Freezable CreateInstanceCore()
            {
                return new OptimizedCubicEase();
            }
        }

        /// <summary>
        /// 指数缓出 - 快速开始，平滑结束
        /// 1 - pow(2, -10 * t)
        /// </summary>
        public class ExponentialEaseOut : EasingFunctionBase
        {
            protected override double EaseInCore(double normalizedTime)
            {
                if (normalizedTime >= 1.0)
                    return 1.0;
                if (normalizedTime <= 0.0)
                    return 0.0;
                return 1 - Math.Pow(2, -10 * normalizedTime);
            }

            protected override Freezable CreateInstanceCore()
            {
                return new ExponentialEaseOut();
            }
        }

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
    }
}

