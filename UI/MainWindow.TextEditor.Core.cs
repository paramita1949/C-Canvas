using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.EntityFrameworkCore;
using ImageColorChanger.Core;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Repositories.TextEditor;
using ImageColorChanger.Services.TextEditor;
using ImageColorChanger.Services.TextEditor.Application;
using ImageColorChanger.Services.TextEditor.Rendering;
using ImageColorChanger.Services.TextEditor.Components.Notice;
using ImageColorChanger.UI.Controls;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfColor = System.Windows.Media.Color;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow TextEditor Core (Fields, Initialization, Project Management)
    /// </summary>
    public partial class MainWindow
    {
        #region 字段

        private ITextProjectService _textProjectService;
        private Database.CanvasDbContext _dbContext; // 数据库上下文
        private TextProject _currentTextProject;
        private List<DraggableTextBox> _textBoxes = new List<DraggableTextBox>();
        private DraggableTextBox _selectedTextBox;
        private TextElement _textBoxClipboardElement;
        private List<RichTextSpan> _textBoxClipboardSpans = new List<RichTextSpan>();
        private int _textBoxPasteOffsetStep = 1;
        private string _currentTextColor = "#000000";
        private ITextBoxEditSessionService _textBoxEditSessionService;
        private ITextElementPersistenceService _textElementPersistenceService;
        private ITextEditorSaveOrchestrator _textEditorSaveOrchestrator;
        private ITextElementRepository _textElementRepository;
        private IRichTextSpanRepository _richTextSpanRepository;
        private ITextEditorProjectionComposer _textEditorProjectionComposer;
        private ITextEditorThumbnailService _textEditorThumbnailService;
        private ITextEditorProjectionRenderStateService _textEditorProjectionRenderStateService;
        private ITextEditorRenderSafetyService _textEditorRenderSafetyService;
        private TextEditorNoticeOverlayRenderService _textEditorNoticeOverlayRenderService;
        private readonly NoticeRuntimeService _noticeRuntimeService = new NoticeRuntimeService();
        private static readonly TimeSpan NoticeAnimationFrameInterval = TimeSpan.FromMilliseconds(16);
        private const int NoticeProjectionFrameIntervalMs = 16;
        private bool _noticeRenderingSubscribed;
        private long _lastNoticeProjectionUpdateMs;
        private bool _noticeProjectionRefreshPending;
        private bool _hideNoticeOnProjection;
        private readonly Dictionary<int, NoticeVisualBoundsCache> _noticeVisualBoundsCache = new();
        private CancellationTokenSource _noticeConfigPersistCts;
        private readonly SemaphoreSlim _noticeConfigPersistGate = new SemaphoreSlim(1, 1);
        private SlideThemeMode _slideThemeMode = SlideThemeMode.Dark;

        // 辅助线相关
        private const double SNAP_THRESHOLD = 10.0; // 吸附阈值（像素）
        
        // 分割模式角标相关（统一参数，主屏幕和投影屏幕共用）
        private const double REGION_LABEL_FONT_SIZE = 24;      // 角标字体大小
        private const double REGION_LABEL_PADDING_X = 12;      // 角标左右内边距
        private const double REGION_LABEL_PADDING_Y = 6;       // 角标上下内边距
        private const double REGION_LABEL_CORNER_RADIUS = 12;  // 角标圆角半径
        
        // 分割线相关（统一参数，主屏幕和投影屏幕共用）
        private const double SPLIT_LINE_THICKNESS_MAIN = 3;    // 主屏幕分割线宽度
        private const double SPLIT_LINE_THICKNESS_PROJECTION = 1; // 投影屏幕分割线宽度（细线）
        private const double SPLIT_LINE_DASH_LENGTH = 5;       // 虚线段长度
        private const double SPLIT_LINE_DASH_GAP = 3;          // 虚线间隔
        // 分割线颜色（红色 RGB(255, 0, 0)）
        private const byte SPLIT_LINE_COLOR_R = 255;
        private const byte SPLIT_LINE_COLOR_G = 0;
        private const byte SPLIT_LINE_COLOR_B = 0;
        
        // 分割区域相关
        private int _selectedRegionIndex = 0; // 当前选中的区域索引（0-3）
        private List<WpfRectangle> _splitRegionBorders = new List<WpfRectangle>(); // 区域边框
        private Dictionary<int, System.Windows.Controls.Image> _regionImages = new Dictionary<int, System.Windows.Controls.Image>(); // 区域图片控件
        private Dictionary<int, string> _regionImagePaths = new Dictionary<int, string>(); // 区域图片路径
        private Dictionary<int, bool> _regionImageColorEffects = new Dictionary<int, bool>(); // 区域图片是否需要变色效果
        private SplitImageDisplayMode _splitImageDisplayMode = SplitImageDisplayMode.FitCenter; // 分割图片显示模式
        
        // 渲染节流（避免过于频繁的更新）
        private const int CanvasUpdateThrottleMs = 100; // 100ms内只更新一次

        // 画布缩放比例（用于投影渲染）
        private double _currentCanvasScaleX = 1.0;
        private double _currentCanvasScaleY = 1.0;

        // PAK字体列表输出标记（仅输出一次）

        //  投影屏幕动画设置（全局设置，不是针对单个文本框）
        private bool _projectionAnimationEnabled = true;  //  默认启用
        private double _projectionAnimationOpacity = 0.1; //  默认透明度 0.1
        private int _projectionAnimationDuration = 800;   //  默认动画时长 800ms
        private bool _biblePopupAnimationEnabled = true;
        private double _biblePopupAnimationOpacity = 0.1;
        private int _biblePopupAnimationDuration = 800;
        private string _biblePopupAnimationType = "TopReveal";
        private const string SlideThemeSettingKey = "TextEditorSlideTheme";
        private const string SlideThemeDarkValue = "Dark";
        private const string SlideThemeLightValue = "Light";
        private const string NoticeDefaultConfigSettingKey = "TextEditorNoticeDefaultConfig";
        private NoticeComponentConfig _noticeDefaultConfig = new NoticeComponentConfig();

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化文本编辑器
        /// </summary>
        private void InitializeTextEditor()
        {
            if (_dbContext == null)
            {
                throw new InvalidOperationException("Database context is not initialized.");
            }
            _textProjectService = _mainWindowServices.GetRequired<ITextProjectService>();
            _textBoxEditSessionService = _mainWindowServices.GetRequired<ITextBoxEditSessionService>();
            _textElementPersistenceService = _mainWindowServices.GetRequired<ITextElementPersistenceService>();
            _textElementRepository = _mainWindowServices.GetRequired<ITextElementRepository>();
            _richTextSpanRepository = _mainWindowServices.GetRequired<IRichTextSpanRepository>();
            _textEditorProjectionComposer = _mainWindowServices.GetRequired<ITextEditorProjectionComposer>();
            _textEditorThumbnailService = _mainWindowServices.GetRequired<ITextEditorThumbnailService>();
            _textEditorProjectionRenderStateService = _mainWindowServices.GetRequired<ITextEditorProjectionRenderStateService>();
            _textEditorRenderSafetyService = _mainWindowServices.GetRequired<ITextEditorRenderSafetyService>();
            _textEditorNoticeOverlayRenderService = new TextEditorNoticeOverlayRenderService(_textEditorRenderSafetyService);
            _textEditorSaveOrchestrator = _mainWindowServices.GetRequired<ITextEditorSaveOrchestrator>();
            LoadSlideThemePreference();
            LoadNoticeDefaultConfigPreference();

            // 加载系统字体
            LoadSystemFonts();

            // 初始化画布比例
            InitializeCanvasAspectRatio();

            // 右键空白画布菜单（复制/粘贴）
            if (EditorCanvas != null)
            {
                EditorCanvas.MouseRightButtonDown -= EditorCanvas_MouseRightButtonDown;
                EditorCanvas.MouseRightButtonDown += EditorCanvas_MouseRightButtonDown;
                EditorCanvas.MouseRightButtonUp -= EditorCanvas_MouseRightButtonUp;
                EditorCanvas.MouseRightButtonUp += EditorCanvas_MouseRightButtonUp;
            }
        }

        /// <summary>
        /// 加载自定义字体库
        /// </summary>
        private void LoadSystemFonts()
        {
            try
            {
                // 使用FontService统一加载字体
                if (!Core.FontService.Instance.Initialize())
                {
                    //System.Diagnostics.Debug.WriteLine($" FontService初始化失败，加载系统默认字体");
                    LoadSystemDefaultFonts();
                    return;
                }

                // 使用FontService填充字体选择器
                int totalFonts = Core.FontService.Instance.PopulateComboBox(
                    FontFamilySelector,
                    showCategoryHeaders: true,
                    showFavoriteIcon: true,
                    applyFontToItem: true
                );

                if (totalFonts == 0)
                {
                    //System.Diagnostics.Debug.WriteLine($" 未加载到任何字体，加载系统默认字体");
                    LoadSystemDefaultFonts();
                }
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($" 加载自定义字体库失败: {ex.Message}");
                LoadSystemDefaultFonts();
            }
        }

        /// <summary>
        /// 加载系统默认字体（后备方案）
        /// </summary>
        private void LoadSystemDefaultFonts()
        {
            try
            {
                FontFamilySelector.Items.Clear();

                var defaultFonts = new[]
                {
                    "Microsoft YaHei UI",
                    "Microsoft YaHei",
                    "SimSun",
                    "SimHei",
                    "KaiTi",
                    "Arial",
                    "Times New Roman",
                    "Calibri"
                };

                foreach (var fontName in defaultFonts)
                {
                    try
                    {
                        var fontFamily = new System.Windows.Media.FontFamily(fontName);
                        var item = new ComboBoxItem
                        {
                            Content = fontName,
                            FontFamily = fontFamily,
                            Tag = fontName
                        };
                        FontFamilySelector.Items.Add(item);
                    }
                    catch
                    {
                        // 忽略不存在的字体
                    }
                }

                if (FontFamilySelector.Items.Count > 0)
                {
                    FontFamilySelector.SelectedIndex = 0;
                }

                //System.Diagnostics.Debug.WriteLine($" 加载系统默认字体完成: {FontFamilySelector.Items.Count} 种");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($" 加载系统默认字体失败: {ex.Message}");
            }
        }

        private enum SlideThemeMode
        {
            Dark,
            Light
        }

        private void LoadSlideThemePreference()
        {
            try
            {
                var savedValue = DatabaseManagerService.GetUISetting(SlideThemeSettingKey, SlideThemeDarkValue);
                _slideThemeMode = string.Equals(savedValue, SlideThemeLightValue, StringComparison.OrdinalIgnoreCase)
                    ? SlideThemeMode.Light
                    : SlideThemeMode.Dark;
            }
            catch
            {
                _slideThemeMode = SlideThemeMode.Dark;
            }

            _currentTextColor = GetCurrentSlideThemeTextColorHex();
        }

        private void SaveSlideThemePreference()
        {
            try
            {
                var value = _slideThemeMode == SlideThemeMode.Light ? SlideThemeLightValue : SlideThemeDarkValue;
                DatabaseManagerService.SaveUISetting(SlideThemeSettingKey, value);
            }
            catch
            {
                // 忽略持久化失败，保持当前会话主题可用
            }
        }

        private void LoadNoticeDefaultConfigPreference()
        {
            try
            {
                string savedJson = DatabaseManagerService.GetUISetting(NoticeDefaultConfigSettingKey, string.Empty);
                var parsed = NoticeComponentConfigCodec.Deserialize(savedJson);
                var normalized = NoticeComponentConfigCodec.Normalize(parsed);
                normalized.ScrollingEnabled = false;
                _noticeDefaultConfig = normalized;
            }
            catch
            {
                _noticeDefaultConfig = new NoticeComponentConfig();
                _noticeDefaultConfig.ScrollingEnabled = false;
            }
        }

        private void SaveNoticeDefaultConfigPreference(NoticeComponentConfig config)
        {
            try
            {
                var normalized = NoticeComponentConfigCodec.Normalize(config);
                normalized.ScrollingEnabled = false;
                _noticeDefaultConfig = normalized;
                DatabaseManagerService.SaveUISetting(NoticeDefaultConfigSettingKey, NoticeComponentConfigCodec.Serialize(normalized));
            }
            catch
            {
                // 忽略持久化失败，当前会话继续使用内存默认值。
            }
        }

        private NoticeComponentConfig GetNoticeDefaultConfig()
        {
            var normalized = NoticeComponentConfigCodec.Normalize(_noticeDefaultConfig);
            normalized.ScrollingEnabled = false;
            return normalized;
        }

        private string GetCurrentSlideThemeBackgroundColorHex()
        {
            return _slideThemeMode == SlideThemeMode.Light ? "#FFFFFF" : "#000000";
        }

        private string GetCurrentSlideThemeTextColorHex()
        {
            return _slideThemeMode == SlideThemeMode.Light ? "#000000" : "#FFFFFF";
        }

        private string GetDefaultTextColorForSlide(Slide slide)
        {
            if (!string.IsNullOrWhiteSpace(slide?.BackgroundColor))
            {
                try
                {
                    var background = (WpfColor)WpfColorConverter.ConvertFromString(slide.BackgroundColor);
                    double luminance = (0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B);
                    return luminance >= 160 ? "#000000" : "#FFFFFF";
                }
                catch
                {
                    // 忽略异常，回退到主题默认文本色
                }
            }

            return GetCurrentSlideThemeTextColorHex();
        }

        #endregion

        #region 项目管理

        /// <summary>
        /// 生成默认项目名称
        /// </summary>
        private async Task<string> GenerateDefaultProjectNameAsync()
        {
            try
            {
                // 获取所有现有项目
                var existingProjects = await _textProjectService.GetAllProjectsAsync();
                
                // 找出所有以"项目"开头的名称
                var projectNumbers = existingProjects
                    .Where(p => p.Name.StartsWith("项目"))
                    .Select(p =>
                    {
                        string numStr = p.Name.Substring(2);
                        return int.TryParse(numStr, out int num) ? num : 0;
                    })
                    .Where(n => n > 0)
                    .ToList();

                // 生成新的编号
                int newNumber = projectNumbers.Any() ? projectNumbers.Max() + 1 : 1;
                return $"项目{newNumber}";
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($" 生成默认项目名称失败: {ex.Message}");
                // 失败时使用时间戳
                return $"项目{DateTime.Now:yyyyMMddHHmmss}";
            }
        }

        /// <summary>
        /// 创建新文本项目（由导入按钮调用）
        /// </summary>
        public async Task CreateTextProjectAsync(string projectName)
        {
            try
            {
                // 重置状态：关闭原图模式
                ResetViewStateForTextEditor();
                
                // 创建项目
                _currentTextProject = await _textProjectService.CreateProjectAsync(projectName);

                // 切换到编辑模式
                ShowTextEditor();

                // 创建第一张幻灯片
                var firstSlide = new Slide
                {
                    ProjectId = _currentTextProject.Id,
                    Title = "幻灯片 1",
                    SortOrder = 1,
                    BackgroundColor = GetCurrentSlideThemeBackgroundColorHex(),
                    SplitMode = -1,  // 默认无分割模式
                    SplitStretchMode = _splitImageDisplayMode  // 使用当前分割显示偏好
                };
                await _textProjectService.AddSlideAsync(firstSlide);

                // 加载幻灯片列表
                await LoadSlideList();

                // 添加到导航树
                AddTextProjectToNavigationTree(_currentTextProject);

                // 新建项目后，保存按钮恢复为白色
                BtnSaveTextProject.Background = new SolidColorBrush(Colors.White);

                //System.Diagnostics.Debug.WriteLine($" 创建文本项目成功: {projectName}");
                
                // 强制更新投影（如果投影已开启且未锁定）
                if (_projectionManager.IsProjectionActive && _currentSlide != null && !_isProjectionLocked)
                {
                    //System.Diagnostics.Debug.WriteLine(" 新建项目完成，准备更新投影...");
                    // 延迟确保UI完全渲染（异步执行，不等待）
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdateProjectionFromCanvas();
                        //System.Diagnostics.Debug.WriteLine(" 新建项目后已自动更新投影");
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
            catch (Exception ex)
            {
                string errorMsg = $"创建项目失败: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorMsg += $"\n\n详细信息: {ex.InnerException.Message}";
                }
                
                WpfMessageBox.Show(errorMsg, "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 加载现有文本项目
        /// </summary>
        public async Task LoadTextProjectAsync(int projectId)
        {
            try
            {
                // 重置状态：关闭原图模式
                ResetViewStateForTextEditor();
                
                // 加载项目
                _currentTextProject = await _textProjectService.LoadProjectAsync(projectId);

                // 切换到编辑模式
                ShowTextEditor();

                // 加载幻灯片列表
                await LoadSlideList();

                // 如果没有幻灯片，自动创建第一张
                if (!await _textProjectService.ProjectHasSlidesAsync(_currentTextProject.Id))
                {
                    //System.Diagnostics.Debug.WriteLine(" 项目没有幻灯片，自动创建第一张");
                    var firstSlide = new Slide
                    {
                        ProjectId = _currentTextProject.Id,
                        Title = "幻灯片 1",
                        SortOrder = 1,
                        BackgroundColor = GetCurrentSlideThemeBackgroundColorHex(),
                        SplitMode = -1,  // 默认无分割模式
                        SplitStretchMode = _splitImageDisplayMode  // 使用当前分割显示偏好
                    };
                    await _textProjectService.AddSlideAsync(firstSlide);
                    
                    // 迁移旧的文本元素到第一张幻灯片
                    await _textProjectService.RebindProjectElementsToSlideAsync(_currentTextProject.Id, firstSlide.Id);
                    
                    // 重新加载幻灯片列表
                    await LoadSlideList();
                }

                // 加载完成后，保存按钮恢复为白色
                BtnSaveTextProject.Background = new SolidColorBrush(Colors.White);

                // 修复：重置脚本按钮状态（文本项目没有录制数据）
                if (_playbackViewModel != null)
                {
                    // 文本项目不使用关键帧录制数据，强制设置为无数据状态（imageId=0表示无图片）
                    // SetCurrentImageAsync会检查时间数据，imageId=0时会设置HasTimingData=false
                    // 这样脚本按钮会恢复默认颜色
                    await _playbackViewModel.SetCurrentImageAsync(0, PlaybackMode.Keyframe);
                }

                //System.Diagnostics.Debug.WriteLine($" 加载文本项目成功: {_currentTextProject.Name}");
                
                // 强制更新投影（如果投影已开启且未锁定）
                if (_projectionManager.IsProjectionActive && _currentSlide != null && !_isProjectionLocked)
                {
                    //System.Diagnostics.Debug.WriteLine(" 项目加载完成，准备更新投影...");
                    // 延迟确保UI完全渲染（异步执行，不等待）
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdateProjectionFromCanvas();
                        //System.Diagnostics.Debug.WriteLine(" 项目加载后已自动更新投影");
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($" 加载文本项目失败: {ex.Message}");
                WpfMessageBox.Show($"加载项目失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 显示文本编辑器
        /// </summary>
        private void ShowTextEditor()
        {
            //System.Diagnostics.Debug.WriteLine(" [ShowTextEditor] 开始显示文本编辑器");

            ImageScrollViewer.Visibility = Visibility.Collapsed;
            VideoContainer.Visibility = Visibility.Collapsed;
            TextEditorPanel.Visibility = Visibility.Visible;
            UpdateBoldButtonState(false);
            UpdateUnderlineButtonState(false);
            UpdateItalicButtonState(false);

            //System.Diagnostics.Debug.WriteLine($"   TextEditorPanel.Visibility: {TextEditorPanel.Visibility}");
            //System.Diagnostics.Debug.WriteLine($"   TextEditorPanel.IsVisible: {TextEditorPanel.IsVisible}");

            // 重置投影状态：清空之前的图片投影状态
            if (_projectionManager.IsProjectionActive)
            {
                //System.Diagnostics.Debug.WriteLine(" 切换到文本编辑器模式，清空图片状态");

                // 重置投影滚动位置
                _projectionManager.ResetProjectionScroll();

                // 清空图片投影状态（文本编辑器不使用图片）
                _projectionManager.ClearImageState();

                //System.Diagnostics.Debug.WriteLine(" 图片状态已清空");
            }

            //System.Diagnostics.Debug.WriteLine(" [ShowTextEditor] 文本编辑器已显示");
        }

        /// <summary>
        /// 隐藏文本编辑器（根据当前模式恢复相应的显示区域）
        /// </summary>
        private void HideTextEditor()
        {
            //System.Diagnostics.Debug.WriteLine(" [HideTextEditor] 开始隐藏文本编辑器");
            StopNoticeAnimationLoop(resetPreviewOffsets: true);

            // 1. 隐藏幻灯片面板
            TextEditorPanel.Visibility = Visibility.Collapsed;

            //System.Diagnostics.Debug.WriteLine($"   TextEditorPanel.Visibility: {TextEditorPanel.Visibility}");

            // 2. 根据当前模式恢复相应的显示区域
            if (_isBibleMode)
            {
                // 圣经模式：确保圣经区域可见
                BibleDisplayContainer.Visibility = Visibility.Visible;
                BibleVerseScrollViewer.Visibility = Visibility.Visible;
                ApplyBibleTitleDisplayMode(true);
                SyncProjectionBibleTitle();
                EnsureBibleQuickLocateFocus("HideTextEditor");

                // 隐藏其他区域
                ImageScrollViewer.Visibility = Visibility.Collapsed;
                VideoContainer.Visibility = Visibility.Collapsed;

                //System.Diagnostics.Debug.WriteLine(" 退出幻灯片 → 恢复圣经显示");
            }
            else
            {
                // 文件/项目模式：确保图片/视频区域可见
                ImageScrollViewer.Visibility = Visibility.Visible;
                VideoContainer.Visibility = Visibility.Visible;

                // 隐藏圣经区域
                BibleDisplayContainer.Visibility = Visibility.Collapsed;

                // 如果投影已开启，恢复图片投影
                if (_projectionManager != null && _projectionManager.IsProjectionActive)
                {
                    //System.Diagnostics.Debug.WriteLine(" 退出幻灯片 → 恢复图片投影");
                    UpdateProjection();
                }

                //System.Diagnostics.Debug.WriteLine(" 退出幻灯片 → 恢复图片/视频显示");
            }

            //System.Diagnostics.Debug.WriteLine(" [HideTextEditor] 文本编辑器已隐藏");
        }

        /// <summary>
        /// 关闭文本编辑器（清理状态）
        /// </summary>
        private void CloseTextEditor()
        {
            //System.Diagnostics.Debug.WriteLine(" [CloseTextEditor] 开始关闭文本编辑器");

            _currentTextProject = null;
            _currentSlide = null;
            _textBoxes.Clear();
            _noticeRuntimeService.Clear();
            EditorCanvas.Children.Clear();

            // 重置 Canvas 背景为白色
            EditorCanvas.Background = new SolidColorBrush(Colors.White);

            // 清空幻灯片列表
            SlideListBox.ItemsSource = null;
            SlideListBox.SelectedItem = null;

            //System.Diagnostics.Debug.WriteLine(" [CloseTextEditor] 文本编辑器已关闭并重置");
            HideTextEditor();
        }

        /// <summary>
        /// 检查并自动退出文本编辑器（如果当前在编辑模式）
        /// </summary>
        public async Task<bool> AutoExitTextEditorIfNeededAsync()
        {
            if (TextEditorPanel.Visibility == Visibility.Visible && _currentTextProject != null)
            {
                //System.Diagnostics.Debug.WriteLine(" 检测到文本编辑器模式，自动退出...");
                
                // 检查是否有未保存的更改
                if (BtnSaveTextProject.Background is SolidColorBrush brush && brush.Color == Colors.LightGreen)
                {
                    //System.Diagnostics.Debug.WriteLine(" 有未保存的更改，自动保存");
                    var saveResult = await SaveTextEditorStateAsync(
                        Services.TextEditor.Application.Models.SaveTrigger.AutoExit,
                        _textBoxes,
                        persistAdditionalState: true,
                        saveThumbnail: false);
                    if (!saveResult.Succeeded)
                    {
                        WpfMessageBox.Show(
                            $"自动保存失败，已取消退出：{saveResult.Exception?.Message}",
                            "保存失败",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return false;
                    }

                    BtnSaveTextProject.Background = new SolidColorBrush(Colors.White);
                }
                
                // 关闭文本编辑器
                CloseTextEditor();
                //System.Diagnostics.Debug.WriteLine(" 已自动退出文本编辑器");
                return true;
            }
            return false;
        }

        /// <summary>
        /// 清空编辑画布
        /// </summary>
        private void ClearEditorCanvas()
        {
            _textEditorProjectionRenderStateService?.ClearCache();
            _textBoxes.Clear();
            _selectedTextBox = null;
            StopNoticeAnimationLoop(resetPreviewOffsets: false);
            UpdateBoldButtonState(false);
            UpdateUnderlineButtonState(false);
            UpdateItalicButtonState(false);
            
            // 清除所有文本框
            var textBoxesToRemove = EditorCanvas.Children.OfType<DraggableTextBox>().ToList();
            foreach (var textBox in textBoxesToRemove)
            {
                EditorCanvas.Children.Remove(textBox);
            }

            //  清除视频背景 MediaElement
            var mediaElements = EditorCanvas.Children.OfType<MediaElement>().ToList();
            foreach (var media in mediaElements)
            {
                media.Stop();
                EditorCanvas.Children.Remove(media);
#if DEBUG
                //System.Diagnostics.Debug.WriteLine($" [视频背景] 清除 MediaElement");
#endif
            }
        }

        /// <summary>
        /// 添加文本项目到导航树
        /// </summary>
        private void AddTextProjectToNavigationTree(TextProject project)
        {
            try
            {
                // 创建项目树节点
                var projectNode = new ProjectTreeItem
                {
                    Name = project.Name,
                    Type = TreeItemType.TextProject, // 修正：使用 TextProject 类型
                    Id = project.Id,
                    IconKind = "FileDocument",
                    IconColor = "#2196F3", // 蓝色，与 LoadTextProjectsToTree 保持一致
                    Children = new System.Collections.ObjectModel.ObservableCollection<ProjectTreeItem>()
                };

                // 添加到根节点
                _projectTreeItems.Add(projectNode);

                // 修复：切换到项目模式并刷新显示
                _currentViewMode = NavigationViewMode.Projects;
                UpdateViewModeButtons();
                FilterProjectTree();

                // 选中新创建的项目
                projectNode.IsSelected = true;

                //System.Diagnostics.Debug.WriteLine($" 项目已添加到导航树: {project.Name}");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($" 添加项目到导航树失败: {ex.Message}");
            }
        }

        #endregion

    }
}


