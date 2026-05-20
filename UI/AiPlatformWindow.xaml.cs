using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ImageColorChanger.Core;

namespace ImageColorChanger.UI
{
    public partial class AiPlatformWindow : Window
    {
        private const string DeepSeekProvider = "deepseek";
        private const string GeminiProvider = "gemini";
        private readonly ConfigManager _configManager;
        private string _selectedProvider = DeepSeekProvider;
        private bool _isRefreshing;

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
            _isRefreshing = true;
            _selectedProvider = IsGeminiModel(_configManager.DeepSeekModel) ? GeminiProvider : DeepSeekProvider;
            DeepSeekProviderButton.IsChecked = string.Equals(_selectedProvider, DeepSeekProvider, StringComparison.Ordinal);
            GeminiProviderButton.IsChecked = string.Equals(_selectedProvider, GeminiProvider, StringComparison.Ordinal);
            RefreshProviderFields();
            _isRefreshing = false;
        }

        public void FocusDeepSeekConfig()
        {
            ApiKeyBox.Focus();
        }

        private void OpenAiCaptionButton_Click(object sender, RoutedEventArgs e)
        {
            AiCaptionRequested?.Invoke();
        }

        private void SaveDeepSeekConfigButton_Click(object sender, RoutedEventArgs e)
        {
            string model = GetSelectedModel();
            if (string.Equals(_selectedProvider, GeminiProvider, StringComparison.Ordinal))
            {
                _configManager.GeminiApiKey = ApiKeyBox.Password ?? string.Empty;
                _configManager.DeepSeekModel = model;
            }
            else
            {
                _configManager.DeepSeekApiKey = ApiKeyBox.Password ?? string.Empty;
                _configManager.DeepSeekModel = model;
            }

            RefreshProviderFields();
        }

        private void AsrEngineSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            AsrEngineSettingsRequested?.Invoke();
        }

        private string GetSelectedModel()
        {
            return AiModelComboBox.SelectedItem?.ToString()
                ?? (string.Equals(_selectedProvider, GeminiProvider, StringComparison.Ordinal) ? "gemini-3.5-flash" : "deepseek-v4-flash");
        }

        private void SelectModel(string model)
        {
            string fallback = string.Equals(_selectedProvider, GeminiProvider, StringComparison.Ordinal)
                ? "gemini-3.5-flash"
                : "deepseek-v4-flash";
            string target = string.IsNullOrWhiteSpace(model) ? fallback : model.Trim();
            var item = AiModelComboBox.Items
                .OfType<string>()
                .FirstOrDefault(candidate => string.Equals(candidate, target, StringComparison.OrdinalIgnoreCase));
            AiModelComboBox.SelectedItem = item ?? AiModelComboBox.Items.OfType<string>().FirstOrDefault();
        }

        private void ProviderButton_Checked(object sender, RoutedEventArgs e)
        {
            if (_isRefreshing)
            {
                return;
            }

            _selectedProvider = sender == GeminiProviderButton ? GeminiProvider : DeepSeekProvider;
            RefreshProviderFields();
        }

        private void RefreshProviderFields()
        {
            AiModelComboBox.Items.Clear();
            if (string.Equals(_selectedProvider, GeminiProvider, StringComparison.Ordinal))
            {
                AiModelComboBox.Items.Add("gemini-3.5-flash");
                KeyHintText.Text = "填写 Google AI Studio 的 API Key。";
                ApiKeyBox.Password = _configManager.GeminiApiKey ?? string.Empty;
                SelectModel(IsGeminiModel(_configManager.DeepSeekModel) ? _configManager.DeepSeekModel : "gemini-3.5-flash");
                DeepSeekConfigStatusText.Text = string.IsNullOrWhiteSpace(_configManager.GeminiApiKey)
                    ? "Gemini 密钥未配置"
                    : $"当前平台：Gemini，模型：{GetSelectedModel()}";
                return;
            }

            AiModelComboBox.Items.Add("deepseek-v4-flash");
            AiModelComboBox.Items.Add("deepseek-v4-pro");
            KeyHintText.Text = "填写 DeepSeek API Key。";
            ApiKeyBox.Password = _configManager.DeepSeekApiKey ?? string.Empty;
            SelectModel(IsGeminiModel(_configManager.DeepSeekModel) ? "deepseek-v4-flash" : _configManager.DeepSeekModel);
            DeepSeekConfigStatusText.Text = string.IsNullOrWhiteSpace(_configManager.DeepSeekApiKey)
                ? "DeepSeek 密钥未配置"
                : $"当前平台：DeepSeek，模型：{GetSelectedModel()}";
        }

        private static bool IsGeminiModel(string model)
        {
            return !string.IsNullOrWhiteSpace(model) &&
                   model.Trim().StartsWith("gemini-", StringComparison.OrdinalIgnoreCase);
        }
    }
}
