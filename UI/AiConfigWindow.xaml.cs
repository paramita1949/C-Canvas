using System;
using System.Windows;
using System.Windows.Controls;
using ImageColorChanger.Core;

namespace ImageColorChanger.UI
{
    public partial class AiConfigWindow : Window
    {
        private enum SpeechMode
        {
            Realtime = 0,
            ShortPhrase = 1
        }

        private readonly ConfigManager _configManager;
        private bool _isApplyingModelSelection;
        private bool _isApplyingDialectSelection;
        private bool _isApplyingSpeechModeSelection;
        private bool _isApplyingProviderSelection;
        private SpeechMode _speechMode = SpeechMode.Realtime;
        private string _baseHint = string.Empty;
        private string _draftRealtimeProvider = "baidu";
        private string _draftRealtimeBaseUrl = string.Empty;
        private string _draftRealtimeModel = string.Empty;
        private string _draftRealtimeDialect = "default";
        private string _draftShortProvider = "baidu";
        private string _draftShortBaseUrl = string.Empty;
        private string _draftShortModel = string.Empty;
        private string _draftShortDialect = "default";

        private sealed class ProviderPreset
        {
            public string Provider { get; set; } = "baidu";
            public string Label1 { get; set; } = "凭证1";
            public string Label2 { get; set; } = "凭证2";
            public string Label3 { get; set; } = "凭证3";
            public bool ShowCredential1 { get; set; } = true;
            public bool ShowCredential2 { get; set; } = true;
            public bool ShowCredential3 { get; set; } = true;
            public string Hint { get; set; } = string.Empty;
        }

        private sealed class ModelOption
        {
            public string Label { get; set; } = string.Empty;
            public string ModelId { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
        }

        private sealed class DialectOption
        {
            public string Label { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
        }

        private sealed class ProviderOption
        {
            public string Provider { get; set; } = "baidu";
            public string DisplayName { get; set; } = string.Empty;
        }

        public AiConfigWindow(ConfigManager configManager)
        {
            InitializeComponent();
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
            LoadValues();
        }

        private void LoadValues()
        {
            InitializeModeDraftsFromConfig();
            ApplySpeechMode(ResolveSpeechMode(_configManager.LiveCaptionSpeechMode));
            string provider = GetModeProvider(_speechMode);
            BaseUrlTextBox.Text = GetModeBaseUrl(_speechMode);
            PopulateProviderOptions(_speechMode, provider);
            provider = GetSelectedProvider();
            InitializeModelSelection(provider, GetModeModel(_speechMode));
            InitializeDialectSelection(provider);
            LoadProviderCredentials(provider);
            CaptureCurrentModeDraftFromUi();
            InitializeVerseSourceComboBox();
        }

        private void InitializeModeDraftsFromConfig()
        {
            _draftRealtimeProvider = NormalizeProvider(_configManager.LiveCaptionRealtimeAsrProvider);
            _draftRealtimeBaseUrl = (_configManager.LiveCaptionRealtimeProxyBaseUrl ?? string.Empty).Trim();
            _draftRealtimeModel = (_configManager.LiveCaptionRealtimeAsrModel ?? string.Empty).Trim();
            _draftRealtimeDialect = _configManager.LiveCaptionRealtimeBaiduDevPid.ToString();

            _draftShortProvider = NormalizeProvider(_configManager.LiveCaptionShortAsrProvider);
            _draftShortBaseUrl = (_configManager.LiveCaptionShortProxyBaseUrl ?? string.Empty).Trim();
            _draftShortModel = (_configManager.LiveCaptionShortAsrModel ?? string.Empty).Trim();
            _draftShortDialect = _configManager.LiveCaptionShortBaiduDevPid.ToString();
        }

