using System;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Controls;
using ImageColorChanger.Core;
using ImageColorChanger.UI;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Ui
{
    public sealed class AiConfigWindowProviderSelectionTests
    {
        [Fact]
        public void ProviderSelectionChanged_WhenSwitchingToAliyun_BaseUrlShouldUseAliyunDefault()
        {
            RunInSta(() =>
            {
                string tempFile = Path.Combine(Path.GetTempPath(), $"canvas_config_{Guid.NewGuid():N}.json");
                try
                {
                    var config = new ConfigManager(tempFile);
                    config.LiveCaptionSpeechMode = "realtime";
                    config.LiveCaptionRealtimeAsrProvider = "baidu";
                    config.LiveCaptionRealtimeProxyBaseUrl = "wss://vop.baidu.com/realtime_asr";

                    var window = new AiConfigWindow(config);
                    ComboBoxItem aliyunItem = window.ProviderComboBox.Items
                        .OfType<ComboBoxItem>()
                        .First(item => string.Equals(item.Tag?.ToString(), "aliyun", StringComparison.OrdinalIgnoreCase));

                    window.ProviderComboBox.SelectedItem = aliyunItem;

                    Assert.Equal("wss://nls-gateway-cn-shanghai.aliyuncs.com/ws/v1", window.BaseUrlTextBox.Text);
                }
                finally
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
            });
        }

        [Fact]
        public void ProviderSelectionChanged_WhenSwitchingToXfyun_BaseUrlAndDialectShouldUseDialectDefaults()
        {
            RunInSta(() =>
            {
                string tempFile = Path.Combine(Path.GetTempPath(), $"canvas_config_{Guid.NewGuid():N}.json");
                try
                {
                    var config = new ConfigManager(tempFile);
                    config.LiveCaptionSpeechMode = "realtime";
                    config.LiveCaptionRealtimeAsrProvider = "baidu";
                    config.LiveCaptionRealtimeProxyBaseUrl = "wss://vop.baidu.com/realtime_asr";

                    var window = new AiConfigWindow(config);
                    ComboBoxItem xfyunItem = window.ProviderComboBox.Items
                        .OfType<ComboBoxItem>()
                        .First(item => string.Equals(item.Tag?.ToString(), "xfyun", StringComparison.OrdinalIgnoreCase));

                    window.ProviderComboBox.SelectedItem = xfyunItem;

                    Assert.Equal("wss://office-api-ast-dx.iflyaisol.com/ast/communicate/v1", window.BaseUrlTextBox.Text);
                    ComboBoxItem dialectItem = Assert.IsType<ComboBoxItem>(window.DialectSelectComboBox.SelectedItem);
                    Assert.Equal("mandarin", dialectItem.Tag?.ToString());
                }
                finally
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
            });
        }

        [Fact]
        public void ProviderSelectionChanged_InShortMode_WhenSwitchingToDoubao_BaseUrlShouldUseDoubaoShortDefault()
        {
            RunInSta(() =>
            {
                string tempFile = Path.Combine(Path.GetTempPath(), $"canvas_config_{Guid.NewGuid():N}.json");
                try
                {
                    var config = new ConfigManager(tempFile);
                    config.LiveCaptionSpeechMode = "short";
                    config.LiveCaptionShortAsrProvider = "baidu";
                    config.LiveCaptionShortProxyBaseUrl = "http://vop.baidu.com/server_api";

                    var window = new AiConfigWindow(config);
                    ComboBoxItem doubaoItem = window.ProviderComboBox.Items
                        .OfType<ComboBoxItem>()
                        .First(item => string.Equals(item.Tag?.ToString(), "doubao", StringComparison.OrdinalIgnoreCase));

                    window.ProviderComboBox.SelectedItem = doubaoItem;

                    Assert.Equal("wss://openspeech.bytedance.com/api/v2/asr", window.BaseUrlTextBox.Text);
                }
                finally
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
            });
        }

        [Fact]
        public void SpeechModeToggle_WhenRealtimeProviderChangedBeforeSave_ShouldRestoreRealtimeProviderBaseUrl()
        {
            RunInSta(() =>
            {
                string tempFile = Path.Combine(Path.GetTempPath(), $"canvas_config_{Guid.NewGuid():N}.json");
                AiConfigWindow window = null;
                try
                {
                    var config = new ConfigManager(tempFile);
                    config.LiveCaptionSpeechMode = "realtime";
                    config.LiveCaptionRealtimeAsrProvider = "baidu";
                    config.LiveCaptionRealtimeProxyBaseUrl = "wss://vop.baidu.com/realtime_asr";
                    config.LiveCaptionShortAsrProvider = "baidu";
                    config.LiveCaptionShortProxyBaseUrl = "http://vop.baidu.com/server_api";

                    window = new AiConfigWindow(config);
                    window.Show();

                    ComboBoxItem aliyunItem = window.ProviderComboBox.Items
                        .OfType<ComboBoxItem>()
                        .First(item => string.Equals(item.Tag?.ToString(), "aliyun", StringComparison.OrdinalIgnoreCase));
                    window.ProviderComboBox.SelectedItem = aliyunItem;
                    Assert.Equal("wss://nls-gateway-cn-shanghai.aliyuncs.com/ws/v1", window.BaseUrlTextBox.Text);

                    window.ShortSpeechModeToggle.IsChecked = true;
                    window.RealtimeSpeechModeToggle.IsChecked = true;

                    Assert.Equal("wss://nls-gateway-cn-shanghai.aliyuncs.com/ws/v1", window.BaseUrlTextBox.Text);
                }
                finally
                {
                    window?.Close();
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
            });
        }

        private static void RunInSta(Action action)
        {
            Exception captured = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (captured != null)
            {
                ExceptionDispatchInfo.Capture(captured).Throw();
            }
        }
    }
}
