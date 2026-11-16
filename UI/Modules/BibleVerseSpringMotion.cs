using System;

namespace ImageColorChanger.UI.Modules
{
    /// <summary>短距离经文滚动的临界阻尼解析解。时间以秒计，不依赖每帧固定步长。</summary>
    public sealed class BibleVerseSpringMotion
    {
        // 构造初始响应保持兼容；主程序每次滚动前通过SetSpeedLevel应用配置。
        private double _naturalFrequency = 18.0;
        private const double PositionTolerance = 0.25;
        private const double VelocityTolerance = 5.0;

        public BibleVerseSpringMotion(double position) { Reset(position); }
        public double Position { get; private set; }
        public double Velocity { get; private set; }
        public double Target { get; private set; }
        public bool IsAtRest => Math.Abs(Position - Target) <= PositionTolerance && Math.Abs(Velocity) <= VelocityTolerance;

        public void SetSpeedLevel(int level)
        {
            // 不Reset，活动改速保留当前位置、速度和目标。
            // 三档w5/7.5/10：3档与原1档一致，1/2档进一步放慢，不改变柔滑曲线。
            _naturalFrequency = 2.5 + 2.5 * Math.Clamp(level, 1, 3);
        }

        public void Reset(double position)
        {
            RequireFinite(position, nameof(position));
            Position = Target = position;
            Velocity = 0;
        }

        public void Retarget(double target)
        {
            RequireFinite(target, nameof(target));
            // 连续输入仅改目标，不清零已有位置/速度。
            Target = target;
        }

        public double Advance(double seconds)
        {
            RequireFinite(seconds, nameof(seconds));
            if (seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (seconds == 0) return Position;
            if (seconds >= 2 || IsAtRest) { Snap(); return Position; }

            double a = Position - Target;
            double b = Velocity + _naturalFrequency * a;
            double decay = Math.Exp(-_naturalFrequency * seconds);
            double next = Target + (a + b * seconds) * decay;
            double nextVelocity = (Velocity - _naturalFrequency * b * seconds) * decay;
            double direction = Target - Position;
            if (!double.IsFinite(next) || !double.IsFinite(nextVelocity) ||
                (direction > 0 && next >= Target) || (direction < 0 && next <= Target))
            {
                // 保留反向输入的连续速度；沿目标方向越过终点时不做回弹。
                Snap();
            }
            else
            {
                Position = next;
                Velocity = nextVelocity;
                if (IsAtRest) Snap();
            }
            return Position;
        }

        private void Snap() { Position = Target; Velocity = 0; }
        private static void RequireFinite(double value, string parameter)
        {
            if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(parameter);
        }
    }
}
