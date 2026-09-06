using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ImageColorChanger.UI;
using Xunit;

namespace Canvas.TextEditor.Tests.UI
{
    public sealed class StartupSplashScreenTests
    {
        [Fact]
        public void StartupSplashWindow_UsesLightBottomStatusBarWithoutVersionText()
        {
            string root = FindRepoRoot();
            string splashXamlPath = Path.Combine(root, "UI", "StartupSplashWindow.xaml");

            Assert.True(File.Exists(splashXamlPath), "StartupSplashWindow.xaml should define the startup splash UI.");

            string xaml = File.ReadAllText(splashXamlPath);
            string footerBlock = Regex.Match(
                xaml,
                "x:Name=\"SplashFooter\"[\\s\\S]*?</Border>")
                .Value;

            Assert.Contains("x:Name=\"SplashImage\"", xaml);
            Assert.Contains("x:Name=\"StatusText\"", xaml);
            Assert.Contains("咏慕投影", footerBlock);
            Assert.Contains("VerticalAlignment=\"Bottom\"", footerBlock);
            Assert.Contains("MinHeight=\"54\"", footerBlock);
            Assert.Contains("baodian.ico", footerBlock);
            Assert.DoesNotContain("ScriptureText", footerBlock);
            Assert.DoesNotContain("CanvasCast", xaml);
            Assert.DoesNotContain("Version", xaml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("V6.", xaml);
            Assert.DoesNotContain("DropShadowEffect", xaml);
        }

        [Fact]
        public void StartupSplashWindow_PrintsCenterScriptureAsBodyAndShortReference()
        {
            string root = FindRepoRoot();
            string splashXamlPath = Path.Combine(root, "UI", "StartupSplashWindow.xaml");
            string windowCodePath = Path.Combine(root, "UI", "StartupSplashWindow.xaml.cs");

            string xaml = File.ReadAllText(splashXamlPath);
            string windowCode = File.ReadAllText(windowCodePath);
            string centerTextBlock = Regex.Match(
                xaml,
                "<TextBlock x:Name=\"CenterScriptureText\"[\\s\\S]*?/>")
                .Value;
            string centerReferenceBlock = Regex.Match(
                xaml,
                "<TextBlock x:Name=\"CenterScriptureReferenceText\"[\\s\\S]*?/>")
                .Value;

            Assert.DoesNotContain("x:Name=\"CenterScriptureOverlay\"", xaml);
            Assert.DoesNotContain("Background=\"#EAF7FCFF\"", xaml);
            Assert.DoesNotContain("BorderBrush=\"#66FFFFFF\"", xaml);
            Assert.Contains("x:Name=\"CenterScripturePanel\"", xaml);
            Assert.Contains("x:Name=\"CenterScriptureText\"", centerTextBlock);
            Assert.Contains("你们要以感恩为祭献与神", centerTextBlock);
            Assert.Contains("HorizontalAlignment=\"Center\"", centerTextBlock);
            Assert.DoesNotContain("Background=", centerTextBlock);
            Assert.DoesNotContain("（", centerTextBlock);
            Assert.Contains("x:Name=\"CenterScriptureReferenceText\"", centerReferenceBlock);
            Assert.Contains("—— 诗 50:14 ——", centerReferenceBlock);
            Assert.Contains("FontSize=\"13\"", centerReferenceBlock);
            Assert.Contains("StartupScriptureVerseProvider.GetRandomVerseText", windowCode);
            Assert.Contains("CenterScriptureText.Text", windowCode);
            Assert.Contains("CenterScriptureReferenceText.Text", windowCode);
        }

        [Fact]
        public void StartupSplash_IsShownDuringStartupAndClosedAfterMainWindowRenders()
        {
            string root = FindRepoRoot();
            string appCode = File.ReadAllText(Path.Combine(root, "App.xaml.cs"));
            string diagnosticsCode = File.ReadAllText(Path.Combine(root, "UI", "MainWindow.StartupDiagnostics.cs"));

            Assert.Contains("StartupSplashController.Show", appCode);
            Assert.Contains("StartupScriptureVerseProvider.GetRandomVerseText", appCode);
            Assert.Contains("StartupSplashController.UpdateStatus", appCode);
            Assert.Contains("正在初始化服务", appCode);
            Assert.Contains("正在加载资料库", appCode);
            Assert.Contains("正在准备主界面", appCode);
            Assert.Contains("StartupSplashController.Close", diagnosticsCode);
        }

        [Fact]
        public void StartupSplashImage_IsExternalContentSoLaterUpdatesDoNotRequireCodeChanges()
        {
            string root = FindRepoRoot();
            string projectFile = File.ReadAllText(Path.Combine(root, "ImageColorChanger.csproj"));
            string resolverCodePath = Path.Combine(root, "UI", "StartupSplashImageResolver.cs");

            Assert.True(File.Exists(Path.Combine(root, "data", "splash", "SOM.png")));
            Assert.True(File.Exists(resolverCodePath), "StartupSplashImageResolver.cs should own replaceable image lookup.");

            string resolverCode = File.ReadAllText(resolverCodePath);
            Assert.Contains("SOM.png", resolverCode);
            Assert.Contains("data", resolverCode);
            Assert.Contains("splash", resolverCode);
            Assert.Contains("GetSupportedSplashImagePaths", resolverCode);
            Assert.Contains("SelectRandomSplashImagePath", resolverCode);
            Assert.Contains("SkiaWpfHelper.LoadBitmapSource", resolverCode);
            Assert.Contains("data\\splash\\**\\*.png", projectFile);
            Assert.Contains("data\\splash\\**\\*.jpg", projectFile);
            Assert.Contains("data\\splash\\**\\*.jpeg", projectFile);
            Assert.Contains("data\\splash\\**\\*.webp", projectFile);
            Assert.Contains("data\\splash\\**\\*.bmp", projectFile);
            Assert.Contains("CopyToOutputDirectory", projectFile);
            Assert.Contains("CopyToPublishDirectory", projectFile);
        }

        [Fact]
        public void StartupSplashImageResolver_CanRandomlySelectFromSplashImagePool()
        {
            string root = FindRepoRoot();
            string splashDirectory = Path.Combine(root, "data", "splash");

            var images = StartupSplashImageResolver.GetSupportedSplashImagePaths(splashDirectory);
            string selected = StartupSplashImageResolver.SelectRandomSplashImagePath(splashDirectory, new FixedRandom(1));

            Assert.True(images.Count >= 2, "data/splash should support multiple startup images.");
            Assert.Contains(images, path => Path.GetFileName(path) == "Road of Hope.png");
            Assert.DoesNotContain(images, path => Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(images[1], selected);
        }

        [Fact]
        public void StartupScriptureVerseProvider_Contains300ClassicBuiltInVerses()
        {
            var verses = StartupScriptureVerseProvider.AllVerses;

            Assert.Equal(300, verses.Count);
            Assert.Equal(300, verses.Select(verse => verse.Reference).Distinct().Count());
            Assert.All(verses, verse =>
            {
                Assert.False(string.IsNullOrWhiteSpace(verse.Reference));
                Assert.False(string.IsNullOrWhiteSpace(verse.Text));
            });
            Assert.Contains(verses, verse => verse.Reference == "诗篇 23:1");
            Assert.Contains(verses, verse => verse.Reference == "约翰福音 3:16");
            Assert.Contains(verses, verse => verse.Reference == "罗马书 8:28");
            Assert.Contains(verses, verse => verse.Reference == "希伯来书 11:1");
        }

        [Fact]
        public void StartupScriptureVerseProvider_CanSelectVerseWithoutDatabaseOrNetwork()
        {
            var random = new FixedRandom(12);

            StartupScriptureVerse selected = StartupScriptureVerseProvider.GetRandomVerse(random);
            string displayText = StartupScriptureVerseProvider.GetRandomVerseText(random);

            Assert.Equal(StartupScriptureVerseProvider.AllVerses[12], selected);
            Assert.Contains(selected.Reference, displayText);
            Assert.Contains(selected.Text, displayText);
        }

        [Theory]
        [InlineData("诗篇 46:1", "诗 46:1")]
        [InlineData("约翰福音 3:16", "约 3:16")]
        [InlineData("罗马书 8:28", "罗 8:28")]
        [InlineData("彼得前书 5:7", "彼前 5:7")]
        public void StartupScriptureVerseProvider_CanFormatShortReferenceCaption(
            string reference,
            string shortReference)
        {
            Assert.Equal(shortReference, StartupScriptureVerseProvider.GetShortReference(reference));
            Assert.Equal($"—— {shortReference} ——", StartupScriptureVerseProvider.FormatReferenceCaption(reference));
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "App.xaml.cs")) &&
                    File.Exists(Path.Combine(directory.FullName, "ImageColorChanger.csproj")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate Canvas repo root from test output directory.");
        }

        private sealed class FixedRandom : Random
        {
            private readonly int _value;

            public FixedRandom(int value)
            {
                _value = value;
            }

            public override int Next(int maxValue)
            {
                return _value % maxValue;
            }
        }
    }
}