        private void SetModeDraft(SpeechMode mode, string provider, string baseUrl, string model, string dialect)
        {
            string normalizedProvider = NormalizeProvider(provider);
            string nextBaseUrl = (baseUrl ?? string.Empty).Trim();
            string nextModel = (model ?? string.Empty).Trim();
            string nextDialect = string.IsNullOrWhiteSpace(dialect) ? "default" : dialect.Trim();

            if (mode == SpeechMode.ShortPhrase)
            {
                _draftShortProvider = normalizedProvider;
                _draftShortBaseUrl = nextBaseUrl;
                _draftShortModel = nextModel;
                _draftShortDialect = nextDialect;
                return;
            }

            _draftRealtimeProvider = normalizedProvider;
            _draftRealtimeBaseUrl = nextBaseUrl;
            _draftRealtimeModel = nextModel;
            _draftRealtimeDialect = nextDialect;
        }

        private void CaptureCurrentModeDraftFromUi()
        {
            if (ProviderComboBox == null || BaseUrlTextBox == null || ModelSelectComboBox == null || DialectSelectComboBox == null)
            {
                return;
            }

            string provider = GetSelectedProvider();
            string baseUrl = (BaseUrlTextBox.Text ?? string.Empty).Trim();
            string model = GetSelectedModelId();
            string dialect = GetSelectedDialectValue();
            SetModeDraft(_speechMode, provider, baseUrl, model, dialect);
        }

        private void ProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isApplyingProviderSelection || ProviderComboBox.SelectedItem is not ComboBoxItem item)
            {
                return;
            }

            string provider = NormalizeProvider(item.Tag?.ToString());
            ApplyPreset(GetPreset(provider));
            PopulateModelOptions(provider, GetModeModel(_speechMode));
            ApplyProviderDefaults(provider);
            PopulateDialectOptions(provider, GetCurrentDialectValue(provider));
            LoadProviderCredentials(provider);
            CaptureCurrentModeDraftFromUi();
        }

        private void ModelSelectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isApplyingModelSelection)
            {
                return;
            }

            string provider = GetSelectedProvider();
            if (_speechMode == SpeechMode.ShortPhrase &&
                string.Equals(provider, "baidu", StringComparison.OrdinalIgnoreCase))
            {
                BaseUrlTextBox.Text = GetShortSpeechDefaultBaseUrl(GetSelectedModelId());
            }

