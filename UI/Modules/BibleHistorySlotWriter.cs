using System;
using System.Collections.Generic;
using System.Linq;
using ImageColorChanger.Core;
using ImageColorChanger.UI;

namespace ImageColorChanger.UI.Modules
{
    public sealed class BibleHistorySlotWriter : IBibleHistorySlotWriter
    {
        public BibleHistorySlotWriteResult TryAddHit(IList<MainWindow.BibleHistoryItem> slots, BibleSearchHit hit)
        {
            if (slots == null || hit == null || hit.Book <= 0 || hit.Chapter <= 0 || hit.Verse <= 0)
            {
                return new BibleHistorySlotWriteResult
                {
                    Status = BibleHistorySlotWriteStatus.Invalid,
                    SlotIndex = -1,
                    Message = "记录无效"
                };
            }

            bool duplicate = slots.Any(slot =>
                slot.BookId == hit.Book &&
                slot.Chapter == hit.Chapter &&
                slot.StartVerse == hit.Verse &&
                slot.EndVerse == hit.Verse);

            if (duplicate)
            {
                return new BibleHistorySlotWriteResult
                {
                    Status = BibleHistorySlotWriteStatus.Duplicate,
                    SlotIndex = -1,
                    Message = "记录已存在"
                };
            }

            var targetSlot = slots
                .OrderBy(s => s.Index)
                .FirstOrDefault(s => s.BookId <= 0 || string.IsNullOrWhiteSpace(s.DisplayText));

            if (targetSlot == null)
            {
                return new BibleHistorySlotWriteResult
                {
                    Status = BibleHistorySlotWriteStatus.Full,
                    SlotIndex = -1,
                    Message = "槽位已满，请先清理"
                };
            }

            string bookName = BibleBookConfig.GetBook(hit.Book)?.Name ?? $"书卷{hit.Book}";
            targetSlot.BookId = hit.Book;
            targetSlot.Chapter = hit.Chapter;
            targetSlot.StartVerse = hit.Verse;
            targetSlot.EndVerse = hit.Verse;
            targetSlot.DisplayText = $"{bookName}{hit.Chapter}章{hit.Verse}节";
            targetSlot.IsChecked = false;
            targetSlot.IsLocked = false;

            return new BibleHistorySlotWriteResult
            {
                Status = BibleHistorySlotWriteStatus.Added,
                SlotIndex = targetSlot.Index,
                Message = $"已加入槽位 {targetSlot.Index}"
            };
        }
    }
}
