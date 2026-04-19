using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
        private string _draftTencentRealtimeCustomizationId = string.Empty;
        private string _draftTencentShortCustomizationId = string.Empty;
        private string _draftDoubaoBoostingTableId = string.Empty;
        private string _draftDoubaoBoostingTableName = string.Empty;
        private string _draftDoubaoHotwordOpenApiAk = string.Empty;
        private string _draftDoubaoHotwordOpenApiSk = string.Empty;
        private readonly List<TencentCustomizationModelEntry> _tencentCustomizationModels = new();
        private readonly List<DoubaoBoostingTableEntry> _doubaoBoostingTables = new();
        private string _lastTencentCustomizationSyncFingerprint = string.Empty;
        private bool _tencentCustomizationSyncInFlight;
        private string _lastDoubaoHotwordSyncFingerprint = string.Empty;
        private bool _doubaoHotwordSyncInFlight;
        private const string TencentAsrHost = "asr.tencentcloudapi.com";
        private const string TencentAsrVersion = "2019-06-14";
        private const string TencentAsrService = "asr";
        private const string VolcOpenApiHost = "open.volcengineapi.com";
        private const string VolcOpenApiRegion = "cn-north-1";
        private const string VolcOpenApiService = "speech_saas_prod";
        private const string VolcListBoostingTableVersion = "2022-08-30";

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

        private sealed class ModelPickerItem
        {
            public string DisplayText { get; set; } = string.Empty;
            public string ModelId { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public bool IsCustom { get; set; }
            public bool IsAddAction { get; set; }
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

        private sealed class DoubaoBoostingTableEntry
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
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
            InitializeModelSelection(provider, GetPreferredModelSelectionId(_speechMode, provider));
            InitializeDialectSelection(provider);
            LoadProviderCredentials(provider);
            TriggerTencentCustomizationSyncIfNeeded(provider);
            PopulateDoubaoHotwordOptions(_draftDoubaoBoostingTableId, _draftDoubaoBoostingTableName);
            DoubaoHotwordAkTextBox.Text = _draftDoubaoHotwordOpenApiAk;
            DoubaoHotwordSkTextBox.Text = _draftDoubaoHotwordOpenApiSk;
            TriggerDoubaoHotwordSyncIfNeeded(provider);
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

            _draftTencentRealtimeCustomizationId = (_configManager.LiveCaptionTencentRealtimeCustomizationId ?? string.Empty).Trim();
            _draftTencentShortCustomizationId = (_configManager.LiveCaptionTencentShortCustomizationId ?? string.Empty).Trim();
            _draftDoubaoBoostingTableId = (_configManager.LiveCaptionDoubaoBoostingTableId ?? string.Empty).Trim();
            _draftDoubaoBoostingTableName = (_configManager.LiveCaptionDoubaoBoostingTableName ?? string.Empty).Trim();
            _draftDoubaoHotwordOpenApiAk = (_configManager.LiveCaptionDoubaoHotwordOpenApiAk ?? string.Empty).Trim();
            _draftDoubaoHotwordOpenApiSk = (_configManager.LiveCaptionDoubaoHotwordOpenApiSk ?? string.Empty).Trim();
            _tencentCustomizationModels.Clear();
            var configuredModels = _configManager.LiveCaptionTencentCustomizationModels ?? Array.Empty<TencentCustomizationModelEntry>();
            for (int i = 0; i < configuredModels.Length; i++)
            {
                var item = configuredModels[i];
                if (item == null)
                {
                    continue;
                }

                string id = (item.Id ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                _tencentCustomizationModels.Add(new TencentCustomizationModelEntry
                {
                    Id = id,
                    Name = (item.Name ?? string.Empty).Trim()
                });
            }
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
            PopulateModelOptions(provider, GetPreferredModelSelectionId(_speechMode, provider));
            ApplyProviderDefaults(provider);
            PopulateDialectOptions(provider, GetCurrentDialectValue(provider));
            LoadProviderCredentials(provider);
            TriggerTencentCustomizationSyncIfNeeded(provider);
            TriggerDoubaoHotwordSyncIfNeeded(provider);
            CaptureCurrentModeDraftFromUi();
        }

        private void ModelSelectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isApplyingModelSelection)
            {
                return;
            }

            if (ModelSelectComboBox.SelectedItem is ModelPickerItem picked && picked.IsAddAction)
            {
                var addResult = ShowTencentCustomModelAddDialog();
                if (addResult.Ok)
                {
                    var customId = addResult.ModelId;
                    var customName = addResult.ModelName;
                    UpsertTencentCustomModel(customId, customName);
                    SetModeTencentCustomizationId(_speechMode, customId);
                    if (_speechMode == SpeechMode.ShortPhrase)
                    {
                        _draftShortModel = "16k_zh";
                    }
                    else
                    {
                        _draftRealtimeModel = "16k_zh";
                    }
                    PopulateModelOptions(GetSelectedProvider(), customId);
                    CaptureCurrentModeDraftFromUi();
                }
                else
                {
                    string selectedProvider = GetSelectedProvider();
                    PopulateModelOptions(selectedProvider, GetPreferredModelSelectionId(_speechMode, selectedProvider));
                }
                return;
            }

            string provider = GetSelectedProvider();
            if (_speechMode == SpeechMode.ShortPhrase &&
                string.Equals(provider, "baidu", StringComparison.OrdinalIgnoreCase))
            {
                BaseUrlTextBox.Text = GetShortSpeechDefaultBaseUrl(GetSelectedModelId());
            }

            string modelId = GetSelectedModelId();
            RefreshHintWithModelDescription();
            ModelTextBox.Text = modelId;
            if (IsTencentRealtimeModelManageMode(provider))
            {
                if (ModelSelectComboBox.SelectedItem is ModelPickerItem selectedItem)
                {
                    if (selectedItem.IsCustom)
                    {
                        SetModeTencentCustomizationId(_speechMode, selectedItem.ModelId);
                    }
                    else if (!selectedItem.IsAddAction)
                    {
                        SetModeTencentCustomizationId(_speechMode, string.Empty);
                    }
                }
            }
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
                    _configManager.LiveCaptionDoubaoBoostingTableId = _draftDoubaoBoostingTableId;
                    _configManager.LiveCaptionDoubaoBoostingTableName = _draftDoubaoBoostingTableName;
                    _configManager.LiveCaptionDoubaoHotwordOpenApiAk = (DoubaoHotwordAkTextBox.Text ?? string.Empty).Trim();
                    _configManager.LiveCaptionDoubaoHotwordOpenApiSk = (DoubaoHotwordSkTextBox.Text ?? string.Empty).Trim();
                    break;

            }

            _configManager.LiveCaptionSpeechMode = _speechMode == SpeechMode.ShortPhrase ? "short" : "realtime";
            _configManager.LiveCaptionTencentRealtimeCustomizationId = (_draftTencentRealtimeCustomizationId ?? string.Empty).Trim();
            _configManager.LiveCaptionTencentShortCustomizationId = (_draftTencentShortCustomizationId ?? string.Empty).Trim();
            _configManager.LiveCaptionTencentCustomizationModels = _tencentCustomizationModels.ToArray();
            Debug.WriteLine(
                $"[AiConfig][TencentCustom] save: provider={provider}, mode={_speechMode}, model={model}, " +
                $"realtimeCustomizationId='{_configManager.LiveCaptionTencentRealtimeCustomizationId}', " +
                $"shortCustomizationId='{_configManager.LiveCaptionTencentShortCustomizationId}', listCount={_tencentCustomizationModels.Count}");
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
            string preferredModelId = GetPreferredModelSelectionId(_speechMode, provider);
            PopulateProviderOptions(_speechMode, provider);
            provider = GetSelectedProvider();
            BaseUrlTextBox.Text = GetModeBaseUrl(_speechMode);
            PopulateModelOptions(provider, preferredModelId);
            InitializeDialectSelection(provider);
            LoadProviderCredentials(provider);
            TriggerTencentCustomizationSyncIfNeeded(provider);
            TriggerDoubaoHotwordSyncIfNeeded(provider);
            CaptureCurrentModeDraftFromUi();
        }

        private void PopulateModelOptions(string provider, string preferredModelId)
        {
            _isApplyingModelSelection = true;
            try
            {
                var options = GetBuiltInModelOptions(provider, _speechMode);
                bool canManageCustom = IsTencentRealtimeModelManageMode(provider);
                var items = new List<ModelPickerItem>();
                foreach (ModelOption option in options)
                {
                    items.Add(new ModelPickerItem
                    {
                        DisplayText = option.Label,
                        ModelId = option.ModelId,
                        Description = option.Description,
                        IsCustom = false,
                        IsAddAction = false
                    });
                }
                if (canManageCustom)
                {
                    for (int i = 0; i < _tencentCustomizationModels.Count; i++)
                    {
                        var custom = _tencentCustomizationModels[i];
                        string id = (custom?.Id ?? string.Empty).Trim();
                        if (string.IsNullOrWhiteSpace(id))
                        {
                            continue;
                        }

                        bool duplicated = false;
                        for (int j = 0; j < options.Length; j++)
                        {
                            if (string.Equals(options[j].ModelId, id, StringComparison.OrdinalIgnoreCase))
                            {
                                duplicated = true;
                                break;
                            }
                        }
                        if (duplicated)
                        {
                            continue;
                        }

                        string name = (custom.Name ?? string.Empty).Trim();
                        string display = string.IsNullOrWhiteSpace(name) ? $"{id}（自学习）" : $"{name}（自学习）";
                        items.Add(new ModelPickerItem
                        {
                            DisplayText = display,
                            ModelId = id,
                            Description = $"腾讯自学习模型：{id}",
                            IsCustom = true,
                            IsAddAction = false
                        });
                    }

                    items.Add(new ModelPickerItem
                    {
                        DisplayText = "➕ 新增自学习模型",
                        ModelId = "__add_custom_model__",
                        Description = "添加腾讯自学习模型（请输入模型ID）",
                        IsCustom = false,
                        IsAddAction = true
                    });
                }

                int index = 0;
                if (!string.IsNullOrWhiteSpace(preferredModelId))
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (!items[i].IsAddAction &&
                            string.Equals(items[i].ModelId, preferredModelId, StringComparison.OrdinalIgnoreCase))
                        {
                            index = i;
                            break;
                        }
                    }
                }

                ModelSelectComboBox.ItemsSource = items;
                ModelSelectComboBox.SelectedIndex = items.Count == 0 ? -1 : Math.Clamp(index, 0, items.Count - 1);
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
            if (ModelSelectComboBox.SelectedItem is ModelPickerItem item)
            {
                if (item.IsAddAction)
                {
                    return GetModeModel(_speechMode);
                }

                // 腾讯实时下拉里自学习模型是 customization_id，不是 engine_model_type。
                if (IsTencentRealtimeModelManageMode(GetSelectedProvider()) && item.IsCustom)
                {
                    return "16k_zh";
                }

                string model = (item.ModelId ?? string.Empty).Trim();
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

        private bool IsTencentRealtimeModelManageMode(string provider)
        {
            return string.Equals(provider, "tencent", StringComparison.OrdinalIgnoreCase);
        }

        private void TriggerTencentCustomizationSyncIfNeeded(string provider)
        {
            if (!IsTencentRealtimeModelManageMode(provider))
            {
                return;
            }

            if (_tencentCustomizationSyncInFlight)
            {
                return;
            }

            string secretId = (_configManager.LiveCaptionTencentSecretId ?? string.Empty).Trim();
            string secretKey = (_configManager.LiveCaptionTencentSecretKey ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(secretId) || string.IsNullOrWhiteSpace(secretKey))
            {
                return;
            }

            string fingerprint = $"{secretId}|{secretKey}";
            if (string.Equals(_lastTencentCustomizationSyncFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return;
            }

            _tencentCustomizationSyncInFlight = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    var remote = await FetchTencentCustomizationModelsAsync(secretId, secretKey, CancellationToken.None);
                    if (remote == null || remote.Count == 0)
                    {
                        return;
                    }

                    await Dispatcher.InvokeAsync(() =>
                    {
                        _tencentCustomizationModels.Clear();
                        _tencentCustomizationModels.AddRange(remote);
                        _lastTencentCustomizationSyncFingerprint = fingerprint;
                        string selectedProvider = GetSelectedProvider();
                        PopulateModelOptions(selectedProvider, GetPreferredModelSelectionId(_speechMode, selectedProvider));
                        Debug.WriteLine($"[AiConfig][TencentCustom] auto-sync success: count={remote.Count}");
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AiConfig][TencentCustom] auto-sync failed: {ex.Message}");
                }
                finally
                {
                    _tencentCustomizationSyncInFlight = false;
                }
            });
        }

        private async void RefreshDoubaoHotwordButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshDoubaoHotwordTablesAsync(force: true);
        }

        private void DoubaoHotwordComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DoubaoHotwordComboBox?.SelectedItem is not ComboBoxItem item)
            {
                return;
            }

            if (item.Tag is DoubaoBoostingTableEntry table)
            {
                _draftDoubaoBoostingTableId = (table.Id ?? string.Empty).Trim();
                _draftDoubaoBoostingTableName = (table.Name ?? string.Empty).Trim();
                return;
            }

            _draftDoubaoBoostingTableId = string.Empty;
            _draftDoubaoBoostingTableName = string.Empty;
        }

        private void TriggerDoubaoHotwordSyncIfNeeded(string provider)
        {
            if (!string.Equals(provider, "doubao", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _ = RefreshDoubaoHotwordTablesAsync(force: false);
        }

        private async Task RefreshDoubaoHotwordTablesAsync(bool force)
        {
            if (!string.Equals(GetSelectedProvider(), "doubao", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_doubaoHotwordSyncInFlight)
            {
                return;
            }

            string appId = (Credential1TextBox.Text ?? string.Empty).Trim();
            string accessKey = (DoubaoHotwordAkTextBox.Text ?? string.Empty).Trim();
            string secretKey = (DoubaoHotwordSkTextBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(appId))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey))
            {
                if (force)
                {
                    System.Windows.MessageBox.Show(
                        this,
                        "请先填写“热词管理 AK/SK”（IAM 访问密钥），再刷新热词列表。",
                        "AI配置",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                return;
            }

            if (!int.TryParse(appId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int appIdValue) || appIdValue <= 0)
            {
                if (force)
                {
                    System.Windows.MessageBox.Show(this, "豆包热词自动获取需要 AppID 为数字。", "AI配置", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

            string fingerprint = $"{appId}|{accessKey}|{secretKey}";
            if (!force && string.Equals(_lastDoubaoHotwordSyncFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return;
            }

            _doubaoHotwordSyncInFlight = true;
            string originalButtonText = RefreshDoubaoHotwordButton.Content?.ToString() ?? "刷新列表";
            try
            {
                RefreshDoubaoHotwordButton.IsEnabled = false;
                RefreshDoubaoHotwordButton.Content = "加载中...";

                List<DoubaoBoostingTableEntry> tables = await FetchDoubaoBoostingTablesAsync(
                    appIdValue,
                    accessKey,
                    secretKey,
                    CancellationToken.None);

                _doubaoBoostingTables.Clear();
                _doubaoBoostingTables.AddRange(tables);
                _lastDoubaoHotwordSyncFingerprint = fingerprint;
                PopulateDoubaoHotwordOptions(_draftDoubaoBoostingTableId, _draftDoubaoBoostingTableName);

                if (force)
                {
                    System.Windows.MessageBox.Show(
                        this,
                        $"已加载豆包热词表 {tables.Count} 条。",
                        "AI配置",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AiConfig][DoubaoHotword] sync failed: {ex.Message}");
                if (force)
                {
                    System.Windows.MessageBox.Show(
                        this,
                        $"加载豆包热词表失败：{ex.Message}",
                        "AI配置",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            finally
            {
                _doubaoHotwordSyncInFlight = false;
                RefreshDoubaoHotwordButton.IsEnabled = true;
                RefreshDoubaoHotwordButton.Content = originalButtonText;
            }
        }

        private void PopulateDoubaoHotwordOptions(string preferredId, string preferredName)
        {
            if (DoubaoHotwordComboBox == null)
            {
                return;
            }

            string normalizedPreferredId = (preferredId ?? string.Empty).Trim();
            string normalizedPreferredName = (preferredName ?? string.Empty).Trim();

            DoubaoHotwordComboBox.Items.Clear();
            var noneItem = new ComboBoxItem
            {
                Content = "不使用（保留内置热词上下文）",
                Tag = null
            };
            DoubaoHotwordComboBox.Items.Add(noneItem);

            ComboBoxItem matchedItem = null;
            for (int i = 0; i < _doubaoBoostingTables.Count; i++)
            {
                var table = _doubaoBoostingTables[i];
                string id = (table.Id ?? string.Empty).Trim();
                string name = (table.Name ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var item = new ComboBoxItem
                {
                    Content = string.IsNullOrWhiteSpace(name) ? id : $"{name} ({id})",
                    Tag = new DoubaoBoostingTableEntry { Id = id, Name = name }
                };
                DoubaoHotwordComboBox.Items.Add(item);

                if (!string.IsNullOrWhiteSpace(normalizedPreferredId) &&
                    string.Equals(id, normalizedPreferredId, StringComparison.OrdinalIgnoreCase))
                {
                    matchedItem = item;
                }
                else if (matchedItem == null &&
                    string.IsNullOrWhiteSpace(normalizedPreferredId) &&
                    !string.IsNullOrWhiteSpace(normalizedPreferredName) &&
                    string.Equals(name, normalizedPreferredName, StringComparison.OrdinalIgnoreCase))
                {
                    matchedItem = item;
                }
            }

            if (matchedItem == null &&
                (!string.IsNullOrWhiteSpace(normalizedPreferredId) || !string.IsNullOrWhiteSpace(normalizedPreferredName)))
            {
                var unresolved = new ComboBoxItem
                {
                    Content = $"当前配置（未匹配到列表）：{(string.IsNullOrWhiteSpace(normalizedPreferredName) ? normalizedPreferredId : normalizedPreferredName)}",
                    Tag = new DoubaoBoostingTableEntry
                    {
                        Id = normalizedPreferredId,
                        Name = normalizedPreferredName
                    }
                };
                DoubaoHotwordComboBox.Items.Add(unresolved);
                matchedItem = unresolved;
            }

            DoubaoHotwordComboBox.SelectedItem = matchedItem ?? noneItem;
        }

        private static async Task<List<DoubaoBoostingTableEntry>> FetchDoubaoBoostingTablesAsync(
            int appId,
            string accessKey,
            string secretKey,
            CancellationToken cancellationToken)
        {
            const string action = "ListBoostingTable";
            const string canonicalQuery = "Action=ListBoostingTable&Version=2022-08-30";
            string body = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["Action"] = action,
                ["Version"] = VolcListBoostingTableVersion,
                ["AppID"] = appId,
                ["PageNumber"] = 1,
                ["PageSize"] = 200,
                ["PreviewSize"] = 10
            });

            string payloadHash = Sha256Hex(body);
            DateTime utcNow = DateTime.UtcNow;
            string xDate = utcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            string shortDate = utcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            string authorization = BuildVolcAuthorization(
                accessKey,
                secretKey,
                xDate,
                shortDate,
                canonicalQuery,
                payloadHash);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://{VolcOpenApiHost}/?{canonicalQuery}")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            request.Headers.TryAddWithoutValidation("Host", VolcOpenApiHost);
            request.Headers.TryAddWithoutValidation("X-Date", xDate);
            request.Headers.TryAddWithoutValidation("X-Content-Sha256", payloadHash);
            request.Headers.TryAddWithoutValidation("Authorization", authorization);

            using var response = await client.SendAsync(request, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {TrimForLog(json)}");
            }

            var parsed = ParseDoubaoBoostingTableList(json);
            if (!string.IsNullOrWhiteSpace(parsed.ErrorMessage))
            {
                throw new InvalidOperationException(parsed.ErrorMessage);
            }

            return parsed.Tables;
        }

        private static string BuildVolcAuthorization(
            string accessKey,
            string secretKey,
            string xDate,
            string shortDate,
            string canonicalQueryString,
            string payloadHash)
        {
            string credentialScope = $"{shortDate}/{VolcOpenApiRegion}/{VolcOpenApiService}/request";
            string canonicalHeaders =
                $"content-type:application/json; charset=utf-8\n" +
                $"host:{VolcOpenApiHost}\n" +
                $"x-content-sha256:{payloadHash}\n" +
                $"x-date:{xDate}\n";
            const string signedHeaders = "content-type;host;x-content-sha256;x-date";
            string canonicalRequest =
                $"POST\n/\n{canonicalQueryString}\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";
            string stringToSign =
                $"HMAC-SHA256\n{xDate}\n{credentialScope}\n{Sha256Hex(canonicalRequest)}";

            byte[] kDate = HmacSha256Bytes(Encoding.UTF8.GetBytes(secretKey ?? string.Empty), shortDate);
            byte[] kRegion = HmacSha256Bytes(kDate, VolcOpenApiRegion);
            byte[] kService = HmacSha256Bytes(kRegion, VolcOpenApiService);
            byte[] kSigning = HmacSha256Bytes(kService, "request");
            string signature = ToHex(HmacSha256Bytes(kSigning, stringToSign));

            return $"HMAC-SHA256 Credential={accessKey}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}";
        }

        private sealed class DoubaoBoostingTableParseResult
        {
            public List<DoubaoBoostingTableEntry> Tables { get; } = new();
            public string ErrorMessage { get; set; } = string.Empty;
        }

        private static DoubaoBoostingTableParseResult ParseDoubaoBoostingTableList(string json)
        {
            var result = new DoubaoBoostingTableParseResult();
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("ResponseMetadata", out JsonElement metadata) &&
                metadata.TryGetProperty("Error", out JsonElement error))
            {
                string code = error.TryGetProperty("Code", out JsonElement codeEl) ? (codeEl.GetString() ?? string.Empty) : string.Empty;
                string message = error.TryGetProperty("Message", out JsonElement messageEl) ? (messageEl.GetString() ?? string.Empty) : string.Empty;
                result.ErrorMessage = string.IsNullOrWhiteSpace(code)
                    ? message
                    : $"{code}: {message}";
                return result;
            }

            if (!doc.RootElement.TryGetProperty("Result", out JsonElement rootResult))
            {
                return result;
            }

            if (!rootResult.TryGetProperty("BoostingTables", out JsonElement listElement) ||
                listElement.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (JsonElement item in listElement.EnumerateArray())
            {
                string id = item.TryGetProperty("BoostingTableID", out JsonElement idEl) ? (idEl.GetString() ?? string.Empty).Trim() : string.Empty;
                string name = item.TryGetProperty("BoostingTableName", out JsonElement nameEl) ? (nameEl.GetString() ?? string.Empty).Trim() : string.Empty;
                if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                result.Tables.Add(new DoubaoBoostingTableEntry
                {
                    Id = id,
                    Name = name
                });
            }

            return result;
        }

        private static async Task<List<TencentCustomizationModelEntry>> FetchTencentCustomizationModelsAsync(
            string secretId,
            string secretKey,
            CancellationToken cancellationToken)
        {
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string date = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string payload = "{\"Limit\":100,\"Offset\":0}";
            string authorization = BuildTencentAuthorization(
                secretId,
                secretKey,
                TencentAsrHost,
                TencentAsrService,
                timestamp,
                date,
                "GetCustomizationList",
                payload);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{TencentAsrHost}")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
            request.Headers.TryAddWithoutValidation("Host", TencentAsrHost);
            request.Headers.TryAddWithoutValidation("X-TC-Action", "GetCustomizationList");
            request.Headers.TryAddWithoutValidation("X-TC-Version", TencentAsrVersion);
            request.Headers.TryAddWithoutValidation("X-TC-Timestamp", timestamp.ToString(CultureInfo.InvariantCulture));

            using var response = await client.SendAsync(request, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[AiConfig][TencentCustom] auto-sync http-failed: status={(int)response.StatusCode}, raw={TrimForLog(json)}");
                return new List<TencentCustomizationModelEntry>();
            }

            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Response", out JsonElement resp))
            {
                return new List<TencentCustomizationModelEntry>();
            }

            if (resp.TryGetProperty("Error", out JsonElement err))
            {
                string code = err.TryGetProperty("Code", out JsonElement codeEl) ? (codeEl.GetString() ?? string.Empty) : string.Empty;
                string message = err.TryGetProperty("Message", out JsonElement msgEl) ? (msgEl.GetString() ?? string.Empty) : string.Empty;
                Debug.WriteLine($"[AiConfig][TencentCustom] auto-sync api-error: code={code}, message={message}");
                return new List<TencentCustomizationModelEntry>();
            }

            var result = new List<TencentCustomizationModelEntry>();
            if (!resp.TryGetProperty("Data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in data.EnumerateArray())
            {
                string id = item.TryGetProperty("ModelId", out JsonElement idEl) ? (idEl.GetString() ?? string.Empty).Trim() : string.Empty;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                string serviceType = item.TryGetProperty("ServiceType", out JsonElement typeEl) ? (typeEl.GetString() ?? string.Empty).Trim().ToLowerInvariant() : string.Empty;
                if (!string.IsNullOrWhiteSpace(serviceType) && !string.Equals(serviceType, "realtime", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string name = item.TryGetProperty("ModelName", out JsonElement nameEl) ? (nameEl.GetString() ?? string.Empty).Trim() : string.Empty;
                result.Add(new TencentCustomizationModelEntry
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(name) ? id : name
                });
            }

            return result;
        }

        private static string BuildTencentAuthorization(
            string secretId,
            string secretKey,
            string host,
            string service,
            long timestamp,
            string date,
            string action,
            string payloadJson)
        {
            const string algorithm = "TC3-HMAC-SHA256";
            string httpRequestMethod = "POST";
            string canonicalUri = "/";
            string canonicalQueryString = string.Empty;
            string canonicalHeaders = $"content-type:application/json; charset=utf-8\nhost:{host}\nx-tc-action:{action.ToLowerInvariant()}\n";
            string signedHeaders = "content-type;host;x-tc-action";
            string hashedRequestPayload = Sha256Hex(payloadJson);
            string canonicalRequest = $"{httpRequestMethod}\n{canonicalUri}\n{canonicalQueryString}\n{canonicalHeaders}\n{signedHeaders}\n{hashedRequestPayload}";

            string credentialScope = $"{date}/{service}/tc3_request";
            string stringToSign = $"{algorithm}\n{timestamp}\n{credentialScope}\n{Sha256Hex(canonicalRequest)}";

            byte[] secretDate = HmacSha256Bytes(Encoding.UTF8.GetBytes("TC3" + secretKey), date);
            byte[] secretService = HmacSha256Bytes(secretDate, service);
            byte[] secretSigning = HmacSha256Bytes(secretService, "tc3_request");
            string signature = ToHex(HmacSha256Bytes(secretSigning, stringToSign));

            return $"{algorithm} Credential={secretId}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}";
        }

        private static byte[] HmacSha256Bytes(byte[] key, string message)
        {
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(message ?? string.Empty));
        }

        private static string Sha256Hex(string content)
        {
            using var sha = SHA256.Create();
            return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(content ?? string.Empty)));
        }

        private static string ToHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
            {
                sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        private static string TrimForLog(string text)
        {
            const int max = 220;
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return normalized.Length <= max ? normalized : normalized.Substring(0, max) + "...";
        }

        private static (bool Ok, string ModelId, string ModelName) ShowTencentCustomModelAddDialog()
        {
            string resultId = string.Empty;
            string resultName = string.Empty;

            var dialog = new Window
            {
                Title = "新增自学习模型",
                Width = 460,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = System.Windows.Media.Brushes.White
            };

            var root = new Grid { Margin = new Thickness(20) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var nameLabel = new TextBlock { Text = "模型名称", VerticalAlignment = VerticalAlignment.Center };
            var nameBox = new System.Windows.Controls.TextBox { Height = 34, Padding = new Thickness(8, 0, 8, 0) };
            Grid.SetRow(nameLabel, 0); Grid.SetColumn(nameLabel, 0);
            Grid.SetRow(nameBox, 0); Grid.SetColumn(nameBox, 1);

            var idLabel = new TextBlock { Text = "模型ID", Margin = new Thickness(0, 12, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var idBox = new System.Windows.Controls.TextBox { Height = 34, Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(8, 0, 8, 0) };
            Grid.SetRow(idLabel, 1); Grid.SetColumn(idLabel, 0);
            Grid.SetRow(idBox, 1); Grid.SetColumn(idBox, 1);

            var hint = new TextBlock
            {
                Text = "模型ID必填，名称可选。默认模型不支持删除。",
                Margin = new Thickness(0, 12, 0, 0),
                Foreground = System.Windows.Media.Brushes.Gray
            };
            Grid.SetRow(hint, 2); Grid.SetColumnSpan(hint, 2);

            var actions = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            var cancel = new System.Windows.Controls.Button { Content = "取消", Width = 82, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
            var ok = new System.Windows.Controls.Button { Content = "确定", Width = 82, Height = 32 };
            actions.Children.Add(cancel);
            actions.Children.Add(ok);
            Grid.SetRow(actions, 3); Grid.SetColumnSpan(actions, 2);

            cancel.Click += (_, _) => dialog.DialogResult = false;
            ok.Click += (_, _) =>
            {
                string id = (idBox.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(id))
                {
                    System.Windows.MessageBox.Show(dialog, "请输入模型ID。", "AI配置", MessageBoxButton.OK, MessageBoxImage.Warning);
                    idBox.Focus();
                    return;
                }
                resultId = id;
                resultName = (nameBox.Text ?? string.Empty).Trim();
                dialog.DialogResult = true;
            };

            root.Children.Add(nameLabel);
            root.Children.Add(nameBox);
            root.Children.Add(idLabel);
            root.Children.Add(idBox);
            root.Children.Add(hint);
            root.Children.Add(actions);
            dialog.Content = root;

            bool confirmed = dialog.ShowDialog() == true;
            return confirmed
                ? (true, resultId, resultName)
                : (false, string.Empty, string.Empty);
        }

        private void UpsertTencentCustomModel(string id, string name)
        {
            string normalizedId = (id ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedId))
            {
                return;
            }

            int index = _tencentCustomizationModels.FindIndex(m => string.Equals(m.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _tencentCustomizationModels[index].Name = name.Trim();
                }
                return;
            }

            _tencentCustomizationModels.Add(new TencentCustomizationModelEntry
            {
                Id = normalizedId,
                Name = (name ?? string.Empty).Trim()
            });
        }

        private bool IsBuiltInModelIdForCurrentProvider(string modelId)
        {
            string id = (modelId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            var options = GetBuiltInModelOptions(GetSelectedProvider(), _speechMode);
            for (int i = 0; i < options.Length; i++)
            {
                if (string.Equals(options[i].ModelId, id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void ModelItemDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button button)
            {
                return;
            }

            if (button.Tag is not string id || string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            string provider = GetSelectedProvider();
            if (!IsTencentRealtimeModelManageMode(provider))
            {
                return;
            }

            if (IsBuiltInModelIdForCurrentProvider(id))
            {
                return;
            }

            int index = _tencentCustomizationModels.FindIndex(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return;
            }

            _tencentCustomizationModels.RemoveAt(index);
            if (string.Equals(_draftTencentRealtimeCustomizationId, id, StringComparison.OrdinalIgnoreCase))
            {
                _draftTencentRealtimeCustomizationId = string.Empty;
            }

            if (string.Equals(_draftTencentShortCustomizationId, id, StringComparison.OrdinalIgnoreCase))
            {
                _draftTencentShortCustomizationId = string.Empty;
            }

            string fallback = "16k_zh";
            if (_speechMode == SpeechMode.ShortPhrase)
            {
                _draftShortModel = fallback;
            }
            else
            {
                _draftRealtimeModel = fallback;
            }
            PopulateModelOptions(provider, fallback);
            e.Handled = true;
        }

        private void RefreshHintWithModelDescription()
        {
            string desc = string.Empty;
            if (ModelSelectComboBox.SelectedItem is ModelPickerItem item)
            {
                desc = (item.Description ?? string.Empty).Trim();
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
                        : "腾讯云实时识别：连接地址示例 wss://asr.cloud.tencent.com/asr/v2（实时链路会自动拼接 AppID、签名及 customization_id）"
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
                        ? "豆包一句话识别：连接地址示例 wss://openspeech.bytedance.com/api/v2/asr（请求头 Authorization=Bearer; Token；模型框填写 Cluster，默认 volcengine_input_common）。热词表自动获取需填写“热词管理 AK/SK”（IAM 访问密钥）。"
                        : "豆包实时识别：连接地址示例 wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_async。热词表自动获取需填写“热词管理 AK/SK”（IAM 访问密钥）。"
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
            bool showDoubaoHotword = string.Equals(preset.Provider, "doubao", StringComparison.OrdinalIgnoreCase);
            DoubaoHotwordLabel.Visibility = showDoubaoHotword ? Visibility.Visible : Visibility.Collapsed;
            DoubaoHotwordPanel.Visibility = showDoubaoHotword ? Visibility.Visible : Visibility.Collapsed;
            DoubaoHotwordAkLabel.Visibility = showDoubaoHotword ? Visibility.Visible : Visibility.Collapsed;
            DoubaoHotwordAkTextBox.Visibility = showDoubaoHotword ? Visibility.Visible : Visibility.Collapsed;
            DoubaoHotwordSkLabel.Visibility = showDoubaoHotword ? Visibility.Visible : Visibility.Collapsed;
            DoubaoHotwordSkTextBox.Visibility = showDoubaoHotword ? Visibility.Visible : Visibility.Collapsed;
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

        private string GetPreferredModelSelectionId(SpeechMode mode, string provider)
        {
            if (string.Equals(provider, "tencent", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(GetModeTencentCustomizationId(mode)))
            {
                return GetModeTencentCustomizationId(mode);
            }

            return GetModeModel(mode);
        }

        private string GetModeTencentCustomizationId(SpeechMode mode)
        {
            return mode == SpeechMode.ShortPhrase
                ? (_draftTencentShortCustomizationId ?? string.Empty).Trim()
                : (_draftTencentRealtimeCustomizationId ?? string.Empty).Trim();
        }

        private void SetModeTencentCustomizationId(SpeechMode mode, string customizationId)
        {
            string next = (customizationId ?? string.Empty).Trim();
            if (mode == SpeechMode.ShortPhrase)
            {
                _draftTencentShortCustomizationId = next;
                return;
            }

            _draftTencentRealtimeCustomizationId = next;
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
                    DoubaoHotwordAkTextBox.Text = _configManager.LiveCaptionDoubaoHotwordOpenApiAk;
                    DoubaoHotwordSkTextBox.Text = _configManager.LiveCaptionDoubaoHotwordOpenApiSk;
                    break;
                default:
                    Credential1TextBox.Text = _configManager.LiveCaptionBaiduAppId;
                    Credential2TextBox.Text = _configManager.LiveCaptionBaiduApiKey;
                    Credential3TextBox.Text = _configManager.LiveCaptionBaiduSecretKey;
                    DoubaoHotwordAkTextBox.Text = string.Empty;
                    DoubaoHotwordSkTextBox.Text = string.Empty;
                    break;
            }

            if (string.Equals(provider, "doubao", StringComparison.OrdinalIgnoreCase))
            {
                PopulateDoubaoHotwordOptions(_draftDoubaoBoostingTableId, _draftDoubaoBoostingTableName);
            }
        }
    }
}
