using System;
using System.IO;
using Xunit;

namespace Canvas.TextEditor.Tests.UI
{
    public sealed class AiPlatformWindowLayoutTests
    {
        [Fact]
        public void AiPlatformWindow_UsesHtmlPrototypeASidebarAndDetailLayout()
        {
            string xaml = File.ReadAllText(FindWorkspaceFile("UI/AiPlatformWindow.xaml"));

            Assert.Contains("x:Name=\"ProfileListBox\"", xaml, StringComparison.Ordinal);
            Assert.Contains("x:Name=\"AddProfileButton\"", xaml, StringComparison.Ordinal);
            Assert.Contains("Text=\"我的配置\"", xaml, StringComparison.Ordinal);
            Assert.Contains("Grid.Column=\"1\"", xaml, StringComparison.Ordinal);
            Assert.Contains("x:Name=\"ProfileNameTextBox\"", xaml, StringComparison.Ordinal);
            Assert.Contains("Text=\"配置名称\"", xaml, StringComparison.Ordinal);
            Assert.Contains("Style=\"{StaticResource InputStyle}\"", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("Text=\"厂家预设\"", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("Header=\"高级设置\"", xaml, StringComparison.Ordinal);
            Assert.Contains("x:Name=\"DeleteProfileButton\"", xaml, StringComparison.Ordinal);
            Assert.Contains("Click=\"DeleteProfileButton_Click\"", xaml, StringComparison.Ordinal);
            Assert.Contains("Tag=\"{Binding Id}\"", xaml, StringComparison.Ordinal);
            Assert.Contains("IsEnabled=\"{Binding CanDelete}\"", xaml, StringComparison.Ordinal);
            int myProfilesIndex = xaml.IndexOf("Text=\"我的配置\"", StringComparison.Ordinal);
            int deleteIndex = xaml.IndexOf("x:Name=\"DeleteProfileButton\"", StringComparison.Ordinal);
            int detailIndex = xaml.IndexOf("x:Name=\"ProfileNameTextBox\"", StringComparison.Ordinal);
            Assert.True(deleteIndex > myProfilesIndex && deleteIndex < detailIndex, "删除快捷按钮应在左侧我的配置条目名称右边。");
            Assert.Contains("DropDownScrollBarStyle", xaml, StringComparison.Ordinal);
            Assert.Contains("DropDownItemStyle", xaml, StringComparison.Ordinal);
            Assert.Contains("MaxDropDownHeight", xaml, StringComparison.Ordinal);
        }

        [Fact]
        public void AiPlatformWindow_StretchesProfileRowsSoDeleteButtonsShareRightEdge()
        {
            string xaml = File.ReadAllText(FindWorkspaceFile("UI/AiPlatformWindow.xaml"));

            Assert.Contains(@"x:Name=""ProfileListBox""", xaml, StringComparison.Ordinal);
            Assert.Contains(@"HorizontalContentAlignment=""Stretch""", xaml, StringComparison.Ordinal);
            Assert.Contains(@"Grid HorizontalAlignment=""Stretch""", xaml, StringComparison.Ordinal);
            Assert.Contains(@"ContentPresenter HorizontalAlignment=""Stretch""", xaml, StringComparison.Ordinal);
        }

        [Fact]
        public void AiPlatformWindow_SavesCustomProfileNameAndDeletesFromListItem()
        {
            string codeBehind = File.ReadAllText(FindWorkspaceFile("UI/AiPlatformWindow.xaml.cs"));

            Assert.Contains("ProfileNameTextBox_TextChanged", codeBehind, StringComparison.Ordinal);
            Assert.Contains("profile.Name = string.IsNullOrWhiteSpace(ProfileNameTextBox.Text)", codeBehind, StringComparison.Ordinal);
            Assert.Contains("(sender as FrameworkElement)?.Tag as string", codeBehind, StringComparison.Ordinal);
            Assert.Contains("_configManager.DeleteAiProfile(selected.Id)", codeBehind, StringComparison.Ordinal);
        }

        [Fact]
        public void AiPlatformWindow_UsesProviderDisplayTemplateAndModernScrollBars()
        {
            string xaml = File.ReadAllText(FindWorkspaceFile("UI/AiPlatformWindow.xaml"));
            string codeBehind = File.ReadAllText(FindWorkspaceFile("UI/AiPlatformWindow.xaml.cs"));

            Assert.Contains("ContentTemplateSelector=", xaml, StringComparison.Ordinal);
            Assert.Contains("DisplayName", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("ProviderComboBox.DisplayMemberPath", codeBehind, StringComparison.Ordinal);
            Assert.Contains("ScrollViewer.Resources", xaml, StringComparison.Ordinal);
            Assert.Contains("DropDownScrollBarStyle", xaml, StringComparison.Ordinal);
            Assert.Contains("CornerRadius=", xaml, StringComparison.Ordinal);
            Assert.Contains("Visibility=", xaml, StringComparison.Ordinal);
        }

        [Fact]
        public void AiPlatformWindow_UsesCompactConfigurationSurface()
        {
            string xaml = File.ReadAllText(FindWorkspaceFile("UI/AiPlatformWindow.xaml"));
            string codeBehind = File.ReadAllText(FindWorkspaceFile("UI/AiPlatformWindow.xaml.cs"));

            Assert.DoesNotContain("管理厂家、协议、密钥和模型列表", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("Provider 工作台", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("点击厂家预设只修改当前草稿", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("DetailSubtitleText", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("ProviderDescriptionText", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("只填写服务根地址", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("KeyHintText", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("ModelCatalogStatusText", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("DetailSubtitleText", codeBehind, StringComparison.Ordinal);
            Assert.DoesNotContain("ProviderDescriptionText", codeBehind, StringComparison.Ordinal);
            Assert.DoesNotContain("KeyHintText", codeBehind, StringComparison.Ordinal);
            Assert.DoesNotContain("ModelCatalogStatusText", codeBehind, StringComparison.Ordinal);
        }

        [Fact]
        public void ProjectFile_ExcludesVerificationTransactionArtifactsFromCompilation()
        {
            string projectFile = FindWorkspaceFile("ImageColorChanger.csproj");
            string project = File.ReadAllText(projectFile);

            Assert.Contains("<Compile Remove=\"release-txn\\**\" />", project, StringComparison.Ordinal);
            Assert.Contains("<EmbeddedResource Remove=\"release-txn\\**\" />", project, StringComparison.Ordinal);
            Assert.Contains("<None Remove=\"release-txn\\**\" />", project, StringComparison.Ordinal);
            Assert.Contains("<Page Remove=\"release-txn\\**\" />", project, StringComparison.Ordinal);
        }

        [Fact]
        public void AiPlatformWindow_RestoresAndSavesTheFullFetchedModelCatalog()
        {
            string codeBehind = File.ReadAllText(FindWorkspaceFile("UI/AiPlatformWindow.xaml.cs"));

            Assert.Contains("PopulateModels(preset, activeProfile.AvailableModels, activeProfile.ModelId)", codeBehind, StringComparison.Ordinal);
            Assert.Contains("PopulateModels(preset, profile.AvailableModels, profile.ModelId)", codeBehind, StringComparison.Ordinal);
            Assert.Contains("profile.AvailableModels = GetCurrentModels()", codeBehind, StringComparison.Ordinal);
        }

        private static string FindWorkspaceFile(string relativePath)
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
                directory = directory.Parent;
            }

            throw new FileNotFoundException(relativePath);
        }
    }
}
