using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ImageColorChanger.Core;

namespace ImageColorChanger.UI
{
    public partial class AiPlatformWindow : Window
    {
        private readonly ConfigManager _configManager;

        public event Action AiCaptionRequested;
        public event Action AsrEngineSettingsRequested;

        public AiPlatformWindow(ConfigManager configManager)
        {
            InitializeComponent();
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
            RefreshFromConfig();
        }

        public void RefreshFromConfig()
        {
            DeepSeekApiKeyBox.Password = _configManager.DeepSeekApiKey ?? string.Empty;
            SelectModel(_configManager.DeepSeekModel);
            DeepSeekConfigStatusText.Text = string.IsNullOrWhiteSpace(_configManager.DeepSeekApiKey)
                ? "DeepSeek API Key 未配置"
                : $"当前模型：{_configManager.DeepSeekModel}";
        }

        public void FocusDeepSeekConfig()
        {
            DeepSeekApiKeyBox.Focus();
        }

        private void OpenAiCaptionButton_Click(object sender, RoutedEventArgs e)
        {
            AiCaptionRequested?.Invoke();
        }

        private void SaveDeepSeekConfigButton_Click(object sender, RoutedEventArgs e)
        {
            _configManager.DeepSeekApiKey = DeepSeekApiKeyBox.Password ?? string.Empty;
            _configManager.DeepSeekModel = GetSelectedModel();
            DeepSeekConfigStatusText.Text = string.IsNullOrWhiteSpace(_configManager.DeepSeekApiKey)
                ? "DeepSeek API Key 已清空，AI字幕暂不能请求AI理解。"
                : $"已保存，AI字幕将使用 {_configManager.DeepSeekModel}。";
        }

        private void AsrEngineSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            AsrEngineSettingsRequested?.Invoke();
        }

        private string GetSelectedModel()
        {
            return (DeepSeekModelComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "deepseek-v4-flash";
        }

        private void SelectModel(string model)
        {
            string target = string.IsNullOrWhiteSpace(model) ? "deepseek-v4-flash" : model.Trim();
            var item = DeepSeekModelComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(candidate => string.Equals(candidate.Content?.ToString(), target, StringComparison.OrdinalIgnoreCase));
            DeepSeekModelComboBox.SelectedItem = item ?? DeepSeekModelComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        }
    }
}