            RefreshHintWithModelDescription();
            ModelTextBox.Text = GetSelectedModelId();
            CaptureCurrentModeDraftFromUi();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string provider = GetSelectedProvider();
            string baseUrl = (BaseUrlTextBox.Text ?? string.Empty).Trim();
            string model = GetSelectedModelId();
            string dialect = GetSelectedDialectValue();
            string v1 = (Credential1TextBox.Text ?? string.Empty).Trim();
            string v2 = (Credential2TextBox.Text ?? string.Empty).Trim();
            string v3 = (Credential3TextBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                System.Windows.MessageBox.Show("请填写连接地址。", "AI配置", MessageBoxButton.OK, MessageBoxImage.Warning);
                BaseUrlTextBox.Focus();
                return;
            }

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
                        string message = _speechMode == SpeechMode.ShortPhrase
                            ? "豆包一句话识别需填写 AppID / Token。"
                            : "豆包语音需填写 App Key / Access Key。Resource ID 可保持默认。";
                        System.Windows.MessageBox.Show(message, "AI配置", MessageBoxButton.OK, MessageBoxImage.Warning);
                        Credential1TextBox.Focus();
                        return;
                    }
                    _configManager.LiveCaptionDoubaoAppKey = v1;
                    _configManager.LiveCaptionDoubaoAccessKey = v2;
                    if (_speechMode != SpeechMode.ShortPhrase)
                    {
                        _configManager.LiveCaptionDoubaoResourceId = string.IsNullOrWhiteSpace(v3)
                            ? "volc.seedasr.sauc.duration"
                            : v3;
                    }
                    break;

            }

            _configManager.LiveCaptionSpeechMode = _speechMode == SpeechMode.ShortPhrase ? "short" : "realtime";
            SaveModeConfig(_speechMode, provider, baseUrl, model, dialect);

            // Keep current-mode values mirrored to legacy global fields so existing runtime paths continue working.
            _configManager.LiveCaptionAsrProvider = provider;
            _configManager.LiveCaptionProxyBaseUrl = baseUrl;
            _configManager.LiveCaptionAsrModel = model;
            _configManager.LiveCaptionVerseSource = GetSelectedVerseSource();
            if (string.Equals(provider, "baidu", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(dialect, out int devPid) &&
                devPid > 0)
            {
                _configManager.LiveCaptionBaiduDevPid = devPid;
            }
            _configManager.SaveConfig();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ─── 经文识别来源 ──────────────────────────────────────────────────

        private static readonly (string Value, string Label)[] VerseSourceOptions =
        {
            ("shortPhrase", "短语识别"),
            ("realtime",    "实时语音"),
            ("both",        "双路并行"),
        };

        private void InitializeVerseSourceComboBox()
        {
            string current = _configManager.LiveCaptionVerseSource ?? "shortPhrase";
            VerseSourceComboBox.Items.Clear();
            int selectedIndex = 0;
            for (int i = 0; i < VerseSourceOptions.Length; i++)
            {
                var (value, label) = VerseSourceOptions[i];
                VerseSourceComboBox.Items.Add(new ComboBoxItem { Content = label, Tag = value });
                if (string.Equals(value, current, StringComparison.OrdinalIgnoreCase))
                    selectedIndex = i;
            }
            VerseSourceComboBox.SelectedIndex = selectedIndex;
        }

        private string GetSelectedVerseSource()
        {
            if (VerseSourceComboBox.SelectedItem is ComboBoxItem item && item.Tag is string v)
                return v;
            return "shortPhrase";
        }

        private void VerseSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

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

        private void ApplyProviderDefaults(string provider)
        {
            string normalized = NormalizeProvider(provider);
            if (_speechMode == SpeechMode.ShortPhrase &&
                string.Equals(normalized, "baidu", StringComparison.OrdinalIgnoreCase))
            {
                BaseUrlTextBox.Text = GetShortSpeechDefaultBaseUrl(GetSelectedModelId());
                return;
            }

            BaseUrlTextBox.Text = GetProviderDefaultBaseUrl(_speechMode, normalized);
        }

        private static string GetProviderDefaultBaseUrl(string provider)
        {
            return NormalizeProvider(provider) switch
            {
                "aliyun" => "wss://nls-gateway-cn-shanghai.aliyuncs.com/ws/v1",
                "doubao" => "wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_async",
                "tencent" => "wss://asr.cloud.tencent.com/asr/v2",
                _ => "wss://vop.baidu.com/realtime_asr"
            };
        }

        private static string GetProviderDefaultBaseUrl(SpeechMode mode, string provider)
        {
            if (mode == SpeechMode.ShortPhrase)
            {
                return NormalizeProvider(provider) switch
                {
                    "aliyun" => "https://nls-gateway-cn-shanghai.aliyuncs.com/stream/v1/asr",
                    "doubao" => "wss://openspeech.bytedance.com/api/v2/asr",
                    "tencent" => "https://asr.tencentcloudapi.com",
                    _ => "http://vop.baidu.com/server_api"
                };
            }

            return GetProviderDefaultBaseUrl(provider);
        }

        private static string GetShortSpeechDefaultBaseUrl(string modelId)
        {
            string model = (modelId ?? string.Empty).Trim().ToLowerInvariant();
            return model.Contains("short-pro", StringComparison.Ordinal) || model.Contains("80001", StringComparison.Ordinal)
                ? "https://vop.baidu.com/pro_api"
                : "http://vop.baidu.com/server_api";
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
                "funasr" => "doubao",
                _ => "baidu"
            };
        }

        private void InitializeModelSelection(string provider, string currentModel)
        {
            PopulateModelOptions(provider, currentModel);
        }

        private void SpeechModeToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_isApplyingSpeechModeSelection || !IsLoaded)
            {
                return;
            }

            CaptureCurrentModeDraftFromUi();

            _speechMode = ShortSpeechModeToggle?.IsChecked == true
                ? SpeechMode.ShortPhrase
                : SpeechMode.Realtime;

            if (RealtimeSpeechModeToggle != null)
            {
                RealtimeSpeechModeToggle.IsChecked = _speechMode == SpeechMode.Realtime;
            }
            if (ShortSpeechModeToggle != null)
            {
                ShortSpeechModeToggle.IsChecked = _speechMode == SpeechMode.ShortPhrase;
            }

            string provider = GetModeProvider(_speechMode);
            string preferredModelId = GetModeModel(_speechMode);
            PopulateProviderOptions(_speechMode, provider);
            provider = GetSelectedProvider();
            BaseUrlTextBox.Text = GetModeBaseUrl(_speechMode);
            PopulateModelOptions(provider, preferredModelId);
            InitializeDialectSelection(provider);
            LoadProviderCredentials(provider);
            CaptureCurrentModeDraftFromUi();
        }

        private void PopulateModelOptions(string provider, string preferredModelId)
        {
            _isApplyingModelSelection = true;
            try
            {
                var options = GetBuiltInModelOptions(provider, _speechMode);
                ModelSelectComboBox.Items.Clear();
                foreach (ModelOption option in options)
                {
                    ModelSelectComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = option.Label,
                        Tag = option.ModelId,
                        ToolTip = option.Description
                    });
                }

                int index = 0;
                if (!string.IsNullOrWhiteSpace(preferredModelId))
                {
                    for (int i = 0; i < ModelSelectComboBox.Items.Count; i++)
                    {
                        if (ModelSelectComboBox.Items[i] is ComboBoxItem item &&
                            string.Equals(item.Tag?.ToString(), preferredModelId, StringComparison.OrdinalIgnoreCase))
                        {
                            index = i;
                            break;
                        }
                    }
                }
                ModelSelectComboBox.SelectedIndex = index;
            }
            finally
            {
                _isApplyingModelSelection = false;
            }
            ModelTextBox.Text = GetSelectedModelId();
            RefreshHintWithModelDescription();
        }

        private string GetSelectedModelId()
        {
            if (ModelSelectComboBox.SelectedItem is ComboBoxItem item)
            {
                string model = (item.Tag?.ToString() ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(model))
                {
                    return model;
                }
            }

            var builtIn = GetBuiltInModelOptions(GetSelectedProvider(), _speechMode);
            return builtIn.Length > 0 ? builtIn[0].ModelId : string.Empty;
        }

        private static ModelOption[] GetBuiltInModelOptions(string provider, SpeechMode speechMode)
        {
            string p = NormalizeProvider(provider);
            return p switch
            {
                "aliyun" => new[]
                {
                    new ModelOption
                    {
                        Label = speechMode == SpeechMode.ShortPhrase ? "阿里云一句话识别（默认）" : "Qwen3-ASR-Flash（实时）",
                        ModelId = speechMode == SpeechMode.ShortPhrase ? "aliyun-short" : "qwen3-asr-flash",
                        Description = speechMode == SpeechMode.ShortPhrase
                            ? "阿里云一句话识别 REST API，60 秒内短音频。"
                            : "低延迟实时识别，默认推荐用于直播/唱歌。"
                    }
                },
                "doubao" => new[]
                {
                    new ModelOption
                    {
                        Label = speechMode == SpeechMode.ShortPhrase ? "豆包一句话识别（默认）" : "豆包实时语音（默认）",
                        ModelId = speechMode == SpeechMode.ShortPhrase ? "volcengine_input_common" : "doubao-realtime",
                        Description = speechMode == SpeechMode.ShortPhrase
                            ? "短音频一句话识别，模型ID作为 Cluster，默认 volcengine_input_common。"
                            : "默认实时语音链路，适合现场字幕。"
                    }
                },
                "tencent" => new[]
                {
                    new ModelOption
                    {
                        Label = speechMode == SpeechMode.ShortPhrase ? "腾讯一句话识别（默认）" : "腾讯实时语音（默认）",
                        ModelId = "16k_zh",
                        Description = speechMode == SpeechMode.ShortPhrase
                            ? "腾讯云一句话识别（普通话 16k，60 秒内短音频）。"
                            : "实时流式识别（普通话 16k）。"
                    }
                },
                _ => speechMode == SpeechMode.ShortPhrase
                    ? new[]
                    {
                        new ModelOption
                        {
                            Label = "百度经文识别标准版",
                            ModelId = "baidu-short-standard",
                            Description = "标准版经文识别（server_api，默认 dev_pid=1537）。"
                        },
                        new ModelOption
                        {
                            Label = "百度经文识别极速版",
                            ModelId = "baidu-short-pro",
                            Description = "极速版经文识别（pro_api，默认 dev_pid=80001）。"
                        }
                    }
                    : new[]
                    {
                        new ModelOption
                        {
                            Label = "百度实时语音（WebSocket）",
                            ModelId = "baidu-realtime",
                            Description = "实时识别主链路；失败时回退经文识别。"
                        }
                    }
            };
        }

        private void InitializeDialectSelection(string provider)
        {
            PopulateDialectOptions(provider, GetCurrentDialectValue(provider));
        }

        private void PopulateDialectOptions(string provider, string preferredValue)
        {
            _isApplyingDialectSelection = true;
            try
            {
                var options = GetBuiltInDialectOptions(provider);
                DialectSelectComboBox.Items.Clear();
                foreach (DialectOption option in options)
                {
                    DialectSelectComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = option.Label,
                        Tag = option.Value,
                        ToolTip = option.Description
                    });
                }

                int index = 0;
                if (!string.IsNullOrWhiteSpace(preferredValue))
                {
                    for (int i = 0; i < DialectSelectComboBox.Items.Count; i++)
                    {
                        if (DialectSelectComboBox.Items[i] is ComboBoxItem item &&
                            string.Equals(item.Tag?.ToString(), preferredValue, StringComparison.OrdinalIgnoreCase))
                        {
                            index = i;
                            break;
                        }
                    }
                }
                DialectSelectComboBox.SelectedIndex = index;
            }
            finally
            {
                _isApplyingDialectSelection = false;
            }
            RefreshHintWithModelDescription();
        }

        private string GetCurrentDialectValue(string provider)
        {
            if (string.Equals(provider, "baidu", StringComparison.OrdinalIgnoreCase))
            {
                return _speechMode == SpeechMode.ShortPhrase
                    ? (string.IsNullOrWhiteSpace(_draftShortDialect) ? _configManager.LiveCaptionShortBaiduDevPid.ToString() : _draftShortDialect)
                    : (string.IsNullOrWhiteSpace(_draftRealtimeDialect) ? _configManager.LiveCaptionRealtimeBaiduDevPid.ToString() : _draftRealtimeDialect);
            }

            return "default";
        }

        private string GetSelectedDialectValue()
        {
            if (DialectSelectComboBox.SelectedItem is ComboBoxItem item)
            {
                string value = (item.Tag?.ToString() ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return "default";
        }

        private static DialectOption[] GetBuiltInDialectOptions(string provider)
        {
            string p = NormalizeProvider(provider);
            return p switch
            {
                "baidu" => new[]
                {
                    new DialectOption { Label = "普通话（默认）", Value = "1537", Description = "百度 dev_pid=1537。" },
                    new DialectOption { Label = "普通话远场", Value = "1936", Description = "百度 dev_pid=1936，远场识别场景。" },
                    new DialectOption { Label = "粤语", Value = "1637", Description = "百度 dev_pid=1637。" },
                    new DialectOption { Label = "四川话", Value = "1837", Description = "百度 dev_pid=1837。" }
                },
                _ => new[]
                {
                    new DialectOption { Label = "默认（由平台自动）", Value = "default", Description = "当前平台在本应用内未开放独立方言参数。" }
                }
            };
        }

        private void DialectSelectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isApplyingDialectSelection)
            {
                return;
            }

            RefreshHintWithModelDescription();
        }

        private void RefreshHintWithModelDescription()
        {
            string desc = string.Empty;
            if (ModelSelectComboBox.SelectedItem is ComboBoxItem item)
            {
                desc = (item.ToolTip?.ToString() ?? string.Empty).Trim();
            }
            string dialectDesc = string.Empty;
            if (DialectSelectComboBox.SelectedItem is ComboBoxItem dialectItem)
            {
                dialectDesc = (dialectItem.ToolTip?.ToString() ?? string.Empty).Trim();
            }

            string hint = string.IsNullOrWhiteSpace(desc)
                ? _baseHint
                : $"{_baseHint}{Environment.NewLine}模型说明：{desc}";
            if (!string.IsNullOrWhiteSpace(dialectDesc))
            {
                hint = $"{hint}{Environment.NewLine}方言说明：{dialectDesc}";
            }
            HintTextBlock.Text = hint;
        }

        private ProviderPreset GetPreset(string provider)
        {
            return provider switch
            {
                "tencent" => new ProviderPreset
                {
                    Provider = "tencent",
                    Label1 = "AppID（可选）",
                    Label2 = "SecretId",
                    Label3 = "SecretKey",
                    Hint = _speechMode == SpeechMode.ShortPhrase
                        ? "腾讯云一句话识别：接口地址 https://asr.tencentcloudapi.com（短语识别链会自动完成 TC3 签名）。"
                        : "腾讯云实时识别：连接地址示例 wss://asr.cloud.tencent.com/asr/v2（实时链路会自动拼接 AppID 与签名参数）"
                },
                "aliyun" => new ProviderPreset
                {
                    Provider = "aliyun",
                    Label1 = "AppKey",
                    Label2 = "AccessKey ID",
                    Label3 = "AccessKey Secret",
                    Hint = _speechMode == SpeechMode.ShortPhrase
                        ? "阿里云一句话识别：REST API POST 音频到 nls-gateway-cn-shanghai.aliyuncs.com/stream/v1/asr"
                        : "阿里云实时识别：连接地址示例 wss://nls-gateway-cn-shanghai.aliyuncs.com/ws/v1"
                },
                "doubao" => new ProviderPreset
                {
                    Provider = "doubao",
                    Label1 = _speechMode == SpeechMode.ShortPhrase ? "AppID" : "App Key",
                    Label2 = _speechMode == SpeechMode.ShortPhrase ? "Token" : "Access Key",
                    Label3 = _speechMode == SpeechMode.ShortPhrase
                        ? "Resource ID（短语音不使用）"
                        : "Resource ID（默认 volc.seedasr.sauc.duration）",
                    ShowCredential3 = false,
                    Hint = _speechMode == SpeechMode.ShortPhrase
                        ? "豆包一句话识别：连接地址示例 wss://openspeech.bytedance.com/api/v2/asr（请求头 Authorization=Bearer; Token；模型框填写 Cluster，默认 volcengine_input_common）。"
                        : "豆包实时识别：连接地址示例 wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_async"
                },
                _ => new ProviderPreset
                {
                    Provider = "baidu",
                    Label1 = "AppID",
                    Label2 = "API Key",
                    Label3 = "Secret Key",
                    Hint = "百度语音实时识别：连接地址示例 wss://vop.baidu.com/realtime_asr（经文识别独立配置）"
                }
            };
        }

        private void ApplyPreset(ProviderPreset preset)
        {
            Credential1Label.Text = preset.Label1;
            Credential2Label.Text = preset.Label2;
            Credential3Label.Text = preset.Label3;
            Credential1Label.Visibility = preset.ShowCredential1 ? Visibility.Visible : Visibility.Collapsed;
            Credential1TextBox.Visibility = preset.ShowCredential1 ? Visibility.Visible : Visibility.Collapsed;
            Credential2Label.Visibility = preset.ShowCredential2 ? Visibility.Visible : Visibility.Collapsed;
            Credential2TextBox.Visibility = preset.ShowCredential2 ? Visibility.Visible : Visibility.Collapsed;
            Credential3Label.Visibility = preset.ShowCredential3 ? Visibility.Visible : Visibility.Collapsed;
            Credential3TextBox.Visibility = preset.ShowCredential3 ? Visibility.Visible : Visibility.Collapsed;
                _baseHint = preset.Hint;
            RefreshHintWithModelDescription();
        }

        private void ApplySpeechMode(SpeechMode speechMode)
        {
            _speechMode = speechMode;
            if (RealtimeSpeechModeToggle == null || ShortSpeechModeToggle == null)
            {
                return;
            }

            _isApplyingSpeechModeSelection = true;
            try
            {
                RealtimeSpeechModeToggle.IsChecked = speechMode == SpeechMode.Realtime;
                ShortSpeechModeToggle.IsChecked = speechMode == SpeechMode.ShortPhrase;
            }
            finally
            {
                _isApplyingSpeechModeSelection = false;
            }
        }

        private static SpeechMode ResolveSpeechMode(string modelId)
        {
            string model = (modelId ?? string.Empty).Trim().ToLowerInvariant();
            return model.Contains("short", StringComparison.Ordinal)
                ? SpeechMode.ShortPhrase
                : SpeechMode.Realtime;
        }

        private void PopulateProviderOptions(SpeechMode mode, string preferredProvider)
        {
            _isApplyingProviderSelection = true;
            try
            {
                ProviderComboBox.Items.Clear();
                foreach (ProviderOption option in GetProviderOptions(mode))
                {
                    ProviderComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = option.DisplayName,
                        Tag = option.Provider
                    });
                }

                SelectProvider(preferredProvider);
            }
            finally
            {
                _isApplyingProviderSelection = false;
            }

            string selectedProvider = GetSelectedProvider();
            ApplyPreset(GetPreset(selectedProvider));
        }

        private static ProviderOption[] GetProviderOptions(SpeechMode mode)
        {
            if (mode == SpeechMode.ShortPhrase)
            {
                return new[]
                {
                    new ProviderOption { Provider = "baidu", DisplayName = "百度语音" },
                    new ProviderOption { Provider = "aliyun", DisplayName = "阿里云语音" },
                    new ProviderOption { Provider = "doubao", DisplayName = "豆包语音" },
                    new ProviderOption { Provider = "tencent", DisplayName = "腾讯云语音" }
                };
            }

            return new[]
            {
                new ProviderOption { Provider = "baidu", DisplayName = "百度语音" },
                new ProviderOption { Provider = "aliyun", DisplayName = "阿里云语音" },
                new ProviderOption { Provider = "doubao", DisplayName = "豆包语音" },
                new ProviderOption { Provider = "tencent", DisplayName = "腾讯云语音" }
            };
        }

        private string GetModeProvider(SpeechMode mode)
        {
            return mode == SpeechMode.ShortPhrase
                ? _draftShortProvider
                : _draftRealtimeProvider;
        }

        private string GetModeBaseUrl(SpeechMode mode)
        {
            string provider = GetModeProvider(mode);
            string value = mode == SpeechMode.ShortPhrase
                ? _draftShortBaseUrl
                : _draftRealtimeBaseUrl;

            return string.IsNullOrWhiteSpace(value)
                ? (mode == SpeechMode.ShortPhrase && string.Equals(provider, "baidu", StringComparison.OrdinalIgnoreCase)
                    ? GetShortSpeechDefaultBaseUrl(GetModeModel(mode))
                    : GetProviderDefaultBaseUrl(mode, provider))
                : value;
        }

        private string GetModeModel(SpeechMode mode)
        {
            return mode == SpeechMode.ShortPhrase
                ? _draftShortModel
                : _draftRealtimeModel;
        }

        private void SaveModeConfig(SpeechMode mode, string provider, string baseUrl, string model, string dialect)
        {
            if (mode == SpeechMode.ShortPhrase)
            {
                _configManager.LiveCaptionShortAsrProvider = provider;
                _configManager.LiveCaptionShortProxyBaseUrl = baseUrl;
                _configManager.LiveCaptionShortAsrModel = model;
                if (string.Equals(provider, "baidu", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(dialect, out int shortDevPid) &&
                    shortDevPid > 0)
                {
                    _configManager.LiveCaptionShortBaiduDevPid = shortDevPid;
                }
                return;
            }

            _configManager.LiveCaptionRealtimeAsrProvider = provider;
            _configManager.LiveCaptionRealtimeProxyBaseUrl = baseUrl;
            _configManager.LiveCaptionRealtimeAsrModel = model;
            if (string.Equals(provider, "baidu", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(dialect, out int realtimeDevPid) &&
                realtimeDevPid > 0)
            {
                _configManager.LiveCaptionRealtimeBaiduDevPid = realtimeDevPid;
            }
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
