using System;
using System.Windows;
using System.Windows.Controls;
using ImageColorChanger.Core;

namespace ImageColorChanger.UI
{
    public partial class AiConfigWindow : Window
    {
        private readonly ConfigManager _configManager;

        private sealed class ProviderPreset
        {
            public string Provider { get; set; } = "baidu";
            public string AsrEndpoint { get; set; } = string.Empty;
            public string AuthEndpoint { get; set; } = string.Empty;
            public string Label1 { get; set; } = "凭证1";
            public string Label2 { get; set; } = "凭证2";
            public string Label3 { get; set; } = "凭证3";
            public bool ShowCredential3 { get; set; } = true;
            public string Hint { get; set; } = string.Empty;
        }

        public AiConfigWindow(ConfigManager configManager)
        {
            InitializeComponent();
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
            LoadValues();
        }

        private void LoadValues()
        {
            string provider = NormalizeProvider(_configManager.LiveCaptionAsrProvider);
            SelectProvider(provider);
            LoadProviderCredentials(provider);
        }

        private void ProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProviderComboBox.SelectedItem is not ComboBoxItem item)
            {
                return;
            }

            string provider = NormalizeProvider(item.Tag?.ToString());
            ApplyPreset(GetPreset(provider));
            LoadProviderCredentials(provider);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string provider = GetSelectedProvider();
            string v1 = (Credential1TextBox.Text ?? string.Empty).Trim();
            string v2 = (Credential2TextBox.Text ?? string.Empty).Trim();
            string v3 = (Credential3TextBox.Text ?? string.Empty).Trim();

            switch (provider)
            {
                case "baidu":
                    if (string.IsNullOrWhiteSpace(v1) || string.IsNullOrWhiteSpace(v2) || string.IsNullOrWhiteSpace(v3))
                    {
                        System.Windows.MessageBox.Show("百度语音需填写 AppID / API Key / Secret Key。", "AI配置", MessageBoxButton.OK, MessageBoxImage.Warning);
                        Credential1TextBox.Focus();
                        return;
                    }
                    _configManager.LiveCaptionBaiduAppId = v1;
                    _configManager.LiveCaptionBaiduApiKey = v2;
                    _configManager.LiveCaptionBaiduSecretKey = v3;
                    break;

                case "tencent":
                    if (string.IsNullOrWhiteSpace(v2) || string.IsNullOrWhiteSpace(v3))
                    {
                        System.Windows.MessageBox.Show("腾讯云语音需填写 SecretId / SecretKey。", "AI配置", MessageBoxButton.OK, MessageBoxImage.Warning);
                        Credential2TextBox.Focus();
                        return;
                    }
                    _configManager.LiveCaptionTencentAppId = v1;
                    _configManager.LiveCaptionTencentSecretId = v2;
                    _configManager.LiveCaptionTencentSecretKey = v3;
                    break;

                case "aliyun":
                    if (string.IsNullOrWhiteSpace(v1) || string.IsNullOrWhiteSpace(v2) || string.IsNullOrWhiteSpace(v3))
                    {
                        System.Windows.MessageBox.Show("阿里云语音需填写 AppKey / AccessKey ID / AccessKey Secret。", "AI配置", MessageBoxButton.OK, MessageBoxImage.Warning);
                        Credential1TextBox.Focus();
                        return;
                    }
                    _configManager.LiveCaptionAliAppKey = v1;
                    _configManager.LiveCaptionAliAccessKeyId = v2;
                    _configManager.LiveCaptionAliAccessKeySecret = v3;
                    break;

                case "doubao":
                    if (string.IsNullOrWhiteSpace(v1) || string.IsNullOrWhiteSpace(v2))
                    {
                        System.Windows.MessageBox.Show("豆包语音需填写 App Key / Access Key。Resource ID 可保持默认。", "AI配置", MessageBoxButton.OK, MessageBoxImage.Warning);
                        Credential1TextBox.Focus();
                        return;
                    }
                    _configManager.LiveCaptionDoubaoAppKey = v1;
                    _configManager.LiveCaptionDoubaoAccessKey = v2;
                    _configManager.LiveCaptionDoubaoResourceId = string.IsNullOrWhiteSpace(v3)
                        ? "volc.seedasr.sauc.duration"
                        : v3;
                    break;
            }

            _configManager.LiveCaptionAsrProvider = provider;
            _configManager.SaveConfig();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SelectProvider(string provider)
        {
            foreach (var raw in ProviderComboBox.Items)
            {
                if (raw is ComboBoxItem item &&
                    string.Equals(item.Tag?.ToString(), provider, StringComparison.OrdinalIgnoreCase))
                {
                    ProviderComboBox.SelectedItem = item;
                    return;
                }
            }

            ProviderComboBox.SelectedIndex = 0;
        }

