using System;
using System.IO;
using ImageColorChanger.UI.Modules;

namespace Canvas.TextEditor.Tests.UI
{
    public sealed class DraggableTextBoxInteractionPolicyTests
    {
        [Theory]
        [InlineData(false, false, false, false)]
        [InlineData(true, true, false, false)]
        [InlineData(true, false, true, false)]
        [InlineData(true, false, false, true)]
        public void DragAreaHitTesting_ShouldOnlyBeEnabledForSelectedNonEditingRegularTextBox(
            bool isSelected,
            bool isInEditMode,
            bool isNoticeComponent,
            bool expected)
        {
            bool actual = DraggableTextBoxInteractionPolicy.ShouldEnableDragAreaHitTesting(
                isSelected,
                isInEditMode,
                isNoticeComponent);

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(false, false, false, false)]
        [InlineData(true, true, false, false)]
        [InlineData(true, false, true, false)]
        [InlineData(true, false, false, true)]
        public void DragAreaMouseDown_ShouldFollowSameGateAsHitTesting(
            bool isSelected,
            bool isInEditMode,
            bool isNoticeComponent,
            bool expected)
        {
            bool actual = DraggableTextBoxInteractionPolicy.CanStartDragAreaDrag(
                isSelected,
                isInEditMode,
                isNoticeComponent);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void DraggableTextBox_ShouldWireDragAreasThroughInteractionPolicy()
        {
            string root = FindRepoRoot();
            string dragSource = File.ReadAllText(Path.Combine(root, "UI", "Controls", "DraggableTextBox.Drag.cs"));
            string initSource = File.ReadAllText(Path.Combine(root, "UI", "Controls", "DraggableTextBox.Init.cs"));
            string editSource = File.ReadAllText(Path.Combine(root, "UI", "Controls", "DraggableTextBox.EditMode.cs"));
            string selectionSource = File.ReadAllText(Path.Combine(root, "UI", "Controls", "DraggableTextBox.Selection.cs"));

            Assert.Contains("DraggableTextBoxInteractionPolicy.CanStartDragAreaDrag", dragSource);
            Assert.Contains("UpdateDragAreaHitTesting();", initSource);
            Assert.Contains("UpdateDragAreaHitTesting();", editSource);
            Assert.Contains("UpdateDragAreaHitTesting();", selectionSource);
        }

        [Fact]
        public void ZIndexPolicy_ShouldUseBothDataAndPanelLayerSources()
        {
            int actual = TextBoxZIndexPolicy.GetNextFrontZIndex(new[]
            {
                (DataZIndex: 4, PanelZIndex: 12),
                (DataZIndex: 9, PanelZIndex: 3)
            });

            Assert.Equal(13, actual);
        }

        [Fact]
        public void BringTextBoxToFront_ShouldSyncPanelAndDataZIndex()
        {
            string helpersSource = File.ReadAllText(Path.Combine(FindRepoRoot(), "UI", "MainWindow.TextEditor.Helpers.cs"));

            Assert.Contains("TextBoxZIndexPolicy.GetNextFrontZIndex", helpersSource);
            Assert.Contains("target.Data.ZIndex = targetZ;", helpersSource);
            Assert.Contains("Panel.SetZIndex(target, targetZ)", helpersSource);
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "UI", "MainWindow.xaml")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate Canvas repo root from test output directory.");
        }
    }
}
