using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;

namespace ImageColorChanger.Database.Repositories
{
    public interface IOriginalMarkRepository
    {
        OriginalMark MarkAsOriginal(ItemType itemType, int itemId, MarkType markType = MarkType.Loop);
        void UnmarkAsOriginal(ItemType itemType, int itemId);
        bool HasOriginalMark(ItemType itemType, int itemId);

        bool AddOriginalMark(OriginalMark mark);
        bool RemoveOriginalMark(ItemType itemType, int itemId);
        bool CheckOriginalMark(ItemType itemType, int itemId);
        MarkType? GetOriginalMarkType(ItemType itemType, int itemId);
    }
}