        private string GetSelectedProvider()
        {
            if (ProviderComboBox.SelectedItem is ComboBoxItem item)
            {
                return NormalizeProvider(item.Tag?.ToString());
            }

            return "baidu";
        }

        private static string NormalizeProvider(string provider)
        {
            string p = (provider ?? string.Empty).Trim().ToLowerInvariant();
            return p switch
            {
                "baidu" => "baidu",
                "tencent" => "tencent",
                "aliyun" => "aliyun",
                "doubao" => "doubao",
                _ => "baidu"
            };
        }

        private ProviderPreset GetPreset(string provider)
        {
            return provider switch
            {
                "tencent" => new ProviderPreset
                {
                    Provider = "tencent",
                    AsrEndpoint = "https://asr.tencentcloudapi.com",
                    AuthEndpoint = "TC3-HMAC-SHA256（请求签名）",
                    Label1 = "AppID（可选）",
                    Label2 = "SecretId",
                    Label3 = "SecretKey",
                    Hint = "腾讯云已接入实时语音识别。"
                },
                "aliyun" => new ProviderPreset
                {
                    Provider = "aliyun",
                    AsrEndpoint = "wss://nls-gateway-cn-shanghai.aliyuncs.com/ws/v1?token=...",
                    AuthEndpoint = "CreateToken(OpenAPI) -> Token鉴权",
                    Label1 = "AppKey",
                    Label2 = "AccessKey ID",
                    Label3 = "AccessKey Secret",
                    Hint = "阿里云已接入实时语音识别（中间结果+最终结果）。"
                },
                "doubao" => new ProviderPreset
                {
                    Provider = "doubao",
                    AsrEndpoint = "wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_async",
                    AuthEndpoint = "Header鉴权：X-Api-App-Key / X-Api-Access-Key / X-Api-Resource-Id",
                    Label1 = "App Key",
                    Label2 = "Access Key",
                    Label3 = "Resource ID（默认 volc.seedasr.sauc.duration）",
                    ShowCredential3 = false,
                    Hint = "豆包2.0小时版默认资源ID：volc.seedasr.sauc.duration；默认不传语种键，自动多语种识别。"
                },
                _ => new ProviderPreset
                {
                    Provider = "baidu",
                    AsrEndpoint = "wss://vop.baidu.com/realtime_asr",
                    AuthEndpoint = "https://aip.baidubce.com/oauth/2.0/token（短语音回退）",
                    Label1 = "AppID",
                    Label2 = "API Key",
                    Label3 = "Secret Key",
                    Hint = "百度语音已接入实时识别；异常时自动回退短语音。"
                }
            };
        }

        private void ApplyPreset(ProviderPreset preset)
        {
            AsrEndpointTextBox.Text = preset.AsrEndpoint;
            AuthEndpointTextBox.Text = preset.AuthEndpoint;
            Credential1Label.Text = preset.Label1;
            Credential2Label.Text = preset.Label2;
            Credential3Label.Text = preset.Label3;
            Credential3Label.Visibility = preset.ShowCredential3 ? Visibility.Visible : Visibility.Collapsed;
            Credential3TextBox.Visibility = preset.ShowCredential3 ? Visibility.Visible : Visibility.Collapsed;
            HintTextBlock.Text = preset.Hint;
        }

        private void LoadProviderCredentials(string provider)
        {
            switch (provider)
            {
                case "tencent":
                    Credential1TextBox.Text = _configManager.LiveCaptionTencentAppId;
                    Credential2TextBox.Text = _configManager.LiveCaptionTencentSecretId;
                    Credential3TextBox.Text = _configManager.LiveCaptionTencentSecretKey;
                    break;
                case "aliyun":
                    Credential1TextBox.Text = _configManager.LiveCaptionAliAppKey;
                    Credential2TextBox.Text = _configManager.LiveCaptionAliAccessKeyId;
                    Credential3TextBox.Text = _configManager.LiveCaptionAliAccessKeySecret;
                    break;
                case "doubao":
                    Credential1TextBox.Text = _configManager.LiveCaptionDoubaoAppKey;
                    Credential2TextBox.Text = _configManager.LiveCaptionDoubaoAccessKey;
                    Credential3TextBox.Text = _configManager.LiveCaptionDoubaoResourceId;
                    break;
                default:
                    Credential1TextBox.Text = _configManager.LiveCaptionBaiduAppId;
                    Credential2TextBox.Text = _configManager.LiveCaptionBaiduApiKey;
                    Credential3TextBox.Text = _configManager.LiveCaptionBaiduSecretKey;
                    break;
            }
        }
    }
}
