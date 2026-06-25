using System.Linq;
using System.IO;
using ImageColorChanger.UI.Modules;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Ui
{
    public sealed class BibleSettingsFontSizeOptionsTests
    {
        [Fact]
        public void Values_KeepExistingRangeAndExtendTo160()
        {
            var values = BibleSettingsFontSizeOptions.Values.ToArray();

            Assert.Contains(46, values);
            Assert.Contains(80, values);
            Assert.Contains(160, values);
            Assert.Equal(30, values.First());
            Assert.Equal(160, values.Last());
        }

        [Fact]
        public void DefaultValue_Remains46()
        {
            Assert.Equal(46, BibleSettingsFontSizeOptions.DefaultValue);
        }

        [Fact]
        public void BibleSettingsWindow_PopulatesMainFontSizeFromSharedOptions()
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            string xaml = File.ReadAllText(Path.Combine(root, "UI", "BibleSettingsWindow.xaml"));
            string code = File.ReadAllText(Path.Combine(root, "UI", "BibleSettingsWindow.xaml.cs"));
            int fontSizeComboStart = xaml.IndexOf("x:Name=\"CmbFontSize\"", StringComparison.Ordinal);
            int fontSizeGridEnd = xaml.IndexOf("</Grid>", fontSizeComboStart, StringComparison.Ordinal);
            string fontSizeComboMarkup = xaml.Substring(fontSizeComboStart, fontSizeGridEnd - fontSizeComboStart);

            Assert.DoesNotContain("ComboBoxItem", fontSizeComboMarkup);
            Assert.Contains("PopulateBibleFontSizeOptions();", code);
            Assert.Contains("BibleSettingsFontSizeOptions.Values", code);
        }
    }
}
