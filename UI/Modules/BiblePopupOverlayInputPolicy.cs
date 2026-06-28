using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace ImageColorChanger.UI.Modules
{
    /// <summary>
    /// 幻灯片经文弹出层输入策略。
    /// </summary>
    public static class BiblePopupOverlayInputPolicy
    {
        public static int GetDirectionFromKey(Key key)
        {
            if (key == Key.Up)
            {
                return -1;
            }

            if (key == Key.Down)
            {
                return 1;
            }

            return 0;
        }

        public static bool TryGetClickedVerseIndex(
            double viewportTop,
            double clickY,
            double scrollOffset,
            IReadOnlyList<double> verseAnchors,
            IReadOnlyList<double> verseHeights,
            double fallbackLineHeight,
            out int index)
        {
            index = -1;
            int verseCount = Math.Min(verseAnchors?.Count ?? 0, verseHeights?.Count ?? 0);
            if (verseCount <= 0)
            {
                return false;
            }

            double relativeY = clickY - viewportTop + Math.Max(0, scrollOffset);
            for (int i = 0; i < verseCount; i++)
            {
                double top = verseAnchors[i];
                double bottom = top + Math.Max(1.0, verseHeights[i]);
                if (relativeY >= top && relativeY < bottom)
                {
                    index = i;
                    return true;
                }
            }

            double safeLineHeight = Math.Max(1.0, fallbackLineHeight);
            index = Math.Clamp((int)Math.Round(relativeY / safeLineHeight), 0, verseCount - 1);
            return true;
        }

        public static bool TryMoveHighlightedVerse(
            int currentIndex,
            double currentScrollOffset,
            IReadOnlyList<double> verseAnchors,
            int direction,
            double maxScroll,
            out int targetIndex,
            out double nextOffset)
        {
            targetIndex = currentIndex;
            nextOffset = Math.Clamp(currentScrollOffset, 0, Math.Max(0, maxScroll));

            int verseCount = verseAnchors?.Count ?? 0;
            int normalizedDirection = BibleVerseNavigationInputPolicy.NormalizeDirection(direction);
            if (verseCount <= 0 || normalizedDirection == 0)
            {
                return false;
            }

            int sourceIndex = currentIndex;
            if (sourceIndex < 0 || sourceIndex >= verseCount)
            {
                sourceIndex = FindIndexAtScrollOffset(verseAnchors, currentScrollOffset);
            }

            targetIndex = Math.Clamp(sourceIndex + normalizedDirection, 0, verseCount - 1);
            if (targetIndex == sourceIndex)
            {
                targetIndex = sourceIndex;
                return false;
            }

            nextOffset = Math.Clamp(verseAnchors[targetIndex], 0, Math.Max(0, maxScroll));
            return true;
        }

        private static int FindIndexAtScrollOffset(IReadOnlyList<double> verseAnchors, double scrollOffset)
        {
            const double epsilon = 0.5;
            double safeOffset = Math.Max(0, scrollOffset);
            int index = 0;
            for (int i = 0; i < verseAnchors.Count; i++)
            {
                if (verseAnchors[i] <= safeOffset + epsilon)
                {
                    index = i;
                }
                else
                {
                    break;
                }
            }

            return index;
        }
    }
}
