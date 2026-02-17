using System;
using System.Linq;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;

namespace ImageColorChanger.Database.Repositories
{
    public sealed class OriginalMarkRepository : IOriginalMarkRepository
    {
        private readonly CanvasDbContext _context;

        public OriginalMarkRepository(CanvasDbContext context)
        {
            _context = context;
        }

        public OriginalMark MarkAsOriginal(ItemType itemType, int itemId, MarkType markType = MarkType.Loop)
        {
            var itemTypeStr = itemType == ItemType.Image ? "image" : "folder";
            var existing = _context.OriginalMarks.FirstOrDefault(m => m.ItemTypeString == itemTypeStr && m.ItemId == itemId);
            if (existing != null)
            {
                existing.MarkType = markType;
                _context.SaveChanges();
                return existing;
            }

            var mark = new OriginalMark
            {
                ItemType = itemType,
                ItemId = itemId,
                MarkType = markType,
                CreatedTime = DateTime.Now
            };
            _context.OriginalMarks.Add(mark);
            _context.SaveChanges();
            return mark;
        }

        public void UnmarkAsOriginal(ItemType itemType, int itemId)
        {
            var itemTypeStr = itemType == ItemType.Image ? "image" : "folder";
            var mark = _context.OriginalMarks.FirstOrDefault(m => m.ItemTypeString == itemTypeStr && m.ItemId == itemId);
            if (mark != null)
            {
                _context.OriginalMarks.Remove(mark);
                _context.SaveChanges();
            }
        }

        public bool HasOriginalMark(ItemType itemType, int itemId)
        {
            var itemTypeStr = itemType == ItemType.Image ? "image" : "folder";
            return _context.OriginalMarks.Any(m => m.ItemTypeString == itemTypeStr && m.ItemId == itemId);
        }

        public bool AddOriginalMark(OriginalMark mark)
        {
            try
            {
                var existing = _context.OriginalMarks.FirstOrDefault(m => m.ItemTypeString == mark.ItemTypeString && m.ItemId == mark.ItemId);
                if (existing != null)
                {
                    existing.MarkTypeString = mark.MarkTypeString;
                    existing.CreatedTime = DateTime.Now;
                }
                else
                {
                    _context.OriginalMarks.Add(mark);
                }

                _context.SaveChanges();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool RemoveOriginalMark(ItemType itemType, int itemId)
        {
            try
            {
                string itemTypeStr = itemType == ItemType.Image ? "image" : "folder";
                var mark = _context.OriginalMarks.FirstOrDefault(m => m.ItemTypeString == itemTypeStr && m.ItemId == itemId);
                if (mark != null)
                {
                    _context.OriginalMarks.Remove(mark);
                    _context.SaveChanges();
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool CheckOriginalMark(ItemType itemType, int itemId)
        {
            try
            {
                string itemTypeStr = itemType == ItemType.Image ? "image" : "folder";
                return _context.OriginalMarks.Any(m => m.ItemTypeString == itemTypeStr && m.ItemId == itemId);
            }
            catch
            {
                return false;
            }
        }

        public MarkType? GetOriginalMarkType(ItemType itemType, int itemId)
        {
            try
            {
                string itemTypeStr = itemType == ItemType.Image ? "image" : "folder";
                var mark = _context.OriginalMarks.FirstOrDefault(m => m.ItemTypeString == itemTypeStr && m.ItemId == itemId);
                return mark?.MarkType;
            }
            catch
            {
                return null;
            }
        }
    }
}
