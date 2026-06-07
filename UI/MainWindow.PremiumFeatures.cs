using System;
using System.Windows;
using System.Windows.Controls;
using ImageColorChanger.Services.Licensing;

namespace ImageColorChanger.UI
{
    public partial class MainWindow : Window
    {
        private bool? _lastSlidesFeatureAllowedForProjectTree;

        private bool TryRequirePremiumFeature(PremiumFeature feature)
        {
            try
            {
                var gate = _mainWindowServices?.GetRequired<IFeatureGate>();
                if (gate == null)
                {
                    ShowStatus("授权服务未初始化");
                    return false;
                }

                FeatureGateResult result = gate.Require(feature);
                if (result.IsAllowed)
                {
                    return true;
                }

                ShowStatus(result.UserMessage);
                return false;
            }
            catch (Exception ex)
            {
                ShowStatus($"授权检查失败: {ex.Message}");
                return false;
            }
        }

        private bool TryRequireSlidesFeature()
        {
            return TryRequirePremiumFeature(PremiumFeature.Slides);
        }

        private bool IsPremiumFeatureAllowed(PremiumFeature feature)
        {
            try
            {
                var gate = _mainWindowServices?.GetRequired<IFeatureGate>();
                return gate?.Check(feature).IsAllowed == true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetPremiumFeatureUiState(PremiumFeature feature, out string deniedToolTip)
        {
            deniedToolTip = string.Empty;

            try
            {
                var gate = _mainWindowServices?.GetRequired<IFeatureGate>();
                FeatureGateResult result = gate?.Check(feature);
                if (result?.IsAllowed == true)
                {
                    return true;
                }

                deniedToolTip = string.IsNullOrWhiteSpace(result?.UserMessage)
                    ? "功能未开通"
                    : result.UserMessage;
                return false;
            }
            catch
            {
                deniedToolTip = "校验中...";
                return false;
            }
        }

        private void ApplyPremiumMenuItemState(MenuItem item, PremiumFeature feature)
        {
            if (item == null)
            {
                return;
            }

            bool allowed = TryGetPremiumFeatureUiState(feature, out string deniedToolTip);
            item.IsEnabled = allowed;
            item.ToolTip = allowed ? null : deniedToolTip;
            ToolTipService.SetShowOnDisabled(item, !allowed);
        }

        private void ApplyPremiumButtonState(System.Windows.Controls.Button button, PremiumFeature feature, string allowedToolTip = null)
        {
            if (button == null)
            {
                return;
            }

            bool allowed = TryGetPremiumFeatureUiState(feature, out string deniedToolTip);
            button.IsEnabled = allowed;
            button.Opacity = allowed ? 1.0 : 0.52;
            button.ToolTip = allowed ? allowedToolTip : deniedToolTip;
            ToolTipService.SetShowOnDisabled(button, !allowed);
        }

        public static (bool IsEnabled, string ToolTip) BuildAiPlatformMenuStateForTest(
            bool aiPanelAllowed,
            string aiPanelDeniedToolTip,
            bool aiCaptionAllowed,
            string aiCaptionDeniedToolTip)
        {
            if (aiPanelAllowed || aiCaptionAllowed)
            {
                return (true, null);
            }

            _ = aiPanelDeniedToolTip;
            _ = aiCaptionDeniedToolTip;
            return (false, "功能未开通");
        }

        public static bool ShouldReloadSlidesProjectTreeForTest(bool? previousAllowed, bool currentAllowed)
        {
            return previousAllowed.HasValue && previousAllowed.Value != currentAllowed;
        }

        private void ApplyAiPlatformMenuState(MenuItem aiPlatformItem)
        {
            if (aiPlatformItem == null)
            {
                return;
            }

            bool aiPanelAllowed = TryGetPremiumFeatureUiState(PremiumFeature.AiPanel, out string aiPanelDeniedToolTip);
            bool aiCaptionAllowed = TryGetPremiumFeatureUiState(PremiumFeature.LiveCaption, out string aiCaptionDeniedToolTip);
            var state = BuildAiPlatformMenuStateForTest(
                aiPanelAllowed,
                aiPanelDeniedToolTip,
                aiCaptionAllowed,
                aiCaptionDeniedToolTip);

            aiPlatformItem.IsEnabled = state.IsEnabled;
            aiPlatformItem.ToolTip = state.ToolTip;
            ToolTipService.SetShowOnDisabled(aiPlatformItem, !state.IsEnabled);
        }

        private void RefreshPremiumFeatureUi()
        {
            bool slidesAllowed = IsPremiumFeatureAllowed(PremiumFeature.Slides);
            bool shouldReloadSlidesProjectTree = ShouldReloadSlidesProjectTreeForTest(
                _lastSlidesFeatureAllowedForProjectTree,
                slidesAllowed);
            _lastSlidesFeatureAllowedForProjectTree = slidesAllowed;

            SyncAiCaptionUiState();
            ApplyPremiumButtonState(BtnShowProjects, PremiumFeature.Slides, "幻灯片");
            ApplyTextEditorPremiumUiState();

            if (shouldReloadSlidesProjectTree)
            {
                ReloadProjectsAfterSlidesAuthorizationChanged();
            }
        }

        private void ReloadProjectsAfterSlidesAuthorizationChanged()
        {
            if (_projectTreeItems == null || ProjectTree == null)
            {
                return;
            }

            TreeItemType? preferredType = _currentTextProject?.Id > 0
                ? TreeItemType.TextProject
                : null;
            int preferredId = _currentTextProject?.Id ?? 0;
            ReloadProjectsPreservingTreeState(preferredType, preferredId);
        }

        private void ApplyTextEditorPremiumUiState()
        {
            var buttons = new[]
            {
                BtnAddText,
                BtnBackgroundImage,
                BtnBackgroundColor,
                BtnSplitView,
                BtnSplitStretchMode,
                BtnSlideOutputMode,
                BtnComponent,
                BtnSaveTextProject,
                BtnUpdateProjection,
                BtnCanvasAspectRatioInPanel,
                BtnSecondLayerAddText,
                BtnSecondLayerBold,
                BtnSecondLayerTextHighlightColor,
                BtnSecondLayerNoticeSettings,
                BtnSecondLayerNoticeProjectionToggle,
                BtnSecondLayerNoticeToggle,
                BtnSecondLayerNoticeDelete,
                BtnSecondLayerNoticeStickyToggle
            };

            foreach (var button in buttons)
            {
                ApplyPremiumButtonState(button, PremiumFeature.Slides);
            }
        }
    }
}
