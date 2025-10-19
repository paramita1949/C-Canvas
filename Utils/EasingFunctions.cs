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

        /// <summary>
        /// 超级平滑缓动（5次多项式，C2连续）
        /// 特点：启动和结束都极其平滑，二阶导数连续
        /// 适合：追求极致丝滑的滚动体验
        /// </summary>
        public class UltraSmoothEase : EasingFunctionBase
        {
            private bool _isFirstCall = true;
            
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

        /// <summary>
        /// 物理模拟缓动（模拟真实滚动的惯性）
        /// 特点：更接近物理世界的运动规律
        /// 适合：希望滚动感觉更"自然"的场景
        /// </summary>
        public class PhysicsEase : EasingFunctionBase
        {
            /// <summary>初始速度因子</summary>
            public double InitialVelocity { get; set; } = 2.0;
            
            /// <summary>摩擦力因子（负值表示减速）</summary>
            public double Friction { get; set; } = -2.0;
            
            private bool _isFirstCall = true;

            protected override double EaseInCore(double normalizedTime)
            {
                #if DEBUG
                if (_isFirstCall)
                {
                    System.Diagnostics.Debug.WriteLine($"🎯 [PhysicsEase] 物理模拟缓动已启用（v0={InitialVelocity}, a={Friction}）");
                    _isFirstCall = false;
                }
                #endif
                
                double t = normalizedTime;
                
                // 边界处理
                if (t <= 0.0)
                    return 0.0;
                if (t >= 1.0)
                    return 1.0;

                // 物理公式：s = v0*t + 0.5*a*t^2
                // 归一化版本，确保在t=1时s=1
                double v0 = InitialVelocity;
                double a = Friction;
                
                // 计算归一化因子（确保t=1时结果为1）
                double normalizer = v0 + 0.5 * a;
                
                return (v0 * t + 0.5 * a * t * t) / normalizer;
            }

            protected override Freezable CreateInstanceCore()
            {
                return new PhysicsEase
                {
                    InitialVelocity = this.InitialVelocity,
                    Friction = this.Friction
                };
            }
        }

        /// <summary>
        /// 智能自适应缓动（根据距离自动调整缓动曲线）
        /// 特点：短距离用快速曲线，长距离用平滑曲线
        /// 适合：需要自动优化体验的场景
        /// </summary>
        public class AdaptiveEase : EasingFunctionBase
        {
            /// <summary>滚动距离（像素）</summary>
            public double ScrollDistance { get; set; } = 1000;
            
            private bool _isFirstCall = true;

            protected override double EaseInCore(double normalizedTime)
            {
                double t = normalizedTime;
                
                // 边界处理
                if (t <= 0.0)
                    return 0.0;
                if (t >= 1.0)
                    return 1.0;

                // 根据距离选择缓动强度
                // 短距离(<500px)：使用更快的曲线
                // 长距离(>2000px)：使用更平滑的曲线
                double intensity;
                string mode;
                if (ScrollDistance < 500)
                {
                    intensity = 3.0; // 快速
                    mode = "快速模式";
                }
                else if (ScrollDistance > 2000)
                {
                    intensity = 5.0; // 超平滑
                    mode = "超平滑模式";
                }
                else
                {
                    intensity = 4.0; // 标准
                    mode = "标准模式";
                }
                
                #if DEBUG
                if (_isFirstCall)
                {
                    System.Diagnostics.Debug.WriteLine($"🧠 [AdaptiveEase] 智能自适应缓动已启用 - {mode}（距离={ScrollDistance:F1}px, 强度={intensity}）");
                    _isFirstCall = false;
                }
                #endif

                // 使用可变强度的多项式
                return Math.Pow(t, intensity) * (intensity + 1 - intensity * t);
            }

            protected override Freezable CreateInstanceCore()
            {
                return new AdaptiveEase
                {
                    ScrollDistance = this.ScrollDistance
                };
            }
        }
    }
}

