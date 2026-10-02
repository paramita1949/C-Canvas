using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ImageColorChanger.Core;
using ImageColorChanger.Services.Ai;
using ImageColorChanger.UI;
using Xunit;

namespace Canvas.TextEditor.Tests.UI;

public sealed class AiPlatformWindowBehaviorTests
{
    [Fact]
    public void SavingCustomBaseUrl_UpdatesOnlyTheSelectedProtocolEndpoint()
    {
        RunInSta(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), $"canvas-ai-platform-{Guid.NewGuid():N}.json");
            AiPlatformWindow window = null;
            try
            {
                var config = new ConfigManager(path);
                AiConnectionProfile profile = config.GetActiveAiProfile();
                profile.ProviderId = "custom";
                profile.Protocol = AiProviderProtocol.OpenAiCompletions;
                profile.BaseUrl = "https://old.example/v1";
                profile.ChatCompletionsEndpoint = "https://old.example/v1/chat/completions";
                profile.ResponsesEndpoint = "https://keep.example/respond";
                profile.ModelId = "example-model";
                config.SaveAiProfile(profile);

                window = new AiPlatformWindow(config);
                Assert.IsType<TextBox>(window.FindName("BaseUrlTextBox")).Text = "https://new.example/v1";
                ComboBox protocol = Assert.IsType<ComboBox>(window.FindName("ProtocolComboBox"));
                protocol.SelectedItem = protocol.Items.OfType<ComboBoxItem>().Single(item =>
                    string.Equals(item.Tag?.ToString(), AiProviderProtocol.OpenAiResponses, StringComparison.OrdinalIgnoreCase));

                typeof(AiPlatformWindow)
                    .GetMethod("SaveConfigButton_Click", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(window, new object[] { window, null });

                AiConnectionProfile restored = new ConfigManager(path).GetActiveAiProfile();
                Assert.Equal("https://new.example/v1", restored.BaseUrl);
                Assert.Equal("https://old.example/v1/chat/completions", restored.ChatCompletionsEndpoint);
                Assert.Equal("https://new.example/v1/responses", restored.ResponsesEndpoint);
            }
            finally
            {
                window?.Close();
                if (File.Exists(path)) File.Delete(path);
            }
        });
    }

    [Fact]
    public void SwitchingBuiltInProviderProtocol_UpdatesProtocolSpecificBaseUrl()
    {
        RunInSta(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), $"canvas-ai-protocol-base-{Guid.NewGuid():N}.json");
            AiPlatformWindow window = null;
            try
            {
                var config = new ConfigManager(path);
                AiConnectionProfile profile = config.GetActiveAiProfile();
                profile.ProviderId = "zhipu";
                profile.Protocol = AiProviderProtocol.OpenAiCompletions;
                profile.BaseUrl = "https://open.bigmodel.cn/api/paas/v4";
                profile.ChatCompletionsEndpoint = "https://open.bigmodel.cn/api/paas/v4/chat/completions";
                profile.ResponsesEndpoint = "https://open.bigmodel.cn/api/v1/responses";
                profile.ModelId = "glm-4.5-air";
                config.SaveAiProfile(profile);

                window = new AiPlatformWindow(config);
                TextBox baseUrl = Assert.IsType<TextBox>(window.FindName("BaseUrlTextBox"));
                ComboBox protocol = Assert.IsType<ComboBox>(window.FindName("ProtocolComboBox"));

                Assert.Equal("https://open.bigmodel.cn/api/paas/v4", baseUrl.Text);

                protocol.SelectedItem = protocol.Items.OfType<ComboBoxItem>().Single(item =>
                    string.Equals(item.Tag?.ToString(), AiProviderProtocol.OpenAiResponses, StringComparison.OrdinalIgnoreCase));

                Assert.Equal("https://open.bigmodel.cn/api/v1", baseUrl.Text);

                protocol.SelectedItem = protocol.Items.OfType<ComboBoxItem>().Single(item =>
                    string.Equals(item.Tag?.ToString(), AiProviderProtocol.OpenAiCompletions, StringComparison.OrdinalIgnoreCase));

                Assert.Equal("https://open.bigmodel.cn/api/paas/v4", baseUrl.Text);
            }
            finally
            {
                window?.Close();
                if (File.Exists(path)) File.Delete(path);
            }
        });
    }

    [Fact]
    public void LeftProfileList_TracksPersistedActiveProfileEvenWhenAnotherProfileIsOpened()
    {
        RunInSta(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), $"canvas-ai-active-highlight-{Guid.NewGuid():N}.json");
            AiPlatformWindow window = null;
            try
            {
                var config = new ConfigManager(path);
                AiConnectionProfile first = config.GetActiveAiProfile();
                first.Id = "profile-first";
                first.Name = "第一个配置";
                first.ModelId = "model-first";
                config.SaveAiProfile(first);

                AiConnectionProfile second = config.CreateAiProfile("第二个配置");
                second.Id = "profile-second";
                second.ModelId = "model-second";
                config.SaveAiProfile(second);
                Assert.True(config.SetActiveAiProfile(second.Id));

                window = new AiPlatformWindow(config);
                ListBox profiles = Assert.IsType<ListBox>(window.FindName("ProfileListBox"));
                window.Show();
                window.UpdateLayout();
                object activeSummary = profiles.Items.Cast<object>().Single(item =>
                    string.Equals(item.GetType().GetProperty("Id")?.GetValue(item) as string, second.Id, StringComparison.Ordinal));
                object inactiveSummary = profiles.Items.Cast<object>().Single(item =>
                    string.Equals(item.GetType().GetProperty("Id")?.GetValue(item) as string, first.Id, StringComparison.Ordinal));

                var activeProperty = activeSummary.GetType().GetProperty("IsActive");
                Assert.NotNull(activeProperty);
                Assert.True((bool)activeProperty.GetValue(activeSummary));
                Assert.False((bool)activeProperty.GetValue(inactiveSummary));

                profiles.SelectedItem = inactiveSummary;
                window.UpdateLayout();

                Assert.True((bool)activeProperty.GetValue(activeSummary));
                Assert.False((bool)activeProperty.GetValue(inactiveSummary));

                ListBoxItem activeContainer = Assert.IsType<ListBoxItem>(profiles.ItemContainerGenerator.ContainerFromItem(activeSummary));
                Border activeBorder = FindVisualChild<Border>(activeContainer);
                Assert.NotNull(activeBorder);
                Assert.Equal("#FFE1F7ED", activeBorder.Background.ToString());
            }
            finally
            {
                window?.Close();
                if (File.Exists(path)) File.Delete(path);
            }
        });
    }

    [Fact]
    public void OpeningProviderAndProtocolLists_DoesNotEmitAlignmentFindAncestorBindingErrors()
    {
        RunInSta(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), $"canvas-ai-binding-{Guid.NewGuid():N}.json");
            AiPlatformWindow window = null;
            var trace = new StringWriter();
            var listener = new TextWriterTraceListener(trace);
            SourceLevels previousLevel = PresentationTraceSources.DataBindingSource.Switch.Level;
            try
            {
                PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
                PresentationTraceSources.DataBindingSource.Listeners.Add(listener);

                window = new AiPlatformWindow(new ConfigManager(path));
                window.Show();
                Assert.IsType<ComboBox>(window.FindName("ProviderComboBox")).IsDropDownOpen = true;
                Assert.IsType<ComboBox>(window.FindName("ProtocolComboBox")).IsDropDownOpen = true;
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                listener.Flush();

                string output = trace.ToString();
                Assert.DoesNotContain("HorizontalContentAlignment", output, StringComparison.Ordinal);
                Assert.DoesNotContain("VerticalContentAlignment", output, StringComparison.Ordinal);
                Assert.DoesNotContain("RelativeSource FindAncestor, AncestorType='System.Windows.Controls.ItemsControl'", output, StringComparison.Ordinal);
            }
            finally
            {
                window?.Close();
                PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
                PresentationTraceSources.DataBindingSource.Switch.Level = previousLevel;
                listener.Dispose();
                trace.Dispose();
                if (File.Exists(path)) File.Delete(path);
            }
        });
    }

    [Fact]
    public void ApplicationComboBoxItemAlignmentStyle_DoesNotEmitAlignmentFindAncestorBindingErrors()
    {
        RunInSta(() =>
        {
            ImageColorChanger.App app = null;
            try
            {
                app = new ImageColorChanger.App();
                typeof(ImageColorChanger.App)
                    .GetMethod("InitializeComponent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.Invoke(app, null);

                var itemStyle = app.FindResource(typeof(ComboBoxItem)) as Style;
                Assert.NotNull(itemStyle);
                Assert.Same(
                    app.FindResource("CanvasComboBoxItemAlignmentStyle"),
                    itemStyle.BasedOn);
            }
            finally
            {
                app?.Shutdown();
            }
        });
    }

    private static void RunInSta(Action action)
    {
        Exception error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            T nested = FindVisualChild<T>(child);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }
}
