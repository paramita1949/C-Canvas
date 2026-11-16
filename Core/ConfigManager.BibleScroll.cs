namespace ImageColorChanger.Core
{
    public enum BibleVerseScrollAlgorithm
    {
        Legacy = 0,
        CriticalDampedTest = 1
    }

    public partial class ConfigManager
    {
        // 三档慢速使用独立新字段，避免把已保存的旧1..10档静默重解释。
        // 缺字段默认1档；已有显式1..3档保留，读取不写盘，旧慢/中/快不受影响。
        public int BibleVerseSpringSpeedLevel
        {
            get => System.Math.Clamp(_config.BibleVerseSpringSlowLevel ?? 1, 1, 3);
            set
            {
                int normalized = System.Math.Clamp(value, 1, 3);
                if (_config.BibleVerseSpringSlowLevel != normalized)
                {
                    _config.BibleVerseSpringSlowLevel = normalized;
                    SaveConfig();
                }
            }
        }

        // 算法和旧速度独立存储；缺字段默认柔滑，已有选择不强制重置。
        public BibleVerseScrollAlgorithm BibleVerseScrollAlgorithm
        {
            get => NormalizeBibleScrollAlgorithm(_config.BibleVerseScrollAlgorithm);
            set
            {
                var normalized = NormalizeBibleScrollAlgorithm(value);
                if (_config.BibleVerseScrollAlgorithm != normalized)
                {
                    _config.BibleVerseScrollAlgorithm = normalized;
                    SaveConfig();
                }
            }
        }

        private static BibleVerseScrollAlgorithm NormalizeBibleScrollAlgorithm(BibleVerseScrollAlgorithm value)
        {
            return value == Core.BibleVerseScrollAlgorithm.CriticalDampedTest
                ? Core.BibleVerseScrollAlgorithm.CriticalDampedTest
                : Core.BibleVerseScrollAlgorithm.Legacy;
        }
    }

    public partial class AppConfig
    {
        // 兼容历史JSON，保留旧数值；运行时不再使用旧1..10档字段。
        public int BibleVerseSpringSpeedLevel { get; set; } = 5;
        public int? BibleVerseSpringSlowLevel { get; set; }
        public BibleVerseScrollAlgorithm BibleVerseScrollAlgorithm { get; set; } = Core.BibleVerseScrollAlgorithm.CriticalDampedTest;
    }
}
