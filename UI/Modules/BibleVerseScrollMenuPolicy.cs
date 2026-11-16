using System.Windows.Controls;
using ImageColorChanger.Core;

namespace ImageColorChanger.UI.Modules
{
    public static class BibleVerseScrollMenuPolicy
    {
        public static void Refresh(ConfigManager config, MenuItem slow, MenuItem medium, MenuItem fast, MenuItem test)
        {
            bool legacy = (config?.BibleVerseScrollAlgorithm ?? BibleVerseScrollAlgorithm.CriticalDampedTest) == BibleVerseScrollAlgorithm.Legacy;
            int speed = config?.BibleVerseScrollSpeed ?? 1;
            if (slow != null) slow.IsChecked = legacy && speed == 0;
            if (medium != null) medium.IsChecked = legacy && speed == 1;
            if (fast != null) fast.IsChecked = legacy && speed == 2;
            if (test != null) test.IsChecked = !legacy;
        }

        public static void SelectLegacy(ConfigManager config, int speed)
        {
            if (config == null) return;
            config.BibleVerseScrollAlgorithm = BibleVerseScrollAlgorithm.Legacy;
            config.BibleVerseScrollSpeed = speed;
            // 选档即启用对应算法；旧字段只为历史版本配置兼容保留。
            config.BibleVerseScrollAnimationEnabled = true;
        }

        public static void SelectTest(ConfigManager config)
        {
            if (config == null) return;
            config.BibleVerseScrollAlgorithm = BibleVerseScrollAlgorithm.CriticalDampedTest;
            config.BibleVerseScrollAnimationEnabled = true;
        }

        public static void SelectTestSpeed(ConfigManager config, int level)
        {
            if (config == null) return;
            config.BibleVerseSpringSpeedLevel = level;
            SelectTest(config);
        }

        public static void RefreshTestSpeed(ConfigManager config, MenuItem parent)
        {
            if (parent == null) return;
            int level = config?.BibleVerseSpringSpeedLevel ?? 1;
            bool selected = (config?.BibleVerseScrollAlgorithm ?? BibleVerseScrollAlgorithm.CriticalDampedTest) == BibleVerseScrollAlgorithm.CriticalDampedTest;
            parent.Header = "柔滑滚动";
            parent.IsChecked = selected;
            foreach (object child in parent.Items)
            {
                if (child is MenuItem item && int.TryParse(item.Tag as string, out int value))
                    item.IsChecked = selected && value == level;
            }
        }

        public static void RefreshLegacy(ConfigManager config, MenuItem parent)
        {
            if (parent != null)
                parent.IsChecked = (config?.BibleVerseScrollAlgorithm ?? BibleVerseScrollAlgorithm.CriticalDampedTest) == BibleVerseScrollAlgorithm.Legacy;
        }
    }
}
