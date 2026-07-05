namespace ImageColorChanger.UI.Modules
{
    internal static class DraggableTextBoxInteractionPolicy
    {
        public static bool ShouldEnableDragAreaHitTesting(
            bool isSelected,
            bool isInEditMode,
            bool isNoticeComponent)
        {
            return isSelected && !isInEditMode && !isNoticeComponent;
        }

        public static bool CanStartDragAreaDrag(
            bool isSelected,
            bool isInEditMode,
            bool isNoticeComponent)
        {
            return ShouldEnableDragAreaHitTesting(isSelected, isInEditMode, isNoticeComponent);
        }
    }
}
