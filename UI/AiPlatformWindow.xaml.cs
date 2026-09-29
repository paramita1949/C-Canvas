using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ImageColorChanger.Core;
using ImageColorChanger.Services.Ai;

namespace ImageColorChanger.UI
{
    public partial class AiPlatformWindow : Window
    {
        private static readonly HttpClient ModelHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private readonly ConfigManager _configManager;
        private readonly AiModelCatalogClient _modelCatalogClient;
        private readonly ObservableCollection<AiProfileSummary> _profiles = new ObservableCollection<AiProfileSummary>();
        private bool _isRefreshing;
        private bool _isDraft;

        public event Action AiCaptionRequested;
        public event Action AsrEngineSettingsRequested;

        public AiPlatformWindow(ConfigManager configManager)
        {
            InitializeComponent();
            _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
            _modelCatalogClient = new AiModelCatalogClient(ModelHttpClient);
            var providerView = new ListCollectionView(AiProviderCatalog.GetAll().ToList());
            providerView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(AiProviderPreset.Group)));
            ProviderComboBox.ItemsSource = providerView;
            ProfileListBox.ItemsSource = _profiles;
            RefreshFromConfig();
        }

        public void RefreshFromConfig()
        {
            _isRefreshing = true;
            _isDraft = false;
            AiConnectionProfile activeProfile = _configManager.GetActiveAiProfile();
            AiProviderPreset preset = AiProviderCatalog.Find(activeProfile.ProviderId)
                ?? AiProviderCatalog.Find("custom");
            RefreshProfileList();
            ProfileListBox.SelectedItem = _profiles.FirstOrDefault(profile => profile.Id == activeProfile.Id);
            ProviderComboBox.SelectedItem = preset;
            SelectProtocol(activeProfile.Protocol);
            BaseUrlTextBox.Text = activeProfile.BaseUrl;
            ApiKeyBox.Password = activeProfile.ApiKey;
            PopulateModels(preset, activeProfile.AvailableModels, activeProfile.ModelId);
            ProfileNameTextBox.Text = string.IsNullOrWhiteSpace(activeProfile.Name) ? BuildProfileName(preset) : activeProfile.Name;
            DetailStatusText.Text = "● 已启用";
            UpdateRequestPreview();
            _isRefreshing = false;
        }

        public void FocusDeepSeekConfig()
        {
            ApiKeyBox.Focus();
        }

        private void RefreshProfileList()
        {
            _profiles.Clear();
            IReadOnlyList<AiConnectionProfile> savedProfiles = _configManager.GetAiProfiles();
            foreach (AiConnectionProfile profile in savedProfiles)
            {
                _profiles.Add(new AiProfileSummary
                {
                    Id = profile.Id,
                    DisplayName = string.IsNullOrWhiteSpace(profile.Name) ? BuildProfileName(AiProviderCatalog.Find(profile.ProviderId)) : profile.Name,
                    Summary = GetProtocolShortName(profile.Protocol) + Environment.NewLine + profile.ModelId,
                    CanDelete = savedProfiles.Count > 1
                });
            }
            if (_isDraft)
            {
                _profiles.Add(new AiProfileSummary
                {
                    Id = "draft",
                    DisplayName = "新建配置",
                    Summary = "未保存草稿" + Environment.NewLine + "等待填写连接信息",
                    CanDelete = true
                });
            }
        }

        private void AddProfileButton_Click(object sender, RoutedEventArgs e)
        {
            _isDraft = true;
            _isRefreshing = true;
            AiProviderPreset custom = AiProviderCatalog.Find("custom");
            RefreshProfileList();
            ProfileListBox.SelectedItem = _profiles.FirstOrDefault(profile => profile.Id == "draft");
            SelectProvider(custom);
            BaseUrlTextBox.Text = string.Empty;
            ApiKeyBox.Password = string.Empty;
            PopulateModels(custom, Array.Empty<AiModelOption>(), string.Empty);
            ProfileNameTextBox.Text = "新建配置";
            DetailStatusText.Text = "草稿";
            DeepSeekConfigStatusText.Text = "尚未保存";
            UpdateRequestPreview();
            _isRefreshing = false;
        }

        private void ProfileListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshing || ProfileListBox.SelectedItem is not AiProfileSummary profile)
            {
                return;
            }

            if (profile.Id == "draft")
            {
                return;
            }

            AiConnectionProfile selected = _configManager.GetAiProfiles().FirstOrDefault(item => item.Id == profile.Id);
            if (selected == null) return;
            LoadProfileToEditor(selected);
        }

        private void LoadProfileToEditor(AiConnectionProfile profile)
        {
            _isRefreshing = true;
            _isDraft = false;
            AiProviderPreset preset = AiProviderCatalog.Find(profile.ProviderId) ?? AiProviderCatalog.Find("custom");
            ProviderComboBox.SelectedItem = preset;
            SelectProtocol(profile.Protocol);
            BaseUrlTextBox.Text = profile.BaseUrl;
            ApiKeyBox.Password = profile.ApiKey;
            PopulateModels(preset, profile.AvailableModels, profile.ModelId);
            ProfileNameTextBox.Text = string.IsNullOrWhiteSpace(profile.Name) ? BuildProfileName(preset) : profile.Name;
            DetailStatusText.Text = string.Equals(profile.Id, _configManager.ActiveAiProfileId, StringComparison.Ordinal) ? "● 已启用" : "○ 可用";
            UpdateRequestPreview();
            _isRefreshing = false;
        }

        private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
        {
            string profileId = (sender as FrameworkElement)?.Tag as string;
            AiProfileSummary selected = _profiles.FirstOrDefault(profile => profile.Id == profileId);
            if (selected == null) return;
            if (selected.Id == "draft")
            {
                RefreshFromConfig();
                return;
            }

            if (_configManager.GetAiProfiles().Count <= 1)
            {
                System.Windows.MessageBox.Show("至少保留一个 AI 配置。", "AI 配置", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBoxResult result = System.Windows.MessageBox.Show(
                $"确定删除“{selected.DisplayName}”？\n删除后将移除该配置的地址、密钥和模型设置。",
                "删除配置",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            _configManager.DeleteAiProfile(selected.Id);
            RefreshFromConfig();
        }

        private void ProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshing || ProviderComboBox.SelectedItem is not AiProviderPreset preset)
            {
                return;
            }

            _isRefreshing = true;
            BaseUrlTextBox.Text = preset.BaseUrl;
            SelectProtocol(preset.Protocol);
            PopulateModels(preset, Array.Empty<AiModelOption>(), preset.RecommendedModels.FirstOrDefault());
            UpdateRequestPreview();
            _isRefreshing = false;
        }

        private void ProtocolComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshing)
            {
                return;
            }

            string protocol = GetSelectedProtocol();
            UpdateRequestPreview();
        }

        private void BaseUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isRefreshing)
            {
                UpdateRequestPreview();
            }
        }

        private void ProfileNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isRefreshing || ProfileListBox.SelectedItem is not AiProfileSummary selected) return;
            string name = ProfileNameTextBox.Text?.Trim();
            selected.DisplayName = string.IsNullOrWhiteSpace(name) ? "未命名配置" : name;
        }

        private async void GetModelsButton_Click(object sender, RoutedEventArgs e)
        {
            GetModelsButton.IsEnabled = false;
            DeepSeekConfigStatusText.Text = "正在从接口获取模型…";
            try
            {
                var models = await _modelCatalogClient.GetModelOptionsAsync(BaseUrlTextBox.Text, ApiKeyBox.Password, CancellationToken.None);
                string current = GetSelectedModel();
                AiModelComboBox.Items.Clear();
                foreach (AiModelOption model in models)
                {
                    AiModelComboBox.Items.Add(model);
                }
                SelectModel(current);
                DeepSeekConfigStatusText.Text = $"模型获取成功：{models.Count} 个";
            }
            catch (Exception ex)
            {
                DeepSeekConfigStatusText.Text = $"模型获取失败：{ex.Message}";
            }
            finally
            {
                GetModelsButton.IsEnabled = true;
            }
        }

        private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            GetModelsButton.IsEnabled = false;
            DeepSeekConfigStatusText.Text = "正在测试连接…";
            try
            {
                var models = await _modelCatalogClient.GetModelOptionsAsync(BaseUrlTextBox.Text, ApiKeyBox.Password, CancellationToken.None);
                DeepSeekConfigStatusText.Text = $"连接成功：模型接口可用 · {models.Count} 个模型";
            }
            catch (Exception ex)
            {
                DeepSeekConfigStatusText.Text = $"连接失败：{ex.Message}";
            }
            finally
            {
                GetModelsButton.IsEnabled = true;
            }
        }

        private void SaveConfigButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProviderComboBox.SelectedItem is not AiProviderPreset preset)
            {
                return;
            }

            AiProfileSummary selected = ProfileListBox.SelectedItem as AiProfileSummary;
            AiConnectionProfile profile = selected != null && selected.Id != "draft"
                ? _configManager.GetAiProfiles().FirstOrDefault(item => item.Id == selected.Id)
                : _configManager.CreateAiProfile(BuildProfileName(preset));
            if (profile == null) return;

            profile.Name = string.IsNullOrWhiteSpace(ProfileNameTextBox.Text) ? BuildProfileName(preset) : ProfileNameTextBox.Text.Trim();
            profile.ProviderId = preset.Id;
            profile.Protocol = GetSelectedProtocol();
            profile.BaseUrl = BaseUrlTextBox.Text?.Trim() ?? string.Empty;
            profile.ModelId = GetSelectedModel();
            profile.ModelDisplayName = GetSelectedModelDisplayName();
            profile.AvailableModels = GetCurrentModels();
            profile.ApiKey = ApiKeyBox.Password ?? string.Empty;
            profile.LastTestStatus = "未测试";
            _configManager.SaveAiProfile(profile);
            _configManager.SetActiveAiProfile(profile.Id);
            _isDraft = false;
            RefreshFromConfig();
            DeepSeekConfigStatusText.Text = $"已保存并启用：{preset.DisplayName} · {GetSelectedProtocolDisplay()} · {GetSelectedModel()}";
        }

        private void OpenAiCaptionButton_Click(object sender, RoutedEventArgs e)
        {
            AiCaptionRequested?.Invoke();
        }

        private void AsrEngineSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            AsrEngineSettingsRequested?.Invoke();
        }

        private void SelectProvider(AiProviderPreset preset)
        {
            ProviderComboBox.SelectedItem = preset;
            SelectProtocol(preset?.Protocol);
        }

        private void PopulateModels(
            AiProviderPreset preset,
            IEnumerable<AiModelOption> availableModels,
            string preferredModel)
        {
            AiModelComboBox.Items.Clear();
            var knownModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AiModelOption model in availableModels ?? Array.Empty<AiModelOption>())
            {
                if (model == null || string.IsNullOrWhiteSpace(model.Id) || !knownModelIds.Add(model.Id))
                {
                    continue;
                }

                AiModelComboBox.Items.Add(new AiModelOption
                {
                    Id = model.Id,
                    DisplayName = string.IsNullOrWhiteSpace(model.DisplayName) ? model.Id : model.DisplayName,
                    ContextWindow = model.ContextWindow
                });
            }
            foreach (string model in preset?.RecommendedModels ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(model) && knownModelIds.Add(model))
                {
                    AiModelComboBox.Items.Add(new AiModelOption { Id = model, DisplayName = model });
                }
            }
            SelectModel(preferredModel);
        }

        private List<AiModelOption> GetCurrentModels()
        {
            return AiModelComboBox.Items
                .OfType<AiModelOption>()
                .Where(model => !string.IsNullOrWhiteSpace(model.Id))
                .GroupBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Select(model => new AiModelOption
                {
                    Id = model.Id,
                    DisplayName = string.IsNullOrWhiteSpace(model.DisplayName) ? model.Id : model.DisplayName,
                    ContextWindow = model.ContextWindow
                })
                .ToList();
        }

        private void SelectModel(string model)
        {
            string target = (model ?? string.Empty).Trim();
            var item = AiModelComboBox.Items.OfType<AiModelOption>().FirstOrDefault(candidate => string.Equals(candidate.Id, target, StringComparison.OrdinalIgnoreCase));
            if (item == null && !string.IsNullOrWhiteSpace(target))
            {
                item = new AiModelOption { Id = target, DisplayName = target };
                AiModelComboBox.Items.Add(item);
            }
            AiModelComboBox.SelectedItem = item ?? AiModelComboBox.Items.OfType<AiModelOption>().FirstOrDefault();
        }

        private string GetSelectedModel()
        {
            return (AiModelComboBox.SelectedItem as AiModelOption)?.Id ?? string.Empty;
        }

        private string GetSelectedModelDisplayName()
        {
            return (AiModelComboBox.SelectedItem as AiModelOption)?.DisplayName ?? GetSelectedModel();
        }

        private string GetSelectedProtocol()
        {
            return (ProtocolComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? AiProviderProtocol.OpenAiCompletions;
        }

        private string GetSelectedProtocolDisplay()
        {
            return (ProtocolComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "OpenAI Chat Completions";
        }

        private void SelectProtocol(string protocol)
        {
            ProtocolComboBox.SelectedItem = ProtocolComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag?.ToString(), protocol, StringComparison.OrdinalIgnoreCase))
                ?? ProtocolComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        }

        private void UpdateRequestPreview()
        {
            if (RequestPreviewText == null)
            {
                return;
            }

            string baseUrl = (BaseUrlTextBox?.Text ?? string.Empty).Trim().TrimEnd('/');
            string path = string.Equals(GetSelectedProtocol(), AiProviderProtocol.OpenAiResponses, StringComparison.OrdinalIgnoreCase) ? "/responses" : "/chat/completions";
            RequestPreviewText.Text = string.IsNullOrWhiteSpace(baseUrl) ? $"POST {path}" : $"POST {baseUrl}{path}";
        }

        private static string BuildProfileName(AiProviderPreset preset)
        {
            return string.IsNullOrWhiteSpace(preset?.DisplayName) ? "AI 主配置" : $"{preset.DisplayName} 主配置";
        }

        private static string GetProtocolShortName(string protocol)
        {
            return string.Equals(protocol, AiProviderProtocol.OpenAiResponses, StringComparison.OrdinalIgnoreCase) ? "Responses" : "Chat Completions";
        }

        public sealed class AiProfileSummary : INotifyPropertyChanged
        {
            public string Id { get; set; } = string.Empty;
            private string _displayName = string.Empty;
            public string DisplayName
            {
                get => _displayName;
                set
                {
                    if (string.Equals(_displayName, value, StringComparison.Ordinal)) return;
                    _displayName = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
                }
            }
            public string Summary { get; set; } = string.Empty;
            public bool CanDelete { get; set; }
            public event PropertyChangedEventHandler PropertyChanged;
        }
    }
}
