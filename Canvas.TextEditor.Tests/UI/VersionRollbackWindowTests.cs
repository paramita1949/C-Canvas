using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Canvas.TextEditor.Tests.UI
{
    public sealed class VersionRollbackWindowTests
    {
        private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        [Fact]
        public void Layout_UsesResizableSplitViewAndExistingBrandResources()
        {
            var document = XDocument.Load(Path.Combine(FindRoot(), "UI", "VersionRollbackWindow.xaml"));
            Assert.Equal("None", document.Root.Attribute("WindowStyle")?.Value);
            Assert.Equal("CanResize", document.Root.Attribute("ResizeMode")?.Value);
            Assert.Contains(document.Descendants(), e => e.Attribute(Xaml + "Name")?.Value == "VersionSelectionPanel");
            Assert.Contains(document.Descendants(), e => e.Attribute(Xaml + "Name")?.Value == "ReadmePanel");
            Assert.Contains("BrushDialogBrandPrimary", document.ToString());
            Assert.Contains("CompactScrollBarStyleLight", document.ToString());
        }

        [Fact]
        public void DefaultState_HasDisabledRollbackAndHiddenDownloadProgress()
        {
            var document = XDocument.Load(Path.Combine(FindRoot(), "UI", "VersionRollbackWindow.xaml"));
            Assert.Equal("False", Named(document, "RollbackButton").Attribute("IsEnabled")?.Value);
            Assert.Equal("Collapsed", Named(document, "ProgressPanel").Attribute("Visibility")?.Value);
            Assert.Contains("仅显示低于当前版本", document.ToString());
            Assert.DoesNotContain("不高于当前版本", document.ToString());
        }

        [Fact]
        public void VersionMapping_UsesCanonicalSelectionAndBusyStateNotDisplayText()
        {
            var code = ReadCode();
            Assert.Contains("_versions[index]", code);
            Assert.Contains("RollbackButton.IsEnabled = !_isBusy && selectedVersion != null", code);
            Assert.Contains("VersionsListBox.IsEnabled = !busy", code);
            Assert.DoesNotContain("SelectedItem.ToString", code);
            Assert.DoesNotContain("Substring(1)", code);
        }

        [Fact]
        public void RefreshAndReadme_DiscardStaleTargetsAndResponses()
        {
            var code = ReadCode();
            Assert.True(code.IndexOf("VersionsListBox.ItemsSource = null", StringComparison.Ordinal)
                < code.IndexOf("await UpdateService.GetRollbackVersionsAsync", StringComparison.Ordinal));
            Assert.Contains("requestId != _readmeRequestId", code);
            Assert.Contains("requestId == _readmeRequestId", code);
            Assert.DoesNotContain("修复若干BUG", code);
        }

        [Fact]
        public void Installation_KeepsConfirmationManifestDownloadAndApplyOrder()
        {
            var code = ReadCode();
            var confirmation = code.IndexOf("var confirm = MessageBox.Show", StringComparison.Ordinal);
            var manifest = code.IndexOf("await UpdateService.GetVersionInfoAsync", StringComparison.Ordinal);
            var download = code.IndexOf("await UpdateService.DownloadUpdateAsync", StringComparison.Ordinal);
            var apply = code.IndexOf("UpdateService.ApplyUpdate(updateDir)", StringComparison.Ordinal);
            Assert.True(confirmation >= 0 && confirmation < manifest && manifest < download && download < apply);
            Assert.Contains("if (_isBusy || _closed) return", code);
            Assert.Contains("安装后自动重启", code);
        }

        private static XElement Named(XDocument document, string name) =>
            document.Descendants().Single(e => e.Attribute(Xaml + "Name")?.Value == name);
        private static string ReadCode() => File.ReadAllText(Path.Combine(FindRoot(), "UI", "VersionRollbackWindow.xaml.cs"));
        private static string FindRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "UI", "VersionRollbackWindow.xaml"))) return directory.FullName;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Canvas source root not found");
        }
    }
}
