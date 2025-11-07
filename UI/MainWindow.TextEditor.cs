using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.EntityFrameworkCore;
using ImageColorChanger.Core;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;
using ImageColorChanger.Managers;
using ImageColorChanger.UI.Controls;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfColor = System.Windows.Media.Color;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using SkiaSharp;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 的文本编辑器功能分部类
    /// </summary>
    public partial class MainWindow
    {
        #region 字段

        private TextProjectManager _textProjectManager;
        private Database.CanvasDbContext _dbContext; // 🆕 数据库上下文
        private TextProject _currentTextProject;
        private List<DraggableTextBox> _textBoxes = new List<DraggableTextBox>();
        private DraggableTextBox _selectedTextBox;
        private string _currentTextColor = "#000000";

        // 辅助线相关
        private const double SNAP_THRESHOLD = 10.0; // 吸附阈值（像素）
        
        // 分割区域相关
        private int _selectedRegionIndex = 0; // 当前选中的区域索引（0-3）
        private List<WpfRectangle> _splitRegionBorders = new List<WpfRectangle>(); // 区域边框
        private Dictionary<int, System.Windows.Controls.Image> _regionImages = new Dictionary<int, System.Windows.Controls.Image>(); // 区域图片控件
        private Dictionary<int, string> _regionImagePaths = new Dictionary<int, string>(); // 区域图片路径
        private bool _splitStretchMode = false; // false = 适中显示(Uniform), true = 拉伸显示(Fill)
        
        // 🚀 Canvas渲染缓存（避免重复渲染）
        private SKBitmap _lastCanvasRenderCache = null;
        private string _lastCanvasCacheKey = "";
        
        // 🚀 渲染节流（避免过于频繁的更新）
        private DateTime _lastCanvasUpdateTime = DateTime.MinValue;
        private const int CanvasUpdateThrottleMs = 100; // 100ms内只更新一次
        
        // 🔍 PAK字体列表输出标记（仅输出一次）

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化文本编辑器
        /// </summary>
        private void InitializeTextEditor()
        {
            _dbContext = _dbManager.GetDbContext(); // 🆕 保存数据库上下文引用
            _textProjectManager = new TextProjectManager(_dbContext);
            
            // 加载系统字体
            LoadSystemFonts();
        }

        /// <summary>
        /// 加载自定义字体库
        /// </summary>
        private void LoadSystemFonts()
        {
            try
            {
                // 🔧 使用ResourceLoader加载字体配置（支持PAK）
                var json = Core.ResourceLoader.LoadTextFile("Fonts/fonts.json");
                
                if (string.IsNullOrEmpty(json))
                {
                    //System.Diagnostics.Debug.WriteLine($"⚠️ 未找到 fonts.json，加载系统默认字体");
                    LoadSystemDefaultFonts();
                    return;
                }

                // 读取配置文件
                var config = JsonSerializer.Deserialize<FontConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (config == null || config.FontCategories == null || config.FontCategories.Count == 0)
                {
                    //System.Diagnostics.Debug.WriteLine($"⚠️ fonts.json 配置为空，加载系统默认字体");
                    LoadSystemDefaultFonts();
                    return;
                }

                // 清空字体选择器
                FontFamilySelector.Items.Clear();

                int totalFonts = 0;

                // 按分类加载字体
                foreach (var category in config.FontCategories)
                {
                    // 添加分类标题（不可选）
                    var categoryHeader = new ComboBoxItem
                    {
                        Content = $"━━ {category.Name} ━━",
                        IsEnabled = false,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x21, 0x96, 0xF3))
                    };
                    FontFamilySelector.Items.Add(categoryHeader);

                    // 添加该分类下的字体
                    foreach (var font in category.Fonts)
                    {
                        try
                        {
                            System.Windows.Media.FontFamily fontFamily;

                            // 判断是系统字体还是自定义字体
                            if (font.File == "system")
                            {
                                // 系统字体
                                fontFamily = new System.Windows.Media.FontFamily(font.Family);
                            }
                            else
                            {
                                // 自定义字体文件
                                var fontRelativePath = $"Fonts/{font.File}";
                                if (!Core.ResourceLoader.ResourceExists(fontRelativePath))
                                {
                                    //System.Diagnostics.Debug.WriteLine($"⚠️ 字体文件不存在: {fontRelativePath}");
                                    continue;
                                }

                                // 🔧 使用ResourceLoader加载字体（支持PAK）
                                try
                                {
                                    fontFamily = Core.ResourceLoader.LoadFont(fontRelativePath, font.Family);
                                    
                                    if (fontFamily == null)
                                    {
                                        //System.Diagnostics.Debug.WriteLine($"❌ 字体加载失败: {font.Name}");
                                        continue;
                                    }
                                }
                                catch (Exception)
                                {
                                    //System.Diagnostics.Debug.WriteLine($"❌ 字体加载失败: {font.Name}");
                                    continue;
                                }
                            }

                            // 创建字体项
                            var displayName = font.IsFavorite ? $"⭐ {font.Name}" : $"   {font.Name}";
                            var item = new ComboBoxItem
                            {
                                Content = displayName,
                                FontFamily = fontFamily,
                                Tag = new FontItemData 
                                { 
                                    Config = font, 
                                    FontFamily = fontFamily 
                                },
                                ToolTip = font.Preview
                            };

                            FontFamilySelector.Items.Add(item);
                            totalFonts++;
                        }
                        catch (Exception)
                        {
                            //System.Diagnostics.Debug.WriteLine($"⚠️ 加载字体失败 [{font.Name}]: {ex.Message}");
                        }
                    }
                }

                // 默认选择第一个可用字体
                for (int i = 0; i < FontFamilySelector.Items.Count; i++)
                {
                    if (FontFamilySelector.Items[i] is ComboBoxItem item && item.IsEnabled)
                    {
                        FontFamilySelector.SelectedIndex = i;
                        break;
                    }
                }
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 加载自定义字体库失败: {ex.Message}");
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

                //System.Diagnostics.Debug.WriteLine($"✅ 加载系统默认字体完成: {FontFamilySelector.Items.Count} 种");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 加载系统默认字体失败: {ex.Message}");
            }
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
                var existingProjects = await _textProjectManager.GetAllProjectsAsync();
                
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
                //System.Diagnostics.Debug.WriteLine($"❌ 生成默认项目名称失败: {ex.Message}");
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
                // 🆕 重置状态：关闭原图模式
                ResetViewStateForTextEditor();
                
                // 创建项目
                _currentTextProject = await _textProjectManager.CreateProjectAsync(projectName);

                // 切换到编辑模式
                ShowTextEditor();

                // 🆕 创建第一张幻灯片
                var firstSlide = new Slide
                {
                    ProjectId = _currentTextProject.Id,
                    Title = "幻灯片 1",
                    SortOrder = 1,
                    BackgroundColor = "#000000",  // 默认黑色背景
                    SplitMode = -1,  // 默认无分割模式
                    SplitStretchMode = false  // 默认适中模式
                };
                _dbContext.Slides.Add(firstSlide);
                await _dbContext.SaveChangesAsync();

                // 🆕 加载幻灯片列表
                LoadSlideList();

                // 添加到导航树
                AddTextProjectToNavigationTree(_currentTextProject);

                // 🆕 新建项目后，保存按钮恢复为白色
                BtnSaveTextProject.Background = new SolidColorBrush(Colors.White);

                //System.Diagnostics.Debug.WriteLine($"✅ 创建文本项目成功: {projectName}");
                
                // 🆕 强制更新投影（如果投影已开启且未锁定）
                if (_projectionManager.IsProjectionActive && _currentSlide != null && !_isProjectionLocked)
                {
                    //System.Diagnostics.Debug.WriteLine("🔄 新建项目完成，准备更新投影...");
                    // 延迟确保UI完全渲染（异步执行，不等待）
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdateProjectionFromCanvas();
                        //System.Diagnostics.Debug.WriteLine("✅ 新建项目后已自动更新投影");
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
                // 🆕 重置状态：关闭原图模式
                ResetViewStateForTextEditor();
                
                // 加载项目
                _currentTextProject = await _textProjectManager.LoadProjectAsync(projectId);

                // 切换到编辑模式
                ShowTextEditor();

                // 🆕 加载幻灯片列表
                LoadSlideList();

                // 🆕 如果没有幻灯片，自动创建第一张
                if (!_dbContext.Slides.Any(s => s.ProjectId == _currentTextProject.Id))
                {
                    //System.Diagnostics.Debug.WriteLine("⚠️ 项目没有幻灯片，自动创建第一张");
                    var firstSlide = new Slide
                    {
                        ProjectId = _currentTextProject.Id,
                        Title = "幻灯片 1",
                        SortOrder = 1,
                        BackgroundColor = "#000000",  // 默认黑色背景
                        SplitMode = -1,  // 默认无分割模式
                        SplitStretchMode = false  // 默认适中模式
                    };
                    _dbContext.Slides.Add(firstSlide);
                    await _dbContext.SaveChangesAsync();
                    
                    // 🔧 迁移旧的文本元素到第一张幻灯片
                    var oldElements = _dbContext.TextElements
                        .Where(e => e.ProjectId == _currentTextProject.Id && e.SlideId == null)
                        .ToList();
                    if (oldElements.Any())
                    {
                        foreach (var element in oldElements)
                        {
                            element.SlideId = firstSlide.Id;
                        }
                        await _dbContext.SaveChangesAsync();
                        //System.Diagnostics.Debug.WriteLine($"✅ 已迁移 {oldElements.Count} 个旧文本元素到第一张幻灯片");
                    }
                    
                    // 重新加载幻灯片列表
                    LoadSlideList();
                }

                // 🆕 加载完成后，保存按钮恢复为白色
                BtnSaveTextProject.Background = new SolidColorBrush(Colors.White);

                // 🔧 修复：重置脚本按钮状态（文本项目没有录制数据）
                if (_playbackViewModel != null)
                {
                    // 文本项目不使用关键帧录制数据，强制设置为无数据状态（imageId=0表示无图片）
                    // SetCurrentImageAsync会检查时间数据，imageId=0时会设置HasTimingData=false
                    // 这样脚本按钮会恢复默认颜色
                    await _playbackViewModel.SetCurrentImageAsync(0, PlaybackMode.Keyframe);
                }

                //System.Diagnostics.Debug.WriteLine($"✅ 加载文本项目成功: {_currentTextProject.Name}");
                
                // 🆕 强制更新投影（如果投影已开启且未锁定）
                if (_projectionManager.IsProjectionActive && _currentSlide != null && !_isProjectionLocked)
                {
                    //System.Diagnostics.Debug.WriteLine("🔄 项目加载完成，准备更新投影...");
                    // 延迟确保UI完全渲染（异步执行，不等待）
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdateProjectionFromCanvas();
                        //System.Diagnostics.Debug.WriteLine("✅ 项目加载后已自动更新投影");
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 加载文本项目失败: {ex.Message}");
                WpfMessageBox.Show($"加载项目失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 显示文本编辑器
        /// </summary>
        private void ShowTextEditor()
        {
            ImageScrollViewer.Visibility = Visibility.Collapsed;
            VideoContainer.Visibility = Visibility.Collapsed;
            TextEditorPanel.Visibility = Visibility.Visible;
            
            // 🆕 重置投影状态：清空之前的图片投影状态
            if (_projectionManager.IsProjectionActive)
            {
                //System.Diagnostics.Debug.WriteLine("🔄 切换到文本编辑器模式，清空图片状态");
                
                // 重置投影滚动位置
                _projectionManager.ResetProjectionScroll();
                
                // 清空图片投影状态（文本编辑器不使用图片）
                _projectionManager.ClearImageState();
                
                //System.Diagnostics.Debug.WriteLine("✅ 图片状态已清空");
            }
        }

        /// <summary>
        /// 隐藏文本编辑器（返回图片模式）
        /// </summary>
        private void HideTextEditor()
        {
            TextEditorPanel.Visibility = Visibility.Collapsed;
            ImageScrollViewer.Visibility = Visibility.Visible;
            
            // 🔧 如果投影已开启，恢复图片投影
            if (_projectionManager != null && _projectionManager.IsProjectionActive)
            {
                //System.Diagnostics.Debug.WriteLine("🔄 退出文本编辑器，恢复图片投影");
                UpdateProjection();
            }
        }

        /// <summary>
        /// 关闭文本编辑器（清理状态）
        /// </summary>
        private void CloseTextEditor()
        {
            _currentTextProject = null;
            _textBoxes.Clear();
            EditorCanvas.Children.Clear();
            BackgroundImage.Source = null;
            HideTextEditor();
        }

        /// <summary>
        /// 检查并自动退出文本编辑器（如果当前在编辑模式）
        /// </summary>
        public bool AutoExitTextEditorIfNeeded()
        {
            if (TextEditorPanel.Visibility == Visibility.Visible && _currentTextProject != null)
            {
                //System.Diagnostics.Debug.WriteLine("🔄 检测到文本编辑器模式，自动退出...");
                
                // 检查是否有未保存的更改
                if (BtnSaveTextProject.Background is SolidColorBrush brush && brush.Color == Colors.LightGreen)
                {
                    //System.Diagnostics.Debug.WriteLine("⚠️ 有未保存的更改，自动保存");
                    // 自动保存
                    BtnSaveTextProject_Click(null, null);
                }
                
                // 关闭文本编辑器
                CloseTextEditor();
                //System.Diagnostics.Debug.WriteLine("✅ 已自动退出文本编辑器");
                return true;
            }
            return false;
        }

        /// <summary>
        /// 清空编辑画布
        /// </summary>
        private void ClearEditorCanvas()
        {
            _textBoxes.Clear();
            _selectedTextBox = null;
            
            // 清除所有文本框（保留背景图）
            var textBoxesToRemove = EditorCanvas.Children.OfType<DraggableTextBox>().ToList();
            foreach (var textBox in textBoxesToRemove)
            {
                EditorCanvas.Children.Remove(textBox);
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
                    Type = TreeItemType.TextProject, // 🔧 修正：使用 TextProject 类型
                    Id = project.Id,
                    IconKind = "FileDocument",
                    IconColor = "#2196F3", // 蓝色，与 LoadTextProjectsToTree 保持一致
                    Children = new System.Collections.ObjectModel.ObservableCollection<ProjectTreeItem>()
                };

                // 添加到根节点
                _projectTreeItems.Add(projectNode);

                // 🔧 修复：切换到项目模式并刷新显示
                _currentViewMode = NavigationViewMode.Projects;
                UpdateViewModeButtons();
                FilterProjectTree();

                // 选中新创建的项目
                projectNode.IsSelected = true;

                //System.Diagnostics.Debug.WriteLine($"✅ 项目已添加到导航树: {project.Name}");
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 添加项目到导航树失败: {ex.Message}");
            }
        }

        #endregion

        #region 工具栏事件处理

        /// <summary>
        /// 添加文本框按钮
        /// </summary>
        private async void BtnAddText_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTextProject == null)
            {
                WpfMessageBox.Show("请先创建或打开一个项目！", "提示", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_currentSlide == null)
            {
                WpfMessageBox.Show("请先选择一个幻灯片！", "提示", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // 🎯 固定位置创建新文本框,新文本在上层(ZIndex最大)
                const double newX = 100;
                const double newY = 100;
                const double newWidth = 300;
                const double newHeight = 100;
                
                // 计算最大ZIndex,新文本始终在最上层
                int maxZIndex = 0;
                if (_textBoxes.Count > 0)
                {
                    maxZIndex = _textBoxes.Max(tb => tb.Data.ZIndex);
                }
                
                // 创建新元素 (关联到当前幻灯片)
                var newElement = new TextElement
                {
                    SlideId = _currentSlide.Id,  // 🆕 关联到幻灯片
                    X = newX,
                    Y = newY,
                    Width = newWidth,
                    Height = newHeight,
                    Content = "双击编辑文字",
                    FontSize = 10,  // 默认字号10（实际渲染时会放大2倍显示为20）
                    FontFamily = "Microsoft YaHei UI",
                    FontColor = "#FFFFFF",  // 默认白色字体
                    ZIndex = maxZIndex + 1  // 新文本在最上层
                };

                // 保存到数据库
                await _textProjectManager.AddElementAsync(newElement);

                // 添加到画布
                var textBox = new DraggableTextBox(newElement);
                AddTextBoxToCanvas(textBox);
                
                // 🔧 新建文本框：自动进入编辑模式，全选占位符文本
                textBox.Focus();
                textBox.EnterEditModeForNew();

                //System.Diagnostics.Debug.WriteLine($"✅ 添加文本框成功: ID={newElement.Id}");
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 添加文本框失败: {ex.Message}");
                WpfMessageBox.Show($"添加文本框失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 复制文本框（立即创建新的副本）
        /// </summary>
        private async Task CopyTextBoxAsync(DraggableTextBox sourceTextBox)
        {
            if (sourceTextBox == null || _currentSlide == null)
                return;

            try
            {
                var sourceElement = sourceTextBox.Data;
                
                // 计算最大ZIndex,新文本框在最上层
                int maxZIndex = 0;
                if (_textBoxes.Count > 0)
                {
                    maxZIndex = _textBoxes.Max(tb => tb.Data.ZIndex);
                }

                // 创建新元素（稍微偏移位置,避免完全重叠）
                var newElement = new TextElement
                {
                    SlideId = _currentSlide.Id,
                    X = sourceElement.X + 20,  // 向右偏移20像素
                    Y = sourceElement.Y + 20,  // 向下偏移20像素
                    Width = sourceElement.Width,
                    Height = sourceElement.Height,
                    Content = sourceElement.Content,
                    FontSize = sourceElement.FontSize,
                    FontFamily = sourceElement.FontFamily,
                    FontColor = sourceElement.FontColor,
                    IsBold = sourceElement.IsBold,
                    TextAlign = sourceElement.TextAlign,
                    ZIndex = maxZIndex + 1
                };

                // 保存到数据库
                await _textProjectManager.AddElementAsync(newElement);

                // 添加到画布
                var textBox = new DraggableTextBox(newElement);
                AddTextBoxToCanvas(textBox);

                // 选中新复制的文本框
                textBox.SetSelected(true);
                _selectedTextBox = textBox;

                // 标记已修改
                MarkContentAsModified();

                //System.Diagnostics.Debug.WriteLine($"✅ 复制文本框成功");
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 复制文本框失败: {ex.Message}");
                WpfMessageBox.Show($"复制文本框失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 删除指定的文本框（通用方法，支持按钮、右键菜单、快捷键调用，直接删除不弹窗确认）
        /// </summary>
        private async Task DeleteTextBoxAsync(DraggableTextBox textBox)
        {
            if (textBox == null)
                return;

            try
            {
                // 从数据库删除
                await _textProjectManager.DeleteElementAsync(textBox.Data.Id);

                // 从画布移除
                EditorCanvas.Children.Remove(textBox);
                _textBoxes.Remove(textBox);

                // 如果删除的是当前选中项，清除选中状态
                if (_selectedTextBox == textBox)
                {
                    _selectedTextBox = null;
                }

                // 标记已修改
                MarkContentAsModified();

                //System.Diagnostics.Debug.WriteLine($"✅ 删除文本框成功");
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 删除文本框失败: {ex.Message}");
                WpfMessageBox.Show($"删除文本框失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 背景图片按钮点击（直接导入图片）
        /// </summary>
        private void BtnBackgroundImage_Click(object sender, RoutedEventArgs e)
        {
            BtnLoadBackgroundImage_Click(sender, e);
        }

        /// <summary>
        /// 背景颜色按钮点击（直接选择颜色）
        /// </summary>
        private void BtnBackgroundColor_Click(object sender, RoutedEventArgs e)
        {
            BtnSelectBackgroundColor_Click(sender, e);
        }

        /// <summary>
        /// 原图拉伸模式切换按钮点击
        /// </summary>
        private async void BtnSplitStretchMode_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTextProject == null || _currentSlide == null)
                return;
            
            // 如果没有区域图片，不执行操作
            if (_regionImages.Count == 0)
                return;
            
            // 检查当前第一个区域图片的拉伸模式
            var firstImage = _regionImages.Values.FirstOrDefault();
            if (firstImage == null)
                return;
            
            // 根据当前状态切换
            bool isCurrentlyFill = firstImage.Stretch == System.Windows.Media.Stretch.Fill;
            var newStretch = isCurrentlyFill ? 
                System.Windows.Media.Stretch.Uniform :  // 当前是拉伸 → 切换为适中
                System.Windows.Media.Stretch.Fill;      // 当前是适中 → 切换为拉伸
            
            // 应用到所有区域图片（包括单画面模式的区域0）
            foreach (var kvp in _regionImages)
            {
                kvp.Value.Stretch = newStretch;
            }
            
            // 更新内部状态和按钮显示
            _splitStretchMode = (newStretch == System.Windows.Media.Stretch.Fill);
            UpdateStretchModeButton();
            
            // 保存到数据库
            await SaveSplitStretchModeAsync();
        }
        
        /// <summary>
        /// 更新拉伸模式按钮显示（根据当前图片的实际拉伸模式）
        /// </summary>
        private void UpdateStretchModeButton()
        {
            // 按钮显示当前的实际模式：
            // - 如果图片是拉伸模式(Fill)，显示"📐 拉伸"
            // - 如果图片是适中模式(Uniform)，显示"📐 适中"
            BtnSplitStretchMode.Content = _splitStretchMode ? "📐 拉伸" : "📐 适中";
        }
        
        /// <summary>
        /// 保存拉伸模式到数据库
        /// </summary>
        private async Task SaveSplitStretchModeAsync()
        {
            if (_currentSlide == null)
                return;
                
            try
            {
                var slideToUpdate = await _dbContext.Slides.FindAsync(_currentSlide.Id);
                if (slideToUpdate != null)
                {
                    slideToUpdate.SplitStretchMode = _splitStretchMode;
                    slideToUpdate.ModifiedTime = DateTime.Now;
                    await _dbContext.SaveChangesAsync();
                    
                    // 更新本地缓存
                    _currentSlide.SplitStretchMode = _splitStretchMode;
                    
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"💾 [SaveSplitStretchMode] 已保存拉伸模式: {_splitStretchMode}");
                    //#endif
                }
            }
            catch
            {
                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($"❌ [SaveSplitStretchMode] 失败");
                #endif
            }
        }
        
        /// <summary>
        /// 分割按钮点击（显示分割模式选择菜单）
        /// </summary>
        private void BtnSplitView_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTextProject == null || _currentSlide == null)
                return;

            var contextMenu = new ContextMenu();
            
            // 🔑 应用自定义样式
            contextMenu.Style = (Style)this.FindResource("NoBorderContextMenuStyle");

            // 获取当前分割模式（-1 表示未设置，不勾选任何项）
            int currentSplitMode = _currentSlide.SplitMode;

            // 单画面
            var singleItem = new MenuItem 
            { 
                Header = currentSplitMode == (int)Database.Models.Enums.ViewSplitMode.Single 
                    ? "✓ 单画面" : "   单画面",
                Height = 36
            };
            singleItem.Click += (s, args) => SetSplitMode(Database.Models.Enums.ViewSplitMode.Single);
            contextMenu.Items.Add(singleItem);

            // 左右分割
            var horizontalItem = new MenuItem 
            { 
                Header = currentSplitMode == (int)Database.Models.Enums.ViewSplitMode.Horizontal 
                    ? "✓ 左右分割" : "   左右分割",
                Height = 36
            };
            horizontalItem.Click += (s, args) => SetSplitMode(Database.Models.Enums.ViewSplitMode.Horizontal);
            contextMenu.Items.Add(horizontalItem);

            // 上下分割
            var verticalItem = new MenuItem 
            { 
                Header = currentSplitMode == (int)Database.Models.Enums.ViewSplitMode.Vertical 
                    ? "✓ 上下分割" : "   上下分割",
                Height = 36
            };
            verticalItem.Click += (s, args) => SetSplitMode(Database.Models.Enums.ViewSplitMode.Vertical);
            contextMenu.Items.Add(verticalItem);

            // 三分割
            var tripleSplitItem = new MenuItem 
            { 
                Header = currentSplitMode == (int)Database.Models.Enums.ViewSplitMode.TripleSplit 
                    ? "✓ 三分割" : "   三分割",
                Height = 36
            };
            tripleSplitItem.Click += (s, args) => SetSplitMode(Database.Models.Enums.ViewSplitMode.TripleSplit);
            contextMenu.Items.Add(tripleSplitItem);

            // 四宫格
            var quadItem = new MenuItem 
            { 
                Header = currentSplitMode == (int)Database.Models.Enums.ViewSplitMode.Quad 
                    ? "✓ 四宫格" : "   四宫格",
                Height = 36
            };
            quadItem.Click += (s, args) => SetSplitMode(Database.Models.Enums.ViewSplitMode.Quad);
            contextMenu.Items.Add(quadItem);

            contextMenu.PlacementTarget = sender as UIElement;
            contextMenu.IsOpen = true;
        }

        /// <summary>
        /// 设置分割模式
        /// </summary>
        private async void SetSplitMode(Database.Models.Enums.ViewSplitMode mode)
        {
            if (_currentSlide == null)
                return;

            try
            {
                // 更新数据库
                var slideToUpdate = await _dbContext.Slides.FindAsync(_currentSlide.Id);
                if (slideToUpdate != null)
                {
                    slideToUpdate.SplitMode = (int)mode;
                    slideToUpdate.ModifiedTime = DateTime.Now;
                    
                    // 切换分割模式时，清空分割区域数据
                    slideToUpdate.SplitRegionsData = null;
                    
                    await _dbContext.SaveChangesAsync();

                    // 更新本地缓存
                    _currentSlide.SplitMode = (int)mode;
                    _currentSlide.SplitRegionsData = slideToUpdate.SplitRegionsData;
                }

                // 更新预览画布显示分割布局
                UpdateSplitLayout(mode);

                MarkContentAsModified();
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [SetSplitMode] 失败: {ex.Message}");
#else
                _ = ex; // 避免未使用警告
#endif
            }
        }

        /// <summary>
        /// 更新分割布局显示
        /// </summary>
        private void UpdateSplitLayout(Database.Models.Enums.ViewSplitMode mode)
        {
            // 清除旧的分割线和边框
            ClearSplitLines();
            ClearRegionBorders();
            
            // 如果模式值 < 0，表示未设置分割模式，不创建任何区域
            if ((int)mode < 0)
            {
                return;
            }
            
            double canvasWidth = EditorCanvas.ActualWidth > 0 ? EditorCanvas.ActualWidth : 1080;
            double canvasHeight = EditorCanvas.ActualHeight > 0 ? EditorCanvas.ActualHeight : 700;
            
            switch (mode)
            {
                case Database.Models.Enums.ViewSplitMode.Single:
                    // 🆕 单画面模式：创建一个占满整个画布的区域（不显示边框和标签）
                    CreateRegionBorder(0, 0, 0, canvasWidth, canvasHeight);
                    break;
                    
                case Database.Models.Enums.ViewSplitMode.Horizontal:
                    // 左右分割：绘制一条竖线
                    DrawVerticalLine(canvasWidth / 2, 0, canvasHeight);
                    // 创建两个区域边框
                    CreateRegionBorder(0, 0, 0, canvasWidth / 2, canvasHeight);
                    CreateRegionBorder(1, canvasWidth / 2, 0, canvasWidth / 2, canvasHeight);
                    break;
                    
                case Database.Models.Enums.ViewSplitMode.Vertical:
                    // 上下分割：绘制一条横线
                    DrawHorizontalLine(canvasHeight / 2, 0, canvasWidth);
                    // 创建两个区域边框
                    CreateRegionBorder(0, 0, 0, canvasWidth, canvasHeight / 2);
                    CreateRegionBorder(1, 0, canvasHeight / 2, canvasWidth, canvasHeight / 2);
                    break;
                    
                case Database.Models.Enums.ViewSplitMode.Quad:
                    // 四宫格：绘制十字线
                    DrawVerticalLine(canvasWidth / 2, 0, canvasHeight);
                    DrawHorizontalLine(canvasHeight / 2, 0, canvasWidth);
                    // 创建四个区域边框
                    CreateRegionBorder(0, 0, 0, canvasWidth / 2, canvasHeight / 2);
                    CreateRegionBorder(1, canvasWidth / 2, 0, canvasWidth / 2, canvasHeight / 2);
                    CreateRegionBorder(2, 0, canvasHeight / 2, canvasWidth / 2, canvasHeight / 2);
                    CreateRegionBorder(3, canvasWidth / 2, canvasHeight / 2, canvasWidth / 2, canvasHeight / 2);
                    break;
                    
                case Database.Models.Enums.ViewSplitMode.TripleSplit:
                    // 三分割：左边上下分割，右边整个竖分割
                    // 绘制一条竖线（左右分割 50%）
                    DrawVerticalLine(canvasWidth / 2, 0, canvasHeight);
                    // 绘制一条横线（左边上下分割）
                    DrawHorizontalLine(canvasHeight / 2, 0, canvasWidth / 2);
                    // 创建三个区域边框
                    CreateRegionBorder(0, 0, 0, canvasWidth / 2, canvasHeight / 2);  // 左上1
                    CreateRegionBorder(1, 0, canvasHeight / 2, canvasWidth / 2, canvasHeight / 2);  // 左下2
                    CreateRegionBorder(2, canvasWidth / 2, 0, canvasWidth / 2, canvasHeight);  // 右3
                    break;
            }
            
            // 默认选中第一个区域
            SelectRegion(0);
        }
        
        /// <summary>
        /// 清除分割线
        /// </summary>
        private void ClearSplitLines()
        {
            // 移除所有带有 "SplitLine" 标记的元素
            var linesToRemove = EditorCanvas.Children.OfType<Line>()
                .Where(l => l.Tag != null && l.Tag.ToString() == "SplitLine")
                .ToList();
                
            foreach (var line in linesToRemove)
            {
                EditorCanvas.Children.Remove(line);
            }
        }
        
        /// <summary>
        /// 绘制竖线
        /// </summary>
        private void DrawVerticalLine(double x, double y1, double y2)
        {
            var line = new Line
            {
                X1 = x,
                Y1 = y1,
                X2 = x,
                Y2 = y2,
                Stroke = new SolidColorBrush(WpfColor.FromRgb(255, 165, 0)), // 橙色
                StrokeThickness = 3,
                StrokeDashArray = new DoubleCollection { 5, 3 }, // 虚线
                Tag = "SplitLine",
                IsHitTestVisible = false // 不响应鼠标事件
            };
            
            Canvas.SetZIndex(line, 1000); // 置于顶层
            EditorCanvas.Children.Add(line);
        }
        
        /// <summary>
        /// 绘制横线
        /// </summary>
        private void DrawHorizontalLine(double y, double x1, double x2)
        {
            var line = new Line
            {
                X1 = x1,
                Y1 = y,
                X2 = x2,
                Y2 = y,
                Stroke = new SolidColorBrush(WpfColor.FromRgb(255, 165, 0)), // 橙色
                StrokeThickness = 3,
                StrokeDashArray = new DoubleCollection { 5, 3 }, // 虚线
                Tag = "SplitLine",
                IsHitTestVisible = false // 不响应鼠标事件
            };
            
            Canvas.SetZIndex(line, 1000); // 置于顶层
            EditorCanvas.Children.Add(line);
        }
        
        /// <summary>
        /// 创建区域边框
        /// </summary>
        private void CreateRegionBorder(int regionIndex, double x, double y, double width, double height)
        {
            // 🆕 判断是否是单画面模式
            bool isSingleMode = _currentSlide != null && _currentSlide.SplitMode == 0;
            
            var border = new WpfRectangle
            {
                Width = width,
                Height = height,
                Stroke = isSingleMode ? System.Windows.Media.Brushes.Transparent : new SolidColorBrush(WpfColor.FromRgb(128, 128, 128)), // 单画面模式透明
                StrokeThickness = isSingleMode ? 0 : 2, // 单画面模式无边框
                Fill = System.Windows.Media.Brushes.Transparent,
                Tag = $"RegionBorder_{regionIndex}",
                Cursor = System.Windows.Input.Cursors.Hand
            };
            
            Canvas.SetLeft(border, x);
            Canvas.SetTop(border, y);
            Canvas.SetZIndex(border, 999); // 低于分割线
            
            // 添加点击事件
            border.MouseLeftButtonDown += (s, e) =>
            {
                SelectRegion(regionIndex);
                e.Handled = true;
            };
            
            // 添加右键菜单事件
            border.MouseRightButtonDown += (s, e) =>
            {
                SelectRegion(regionIndex);
                ShowRegionContextMenu(s as UIElement);
                e.Handled = true;
            };
            
            _splitRegionBorders.Add(border);
            EditorCanvas.Children.Add(border);
            
            // 🆕 只在非单画面模式下显示序列号标签
            if (!isSingleMode)
            {
                var label = new System.Windows.Controls.Border
                {
                    Background = new SolidColorBrush(WpfColor.FromArgb(200, 255, 102, 0)), // 半透明橙色
                    CornerRadius = new System.Windows.CornerRadius(0, 0, 8, 0), // 右下圆角
                    Padding = new System.Windows.Thickness(8, 4, 8, 4),
                    Tag = $"RegionLabel_{regionIndex}",
                    IsHitTestVisible = false // 不响应鼠标事件
                };
                
                var labelText = new System.Windows.Controls.TextBlock
                {
                    Text = (regionIndex + 1).ToString(),
                    FontSize = 18,
                    FontWeight = System.Windows.FontWeights.Bold,
                    Foreground = System.Windows.Media.Brushes.White
                };
                
                label.Child = labelText;
                
                // 定位到左上角
                Canvas.SetLeft(label, x); // 左上角
                Canvas.SetTop(label, y);
                Canvas.SetZIndex(label, 1001); // 置于最顶层
                
                EditorCanvas.Children.Add(label);
            }
        }
        
        /// <summary>
        /// 清除区域边框
        /// </summary>
        private void ClearRegionBorders()
        {
            // 清除边框
            foreach (var border in _splitRegionBorders)
            {
                EditorCanvas.Children.Remove(border);
            }
            _splitRegionBorders.Clear();
            
            // 🆕 清除序列号标签
            var labelsToRemove = EditorCanvas.Children.OfType<System.Windows.Controls.Border>()
                .Where(b => b.Tag != null && b.Tag.ToString().StartsWith("RegionLabel_"))
                .ToList();
            foreach (var label in labelsToRemove)
            {
                EditorCanvas.Children.Remove(label);
            }
            
            // 清除区域图片
            foreach (var image in _regionImages.Values)
            {
                EditorCanvas.Children.Remove(image);
            }
            _regionImages.Clear();
            _regionImagePaths.Clear();
        }
        
        /// <summary>
        /// 选择区域
        /// </summary>
        private void SelectRegion(int regionIndex)
        {
            if (regionIndex < 0 || regionIndex >= _splitRegionBorders.Count)
                return;
                
            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"🎯 [SelectRegion] 选中区域: {regionIndex}");
            //#endif
            
            _selectedRegionIndex = regionIndex;
            
            // 🔑 设置画布焦点，使其能接收键盘事件
            EditorCanvas.Focus();
            
            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"🔑 [SelectRegion] 已设置画布焦点，IsFocused: {EditorCanvas.IsFocused}");
            //#endif
            
            // 更新所有边框的样式
            for (int i = 0; i < _splitRegionBorders.Count; i++)
            {
                var border = _splitRegionBorders[i];
                if (i == regionIndex)
                {
                    // 选中状态：绿色边框
                    border.Stroke = new SolidColorBrush(WpfColor.FromRgb(0, 255, 0));
                    border.StrokeThickness = 2;
                }
                else
                {
                    // 未选中状态：灰色细边框
                    border.Stroke = new SolidColorBrush(WpfColor.FromRgb(128, 128, 128));
                    border.StrokeThickness = 2;
                }
            }
        }
        
        /// <summary>
        /// 加载图片到选中的分割区域
        /// </summary>
        public async Task LoadImageToSplitRegion(string imagePath)
        {
            if (_currentSlide == null || _splitRegionBorders.Count == 0)
                return;
                
            try
            {
                // 🆕 检查图片是否来自原图标记或变色标记的文件夹
                (bool shouldUseStretch, bool shouldApplyColorEffect) = await Task.Run(() =>
                {
                    try
                    {
                        var mediaFile = _dbContext.MediaFiles.FirstOrDefault(m => m.Path == imagePath);
                        
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"🔍 [LoadImageToSplitRegion] 检查图片: {System.IO.Path.GetFileName(imagePath)}");
                        //System.Diagnostics.Debug.WriteLine($"   MediaFile找到: {mediaFile != null}");
                        //if (mediaFile != null)
                        //{
                        //    System.Diagnostics.Debug.WriteLine($"   FolderId: {mediaFile.FolderId}");
                        //}
                        #endif
                        
                        if (mediaFile?.FolderId != null)
                        {
                            // 检查文件夹是否有原图标记
                            bool isOriginalFolder = _originalManager.CheckOriginalMark(
                                Database.Models.Enums.ItemType.Folder,
                                mediaFile.FolderId.Value
                            );

                            // 🎨 检查文件夹是否有变色标记
                            bool hasColorEffectMark = _dbManager.HasFolderAutoColorEffect(mediaFile.FolderId.Value);

                            #if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"   原图标记: {isOriginalFolder}");
                            //System.Diagnostics.Debug.WriteLine($"   变色标记: {hasColorEffectMark}");
                            //if (isOriginalFolder)
                            //{
                            //    System.Diagnostics.Debug.WriteLine($"🎯 [LoadImageToSplitRegion] 检测到原图标记文件夹，自动使用拉伸模式");
                            //}
                            //if (hasColorEffectMark)
                            //{
                            //    System.Diagnostics.Debug.WriteLine($"🎨 [LoadImageToSplitRegion] 检测到变色标记文件夹，自动应用变色效果");
                            //}
                            #endif

                            return (isOriginalFolder, hasColorEffectMark);
                        }
                        
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"   未找到MediaFile或FolderId为空");
                        #endif
                        
                        return (false, false);
                    }
                    catch
                    {
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"❌ [LoadImageToSplitRegion] 检查标记失败");
                        #endif
                        return (false, false);
                    }
                });
                
                // 获取区域边框信息
                var border = _splitRegionBorders[_selectedRegionIndex];
                double x = Canvas.GetLeft(border);
                double y = Canvas.GetTop(border);
                double width = border.Width;
                double height = border.Height;
                
                // 如果该区域已经有图片，先移除
                if (_regionImages.ContainsKey(_selectedRegionIndex))
                {
                    EditorCanvas.Children.Remove(_regionImages[_selectedRegionIndex]);
                    _regionImages.Remove(_selectedRegionIndex);
                }
                
                // 🚀 使用优化的图片加载（GPU加速 + 缓存）
                var bitmapSource = await Task.Run<BitmapSource>(() =>
                {
                    // 🎨 如果需要应用变色效果，使用 SkiaSharp 加载并处理
                    if (shouldApplyColorEffect)
                    {
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"🎨 [LoadImageToSplitRegion] 开始应用变色效果...");
                        #endif
                        
                        try
                        {
                            using var skBitmap = SkiaSharp.SKBitmap.Decode(imagePath);
                            if (skBitmap != null)
                            {
                                #if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"   SKBitmap加载成功，尺寸: {skBitmap.Width}x{skBitmap.Height}");
                                #endif
                                
                                // 应用变色效果
                                _imageProcessor.ApplyYellowTextEffect(skBitmap);

                                #if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"   变色效果已应用");
                                #endif
                                
                                // 转换为 WPF BitmapSource
                                var result = _imageProcessor.ConvertToBitmapSource(skBitmap);
                                
                                #if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"✅ [LoadImageToSplitRegion] 变色效果应用成功");
                                #endif
                                
                                return result;
                            }
                            else
                            {
                                #if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"❌ [LoadImageToSplitRegion] SKBitmap加载失败");
                                #endif
                            }
                        }
                        catch
                        {
                            #if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"❌ [LoadImageToSplitRegion] 应用变色效果失败");
                            #endif
                        }
                    }
                    else
                    {
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"📷 [LoadImageToSplitRegion] 正常加载（无变色效果）");
                        #endif
                    }

                    // 正常加载（无变色效果）
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(imagePath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad; // 立即加载到内存
                    bitmap.EndInit();
                    bitmap.Freeze(); // 🔥 冻结到GPU显存，跨线程共享
                    return bitmap;
                });
                
                // 决定使用的拉伸模式
                System.Windows.Media.Stretch stretchMode;
                if (shouldUseStretch)
                {
                    // 原图标记文件夹：拉伸填满
                    stretchMode = System.Windows.Media.Stretch.Fill;
                }
                else if (_currentSlide.SplitMode == 0 || _currentSlide.SplitMode == 4)
                {
                    // 单画面模式或三分割模式：默认拉伸填满
                    stretchMode = System.Windows.Media.Stretch.Fill;
                }
                else
                {
                    // 其他分割模式：根据用户设置
                    stretchMode = _splitStretchMode ? 
                        System.Windows.Media.Stretch.Fill : 
                        System.Windows.Media.Stretch.Uniform;
                }
                
                // 创建 Image 控件，应用拉伸模式
                var imageControl = new System.Windows.Controls.Image
                {
                    Source = bitmapSource,
                    Width = width,
                    Height = height,
                    Stretch = stretchMode,
                    Tag = $"RegionImage_{_selectedRegionIndex}",
                    CacheMode = new BitmapCache // 🔥 启用GPU缓存，减少重复渲染
                    {
                        RenderAtScale = CalculateOptimalRenderScale()  // 🔥 动态计算渲染质量：自适应1080p/2K/4K投影屏
                    }
                };
                
                Canvas.SetLeft(imageControl, x);
                Canvas.SetTop(imageControl, y);
                Canvas.SetZIndex(imageControl, 998); // 低于边框
                
                // 添加到画布
                EditorCanvas.Children.Add(imageControl);
                
                // 保存引用
                _regionImages[_selectedRegionIndex] = imageControl;
                _regionImagePaths[_selectedRegionIndex] = imagePath;
                
                // 更新边框样式（有图片的区域显示黄色）
                border.Stroke = new SolidColorBrush(WpfColor.FromRgb(255, 215, 0)); // 金色
                
                // 🆕 同步更新拉伸按钮显示
                _splitStretchMode = (stretchMode == System.Windows.Media.Stretch.Fill);
                UpdateStretchModeButton();
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"✅ [LoadImageToSplitRegion] 图片已加载到区域 {_selectedRegionIndex}");
                //#endif
                
                // 保存分割配置到数据库
                await SaveSplitConfigAsync();
                
                MarkContentAsModified();
                
                // 🆕 自动选中下一个未加载图片的区域
                AutoSelectNextEmptyRegion();
            }
            catch
            {
                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($"❌ [LoadImageToSplitRegion] 失败");
                #endif
            }
        }
        
        /// <summary>
        /// 自动选中下一个未加载图片的区域
        /// </summary>
        private void AutoSelectNextEmptyRegion()
        {
            if (_splitRegionBorders.Count == 0)
                return;
                
            // 从当前选中区域的下一个开始查找
            int startIndex = (_selectedRegionIndex + 1) % _splitRegionBorders.Count;
            
            // 循环查找第一个没有图片的区域
            for (int i = 0; i < _splitRegionBorders.Count; i++)
            {
                int checkIndex = (startIndex + i) % _splitRegionBorders.Count;
                
                // 如果该区域没有图片，选中它
                if (!_regionImages.ContainsKey(checkIndex))
                {
                    #if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"🔄 [AutoSelectNextEmptyRegion] 自动选中区域 {checkIndex}（未加载图片）");
                    #endif
                    
                    SelectRegion(checkIndex);
                    return;
                }
            }
            
            // 如果所有区域都有图片了，回到第一个区域
            #if DEBUG
            //System.Diagnostics.Debug.WriteLine($"✅ [AutoSelectNextEmptyRegion] 所有区域都已加载图片，回到区域 0");
            #endif
            SelectRegion(0);
        }
        
        /// <summary>
        /// 检查是否处于分割模式（包括单画面模式）
        /// </summary>
        public bool IsInSplitMode()
        {
            return _currentSlide != null && 
                   _currentSlide.SplitMode >= 0 && 
                   _splitRegionBorders.Count > 0;
        }
        
        /// <summary>
        /// 清空选中区域的图片
        /// </summary>
        public async Task ClearSelectedRegionImage()
        {
            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"🗑️ [ClearSelectedRegionImage] 开始清空区域图片");
            //System.Diagnostics.Debug.WriteLine($"   _selectedRegionIndex: {_selectedRegionIndex}");
            //System.Diagnostics.Debug.WriteLine($"   包含图片: {_regionImages.ContainsKey(_selectedRegionIndex)}");
            //#endif
            
            if (_selectedRegionIndex < 0 || !_regionImages.ContainsKey(_selectedRegionIndex))
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"⚠️ [ClearSelectedRegionImage] 条件不满足，退出");
                //#endif
                return;
            }
                
            try
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"🗑️ [ClearSelectedRegionImage] 开始移除图片控件");
                //#endif
                
                // 移除图片控件
                var imageControl = _regionImages[_selectedRegionIndex];
                EditorCanvas.Children.Remove(imageControl);
                _regionImages.Remove(_selectedRegionIndex);
                _regionImagePaths.Remove(_selectedRegionIndex);
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"✅ [ClearSelectedRegionImage] 图片控件已移除");
                //#endif
                
                // 保持边框选中状态（绿色），不改变分割状态
                // 边框和分割线保持不变，只是清空了图片内容
                
                // 保存到数据库
                await SaveSplitConfigAsync();
                MarkContentAsModified();
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"✅ [ClearSelectedRegionImage] 已保存到数据库");
                //#endif
                
                ShowStatus($"✅ 已清空区域 {_selectedRegionIndex + 1} 的图片");
            }
            catch (Exception ex)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"❌ [ClearSelectedRegionImage] 失败: {ex.Message}");
                //#endif
                
                WpfMessageBox.Show($"清空区域图片失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 清空所有区域的图片
        /// </summary>
        public async Task ClearAllRegionImages()
        {
            if (_regionImages.Count == 0)
            {
                WpfMessageBox.Show("当前没有加载任何图片", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
                
            var result = WpfMessageBox.Show("确定要清空所有区域的图片吗？", "确认",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
                
            if (result != MessageBoxResult.Yes)
                return;
                
            try
            {
                // 移除所有图片控件
                foreach (var kvp in _regionImages.ToList())
                {
                    EditorCanvas.Children.Remove(kvp.Value);
                }
                _regionImages.Clear();
                _regionImagePaths.Clear();
                
                // 恢复所有边框样式为灰色
                foreach (var border in _splitRegionBorders)
                {
                    border.Stroke = new SolidColorBrush(WpfColor.FromRgb(128, 128, 128));
                }
                
                // 保存到数据库
                await SaveSplitConfigAsync();
                MarkContentAsModified();
                
                ShowStatus($"✅ 已清空所有区域的图片");
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"清空所有图片失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 显示区域右键菜单
        /// </summary>
        private void ShowRegionContextMenu(UIElement target)
        {
            var contextMenu = new ContextMenu();
            
            // 应用自定义样式
            contextMenu.Style = (Style)this.FindResource("NoBorderContextMenuStyle");
            
            // 选项1：清空当前区域
            var clearCurrentItem = new MenuItem
            {
                Header = "🗑 清空当前区域",
                Height = 36
            };
            clearCurrentItem.Click += async (s, e) => await ClearSelectedRegionImage();
            contextMenu.Items.Add(clearCurrentItem);
            
            // 选项2：清空所有区域
            var clearAllItem = new MenuItem
            {
                Header = "🗑 清空所有区域",
                Height = 36
            };
            clearAllItem.Click += async (s, e) => await ClearAllRegionImages();
            contextMenu.Items.Add(clearAllItem);
            
            contextMenu.PlacementTarget = target;
            contextMenu.IsOpen = true;
        }
        
        /// <summary>
        /// 保存分割配置到数据库
        /// </summary>
        private async Task SaveSplitConfigAsync()
        {
            if (_currentSlide == null)
                return;
                
            try
            {
                // 将区域图片路径序列化为 JSON
                var regionDataList = _regionImagePaths
                    .Select(kvp => new Database.Models.DTOs.SplitRegionData
                    {
                        RegionIndex = kvp.Key,
                        ImagePath = kvp.Value
                    })
                    .ToList();
                
                string json = JsonSerializer.Serialize(regionDataList);
                
                // 更新数据库
                var slideToUpdate = await _dbContext.Slides.FindAsync(_currentSlide.Id);
                if (slideToUpdate != null)
                {
                    slideToUpdate.SplitRegionsData = json;
                    slideToUpdate.ModifiedTime = DateTime.Now;
                    await _dbContext.SaveChangesAsync();
                    
                    // 更新本地缓存
                    _currentSlide.SplitRegionsData = json;
                    
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"💾 [SaveSplitConfig] 已保存 {regionDataList.Count} 个区域配置");
                    //#endif
                }
            }
            catch
            {
                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($"❌ [SaveSplitConfig] 失败");
                #endif
            }
        }
        
        /// <summary>
        /// 投影前调整分割线和边框样式（细线）
        /// </summary>
        private void HideSplitLinesForProjection()
        {
            try
            {
                // 将所有分割线改为细线（1px实线）
                foreach (var child in EditorCanvas.Children.OfType<Line>())
                {
                    if (child.Tag != null && child.Tag.ToString() == "SplitLine")
                    {
                        // 保存原始样式到Tag
                        child.Tag = new { 
                            Type = "SplitLine", 
                            OriginalThickness = child.StrokeThickness,
                            OriginalDashArray = child.StrokeDashArray
                        };
                        
                        // 改为细实线
                        child.StrokeThickness = 1;
                        child.StrokeDashArray = null; // 实线
                    }
                }
                
                // 隐藏所有区域边框
                foreach (var border in _splitRegionBorders)
                {
                    border.Visibility = Visibility.Collapsed;
                }
                
                // 🔥 隐藏未加载图片的区域的序号标签
                var labels = EditorCanvas.Children.OfType<System.Windows.Controls.Border>()
                    .Where(b => b.Tag != null && b.Tag.ToString().StartsWith("RegionLabel_"))
                    .ToList();
                
                foreach (var label in labels)
                {
                    // 从Tag中提取区域索引
                    var tagStr = label.Tag.ToString();
                    if (int.TryParse(tagStr.Replace("RegionLabel_", ""), out int regionIndex))
                    {
                        // 检查该区域是否已加载图片
                        if (!_regionImages.ContainsKey(regionIndex))
                        {
                            // 未加载图片，隐藏标签
                            label.Visibility = Visibility.Collapsed;
                        }
                    }
                }
                
                //System.Diagnostics.Debug.WriteLine($"🎨 [投影] 已调整分割线为细线，隐藏边框和空白区域标签");
            }
            catch
            {
                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($"❌ [HideSplitLinesForProjection] 失败");
                #endif
            }
        }
        
        /// <summary>
        /// 投影后恢复分割线和边框显示
        /// </summary>
        private void RestoreSplitLinesAfterProjection()
        {
            try
            {
                // 恢复所有分割线的原始样式
                foreach (var child in EditorCanvas.Children.OfType<Line>())
                {
                    if (child.Tag != null)
                    {
                        var tagType = child.Tag.GetType();
                        if (tagType.GetProperty("Type") != null)
                        {
                            dynamic tag = child.Tag;
                            if (tag.Type == "SplitLine")
                            {
                                // 恢复原始粗细和虚线样式
                                child.StrokeThickness = tag.OriginalThickness;
                                child.StrokeDashArray = tag.OriginalDashArray;
                                
                                // 恢复简单的Tag
                                child.Tag = "SplitLine";
                            }
                        }
                    }
                }
                
                // 恢复所有区域边框
                foreach (var border in _splitRegionBorders)
                {
                    border.Visibility = Visibility.Visible;
                }
                
                // 🔥 恢复所有区域序号标签（包括未加载图片的）
                var labels = EditorCanvas.Children.OfType<System.Windows.Controls.Border>()
                    .Where(b => b.Tag != null && b.Tag.ToString().StartsWith("RegionLabel_"))
                    .ToList();
                
                foreach (var label in labels)
                {
                    label.Visibility = Visibility.Visible;
                }
                
                //System.Diagnostics.Debug.WriteLine($"🎨 [投影] 已恢复分割线、边框和标签");
            }
            catch
            {
                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($"❌ [RestoreSplitLinesAfterProjection] 失败");
                #endif
            }
        }
        
        /// <summary>
        /// 恢复分割配置
        /// </summary>
        private void RestoreSplitConfig(Slide slide)
        {
            try
            {
                // 🆕 恢复拉伸模式
                _splitStretchMode = slide.SplitStretchMode;
                UpdateStretchModeButton();
                
                // 检查是否有分割模式（-1 表示无分割模式）
                if (slide.SplitMode < 0)
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"📋 [RestoreSplitConfig] 无分割模式，清空分割区域");
                    //#endif
                    // 清空所有分割元素
                    ClearSplitLines();
                    ClearRegionBorders();
                    return;
                }
                
                // 先更新分割布局（包括单画面模式）
                var splitMode = (Database.Models.Enums.ViewSplitMode)slide.SplitMode;
                UpdateSplitLayout(splitMode);
                
                // 检查是否有区域数据
                if (string.IsNullOrEmpty(slide.SplitRegionsData))
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"📋 [RestoreSplitConfig] 分割模式={splitMode}，但无区域数据");
                    //#endif
                    return;
                }
                
                // 反序列化区域数据
                var regionDataList = JsonSerializer.Deserialize<List<Database.Models.DTOs.SplitRegionData>>(slide.SplitRegionsData);
                if (regionDataList == null || regionDataList.Count == 0)
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"📋 [RestoreSplitConfig] 反序列化失败或数据为空");
                    //#endif
                    return;
                }
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"📋 [RestoreSplitConfig] 开始恢复 {regionDataList.Count} 个区域");
                //#endif
                
                // 清空现有数据
                _regionImagePaths.Clear();
                foreach (var image in _regionImages.Values)
                {
                    EditorCanvas.Children.Remove(image);
                }
                _regionImages.Clear();
                
                // 恢复每个区域的图片
                foreach (var regionData in regionDataList)
                {
                    if (string.IsNullOrEmpty(regionData.ImagePath) || !System.IO.File.Exists(regionData.ImagePath))
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"⚠️ [RestoreSplitConfig] 区域 {regionData.RegionIndex} 图片不存在: {regionData.ImagePath}");
                        //#endif
                        continue;
                    }
                    
                    if (regionData.RegionIndex >= _splitRegionBorders.Count)
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"⚠️ [RestoreSplitConfig] 区域索引超出范围: {regionData.RegionIndex}");
                        //#endif
                        continue;
                    }
                    
                    // 🆕 检查图片是否来自原图标记或变色标记的文件夹
                    bool shouldUseStretch = false;
                    bool shouldApplyColorEffect = false;
                    try
                    {
                        var mediaFile = _dbContext.MediaFiles.FirstOrDefault(m => m.Path == regionData.ImagePath);
                        
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"🔍 [RestoreSplitConfig] 区域 {regionData.RegionIndex} 检查图片: {System.IO.Path.GetFileName(regionData.ImagePath)}");
                        //System.Diagnostics.Debug.WriteLine($"   MediaFile找到: {mediaFile != null}");
                        //if (mediaFile != null)
                        //{
                        //    System.Diagnostics.Debug.WriteLine($"   FolderId: {mediaFile.FolderId}");
                        //}
                        #endif
                        
                        if (mediaFile?.FolderId != null)
                        {
                            shouldUseStretch = _originalManager.CheckOriginalMark(
                                Database.Models.Enums.ItemType.Folder,
                                mediaFile.FolderId.Value
                            );

                            // 🎨 检查文件夹是否有变色标记
                            shouldApplyColorEffect = _dbManager.HasFolderAutoColorEffect(mediaFile.FolderId.Value);

                            #if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"   原图标记: {shouldUseStretch}");
                            //System.Diagnostics.Debug.WriteLine($"   变色标记: {shouldApplyColorEffect}");
                            //if (shouldUseStretch)
                            //{
                            //    System.Diagnostics.Debug.WriteLine($"🎯 [RestoreSplitConfig] 区域 {regionData.RegionIndex} 来自原图标记文件夹，使用拉伸模式");
                            //}
                            //if (shouldApplyColorEffect)
                            //{
                            //    System.Diagnostics.Debug.WriteLine($"🎨 [RestoreSplitConfig] 区域 {regionData.RegionIndex} 来自变色标记文件夹，应用变色效果");
                            //}
                            #endif
                        }
                        else
                        {
                            #if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"   未找到MediaFile或FolderId为空");
                            #endif
                        }
                    }
                    catch
                    {
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"❌ [RestoreSplitConfig] 检查标记失败");
                        #endif
                    }
                    
                    // 获取区域边框信息
                    var border = _splitRegionBorders[regionData.RegionIndex];
                    double x = Canvas.GetLeft(border);
                    double y = Canvas.GetTop(border);
                    double width = border.Width;
                    double height = border.Height;
                    
                    // 🚀 使用优化的图片加载（GPU加速 + 缓存）
                    BitmapSource bitmap;

                    // 🎨 如果需要应用变色效果，使用 SkiaSharp 加载并处理
                    if (shouldApplyColorEffect)
                    {
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"🎨 [RestoreSplitConfig] 区域 {regionData.RegionIndex} 开始应用变色效果...");
                        #endif
                        
                        try
                        {
                            using var skBitmap = SkiaSharp.SKBitmap.Decode(regionData.ImagePath);
                            if (skBitmap != null)
                            {
                                #if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"   SKBitmap加载成功，尺寸: {skBitmap.Width}x{skBitmap.Height}");
                                #endif
                                
                                // 应用变色效果
                                _imageProcessor.ApplyYellowTextEffect(skBitmap);

                                #if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"   变色效果已应用");
                                #endif
                                
                                // 转换为 WPF BitmapSource
                                bitmap = _imageProcessor.ConvertToBitmapSource(skBitmap);
                                
                                #if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"✅ [RestoreSplitConfig] 区域 {regionData.RegionIndex} 变色效果应用成功");
                                #endif
                            }
                            else
                            {
                                #if DEBUG
                                //System.Diagnostics.Debug.WriteLine($"❌ [RestoreSplitConfig] SKBitmap加载失败");
                                #endif
                                
                                // 加载失败，使用正常方式
                                var bmp = new BitmapImage();
                                bmp.BeginInit();
                                bmp.UriSource = new Uri(regionData.ImagePath);
                                bmp.CacheOption = BitmapCacheOption.OnLoad;
                                bmp.EndInit();
                                bmp.Freeze();
                                bitmap = bmp;
                            }
                        }
                        catch
                        {
                            #if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"❌ [RestoreSplitConfig] 应用变色效果失败");
                            #endif

                            // 失败时使用正常方式
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.UriSource = new Uri(regionData.ImagePath);
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.EndInit();
                            bmp.Freeze();
                            bitmap = bmp;
                        }
                    }
                    else
                    {
                        #if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"📷 [RestoreSplitConfig] 区域 {regionData.RegionIndex} 正常加载（无变色效果）");
                        #endif
                        
                        // 正常加载（无变色效果）
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(regionData.ImagePath);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        bmp.Freeze(); // 🔥 冻结到GPU显存
                        bitmap = bmp;
                    }
                    
                    // 决定使用的拉伸模式
                    System.Windows.Media.Stretch stretchMode;
                    if (shouldUseStretch)
                    {
                        // 原图标记文件夹：拉伸填满
                        stretchMode = System.Windows.Media.Stretch.Fill;
                    }
                    else if (slide.SplitMode == 0 || slide.SplitMode == 4)
                    {
                        // 单画面模式或三分割模式：默认拉伸填满
                        stretchMode = System.Windows.Media.Stretch.Fill;
                    }
                    else
                    {
                        // 其他分割模式：根据用户设置
                        stretchMode = _splitStretchMode ? 
                            System.Windows.Media.Stretch.Fill : 
                            System.Windows.Media.Stretch.Uniform;
                    }
                    
                    // 创建 Image 控件，应用拉伸模式
                    var imageControl = new System.Windows.Controls.Image
                    {
                        Source = bitmap,
                        Width = width,
                        Height = height,
                        Stretch = stretchMode,
                        Tag = $"RegionImage_{regionData.RegionIndex}",
                        CacheMode = new BitmapCache // 🔥 启用GPU缓存
                        {
                            RenderAtScale = CalculateOptimalRenderScale()  // 🔥 动态计算渲染质量：自适应1080p/2K/4K投影屏
                        }
                    };
                    
                    Canvas.SetLeft(imageControl, x);
                    Canvas.SetTop(imageControl, y);
                    Canvas.SetZIndex(imageControl, 998);
                    
                    // 添加到画布
                    EditorCanvas.Children.Add(imageControl);
                    
                    // 保存引用
                    _regionImages[regionData.RegionIndex] = imageControl;
                    _regionImagePaths[regionData.RegionIndex] = regionData.ImagePath;
                    
                    // 更新边框样式（有图片的区域显示金色）
                    border.Stroke = new SolidColorBrush(WpfColor.FromRgb(255, 215, 0));
                    
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"✅ [RestoreSplitConfig] 已恢复区域 {regionData.RegionIndex}: {System.IO.Path.GetFileName(regionData.ImagePath)}");
                    //#endif
                }
                
                // 🆕 最终同步：检查实际加载的图片拉伸模式，确保按钮显示正确
                if (_regionImages.Count > 0)
                {
                    var firstImage = _regionImages.Values.FirstOrDefault();
                    if (firstImage != null)
                    {
                        bool actualStretchMode = (firstImage.Stretch == System.Windows.Media.Stretch.Fill);
                        if (_splitStretchMode != actualStretchMode)
                        {
                            _splitStretchMode = actualStretchMode;
                            UpdateStretchModeButton();
                        }
                    }
                }
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"✅ [RestoreSplitConfig] 分割配置恢复完成");
                //#endif
            }
            catch
            {
                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($"❌ [RestoreSplitConfig] 失败");
                #endif
            }
        }

        /// <summary>
        /// 导入背景图片
        /// </summary>
        private async void BtnLoadBackgroundImage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTextProject == null || _currentSlide == null)
                return;

            var dialog = new WpfOpenFileDialog
            {
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif",
                Title = "选择背景图"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    BackgroundImage.Source = new BitmapImage(new Uri(dialog.FileName));
                    BackgroundImage.Visibility = Visibility.Visible;
                    EditorCanvas.Background = new SolidColorBrush(Colors.White); // 重置Canvas背景
                    
                    // 🔧 保存背景图路径到当前幻灯片
                    var slideToUpdate = await _dbContext.Slides.FindAsync(_currentSlide.Id);
                    if (slideToUpdate != null)
                    {
                        slideToUpdate.BackgroundImagePath = dialog.FileName;
                        slideToUpdate.BackgroundColor = null; // 清除背景色
                        slideToUpdate.ModifiedTime = DateTime.Now;
                        await _dbContext.SaveChangesAsync();
                        
                        // 更新本地缓存
                        _currentSlide.BackgroundImagePath = dialog.FileName;
                        _currentSlide.BackgroundColor = null;
                    }
                    
                    // 更新项目的背景图片路径（兼容旧数据）
                    await _textProjectManager.UpdateBackgroundImageAsync(_currentTextProject.Id, dialog.FileName);
                    
                    MarkContentAsModified();
                }
                catch (Exception ex)
                {
                    WpfMessageBox.Show($"加载背景图失败: {ex.Message}", "错误", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 选择背景颜色
        /// </summary>
        private async void BtnSelectBackgroundColor_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTextProject == null || _currentSlide == null)
                return;

            // 创建颜色选择对话框
            var colorDialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                Color = System.Drawing.Color.White
            };

            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                try
                {
                    // 转换为WPF颜色
                    var wpfColor = System.Windows.Media.Color.FromArgb(
                        colorDialog.Color.A,
                        colorDialog.Color.R,
                        colorDialog.Color.G,
                        colorDialog.Color.B
                    );

                    // 转换为十六进制字符串
                    var hexColor = $"#{wpfColor.R:X2}{wpfColor.G:X2}{wpfColor.B:X2}";

                    //System.Diagnostics.Debug.WriteLine($"🎨 准备设置背景色: {hexColor}");
                    //System.Diagnostics.Debug.WriteLine($"   EditorCanvas: {EditorCanvas?.Name ?? "null"}");
                    
                    // 设置Canvas背景色
                    EditorCanvas.Background = new SolidColorBrush(wpfColor);
                    
                    //System.Diagnostics.Debug.WriteLine($"   EditorCanvas.Background 已设置: {EditorCanvas.Background}");
                    
                    // 检查父容器背景色
                    var editorParent = EditorCanvas.Parent as FrameworkElement;
                    if (editorParent != null)
                    {
                        //System.Diagnostics.Debug.WriteLine($"   Canvas父容器 ({editorParent.GetType().Name}): Background={editorParent.GetValue(System.Windows.Controls.Panel.BackgroundProperty)}");
                        
                        var grandParent = editorParent.Parent as FrameworkElement;
                        if (grandParent != null)
                        {
                            //System.Diagnostics.Debug.WriteLine($"   祖父容器 ({grandParent.GetType().Name}): Background={grandParent.GetValue(System.Windows.Controls.Panel.BackgroundProperty)}");
                        }
                    }
                    
                    // 隐藏背景图片
                    BackgroundImage.Visibility = Visibility.Collapsed;
                    BackgroundImage.Source = null;
                    
                    // 🔧 保存背景色到当前幻灯片
                    var slideToUpdate = await _dbContext.Slides.FindAsync(_currentSlide.Id);
                    if (slideToUpdate != null)
                    {
                        slideToUpdate.BackgroundColor = hexColor;
                        slideToUpdate.BackgroundImagePath = null; // 清除背景图片
                        slideToUpdate.ModifiedTime = DateTime.Now;
                        await _dbContext.SaveChangesAsync();
                        
                        // 更新本地缓存
                        _currentSlide.BackgroundColor = hexColor;
                        _currentSlide.BackgroundImagePath = null;
                        
                        //System.Diagnostics.Debug.WriteLine($"✅ 背景色已保存到幻灯片: {hexColor}");
                    }
                    
                    // 清除项目的背景图片路径（兼容旧数据）
                    await _textProjectManager.UpdateBackgroundImageAsync(_currentTextProject.Id, null);
                    
                    //System.Diagnostics.Debug.WriteLine($"✅ 背景色设置成功: {hexColor}");
                    MarkContentAsModified();
                }
                catch (Exception ex)
                {
                    //System.Diagnostics.Debug.WriteLine($"❌ 设置背景色失败: {ex.Message}");
                    WpfMessageBox.Show($"设置背景色失败: {ex.Message}", "错误", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 清除背景
        /// </summary>
        private async void BtnClearBackground_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTextProject == null || _currentSlide == null)
                return;

            try
            {
                // 清除背景图片
                BackgroundImage.Source = null;
                BackgroundImage.Visibility = Visibility.Collapsed;
                
                // 重置Canvas背景为白色
                EditorCanvas.Background = new SolidColorBrush(Colors.White);
                
                // 🔧 保存白色背景到当前幻灯片
                var slideToUpdate = await _dbContext.Slides.FindAsync(_currentSlide.Id);
                if (slideToUpdate != null)
                {
                    slideToUpdate.BackgroundColor = "#FFFFFF";
                    slideToUpdate.BackgroundImagePath = null;
                    slideToUpdate.ModifiedTime = DateTime.Now;
                    await _dbContext.SaveChangesAsync();
                    
                    // 更新本地缓存
                    _currentSlide.BackgroundColor = "#FFFFFF";
                    _currentSlide.BackgroundImagePath = null;
                    
                    //System.Diagnostics.Debug.WriteLine("✅ 背景已清除并保存到幻灯片");
                }
                
                // 清除项目的背景图片路径（兼容旧数据）
                await _textProjectManager.UpdateBackgroundImageAsync(_currentTextProject.Id, null);
                
                //System.Diagnostics.Debug.WriteLine("✅ 背景已清除");
                MarkContentAsModified();
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 清除背景失败: {ex.Message}");
                WpfMessageBox.Show($"清除背景失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 字体选择框获得焦点时自动展开下拉列表
        /// </summary>
        private void FontFamilySelector_GotFocus(object sender, RoutedEventArgs e)
        {
            var comboBox = sender as System.Windows.Controls.ComboBox;
            if (comboBox != null && !comboBox.IsDropDownOpen)
            {
                comboBox.IsDropDownOpen = true;
                //System.Diagnostics.Debug.WriteLine($"📖 [字体选择] 自动展开下拉列表");
            }
        }

        /// <summary>
        /// 字体选择改变
        /// </summary>
        private void FontFamily_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_selectedTextBox == null || FontFamilySelector.SelectedItem == null)
                return;

            // 获取选中的字体
            var selectedItem = FontFamilySelector.SelectedItem as ComboBoxItem;
            if (selectedItem != null && selectedItem.Tag is FontItemData fontData)
            {
                // ⚠️ 只保存字体族名称到数据库，不保存完整路径（保证数据可移植性）
                var fontFamilyName = fontData.Config.Family;
                
                // 但是要应用完整的FontFamily对象到TextBox
                _selectedTextBox.Data.FontFamily = fontFamilyName;
                _selectedTextBox.ApplyFontFamily(fontData.FontFamily);
                
                MarkContentAsModified();
                
                //System.Diagnostics.Debug.WriteLine($"✅ 字体已更改: {fontData.Config.Name}");
                //System.Diagnostics.Debug.WriteLine($"   保存到数据库: {fontFamilyName}");
                //System.Diagnostics.Debug.WriteLine($"   应用的FontFamily: {fontData.FontFamily.Source}");
            }
        }

        /// <summary>
        /// 字号输入框文本改变
        /// </summary>
        private void FontSizeInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (FontSizeInput == null || _selectedTextBox == null) return;

            if (int.TryParse(FontSizeInput.Text, out int fontSize))
            {
                // 限制范围 (改为从10开始)
                fontSize = Math.Max(10, Math.Min(200, fontSize));
                
                // 应用到选中的文本框
                _selectedTextBox.ApplyStyle(fontSize: fontSize);
                MarkContentAsModified();
            }
        }

        /// <summary>
        /// 字号输入框预输入验证（只允许数字）
        /// </summary>
        private void FontSizeInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // 只允许数字
            e.Handled = !int.TryParse(e.Text, out _);
        }

        /// <summary>
        /// 字号输入框鼠标滚轮调整
        /// </summary>
        private void FontSizeInput_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (int.TryParse(FontSizeInput.Text, out int currentSize))
            {
                // 滚轮向上增大，向下减小，每次步进1
                int delta = e.Delta > 0 ? 1 : -1;
                int newSize = Math.Max(10, Math.Min(200, currentSize + delta));
                
                FontSizeInput.Text = newSize.ToString();
            }
            
            e.Handled = true;
        }

        /// <summary>
        /// 减小字号按钮
        /// </summary>
        private void BtnDecreaseFontSize_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(FontSizeInput.Text, out int currentSize))
            {
                int newSize = Math.Max(10, currentSize - 1);
                FontSizeInput.Text = newSize.ToString();
            }
        }

        /// <summary>
        /// 增大字号按钮
        /// </summary>
        private void BtnIncreaseFontSize_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(FontSizeInput.Text, out int currentSize))
            {
                int newSize = Math.Min(200, currentSize + 1);
                FontSizeInput.Text = newSize.ToString();
            }
        }

        /// <summary>
        /// 加粗按钮
        /// </summary>
        private void BtnBold_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null)
                return;

            bool isBold = !_selectedTextBox.Data.IsBoldBool;
            _selectedTextBox.ApplyStyle(isBold: isBold);
            
            // 更新加粗按钮状态
            UpdateBoldButtonState(isBold);
            
            MarkContentAsModified(); // 🆕 标记已修改
        }

        /// <summary>
        /// 文字颜色按钮
        /// </summary>
        private void BtnTextColor_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null)
                return;

            // 简化版颜色选择器（使用对话框）
            var dialog = new System.Windows.Forms.ColorDialog
            {
                Color = System.Drawing.ColorTranslator.FromHtml(_currentTextColor)
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _currentTextColor = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
                
                _selectedTextBox.ApplyStyle(color: _currentTextColor);
                MarkContentAsModified(); // 🆕 标记已修改
            }
        }

        /// <summary>
        /// 左对齐按钮
        /// </summary>
        private void BtnAlignLeft_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null) return;
            _selectedTextBox.ApplyStyle(textAlign: "Left");
            MarkContentAsModified(); // 🆕 标记已修改
        }

        /// <summary>
        /// 居中按钮
        /// </summary>
        private void BtnAlignCenter_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null) return;
            _selectedTextBox.ApplyStyle(textAlign: "Center");
            MarkContentAsModified(); // 🆕 标记已修改
        }

        /// <summary>
        /// 右对齐按钮
        /// </summary>
        private void BtnAlignRight_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null) return;
            _selectedTextBox.ApplyStyle(textAlign: "Right");
            MarkContentAsModified(); // 🆕 标记已修改
        }

        /// <summary>
        /// 水平对称按钮
        /// </summary>
        private async void BtnSymmetricH_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null)
            {
                WpfMessageBox.Show("请先选中一个文本框！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                double centerX = EditorCanvas.Width / 2;
                double mirrorX = centerX + (centerX - _selectedTextBox.Data.X - _selectedTextBox.Data.Width);

                // 克隆元素
                var mirrorElement = _textProjectManager.CloneElement(_selectedTextBox.Data);
                mirrorElement.X = mirrorX;
                mirrorElement.IsSymmetricBool = true;
                mirrorElement.SymmetricPairId = _selectedTextBox.Data.Id;
                mirrorElement.SymmetricType = "Horizontal";

                // 保存到数据库
                await _textProjectManager.AddElementAsync(mirrorElement);

                // 添加到画布
                var mirrorBox = new DraggableTextBox(mirrorElement);
                AddTextBoxToCanvas(mirrorBox);

                // 建立联动
                _selectedTextBox.PositionChanged += (s, pos) =>
                {
                    double newMirrorX = centerX + (centerX - pos.X - _selectedTextBox.Data.Width);
                    Canvas.SetLeft(mirrorBox, newMirrorX);
                    mirrorBox.Data.X = newMirrorX;
                };

                //System.Diagnostics.Debug.WriteLine($"✅ 创建水平对称元素成功");
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 创建对称元素失败: {ex.Message}");
                WpfMessageBox.Show($"创建对称元素失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 垂直对称按钮
        /// </summary>
        private async void BtnSymmetricV_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null)
            {
                WpfMessageBox.Show("请先选中一个文本框！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                double centerY = EditorCanvas.Height / 2;
                double mirrorY = centerY + (centerY - _selectedTextBox.Data.Y - _selectedTextBox.Data.Height);

                // 克隆元素
                var mirrorElement = _textProjectManager.CloneElement(_selectedTextBox.Data);
                mirrorElement.Y = mirrorY;
                mirrorElement.IsSymmetricBool = true;
                mirrorElement.SymmetricPairId = _selectedTextBox.Data.Id;
                mirrorElement.SymmetricType = "Vertical";

                // 保存到数据库
                await _textProjectManager.AddElementAsync(mirrorElement);

                // 添加到画布
                var mirrorBox = new DraggableTextBox(mirrorElement);
                AddTextBoxToCanvas(mirrorBox);

                // 建立联动
                _selectedTextBox.PositionChanged += (s, pos) =>
                {
                    double newMirrorY = centerY + (centerY - pos.Y - _selectedTextBox.Data.Height);
                    Canvas.SetTop(mirrorBox, newMirrorY);
                    mirrorBox.Data.Y = newMirrorY;
                };

                //System.Diagnostics.Debug.WriteLine($"✅ 创建垂直对称元素成功");
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 创建对称元素失败: {ex.Message}");
                WpfMessageBox.Show($"创建对称元素失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 保存项目按钮
        /// </summary>
        private async void BtnSaveTextProject_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTextProject == null)
                return;

            try
            {
                //System.Diagnostics.Debug.WriteLine($"💾 [文字保存] 开始保存项目: {_currentTextProject.Name}");
                //System.Diagnostics.Debug.WriteLine($"💾 [文字保存] 文本框数量: {_textBoxes.Count}");
                //System.Diagnostics.Debug.WriteLine($"💾 [文字保存] 投影状态: {(_projectionManager.IsProjectionActive ? "已开启" : "未开启")}");
                
                // 批量更新所有元素
                await _textProjectManager.UpdateElementsAsync(_textBoxes.Select(tb => tb.Data));
                //System.Diagnostics.Debug.WriteLine($"💾 [文字保存] 已更新元素到数据库");

                // 🆕 保存分割区域配置（单画面/分割模式的图片）
                await SaveSplitConfigAsync();
                //System.Diagnostics.Debug.WriteLine($"💾 [文字保存] 已保存分割区域配置");

                // 🆕 生成当前幻灯片的缩略图
                if (_currentSlide != null)
                {
                    var thumbnailPath = SaveSlideThumbnail(_currentSlide.Id);
                    if (!string.IsNullOrEmpty(thumbnailPath))
                    {
                        _currentSlide.ThumbnailPath = thumbnailPath;
                        //System.Diagnostics.Debug.WriteLine($"💾 [文字保存] 已生成缩略图: {thumbnailPath}");
                    }
                }

                // 🆕 保存成功后，恢复按钮为白色
                BtnSaveTextProject.Background = new SolidColorBrush(Colors.White);
                
                // 🆕 刷新幻灯片列表，更新缩略图显示
                RefreshSlideList();
                //System.Diagnostics.Debug.WriteLine($"💾 [文字保存] 已刷新幻灯片列表");
                
                // 🔧 如果投影开启且未锁定，自动更新投影
                if (_projectionManager.IsProjectionActive && !_isProjectionLocked)
                {
                    //System.Diagnostics.Debug.WriteLine($"🔄 [文字保存] 投影已开启，准备自动更新投影...");
                    // 延迟确保UI完全渲染
                    await Task.Delay(100);
                    UpdateProjectionFromCanvas();
                    //System.Diagnostics.Debug.WriteLine($"✅ [文字保存] 已自动更新投影");
                }
                else
                {
                    //System.Diagnostics.Debug.WriteLine($"⚠️ [文字保存] 投影未开启或已锁定，跳过投影更新");
                }
                
                //System.Diagnostics.Debug.WriteLine($"✅ [文字保存] 保存项目成功: {_currentTextProject.Name}");
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [文字保存] 保存项目失败: {ex.Message}");
                #endif
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [文字保存] 堆栈: {ex.StackTrace}");
                #endif
                WpfMessageBox.Show($"保存项目失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 🆕 更新投影按钮（核心功能）
        /// </summary>
        private void BtnUpdateProjection_Click(object sender, RoutedEventArgs e)
        {
            UpdateProjectionFromCanvas();
        }

        #endregion

        #region 画布事件

        /// <summary>
        /// 画布点击（取消选中和退出编辑状态）
        /// </summary>
        private void EditorCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == EditorCanvas || e.OriginalSource == BackgroundImage)
            {
                // 🔧 优化：先检查是否有正在编辑的文本框，如果有则退出编辑状态
                bool hasEditingTextBox = false;
                foreach (var textBox in _textBoxes)
                {
                    if (textBox.IsSelected && textBox.IsInEditMode)
                    {
                        // 使用新的ExitEditMode方法退出编辑状态
                        textBox.ExitEditMode();
                        hasEditingTextBox = true;
                        //System.Diagnostics.Debug.WriteLine("🖱️ 点击画布：退出文本编辑状态");
                    }
                }
                
                // 如果没有正在编辑的文本框，则取消所有选中状态
                if (!hasEditingTextBox)
                {
                    // 取消所有文本框的选中状态
                    foreach (var textBox in _textBoxes)
                    {
                        textBox.SetSelected(false);
                    }
                    _selectedTextBox = null;
                    
                    // 清除焦点
                    Keyboard.ClearFocus();
                    EditorCanvas.Focus();
                    //System.Diagnostics.Debug.WriteLine("🖱️ 点击画布：取消所有选中状态");
                }
            }
        }
        
        /// <summary>
        /// 画布键盘事件（处理DEL快捷键）
        /// </summary>
        private async void EditorCanvas_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"🎹 [EditorCanvas_KeyDown] 按键: {e.Key}");
            //System.Diagnostics.Debug.WriteLine($"   IsInSplitMode: {IsInSplitMode()}");
            //System.Diagnostics.Debug.WriteLine($"   _selectedRegionIndex: {_selectedRegionIndex}");
            //System.Diagnostics.Debug.WriteLine($"   _regionImages.Count: {_regionImages.Count}");
            //System.Diagnostics.Debug.WriteLine($"   包含选中区域图片: {_regionImages.ContainsKey(_selectedRegionIndex)}");
            //#endif
            
            // DEL键：只清除选中区域的图片（仅在分割模式下且有图片时）
            if (e.Key == System.Windows.Input.Key.Delete)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"🎹 [DEL键] 检测到 Delete 键");
                //#endif
                
                if (IsInSplitMode())
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"✅ [DEL键] 在分割模式下");
                    //#endif
                    
                    if (_selectedRegionIndex >= 0)
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"✅ [DEL键] 有选中区域: {_selectedRegionIndex}");
                        //#endif
                        
                        if (_regionImages.ContainsKey(_selectedRegionIndex))
                        {
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"✅ [DEL键] 区域有图片，执行清空");
                            //#endif
                            
                            await ClearSelectedRegionImage();
                            e.Handled = true;
                        }
                        //#if DEBUG
                        //else
                        //{
                        //    System.Diagnostics.Debug.WriteLine($"⚠️ [DEL键] 区域没有图片");
                        //}
                        //#endif
                    }
                    //#if DEBUG
                    //else
                    //{
                    //    System.Diagnostics.Debug.WriteLine($"⚠️ [DEL键] 没有选中区域");
                    //}
                    //#endif
                }
                //#if DEBUG
                //else
                //{
                //    System.Diagnostics.Debug.WriteLine($"⚠️ [DEL键] 不在分割模式");
                //}
                //#endif
            }
        }

        #endregion

        #region 辅助方法


        /// <summary>
        /// 将文本框添加到画布
        /// </summary>
        private void AddTextBoxToCanvas(DraggableTextBox textBox)
        {
            _textBoxes.Add(textBox);
            EditorCanvas.Children.Add(textBox);

            // 监听选中事件
            textBox.SelectionChanged += (s, isSelected) =>
            {
                if (isSelected)
                {
                    // 取消其他文本框的选中状态
                    foreach (var tb in _textBoxes)
                    {
                        if (tb != textBox && tb.IsSelected)
                        {
                            tb.SetSelected(false);
                        }
                    }
                    _selectedTextBox = textBox;

                    // 更新工具栏状态
                    UpdateToolbarFromSelection();
                }
            };

            // 🆕 监听内容变化，保存按钮变绿色
            textBox.ContentChanged += (s, content) =>
            {
                MarkContentAsModified();
                //System.Diagnostics.Debug.WriteLine($"文本内容改变: {content}");
            };
            
            // 🆕 监听位置变化，显示辅助线并保存
            textBox.PositionChanged += (s, pos) =>
            {
                UpdateAlignmentGuides(textBox);
                MarkContentAsModified();
            };
            
            // 🆕 监听拖动结束，隐藏辅助线
            textBox.DragEnded += (s, e) =>
            {
                HideAlignmentGuides();
            };
            
            // 🆕 监听尺寸变化，保存按钮变绿色
            textBox.SizeChanged += (s, size) =>
            {
                MarkContentAsModified();
            };
            
            // 🆕 监听删除请求（右键菜单或DEL键）
            textBox.RequestDelete += async (s, e) =>
            {
                await DeleteTextBoxAsync(textBox);
            };

            // 🆕 监听复制请求（右键菜单 - 立即复制创建新文本框）
            textBox.RequestCopy += async (s, e) =>
            {
                await CopyTextBoxAsync(textBox);
            };
        }

        /// <summary>
        /// 🆕 标记内容已修改（保存按钮变绿）
        /// </summary>
        private void MarkContentAsModified()
        {
            if (BtnSaveTextProject.Background is SolidColorBrush brush && brush.Color == Colors.LightGreen)
                return; // 已经是绿色，不重复设置

            BtnSaveTextProject.Background = new SolidColorBrush(Colors.LightGreen);
            //System.Diagnostics.Debug.WriteLine("🟢 内容已修改，保存按钮变绿");
        }

        /// <summary>
        /// 根据选中的文本框更新工具栏状态
        /// </summary>
        private void UpdateToolbarFromSelection()
        {
            if (_selectedTextBox == null) return;

            // 更新字体选择器
            var fontFamily = _selectedTextBox.Data.FontFamily;
            // System.Diagnostics.Debug.WriteLine($"🔍 同步字体选择器: {fontFamily}");
            
            for (int i = 0; i < FontFamilySelector.Items.Count; i++)
            {
                var item = FontFamilySelector.Items[i] as ComboBoxItem;
                if (item?.Tag is FontItemData fontData)
                {
                    // 匹配字体：可能是完整URI，也可能是简单的字体族名称
                    var fontSource = fontData.FontFamily.Source;
                    
                    // 情况1：完全匹配（新格式：完整URI）
                    if (fontSource == fontFamily)
                    {
                        FontFamilySelector.SelectedIndex = i;
                        //System.Diagnostics.Debug.WriteLine($"✅ 找到匹配字体（完整URI）: {fontData.Config.Name}");
                        break;
                    }
                    
                    // 情况2：旧数据格式匹配（只有字体族名称）
                    if (fontData.Config.Family == fontFamily)
                    {
                        FontFamilySelector.SelectedIndex = i;
                        //System.Diagnostics.Debug.WriteLine($"✅ 找到匹配字体（族名称）: {fontData.Config.Name}");
                        
                        // 🔧 自动修复：更新文本框的字体为完整URI
                        _selectedTextBox.Data.FontFamily = fontSource;
                        //System.Diagnostics.Debug.WriteLine($"🔧 自动修复字体URI: {fontSource}");
                        break;
                    }
                }
            }

            // 更新字号输入框
            FontSizeInput.Text = _selectedTextBox.Data.FontSize.ToString();

            // 保持用户最后一次设置的颜色
            if (string.IsNullOrEmpty(_currentTextColor))
            {
                _currentTextColor = _selectedTextBox.Data.FontColor;
            }
            
            // 更新加粗按钮状态
            UpdateBoldButtonState(_selectedTextBox.Data.IsBoldBool);
        }
        
        /// <summary>
        /// 更新加粗按钮状态
        /// </summary>
        private void UpdateBoldButtonState(bool isBold)
        {
            if (isBold)
            {
                // 加粗状态：按钮背景变为蓝色
                BtnBold.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243)); // 蓝色
                BtnBold.Foreground = new SolidColorBrush(Colors.White);
            }
            else
            {
                // 非加粗状态：恢复默认样式
                BtnBold.Background = new SolidColorBrush(Colors.White);
                BtnBold.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51));
            }
        }
        
        /// <summary>
        /// 查找可视化树中的子元素
        /// </summary>
        private T FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (parent == null) return null;

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                if (child is T typedChild && (string.IsNullOrEmpty(name) || typedChild.Name == name))
                {
                    return typedChild;
                }
                
                var result = FindVisualChild<T>(child, name);
                if (result != null)
                    return result;
            }
            
            return null;
        }

        /// <summary>
        /// 生成Canvas渲染缓存键（基于所有区域图片路径、文本框内容、背景色和背景图）
        /// </summary>
        private string GenerateCanvasCacheKey()
        {
            // 图片路径部分
            var imagePart = string.Join("|", _regionImagePaths.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"));
            
            // 文本框内容部分（包括内容、位置、尺寸、样式等所有影响渲染的属性）
            var textPart = string.Join("|", _textBoxes.Select(tb => 
                $"{tb.Data.Content}_{tb.Data.X}_{tb.Data.Y}_{tb.Data.Width}_{tb.Data.Height}_{tb.Data.FontSize}_{tb.Data.FontFamily}_{tb.Data.FontColor}_{tb.Data.IsBold}_{tb.Data.TextAlign}_{tb.Data.ZIndex}"));
            
            // 🎨 背景色和背景图部分（确保背景变化时缓存失效）
            var bgColor = _currentSlide?.BackgroundColor ?? "";
            var bgImage = _currentSlide?.BackgroundImagePath ?? "";
            
            return $"{imagePart}#{textPart}#{_currentSlide?.SplitMode}#{_splitStretchMode}#{bgColor}#{bgImage}";
        }
        
        /// <summary>
        /// 清除Canvas渲染缓存（在Canvas内容发生变化时调用）
        /// </summary>
        private void ClearCanvasRenderCache()
        {
            _lastCanvasRenderCache?.Dispose();
            _lastCanvasRenderCache = null;
            _lastCanvasCacheKey = "";
            
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"🗑️ [缓存] Canvas渲染缓存已清除");
            #endif
        }
        
        /// <summary>
        /// 🆕 从Canvas更新投影（核心投影功能）
        /// 🚀 优化：添加缓存机制和节流控制
        /// </summary>
        private void UpdateProjectionFromCanvas()
        {
            //System.Diagnostics.Debug.WriteLine($"🎨 [更新投影] ===== 开始更新投影 =====");
            //System.Diagnostics.Debug.WriteLine($"🎨 [更新投影] 投影状态: {(_projectionManager.IsProjectionActive ? "已开启" : "未开启")}");
            
            if (!_projectionManager.IsProjectionActive)
            {
                //System.Diagnostics.Debug.WriteLine("⚠️ [更新投影] 投影未开启，无法更新投影内容");
                WpfMessageBox.Show("请先开启投影！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 🚀 优化1：渲染节流 - 避免过于频繁的更新
            var now = DateTime.Now;
            if ((now - _lastCanvasUpdateTime).TotalMilliseconds < CanvasUpdateThrottleMs)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"⚡ [更新投影] 节流跳过 (距上次 {(now - _lastCanvasUpdateTime).TotalMilliseconds:F0}ms)");
                //#endif
                return;
            }
            _lastCanvasUpdateTime = now;
            
            // 🚀 优化2：缓存检查 - 如果Canvas内容没变，直接复用上次的渲染结果
            string cacheKey = GenerateCanvasCacheKey();
            if (cacheKey == _lastCanvasCacheKey && _lastCanvasRenderCache != null)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"⚡ [更新投影] 缓存命中，直接复用");
                //#endif
                _projectionManager.UpdateProjectionText(_lastCanvasRenderCache);
                return;
            }
            
            //#if DEBUG
            //var totalSw = System.Diagnostics.Stopwatch.StartNew();
            //System.Diagnostics.Debug.WriteLine($"🎨 [更新投影] 缓存未命中，开始完整渲染");
            //#endif

            // 🔧 保存辅助线的可见性状态
            var guidesVisibility = AlignmentGuidesCanvas.Visibility;
            
            try
            {
                // 🔧 渲染前：隐藏辅助线，避免被渲染到投影中
                AlignmentGuidesCanvas.Visibility = Visibility.Collapsed;
                
                // 🔧 渲染前：隐藏分割线和边框，避免被渲染到投影中
                HideSplitLinesForProjection();
                
                // 🎨 渲染前：隐藏所有文本框的装饰元素（边框、拖拽手柄等）
                foreach (var textBox in _textBoxes)
                {
                    textBox.HideDecorations();
                }
                
                // 1. 渲染EditorCanvasContainer（只包含Canvas和背景图，不包含辅助线）
                if (EditorCanvasContainer == null)
                {
                    return;
                }
                
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"🎨 [Canvas信息] 尺寸: {EditorCanvas.ActualWidth}×{EditorCanvas.ActualHeight}");
                //System.Diagnostics.Debug.WriteLine($"🎨 [Canvas信息] 子元素数量: {EditorCanvas.Children.Count}");
                //System.Diagnostics.Debug.WriteLine($"🎨 [Canvas信息] 区域图片: {_regionImages.Count}");
                //System.Diagnostics.Debug.WriteLine($"🎨 [Canvas信息] 文本框: {_textBoxes.Count}");
                //#endif
                
                // 🚀 新方案：直接用SkiaSharp合成Canvas，完全跳过RenderTargetBitmap！
                // 2. 🚀 直接按投影物理像素分辨率合成Canvas内容（最高质量，避免二次缩放）
                //#if DEBUG
                //var composeSw = System.Diagnostics.Stopwatch.StartNew();
                //#endif
                
                // 🎯 使用物理像素分辨率（而非WPF单位），获得最高质量
                var (projWidth, projHeight) = _projectionManager?.GetCurrentProjectionPhysicalSize() ?? (1920, 1080);
                var finalImage = ComposeCanvasWithSkia(projWidth, projHeight);
                
                //#if DEBUG
                //composeSw.Stop();
                //System.Diagnostics.Debug.WriteLine($"⏱️ [性能] ComposeCanvasWithSkia (物理像素分辨率): {composeSw.ElapsedMilliseconds}ms ({finalImage.Width}×{finalImage.Height})");
                //#endif

                // 4. 更新投影（使用专用的文字投影方法，语义清晰）
                //#if DEBUG
                //var updateSw = System.Diagnostics.Stopwatch.StartNew();
                //#endif
                
                _projectionManager.UpdateProjectionText(finalImage);
                
                //#if DEBUG
                //updateSw.Stop();
                //System.Diagnostics.Debug.WriteLine($"⏱️ [性能] UpdateProjectionText: {updateSw.ElapsedMilliseconds}ms");
                //#endif

                // 🚀 优化3：保存渲染结果到缓存
                _lastCanvasRenderCache?.Dispose(); // 释放旧缓存
                _lastCanvasRenderCache = finalImage;
                _lastCanvasCacheKey = cacheKey;

                //#if DEBUG
                //totalSw.Stop();
                //System.Diagnostics.Debug.WriteLine($"✅ [性能] 总耗时: {totalSw.ElapsedMilliseconds}ms");
                //#endif
            }
            catch (Exception ex)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"❌ [更新投影] 更新投影失败: {ex.Message}");
                //#endif
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"❌ [更新投影] 堆栈: {ex.StackTrace}");
                //#endif
                WpfMessageBox.Show($"更新投影失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // 🎨 渲染后：恢复所有文本框的装饰元素
                foreach (var textBox in _textBoxes)
                {
                    textBox.RestoreDecorations();
                }
                
                // 🔧 确保恢复辅助线的可见性（无论成功还是失败）
                AlignmentGuidesCanvas.Visibility = guidesVisibility;
                //System.Diagnostics.Debug.WriteLine($"🎨 [更新投影] 已恢复辅助线状态");
                
                // 🔧 恢复分割线和边框显示
                RestoreSplitLinesAfterProjection();
                
                //System.Diagnostics.Debug.WriteLine($"🎨 [更新投影] ===== 更新投影结束 =====");
            }
        }

        /// <summary>
        /// 使用SkiaSharp直接合成Canvas内容（跳过WPF的RenderTargetBitmap）
        /// 🚀 核心优化：直接访问Image控件的Source，避免WPF渲染管道
        /// </summary>
        /// <param name="targetWidth">目标宽度（0表示使用Canvas实际宽度）</param>
        /// <param name="targetHeight">目标高度（0表示使用Canvas实际高度）</param>
        private SKBitmap ComposeCanvasWithSkia(int targetWidth = 0, int targetHeight = 0)
        {
            // 编辑器画布的实际尺寸（用于计算缩放比例）
            double canvasWidth = EditorCanvas.ActualWidth;
            double canvasHeight = EditorCanvas.ActualHeight;
            
            // 如果没有指定目标尺寸，使用Canvas实际尺寸
            if (targetWidth <= 0) targetWidth = (int)canvasWidth;
            if (targetHeight <= 0) targetHeight = (int)canvasHeight;
            
            // 计算缩放比例
            double scaleX = targetWidth / canvasWidth;
            double scaleY = targetHeight / canvasHeight;
            
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"  [Compose] 画布尺寸: 原始={canvasWidth}×{canvasHeight}, 目标={targetWidth}×{targetHeight}, 缩放={scaleX:F2}×{scaleY:F2}");
            #endif
            
            // 创建SkiaSharp画布（使用目标尺寸）
            var bitmap = new SKBitmap(targetWidth, targetHeight);
            using (var canvas = new SKCanvas(bitmap))
            {
                // 🎨 使用幻灯片设置的背景色
                SKColor backgroundColor = SKColors.Black; // 默认黑色
                if (_currentSlide != null && !string.IsNullOrEmpty(_currentSlide.BackgroundColor))
                {
                    try
                    {
                        // 解析十六进制颜色（如 #FFFFFF）
                        string hexColor = _currentSlide.BackgroundColor.TrimStart('#');
                        if (hexColor.Length == 6)
                        {
                            byte r = Convert.ToByte(hexColor.Substring(0, 2), 16);
                            byte g = Convert.ToByte(hexColor.Substring(2, 2), 16);
                            byte b = Convert.ToByte(hexColor.Substring(4, 2), 16);
                            backgroundColor = new SKColor(r, g, b);
                            
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"  [Compose] 背景色: {_currentSlide.BackgroundColor} -> RGB({r},{g},{b})");
                            //#endif
                        }
                    }
                    catch
                    {
                        // 解析失败，使用默认黑色
                    }
                }
                
                canvas.Clear(backgroundColor);
                
                // 🎨 应用缩放变换，一次性将所有内容缩放到目标尺寸
                canvas.Scale((float)scaleX, (float)scaleY);
                
                //#if DEBUG
                //createSw.Stop();
                //System.Diagnostics.Debug.WriteLine($"  [Compose] 创建画布: {createSw.ElapsedMilliseconds}ms");
                //#endif
                
                // 🎨 绘制背景图（如果有）
                if (_currentSlide != null && !string.IsNullOrEmpty(_currentSlide.BackgroundImagePath) &&
                    System.IO.File.Exists(_currentSlide.BackgroundImagePath))
                {
                    try
                    {
                        //#if DEBUG
                        //var bgSw = System.Diagnostics.Stopwatch.StartNew();
                        //#endif
                        
                        // 加载背景图
                        var bgBitmap = SKBitmap.Decode(_currentSlide.BackgroundImagePath);
                        if (bgBitmap != null)
                        {
                            // 绘制背景图，铺满整个画布
                            var destRect = new SKRect(0, 0, (float)canvasWidth, (float)canvasHeight);
                            var paint = new SKPaint
                            {
                                FilterQuality = SKFilterQuality.High,
                                IsAntialias = true
                            };
                            canvas.DrawBitmap(bgBitmap, destRect, paint);
                            paint.Dispose();
                            bgBitmap.Dispose();
                            
                            //#if DEBUG
                            //bgSw.Stop();
                            //System.Diagnostics.Debug.WriteLine($"  [Compose] 背景图绘制: {_currentSlide.BackgroundImagePath}, 耗时: {bgSw.ElapsedMilliseconds}ms");
                            //#endif
                        }
                    }
                    catch
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"  [Compose] 背景图加载失败: {ex.Message}");
                        //#endif
                    }
                }
                
                // 绘制所有区域图片
                foreach (var kvp in _regionImages)
                {
                    var imageControl = kvp.Value;
                    int regionIndex = kvp.Key;
                    
                    //#if DEBUG
                    //var imgSw = System.Diagnostics.Stopwatch.StartNew();
                    //#endif
                    
                    // 🔧 关键修复：获取Image控件的位置和边框的尺寸
                    // Image控件的ActualWidth/Height在Uniform模式下会小于设置的Width/Height
                    // 应该使用边框的尺寸作为控件区域，才能正确计算居中位置
                    double left = Canvas.GetLeft(imageControl);
                    double top = Canvas.GetTop(imageControl);
                    double width = _splitRegionBorders[regionIndex].Width;  // 使用边框宽度，不是Image的ActualWidth
                    double height = _splitRegionBorders[regionIndex].Height; // 使用边框高度，不是Image的ActualHeight
                    
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"🔍 [Compose] 区域 {regionIndex} - Image控件位置: ({left}, {top}), 尺寸: {width}×{height}, Stretch: {imageControl.Stretch}");
                    System.Diagnostics.Debug.WriteLine($"    边框信息: 位置=({Canvas.GetLeft(_splitRegionBorders[regionIndex])}, {Canvas.GetTop(_splitRegionBorders[regionIndex])}), 尺寸={_splitRegionBorders[regionIndex].Width}×{_splitRegionBorders[regionIndex].Height}");
                    #endif
                    
                    SKBitmap skBitmap = null;
                    
                    // 🎯 优先从原始文件加载高质量图片
                    if (_regionImagePaths.ContainsKey(regionIndex) && 
                        System.IO.File.Exists(_regionImagePaths[regionIndex]))
                    {
                        try
                        {
                            string imagePath = _regionImagePaths[regionIndex];
                            skBitmap = SKBitmap.Decode(imagePath);
                            
                            #if DEBUG
                            System.Diagnostics.Debug.WriteLine($"🔍 [Compose] 区域 {regionIndex} - 原始图片尺寸: {skBitmap.Width}×{skBitmap.Height}");
                            #endif
                            
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($"  [Compose] 处理图片 {regionIndex}: 从原始文件加载 {skBitmap.Width}×{skBitmap.Height}, 位置: ({left}, {top}), 显示: {width}×{height}");
                            //#endif
                        }
                        catch
                        {
                            // 加载失败，回退到BitmapSource
                            skBitmap = null;
                        }
                    }
                    
                    // 回退方案：从WPF控件的BitmapSource转换
                    if (skBitmap == null && imageControl?.Source is BitmapSource bitmapSource)
                    {
                        skBitmap = ConvertBitmapSourceToSKBitmap(bitmapSource);
                        
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"  [Compose] 处理图片 {regionIndex}: 从BitmapSource转换 {bitmapSource.PixelWidth}×{bitmapSource.PixelHeight}, 位置: ({left}, {top}), 显示: {width}×{height}");
                        //#endif
                    }
                    
                    if (skBitmap != null)
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"  [Compose] 加载耗时: {imgSw.ElapsedMilliseconds}ms");
                        //imgSw.Restart();
                        //#endif
                        
                        // 🔧 关键修复：根据Image控件的Stretch属性计算实际绘制区域
                        SKRect destRect;
                        if (imageControl.Stretch == System.Windows.Media.Stretch.Uniform)
                        {
                            // Uniform模式：保持比例，居中显示
                            double imageAspect = (double)skBitmap.Width / skBitmap.Height;
                            double controlAspect = width / height;
                            
                            double drawWidth, drawHeight;
                            double drawLeft, drawTop;
                            
                            double aspectDiff = Math.Abs(imageAspect - controlAspect);
                            if (aspectDiff < 0.001)
                            {
                                // 宽高比几乎相等：填满整个控件区域
                                drawWidth = width;
                                drawHeight = height;
                                drawLeft = left;
                                drawTop = top;
                            }
                            else if (imageAspect > controlAspect)
                            {
                                // 图片更宽（更扁），以宽度为准，垂直居中
                                drawWidth = width;
                                drawHeight = width / imageAspect;
                                drawLeft = left;
                                drawTop = top + (height - drawHeight) / 2; // 垂直居中
                            }
                            else
                            {
                                // 图片更高（更瘦），以高度为准，水平居中
                                drawHeight = height;
                                drawWidth = height * imageAspect;
                                drawLeft = left + (width - drawWidth) / 2; // 水平居中
                                drawTop = top + (height - drawHeight) / 2; // 垂直也居中！
                            }
                            
                            destRect = new SKRect((float)drawLeft, (float)drawTop, 
                                                   (float)(drawLeft + drawWidth), (float)(drawTop + drawHeight));
                            
                            #if DEBUG
                            System.Diagnostics.Debug.WriteLine($"🔍 [Compose] 区域 {regionIndex} - Uniform模式计算:");
                            System.Diagnostics.Debug.WriteLine($"    图片宽高比: {imageAspect:F3}, 控件宽高比: {controlAspect:F3}");
                            System.Diagnostics.Debug.WriteLine($"    绘制位置: ({drawLeft:F1}, {drawTop:F1}), 绘制尺寸: {drawWidth:F1}×{drawHeight:F1}");
                            System.Diagnostics.Debug.WriteLine($"    destRect: Left={destRect.Left:F1}, Top={destRect.Top:F1}, Right={destRect.Right:F1}, Bottom={destRect.Bottom:F1}");
                            #endif
                        }
                        else
                        {
                            // Fill模式：拉伸填满整个控件区域
                            destRect = new SKRect((float)left, (float)top, 
                                                   (float)(left + width), (float)(top + height));
                            
                            #if DEBUG
                            System.Diagnostics.Debug.WriteLine($"🔍 [Compose] 区域 {regionIndex} - Fill模式: 直接填满控件区域");
                            #endif
                        }
                        
                        // 🎨 使用高质量过滤模式，确保投影质量
                        var paint = new SKPaint
                        {
                            FilterQuality = SKFilterQuality.High,
                            IsAntialias = true
                        };
                        canvas.DrawBitmap(skBitmap, destRect, paint);
                        paint.Dispose();
                        
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($"  [Compose] 绘制耗时: {imgSw.ElapsedMilliseconds}ms");
                        //#endif
                        
                        skBitmap.Dispose();
                    }
                }
                
                // 绘制所有文本框
                foreach (var textBox in _textBoxes)
                {
                    //#if DEBUG
                    //var textSw = System.Diagnostics.Stopwatch.StartNew();
                    //System.Diagnostics.Debug.WriteLine($"  [Compose] 文本框位置: ({textBox.Data.X}, {textBox.Data.Y}), 尺寸: {textBox.Data.Width}×{textBox.Data.Height}");
                    //#endif
                    
                    DrawTextBoxToCanvas(canvas, textBox);
                    
                    //#if DEBUG
                    //textSw.Stop();
                    //System.Diagnostics.Debug.WriteLine($"  [Compose] 绘制文本框: {textSw.ElapsedMilliseconds}ms");
                    //#endif
                }
                
                // 🎨 绘制分割线（如果有分割模式）
                if (_currentSlide != null && _currentSlide.SplitMode >= 0)
                {
                    DrawSplitLinesToCanvas(canvas, (Database.Models.Enums.ViewSplitMode)_currentSlide.SplitMode, canvasWidth, canvasHeight);
                }
            }
            
            return bitmap;
        }
        
        /// <summary>
        /// 在SkiaSharp画布上绘制分割线和角标（匹配投影样式：细实线）
        /// </summary>
        private void DrawSplitLinesToCanvas(SKCanvas canvas, Database.Models.Enums.ViewSplitMode mode, double canvasWidth, double canvasHeight)
        {
            // 分割线画笔（橙色细实线，1像素 - 匹配投影前的调整）
            var linePaint = new SKPaint
            {
                Color = new SKColor(255, 165, 0), // 橙色 RGB(255, 165, 0)
                StrokeWidth = 1,                   // 细线1px（投影样式）
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
                // 不使用虚线，投影时用实线
            };
            
            // 角标背景画笔（半透明橙色）
            var labelBgPaint = new SKPaint
            {
                Color = new SKColor(255, 102, 0, 200), // ARGB(200, 255, 102, 0)
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            
            // 角标文字画笔（白色粗体）
            var labelTextPaint = new SKPaint
            {
                Color = SKColors.White,
                TextSize = 18,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
            };
            
            switch (mode)
            {
                case Database.Models.Enums.ViewSplitMode.Single:
                    // 单画面：不绘制分割线和角标
                    break;
                    
                case Database.Models.Enums.ViewSplitMode.Horizontal:
                    // 左右分割：绘制竖线
                    canvas.DrawLine((float)(canvasWidth / 2), 0, (float)(canvasWidth / 2), (float)canvasHeight, linePaint);
                    // 角标（只显示已加载图片的区域）
                    if (_regionImages.ContainsKey(0)) DrawLabel(canvas, "1", 0, 0, labelBgPaint, labelTextPaint);
                    if (_regionImages.ContainsKey(1)) DrawLabel(canvas, "2", (float)(canvasWidth / 2), 0, labelBgPaint, labelTextPaint);
                    break;
                    
                case Database.Models.Enums.ViewSplitMode.Vertical:
                    // 上下分割：绘制横线
                    canvas.DrawLine(0, (float)(canvasHeight / 2), (float)canvasWidth, (float)(canvasHeight / 2), linePaint);
                    // 角标（只显示已加载图片的区域）
                    if (_regionImages.ContainsKey(0)) DrawLabel(canvas, "1", 0, 0, labelBgPaint, labelTextPaint);
                    if (_regionImages.ContainsKey(1)) DrawLabel(canvas, "2", 0, (float)(canvasHeight / 2), labelBgPaint, labelTextPaint);
                    break;
                    
                case Database.Models.Enums.ViewSplitMode.Quad:
                    // 四宫格：绘制十字线
                    canvas.DrawLine((float)(canvasWidth / 2), 0, (float)(canvasWidth / 2), (float)canvasHeight, linePaint);
                    canvas.DrawLine(0, (float)(canvasHeight / 2), (float)canvasWidth, (float)(canvasHeight / 2), linePaint);
                    // 角标（只显示已加载图片的区域）
                    if (_regionImages.ContainsKey(0)) DrawLabel(canvas, "1", 0, 0, labelBgPaint, labelTextPaint);
                    if (_regionImages.ContainsKey(1)) DrawLabel(canvas, "2", (float)(canvasWidth / 2), 0, labelBgPaint, labelTextPaint);
                    if (_regionImages.ContainsKey(2)) DrawLabel(canvas, "3", 0, (float)(canvasHeight / 2), labelBgPaint, labelTextPaint);
                    if (_regionImages.ContainsKey(3)) DrawLabel(canvas, "4", (float)(canvasWidth / 2), (float)(canvasHeight / 2), labelBgPaint, labelTextPaint);
                    break;
                    
                case Database.Models.Enums.ViewSplitMode.TripleSplit:
                    // 三分割：左边上下分割，右边整个
                    canvas.DrawLine((float)(canvasWidth / 2), 0, (float)(canvasWidth / 2), (float)canvasHeight, linePaint);
                    canvas.DrawLine(0, (float)(canvasHeight / 2), (float)(canvasWidth / 2), (float)(canvasHeight / 2), linePaint);
                    // 角标（只显示已加载图片的区域）
                    if (_regionImages.ContainsKey(0)) DrawLabel(canvas, "1", 0, 0, labelBgPaint, labelTextPaint);
                    if (_regionImages.ContainsKey(1)) DrawLabel(canvas, "2", 0, (float)(canvasHeight / 2), labelBgPaint, labelTextPaint);
                    if (_regionImages.ContainsKey(2)) DrawLabel(canvas, "3", (float)(canvasWidth / 2), 0, labelBgPaint, labelTextPaint);
                    break;
            }
            
            linePaint.Dispose();
            labelBgPaint.Dispose();
            labelTextPaint.Dispose();
        }
        
        /// <summary>
        /// 绘制角标（带圆角背景的数字标签）
        /// </summary>
        private void DrawLabel(SKCanvas canvas, string text, float x, float y, SKPaint bgPaint, SKPaint textPaint)
        {
            // 测量文本尺寸
            var textBounds = new SKRect();
            textPaint.MeasureText(text, ref textBounds);
            
            // 标签尺寸（padding: 8, 4, 8, 4）
            float padding = 8;
            float labelWidth = textBounds.Width + padding * 2;
            float labelHeight = textBounds.Height + 8; // 上下padding各4
            
            // 绘制圆角矩形背景（右下圆角）
            var path = new SKPath();
            var rect = new SKRect(x, y, x + labelWidth, y + labelHeight);
            float cornerRadius = 8;
            
            // 创建右下圆角的路径
            path.MoveTo(rect.Left, rect.Top);
            path.LineTo(rect.Right, rect.Top);
            path.LineTo(rect.Right, rect.Bottom - cornerRadius);
            path.ArcTo(new SKRect(rect.Right - cornerRadius, rect.Bottom - cornerRadius, rect.Right, rect.Bottom), 0, 90, false);
            path.LineTo(rect.Left, rect.Bottom);
            path.Close();
            
            canvas.DrawPath(path, bgPaint);
            path.Dispose();
            
            // 绘制文本（居中）
            float textX = x + padding;
            float textY = y + labelHeight - 4 - textBounds.Bottom; // 垂直居中
            canvas.DrawText(text, textX, textY, textPaint);
        }
        
        /// <summary>
        /// 将文本框绘制到SkiaSharp画布上
        /// </summary>
        private void DrawTextBoxToCanvas(SKCanvas canvas, DraggableTextBox textBox)
        {
            var data = textBox.Data;
            
            // 🔧 获取文本框在Canvas上的实际位置（而不是Data中的值）
            double actualLeft = Canvas.GetLeft(textBox);
            double actualTop = Canvas.GetTop(textBox);
            double actualWidth = textBox.ActualWidth;
            double actualHeight = textBox.ActualHeight;
            
            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"  [文本框] 内容: '{data.Content}', 字体: {data.FontFamily}, 大小: {data.FontSize}, 加粗: {data.IsBoldBool}, 颜色: {data.FontColor}");
            //System.Diagnostics.Debug.WriteLine($"  [文本框] 位置: ({actualLeft}, {actualTop}), 尺寸: {actualWidth}×{actualHeight}");
            //#endif
            
            // 处理NaN的情况
            if (double.IsNaN(actualLeft)) actualLeft = data.X;
            if (double.IsNaN(actualTop)) actualTop = data.Y;
            if (actualWidth <= 0) actualWidth = data.Width;
            if (actualHeight <= 0) actualHeight = data.Height;
            
            // 🔧 使用VisualBrush渲染，避免破坏Canvas上的控件布局
            try
            {
                int width = (int)Math.Ceiling(actualWidth);
                int height = (int)Math.Ceiling(actualHeight);
                
                if (width > 0 && height > 0)
                {
                    // 🔧 渲染前强制更新文本框的布局（确保样式已应用）
                    textBox.Measure(new System.Windows.Size(actualWidth, actualHeight));
                    textBox.Arrange(new Rect(actualLeft, actualTop, actualWidth, actualHeight));
                    textBox.UpdateLayout();
                    
                    // 🔧 关键：使用VisualBrush创建文本框的视觉副本，不影响原控件
                    var visualBrush = new System.Windows.Media.VisualBrush(textBox)
                    {
                        Stretch = System.Windows.Media.Stretch.None,
                        AlignmentX = System.Windows.Media.AlignmentX.Left,
                        AlignmentY = System.Windows.Media.AlignmentY.Top
                    };
                    
                    // 创建临时容器来承载VisualBrush
                    var container = new System.Windows.Shapes.Rectangle
                    {
                        Width = actualWidth,
                        Height = actualHeight,
                        Fill = visualBrush
                    };
                    
                    // 布局临时容器（不影响原textBox）
                    container.Measure(new System.Windows.Size(actualWidth, actualHeight));
                    container.Arrange(new Rect(0, 0, actualWidth, actualHeight));
                    container.UpdateLayout();
                    
                    // 🎯 渲染临时容器（使用96 DPI，让画布的缩放变换来提升质量）
                    var renderBitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    renderBitmap.Render(container);
                    
                    // 转换为SKBitmap
                    var skBitmap = ConvertBitmapSourceToSKBitmap(renderBitmap);
                    
                    // 绘制到Canvas（使用实际位置和尺寸，画布的Scale变换会自动处理缩放）
                    var destRect = new SKRect(
                        (float)actualLeft, 
                        (float)actualTop, 
                        (float)(actualLeft + actualWidth), 
                        (float)(actualTop + actualHeight));
                    
                    // 🎨 使用高质量过滤模式，确保投影质量
                    var paint = new SKPaint
                    {
                        FilterQuality = SKFilterQuality.High,
                        IsAntialias = true
                    };
                    canvas.DrawBitmap(skBitmap, destRect, paint);
                    paint.Dispose();
                    
                    skBitmap.Dispose();
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [文本绘制] 失败: {ex.Message}");
                #else
                _ = ex;
                #endif
            }
        }
        
        /// <summary>
        /// 直接用SkiaSharp绘制文字（支持加粗、对齐等样式）
        /// </summary>
        private void DrawTextDirectly(SKCanvas canvas, TextElement data, float x, float y, float width, float height)
        {
            // 解析颜色
            SKColor textColor = SKColors.White;
            try
            {
                string hexColor = data.FontColor.TrimStart('#');
                if (hexColor.Length == 6)
                {
                    byte r = Convert.ToByte(hexColor.Substring(0, 2), 16);
                    byte g = Convert.ToByte(hexColor.Substring(2, 2), 16);
                    byte b = Convert.ToByte(hexColor.Substring(4, 2), 16);
                    textColor = new SKColor(r, g, b);
                }
            }
            catch { }
            
            // 🔧 创建字体（支持PAK资源、文件路径和系统字体）
            SKTypeface typeface = null;
            try
            {
                // 字体路径格式：./CCanvas_Fonts/江西拙楷.ttf#江西拙楷
                string fontPath = data.FontFamily;
                
                // 如果是文件路径格式（包含#号分隔符），提取文件路径部分
                if (fontPath.Contains("#"))
                {
                    fontPath = fontPath.Split('#')[0];
                }
                
                // 检查是否是相对路径（从PAK加载）
                if (fontPath.StartsWith("./") || fontPath.StartsWith(".\\"))
                {
                    // 🎯 从PAK资源包加载字体
                    // 提取文件名（例如从 ./CCanvas_Fonts/江西拙楷.ttf 提取 江西拙楷.ttf）
                    string fileName = System.IO.Path.GetFileName(fontPath);
                    
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"  [字体] 尝试从PAK加载: 原始路径='{fontPath}', 文件名='{fileName}'");
                    #endif
                    
                    // 在PAK中搜索匹配的字体文件
                    string actualPakPath = null;
                    var allResources = Core.PakManager.Instance.GetAllResourcePaths();
                    foreach (var resourcePath in allResources)
                    {
                        if (System.IO.Path.GetFileName(resourcePath) == fileName && 
                            resourcePath.StartsWith("Fonts/"))
                        {
                            actualPakPath = resourcePath;
                            break;
                        }
                    }
                    
                    if (actualPakPath != null)
                    {
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"  [字体] 在PAK中找到: {actualPakPath}");
                        #endif
                        
                        var fontData = Core.PakManager.Instance.GetResource(actualPakPath);
                        
                        if (fontData != null)
                        {
                            #if DEBUG
                            System.Diagnostics.Debug.WriteLine($"  [字体] PAK数据获取成功: {fontData.Length} bytes");
                            #endif
                            
                            typeface = SKTypeface.FromData(SKData.CreateCopy(fontData));
                            
                            if (typeface != null)
                            {
                                #if DEBUG
                                System.Diagnostics.Debug.WriteLine($"  [字体] 从PAK加载成功: {actualPakPath}");
                                #endif
                            }
                            else
                            {
                                #if DEBUG
                                System.Diagnostics.Debug.WriteLine($"  [字体] SKTypeface创建失败，数据可能不是有效字体");
                                #endif
                            }
                        }
                    }
                    else
                    {
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"  [字体] PAK中未找到字体，尝试文件系统");
                        #endif
                        
                        // PAK中没有，尝试从文件系统加载
                        if (System.IO.File.Exists(fontPath))
                        {
                            typeface = SKTypeface.FromFile(fontPath);
                            
                            #if DEBUG
                            System.Diagnostics.Debug.WriteLine($"  [字体] 从文件加载: {fontPath}");
                            #endif
                        }
                        else
                        {
                            #if DEBUG
                            System.Diagnostics.Debug.WriteLine($"  [字体] 文件也不存在: {fontPath}");
                            #endif
                        }
                    }
                }
                else if (System.IO.Path.IsPathRooted(fontPath))
                {
                    // 绝对路径，从文件加载
                    if (System.IO.File.Exists(fontPath))
                    {
                        typeface = SKTypeface.FromFile(fontPath);
                        
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"  [字体] 从文件加载: {fontPath}");
                        #endif
                    }
                }
                
                // 如果字体加载失败，使用系统字体
                if (typeface == null)
                {
                    var fontStyle = data.IsBoldBool ? SKFontStyle.Bold : SKFontStyle.Normal;
                    typeface = SKTypeface.FromFamilyName(fontPath, fontStyle);
                    
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"  [字体] 使用系统字体: {fontPath}");
                    #endif
                }
            }
            catch (Exception)
            {
                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($"  [字体] 加载失败: {ex.Message}，使用默认字体");
                #endif
                // 加载失败，使用默认字体
                var fontStyle = data.IsBoldBool ? SKFontStyle.Bold : SKFontStyle.Normal;
                typeface = SKTypeface.FromFamilyName("Arial", fontStyle);
            }
            
            // 创建画笔
            var paint = new SKPaint
            {
                Color = textColor,
                TextSize = (float)data.FontSize,
                IsAntialias = true,
                Typeface = typeface
            };
            
            // 处理文本对齐
            paint.TextAlign = data.TextAlign switch
            {
                "Center" => SKTextAlign.Center,
                "Right" => SKTextAlign.Right,
                _ => SKTextAlign.Left
            };
            
            // 计算文本位置
            float textX = x;
            if (data.TextAlign == "Center")
            {
                textX = x + width / 2;
            }
            else if (data.TextAlign == "Right")
            {
                textX = x + width;
            }
            
            // 绘制文本（支持多行）
            string[] lines = data.Content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            float lineHeight = paint.FontSpacing;
            
            // 🔧 正确计算第一行基线位置
            // 使用 FontMetrics 获取字体度量信息
            var fontMetrics = paint.FontMetrics;
            float firstLineBaseline = y - fontMetrics.Ascent; // Ascent是负值，表示基线到顶部的距离
            float currentY = firstLineBaseline;
            
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"  [文本绘制] 位置: ({textX}, {currentY}), 字号: {paint.TextSize}, 行高: {lineHeight}, 对齐: {paint.TextAlign}");
            System.Diagnostics.Debug.WriteLine($"  [文本绘制] 区域: x={x}, y={y}, w={width}, h={height}");
            System.Diagnostics.Debug.WriteLine($"  [字体度量] Ascent: {fontMetrics.Ascent}, Descent: {fontMetrics.Descent}, Leading: {fontMetrics.Leading}");
            #endif
            
            foreach (string line in lines)
            {
                canvas.DrawText(line, textX, currentY, paint);
                currentY += lineHeight;
            }
            
            paint.Dispose();
            typeface.Dispose();
        }
        
        /// <summary>
        /// 将WPF BitmapSource转换为SKBitmap
        /// </summary>
        private SKBitmap ConvertBitmapSourceToSKBitmap(BitmapSource source)
        {
            int width = source.PixelWidth;
            int height = source.PixelHeight;
            
            var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            source.CopyPixels(pixels, stride, 0);
            
            unsafe
            {
                fixed (byte* ptr = pixels)
                {
                    var dest = bitmap.GetPixels();
                    Buffer.MemoryCopy(ptr, dest.ToPointer(), pixels.Length, pixels.Length);
                }
            }
            
            return bitmap;
        }
        
        /// <summary>
        /// 将UI元素渲染为位图（旧方法，已被ComposeCanvasWithSkia替代）
        /// 🚀 优化策略：先渲染到Canvas原始尺寸（快），后续用GPU缩放到投影分辨率（快）
        /// </summary>
        private RenderTargetBitmap RenderCanvasToBitmap(UIElement element)
        {
            // 获取元素的实际尺寸
            double width = 0;
            double height = 0;
            
            if (element is FrameworkElement frameworkElement)
            {
                width = frameworkElement.ActualWidth > 0 ? frameworkElement.ActualWidth : frameworkElement.Width;
                height = frameworkElement.ActualHeight > 0 ? frameworkElement.ActualHeight : frameworkElement.Height;
            }
            
            // 🚀 新策略：渲染到Canvas原始尺寸，避免DrawingVisual缩放带来的性能损失
            // 后续会用GPU快速缩放到投影分辨率
            int renderWidth = (int)Math.Ceiling(width);
            int renderHeight = (int)Math.Ceiling(height);
            
            // 确保元素已完成布局
            element.Measure(new System.Windows.Size(width, height));
            element.Arrange(new Rect(new System.Windows.Size(width, height)));
            element.UpdateLayout();
            
            // 渲染到Canvas原始尺寸，96 DPI
            var renderBitmap = new RenderTargetBitmap(
                renderWidth,
                renderHeight,
                96, 96,
                PixelFormats.Pbgra32);

            renderBitmap.Render(element);
            return renderBitmap;
        }

        /// <summary>
        /// 将WPF位图转换为SkiaSharp格式
        /// </summary>
        private SKBitmap ConvertBitmapToSkia(BitmapSource bitmap)
        {
            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;

            // 创建SkiaSharp图片
            var image = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

            // 从WPF位图读取像素
            int stride = width * 4; // BGRA32 = 4 bytes per pixel
            byte[] pixels = new byte[height * stride];
            bitmap.CopyPixels(pixels, stride, 0);

            // 直接复制像素数据（WPF和SkiaSharp都使用BGRA格式）
            unsafe
            {
                fixed (byte* src = pixels)
                {
                    var dst = image.GetPixels();
                    Buffer.MemoryCopy(src, dst.ToPointer(), pixels.Length, pixels.Length);
                }
            }

            return image;
        }

        /// <summary>
        /// 将图像缩放到投影屏幕尺寸，拉伸填满整个屏幕
        /// 🚀 优化：使用GPU加速缩放，性能提升10倍
        /// </summary>
        private SKBitmap ScaleImageForProjection(SKBitmap sourceImage, int targetWidth, int targetHeight)
        {
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"🎨 [GPU缩放] 输入: {sourceImage.Width}×{sourceImage.Height}, 输出: {targetWidth}×{targetHeight}");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            #endif

            // 🚀 使用GPU加速缩放（与普通图片投影保持一致）
            var scaled = Core.GPUContext.Instance.ScaleImageGpu(
                sourceImage, 
                targetWidth, 
                targetHeight);
            
            #if DEBUG
            sw.Stop();
            System.Diagnostics.Debug.WriteLine($"🎨 [GPU缩放] GPUContext.ScaleImageGpu 实际耗时: {sw.ElapsedMilliseconds}ms");
            #endif

            return scaled;
        }

        /// <summary>
        /// 计算最佳的BitmapCache渲染缩放比例，以适应投影屏幕分辨率
        /// </summary>
        /// <returns>渲染缩放比例（1.0-4.0）</returns>
        private double CalculateOptimalRenderScale()
        {
            try
            {
                // 获取投影屏幕分辨率
                var (projWidth, projHeight) = _projectionManager?.GetCurrentProjectionSize() ?? (1920, 1080);
                
                // 编辑器画布固定尺寸
                const double canvasWidth = 1080.0;
                const double canvasHeight = 700.0;
                
                // 计算宽度和高度的缩放比例
                double scaleX = projWidth / canvasWidth;
                double scaleY = projHeight / canvasHeight;
                
                // 使用较大的缩放比例，确保投影时质量充足
                double scale = Math.Max(scaleX, scaleY);
                
                // 限制范围：1.0-4.0（避免过大导致内存问题）
                scale = Math.Max(1.0, Math.Min(4.0, scale));
                
                #if DEBUG
                // System.Diagnostics.Debug.WriteLine($"🎨 [RenderScale] 投影屏={projWidth}×{projHeight}, 画布={canvasWidth}×{canvasHeight}, 缩放={scale:F2}");
                #endif
                
                return scale;
            }
            catch
            {
                // 异常时返回默认值2.0（适合1080p投影）
                return 2.0;
            }
        }

        /// <summary>
        /// 重命名文本项目 - 进入内联编辑模式
        /// </summary>
        private void RenameTextProjectAsync(ProjectTreeItem item)
        {
            if (item == null || item.Type != TreeItemType.TextProject)
            {
                //System.Diagnostics.Debug.WriteLine($"⚠️ 无法重命名: item null 或类型不匹配");
                return;
            }

            //System.Diagnostics.Debug.WriteLine($"📝 进入编辑模式: ID={item.Id}, Name={item.Name}");
            
            // 保存原始名称
            item.OriginalName = item.Name;
            
            // 进入编辑模式
            item.IsEditing = true;
            
            //System.Diagnostics.Debug.WriteLine($"✅ IsEditing 已设置为 true, OriginalName={item.OriginalName}");
        }

        /// <summary>
        /// 完成内联重命名
        /// </summary>
        private async Task CompleteRenameAsync(ProjectTreeItem item, string newName)
        {
            try
            {
                //System.Diagnostics.Debug.WriteLine($"💾 完成重命名: OriginalName={item.OriginalName}, CurrentName={item.Name}, NewName={newName}");
                
                // 如果取消或输入为空，恢复原始名称
                if (string.IsNullOrWhiteSpace(newName))
                {
                    //System.Diagnostics.Debug.WriteLine($"⚠️ 名称为空，恢复原始名称");
                    item.Name = item.OriginalName;
                    item.IsEditing = false;
                    return;
                }

                // 如果名称未改变，直接返回
                if (newName.Trim() == item.OriginalName)
                {
                    //System.Diagnostics.Debug.WriteLine($"⚠️ 名称未改变，取消编辑");
                    item.IsEditing = false;
                    return;
                }

                //System.Diagnostics.Debug.WriteLine($"🔄 开始保存项目: ID={item.Id}, {item.OriginalName} -> {newName.Trim()}");
                
                // 加载并更新项目
                var project = await _textProjectManager.LoadProjectAsync(item.Id);
                if (project != null)
                {
                    //System.Diagnostics.Debug.WriteLine($"✅ 项目加载成功，更新名称");
                    
                    project.Name = newName.Trim();
                    await _textProjectManager.SaveProjectAsync(project);
                    
                    // 更新树节点（Name 已经通过绑定更新了，只需更新 OriginalName）
                    item.OriginalName = newName.Trim();
                    item.IsEditing = false;
                    
                    ShowStatus($"✅ 项目已重命名: {newName}");
                    //System.Diagnostics.Debug.WriteLine($"✅ 项目已重命名: ID={item.Id}, NewName={newName}");
                }
                else
                {
                    //System.Diagnostics.Debug.WriteLine($"❌ 项目加载失败: ID={item.Id}，恢复原始名称");
                    item.Name = item.OriginalName;
                    item.IsEditing = false;
                }
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 重命名项目失败: {ex.Message}");
                //System.Diagnostics.Debug.WriteLine($"❌ 堆栈跟踪: {ex.StackTrace}");
                
                // 恢复原始名称
                item.Name = item.OriginalName;
                item.IsEditing = false;
                
                WpfMessageBox.Show($"重命名项目失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 删除文本项目
        /// </summary>
        private async Task DeleteTextProjectAsync(ProjectTreeItem item)
        {
            try
            {
                var result = WpfMessageBox.Show(
                    $"确定要删除项目 '{item.Name}' 吗？\n所有文本元素和背景都将被删除。",
                    "确认删除",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );

                if (result == MessageBoxResult.Yes)
                {
                    await _textProjectManager.DeleteProjectAsync(item.Id);
                    
                    // 如果删除的是当前项目，关闭编辑器
                    if (_currentTextProject != null && _currentTextProject.Id == item.Id)
                    {
                        CloseTextEditor();
                    }
                    
                    // 刷新项目树
                    LoadProjects();
                    
                    ShowStatus($"✅ 已删除项目: {item.Name}");
                    //System.Diagnostics.Debug.WriteLine($"✅ 已删除项目: ID={item.Id}, Name={item.Name}");
                }
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 删除项目失败: {ex.Message}");
                WpfMessageBox.Show($"删除项目失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 更新辅助线显示
        /// </summary>
        private void UpdateAlignmentGuides(DraggableTextBox movingBox)
        {
            if (movingBox == null) return;

            double centerX = EditorCanvas.Width / 2;
            double centerY = EditorCanvas.Height / 2;
            
            double boxCenterX = movingBox.Data.X + movingBox.Data.Width / 2;
            double boxCenterY = movingBox.Data.Y + movingBox.Data.Height / 2;

            bool showVerticalCenter = false;
            bool showHorizontalCenter = false;
            bool showVerticalAlign = false;
            bool showHorizontalAlign = false;
            
            double alignX = 0;
            double alignY = 0;

            // 检查是否接近画布中心
            if (Math.Abs(boxCenterX - centerX) < SNAP_THRESHOLD)
            {
                showVerticalCenter = true;
            }
            
            if (Math.Abs(boxCenterY - centerY) < SNAP_THRESHOLD)
            {
                showHorizontalCenter = true;
            }

            // 检查是否与其他文本框对齐
            foreach (var otherBox in _textBoxes)
            {
                if (otherBox == movingBox) continue;

                double otherCenterX = otherBox.Data.X + otherBox.Data.Width / 2;
                double otherCenterY = otherBox.Data.Y + otherBox.Data.Height / 2;

                // 垂直对齐（左、中、右）
                if (Math.Abs(movingBox.Data.X - otherBox.Data.X) < SNAP_THRESHOLD) // 左对齐
                {
                    showVerticalAlign = true;
                    alignX = otherBox.Data.X;
                }
                else if (Math.Abs(boxCenterX - otherCenterX) < SNAP_THRESHOLD) // 中心对齐
                {
                    showVerticalAlign = true;
                    alignX = otherCenterX;
                }
                else if (Math.Abs(movingBox.Data.X + movingBox.Data.Width - otherBox.Data.X - otherBox.Data.Width) < SNAP_THRESHOLD) // 右对齐
                {
                    showVerticalAlign = true;
                    alignX = otherBox.Data.X + otherBox.Data.Width;
                }

                // 水平对齐（上、中、下）
                if (Math.Abs(movingBox.Data.Y - otherBox.Data.Y) < SNAP_THRESHOLD) // 上对齐
                {
                    showHorizontalAlign = true;
                    alignY = otherBox.Data.Y;
                }
                else if (Math.Abs(boxCenterY - otherCenterY) < SNAP_THRESHOLD) // 中心对齐
                {
                    showHorizontalAlign = true;
                    alignY = otherCenterY;
                }
                else if (Math.Abs(movingBox.Data.Y + movingBox.Data.Height - otherBox.Data.Y - otherBox.Data.Height) < SNAP_THRESHOLD) // 下对齐
                {
                    showHorizontalAlign = true;
                    alignY = otherBox.Data.Y + otherBox.Data.Height;
                }
            }

            // 更新辅助线显示
            VerticalCenterLine.Visibility = showVerticalCenter ? Visibility.Visible : Visibility.Collapsed;
            HorizontalCenterLine.Visibility = showHorizontalCenter ? Visibility.Visible : Visibility.Collapsed;
            
            if (showVerticalAlign)
            {
                VerticalAlignLine.X1 = alignX;
                VerticalAlignLine.X2 = alignX;
                VerticalAlignLine.Visibility = Visibility.Visible;
            }
            else
            {
                VerticalAlignLine.Visibility = Visibility.Collapsed;
            }
            
            if (showHorizontalAlign)
            {
                HorizontalAlignLine.Y1 = alignY;
                HorizontalAlignLine.Y2 = alignY;
                HorizontalAlignLine.Visibility = Visibility.Visible;
            }
            else
            {
                HorizontalAlignLine.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 隐藏所有辅助线
        /// </summary>
        private void HideAlignmentGuides()
        {
            VerticalCenterLine.Visibility = Visibility.Collapsed;
            HorizontalCenterLine.Visibility = Visibility.Collapsed;
            VerticalAlignLine.Visibility = Visibility.Collapsed;
            HorizontalAlignLine.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region 树节点内联编辑事件

        /// <summary>
        /// 编辑框加载时自动聚焦并定位光标到末尾
        /// </summary>
        private void TreeItemEditBox_Loaded(object sender, RoutedEventArgs e)
        {
            // System.Diagnostics.Debug.WriteLine($"🔍 TreeItemEditBox_Loaded 触发");
            
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                // System.Diagnostics.Debug.WriteLine($"🔍 TextBox 实例: Text={textBox.Text}, Visibility={textBox.Visibility}");
                
                if (textBox.DataContext is ProjectTreeItem item)
                {
                    // System.Diagnostics.Debug.WriteLine($"🔍 DataContext: Name={item.Name}, IsEditing={item.IsEditing}");
                    
                    // 只在编辑模式时才聚焦
                    if (!item.IsEditing)
                    {
                        // System.Diagnostics.Debug.WriteLine($"⚠️ IsEditing=false，跳过聚焦");
                        return;
                    }
                    
                    //System.Diagnostics.Debug.WriteLine($"📝 编辑框加载: Text={textBox.Text}, IsEditing={item.IsEditing}");
                    
                    // 延迟聚焦，确保UI完全加载
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (item.IsEditing) // 再次检查，防止在延迟期间状态改变
                        {
                            bool focused = textBox.Focus();
                            textBox.CaretIndex = textBox.Text.Length; // 光标定位到末尾
                            //System.Diagnostics.Debug.WriteLine($"✅ 编辑框已聚焦: Success={focused}, 光标位置: {textBox.CaretIndex}");
                        }
                        else
                        {
                            //System.Diagnostics.Debug.WriteLine($"⚠️ 延迟检查时 IsEditing=false");
                        }
                    }), System.Windows.Threading.DispatcherPriority.Input);
                }
                else
                {
                    //System.Diagnostics.Debug.WriteLine($"⚠️ DataContext 不是 ProjectTreeItem: {textBox.DataContext?.GetType().Name}");
                }
            }
            else
            {
                //System.Diagnostics.Debug.WriteLine($"⚠️ sender 不是 TextBox: {sender?.GetType().Name}");
            }
        }

        /// <summary>
        /// 编辑框按键处理（Enter 保存，Esc 取消）
        /// </summary>
        private async void TreeItemEditBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox textBox && 
                textBox.DataContext is ProjectTreeItem item)
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    // 回车保存
                    await CompleteRenameAsync(item, textBox.Text);
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.Escape)
                {
                    // Esc 取消
                    item.IsEditing = false;
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// 编辑框失去焦点时保存
        /// </summary>
        private async void TreeItemEditBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox textBox && 
                textBox.DataContext is ProjectTreeItem item)
            {
                await CompleteRenameAsync(item, textBox.Text);
            }
        }

        /// <summary>
        /// 编辑框可见性改变时处理
        /// </summary>
        private void TreeItemEditBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox textBox && 
                textBox.DataContext is ProjectTreeItem item)
            {
                bool isVisible = (bool)e.NewValue;
                //System.Diagnostics.Debug.WriteLine($"🔍 编辑框可见性改变: IsVisible={isVisible}, IsEditing={item.IsEditing}, Name={item.Name}");
                
                // 当变为可见且处于编辑模式时，聚焦并定位光标
                if (isVisible && item.IsEditing)
                {
                    //System.Diagnostics.Debug.WriteLine($"📝 编辑框变为可见，准备聚焦");
                    
                    // 延迟聚焦，确保控件完全渲染
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (item.IsEditing && textBox.IsVisible)
                        {
                            bool focused = textBox.Focus();
                            textBox.CaretIndex = textBox.Text.Length;
                            //System.Diagnostics.Debug.WriteLine($"✅ 编辑框已聚焦: Success={focused}, CaretIndex={textBox.CaretIndex}, IsFocused={textBox.IsFocused}");
                        }
                    }), System.Windows.Threading.DispatcherPriority.Input);
                }
            }
        }

        /// <summary>
        /// 根据字体族名称查找FontFamily对象
        /// </summary>
        private System.Windows.Media.FontFamily FindFontFamilyByName(string fontFamilyName)
        {
            if (string.IsNullOrEmpty(fontFamilyName))
                return null;

            // 遍历ComboBox中的所有字体项
            foreach (var item in FontFamilySelector.Items)
            {
                if (item is ComboBoxItem comboItem && comboItem.Tag is FontItemData fontData)
                {
                    // 匹配字体族名称
                    if (fontData.Config.Family == fontFamilyName || 
                        fontData.Config.Name == fontFamilyName ||
                        fontFamilyName.Contains(fontData.Config.Family))
                    {
                        return fontData.FontFamily;
                    }
                }
            }

            // 如果没找到，返回null（将使用系统默认字体）
            //System.Diagnostics.Debug.WriteLine($"⚠️ 未找到字体: {fontFamilyName}，将使用默认字体");
            return null;
        }

        /// <summary>
        /// 退出文本编辑器按钮点击事件
        /// </summary>
        private void BtnCloseTextEditor_Click(object sender, RoutedEventArgs e)
        {
            // 检查是否有未保存的更改
            if (_currentTextProject != null && BtnSaveTextProject.Background is SolidColorBrush brush && 
                brush.Color == Colors.Yellow)
            {
                var result = WpfMessageBox.Show(
                    "当前项目有未保存的更改，是否保存？", 
                    "提示", 
                    MessageBoxButton.YesNoCancel, 
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // 保存项目
                    BtnSaveTextProject_Click(sender, e);
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    // 取消退出
                    return;
                }
                // No: 不保存，直接退出
            }

            // 关闭文本编辑器
            CloseTextEditor();
            
            //System.Diagnostics.Debug.WriteLine("状态: ✅ 已退出文本编辑器，返回图片/视频浏览模式");
        }

        #endregion

        #region 🆕 投影锁定功能

        /// <summary>
        /// 投影锁定状态（true=锁定，切换幻灯片不自动更新投影；false=未锁定，自动更新）
        /// </summary>
        private bool _isProjectionLocked = false;

        /// <summary>
        /// 锁定投影按钮点击事件
        /// </summary>
        private void BtnLockProjection_Click(object sender, RoutedEventArgs e)
        {
            // 切换锁定状态
            _isProjectionLocked = !_isProjectionLocked;

            // 更新按钮显示
            if (_isProjectionLocked)
            {
                // 锁定状态：设置橙色，Tag标记锁定（样式会根据Tag禁用悬停效果）
                BtnLockProjection.Content = "🔒 锁定投影";
                BtnLockProjection.ToolTip = "投影已锁定：切换幻灯片不会自动更新投影，点击解锁";
                BtnLockProjection.Tag = "Locked";
                BtnLockProjection.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 165, 0));
                BtnLockProjection.Foreground = new SolidColorBrush(Colors.White);
            }
            else
            {
                // 未锁定状态：恢复默认蓝色
                BtnLockProjection.Content = "🔓 锁定投影";
                BtnLockProjection.ToolTip = "投影未锁定：切换幻灯片自动更新投影，点击锁定";
                BtnLockProjection.Tag = null;
                BtnLockProjection.ClearValue(System.Windows.Controls.Button.BackgroundProperty);
                BtnLockProjection.ClearValue(System.Windows.Controls.Button.ForegroundProperty);
            }
        }

        #endregion

        #region 🆕 幻灯片管理

        /// <summary>
        /// 当前选中的幻灯片
        /// </summary>
        private Slide _currentSlide;

        /// <summary>
        /// 幻灯片列表选择改变事件
        /// </summary>
        private void SlideListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (SlideListBox.SelectedItem is Slide selectedSlide)
            {
                // 切换到选中的幻灯片
                LoadSlide(selectedSlide);
            }
        }

        /// <summary>
        /// 幻灯片列表键盘事件（处理DEL删除）
        /// </summary>
        private void SlideListBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // DEL键删除幻灯片
            if (e.Key == System.Windows.Input.Key.Delete)
            {
                if (SlideListBox.SelectedItem != null)
                {
                    BtnDeleteSlide_Click(sender, new RoutedEventArgs());
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// 幻灯片列表右键点击事件
        /// </summary>
        private void SlideListBox_RightClick(object sender, MouseButtonEventArgs e)
        {
            // 创建右键菜单
            var contextMenu = new ContextMenu();
            
            // 🔑 应用自定义样式
            contextMenu.Style = (Style)this.FindResource("NoBorderContextMenuStyle");

            // 新建幻灯片
            var addItem = new MenuItem 
            { 
                Header = "新建",
                FontSize = 14
            };
            addItem.Click += BtnAddSlide_Click;
            contextMenu.Items.Add(addItem);

            // 复制幻灯片
            var copyItem = new MenuItem 
            { 
                Header = "复制",
                FontSize = 14,
                IsEnabled = SlideListBox.SelectedItem != null
            };
            copyItem.Click += BtnCopySlide_Click;
            contextMenu.Items.Add(copyItem);

            // 删除幻灯片
            var deleteItem = new MenuItem 
            { 
                Header = "删除",
                FontSize = 14,
                IsEnabled = SlideListBox.SelectedItem != null
            };
            deleteItem.Click += BtnDeleteSlide_Click;
            contextMenu.Items.Add(deleteItem);

            contextMenu.PlacementTarget = sender as UIElement;
            contextMenu.IsOpen = true;
        }

        #region 幻灯片拖动排序

        private Slide _draggingSlide = null;
        private System.Windows.Point _slideDragStartPoint;

        /// <summary>
        /// 幻灯片列表鼠标按下事件（开始拖动）
        /// </summary>
        private void SlideListBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _slideDragStartPoint = e.GetPosition(null);
            
            // 获取点击的幻灯片
            var item = FindVisualAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
            if (item != null)
            {
                _draggingSlide = item.DataContext as Slide;
            }
        }

        /// <summary>
        /// 幻灯片列表鼠标移动事件（执行拖动）
        /// </summary>
        private void SlideListBox_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggingSlide != null)
            {
                System.Windows.Point currentPosition = e.GetPosition(null);
                Vector diff = _slideDragStartPoint - currentPosition;

                // 检查是否移动了足够的距离来开始拖动
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    // 开始拖放操作
                    System.Windows.DataObject dragData = new System.Windows.DataObject("Slide", _draggingSlide);
                    DragDrop.DoDragDrop(SlideListBox, dragData, System.Windows.DragDropEffects.Move);
                    _draggingSlide = null;
                }
            }
        }

        /// <summary>
        /// 幻灯片列表拖放over事件
        /// </summary>
        private void SlideListBox_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent("Slide"))
            {
                e.Effects = System.Windows.DragDropEffects.Move;
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.None;
            }
            e.Handled = true;
        }

        /// <summary>
        /// 幻灯片列表放下事件（完成排序）
        /// </summary>
        private void SlideListBox_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent("Slide"))
            {
                Slide sourceSlide = e.Data.GetData("Slide") as Slide;
                
                // 获取放下位置的幻灯片
                var targetItem = FindVisualAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
                if (targetItem != null)
                {
                    Slide targetSlide = targetItem.DataContext as Slide;
                    if (targetSlide != null && sourceSlide != targetSlide)
                    {
                        // 执行排序
                        ReorderSlides(sourceSlide, targetSlide);
                    }
                }
            }
        }

        /// <summary>
        /// 重新排序幻灯片
        /// </summary>
        private async void ReorderSlides(Slide sourceSlide, Slide targetSlide)
        {
            try
            {
                var slides = await _dbContext.Slides
                    .Where(s => s.ProjectId == _currentTextProject.Id)
                    .OrderBy(s => s.SortOrder)
                    .ToListAsync();

                int sourceIndex = slides.IndexOf(sourceSlide);
                int targetIndex = slides.IndexOf(targetSlide);

                if (sourceIndex == -1 || targetIndex == -1)
                    return;

                // 移除源幻灯片
                slides.RemoveAt(sourceIndex);
                
                // 插入到目标位置
                slides.Insert(targetIndex, sourceSlide);

                // 更新所有幻灯片的SortOrder
                for (int i = 0; i < slides.Count; i++)
                {
                    slides[i].SortOrder = i;
                }

                await _dbContext.SaveChangesAsync();

                // 刷新列表
                LoadSlideList();

                // 保持选中当前幻灯片
                SlideListBox.SelectedItem = sourceSlide;
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"排序失败: {ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 查找可视树祖先元素（用于幻灯片拖动）
        /// </summary>
        private T FindVisualAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            do
            {
                if (current is T)
                {
                    return (T)current;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            while (current != null);
            return null;
        }

        #endregion

        /// <summary>
        /// 文本编辑器面板键盘事件（处理PageUp/PageDown切换幻灯片）
        /// </summary>
        private void TextEditorPanel_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // 只在文本编辑器可见时处理
            if (TextEditorPanel.Visibility != Visibility.Visible)
                return;

            // PageUp: 切换到上一张幻灯片
            if (e.Key == System.Windows.Input.Key.PageUp)
            {
                NavigateToPreviousSlide();
                e.Handled = true; // 阻止事件冒泡，避免触发全局热键
                //System.Diagnostics.Debug.WriteLine("⌨️ 文本编辑器: PageUp 切换幻灯片");
            }
            // PageDown: 切换到下一张幻灯片
            else if (e.Key == System.Windows.Input.Key.PageDown)
            {
                NavigateToNextSlide();
                e.Handled = true; // 阻止事件冒泡，避免触发全局热键
                //System.Diagnostics.Debug.WriteLine("⌨️ 文本编辑器: PageDown 切换幻灯片");
            }
        }

        /// <summary>
        /// 切换到上一张幻灯片
        /// </summary>
        private void NavigateToPreviousSlide()
        {
            if (SlideListBox.Items.Count == 0)
                return;

            int currentIndex = SlideListBox.SelectedIndex;
            if (currentIndex > 0)
            {
                SlideListBox.SelectedIndex = currentIndex - 1;
                //System.Diagnostics.Debug.WriteLine($"⬆️ 切换到上一张幻灯片: Index={currentIndex - 1}");
            }
        }

        /// <summary>
        /// 切换到下一张幻灯片
        /// </summary>
        private void NavigateToNextSlide()
        {
            if (SlideListBox.Items.Count == 0)
                return;

            int currentIndex = SlideListBox.SelectedIndex;
            if (currentIndex < SlideListBox.Items.Count - 1)
            {
                SlideListBox.SelectedIndex = currentIndex + 1;
                //System.Diagnostics.Debug.WriteLine($"⬇️ 切换到下一张幻灯片: Index={currentIndex + 1}");
            }
        }

        /// <summary>
        /// 加载幻灯片内容到编辑器
        /// </summary>
        private void LoadSlide(Slide slide)
        {
            try
            {
                _currentSlide = slide;

                // 清空画布
                ClearEditorCanvas();

                // 加载背景
                if (!string.IsNullOrEmpty(slide.BackgroundImagePath) &&
                    System.IO.File.Exists(slide.BackgroundImagePath))
                {
                    BackgroundImage.Source = new BitmapImage(new Uri(slide.BackgroundImagePath));
                }
                else
                {
                    BackgroundImage.Source = null;
                    // 设置背景颜色
                    if (!string.IsNullOrEmpty(slide.BackgroundColor))
                    {
                        EditorCanvas.Background = (SolidColorBrush)new BrushConverter().ConvertFrom(slide.BackgroundColor);
                    }
                    else
                    {
                        EditorCanvas.Background = new SolidColorBrush(Colors.White);
                    }
                }

                // 加载文本元素
                var elements = _dbContext.TextElements
                    .Where(e => e.SlideId == slide.Id)
                    .OrderBy(e => e.ZIndex)
                    .ToList();

                foreach (var element in elements)
                {
                    var textBox = new DraggableTextBox(element);
                    
                    // 应用字体
                    var fontFamilyToApply = FindFontFamilyByName(element.FontFamily);
                    if (fontFamilyToApply != null)
                    {
                        textBox.ApplyFontFamily(fontFamilyToApply);
                    }
                    
                    AddTextBoxToCanvas(textBox);
                }

                //System.Diagnostics.Debug.WriteLine($"✅ 加载幻灯片成功: ID={slide.Id}, Title={slide.Title}, Elements={elements.Count}");
                
                // 🆕 恢复分割配置
                RestoreSplitConfig(slide);
                
                // 🆕 加载完成后，如果投影已开启且未锁定，自动更新投影
                if (_projectionManager.IsProjectionActive && !_isProjectionLocked)
                {
                    // 延迟一点点，确保UI渲染完成
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdateProjectionFromCanvas();
                        //System.Diagnostics.Debug.WriteLine("✅ 幻灯片加载后已自动更新投影");
                    }), System.Windows.Threading.DispatcherPriority.Render);
                }
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 加载幻灯片失败: {ex.Message}");
                WpfMessageBox.Show($"加载幻灯片失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 新建幻灯片按钮点击事件
        /// </summary>
        private async void BtnAddSlide_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTextProject == null)
                return;

            try
            {
                // 🔧 获取当前幻灯片总数（用于生成标题序号）
                var slideCount = await _dbContext.Slides
                    .Where(s => s.ProjectId == _currentTextProject.Id)
                    .CountAsync();
                
                // 🔧 获取当前最大排序号（用于SortOrder）
                var maxOrderValue = await _dbContext.Slides
                    .Where(s => s.ProjectId == _currentTextProject.Id)
                    .Select(s => (int?)s.SortOrder)
                    .MaxAsync();
                
                int maxOrder = maxOrderValue ?? 0;

                // 创建新幻灯片（标题序号 = 总数 + 1）
                var newSlide = new Slide
                {
                    ProjectId = _currentTextProject.Id,
                    Title = $"幻灯片 {slideCount + 1}",
                    SortOrder = maxOrder + 1,
                    BackgroundColor = "#000000",  // 默认黑色背景
                    SplitMode = -1,  // 默认无分割模式
                    SplitStretchMode = false  // 默认适中模式
                };

                _dbContext.Slides.Add(newSlide);
                await _dbContext.SaveChangesAsync();

                // 刷新幻灯片列表
                LoadSlideList();

                // 选中新建的幻灯片
                SlideListBox.SelectedItem = newSlide;

                //System.Diagnostics.Debug.WriteLine($"✅ 新建幻灯片成功: ID={newSlide.Id}, Title={newSlide.Title}");
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 新建幻灯片失败: {ex.Message}");
                WpfMessageBox.Show($"新建幻灯片失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 复制幻灯片按钮点击事件
        /// </summary>
        private async void BtnCopySlide_Click(object sender, RoutedEventArgs e)
        {
            if (SlideListBox.SelectedItem is not Slide sourceSlide)
            {
                WpfMessageBox.Show("请先选择要复制的幻灯片", "提示", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // 加载源幻灯片的所有元素
                var sourceElements = await _dbContext.TextElements
                    .Where(e => e.SlideId == sourceSlide.Id)
                    .ToListAsync();

                // 计算新的排序位置（在源幻灯片后面）
                int newSortOrder = sourceSlide.SortOrder + 1;
                
                // 将后面的幻灯片排序顺序都+1
                var slidesToUpdate = await _dbContext.Slides
                    .Where(s => s.ProjectId == _currentTextProject.Id && s.SortOrder >= newSortOrder)
                    .ToListAsync();
                
                foreach (var slide in slidesToUpdate)
                {
                    slide.SortOrder++;
                }

                // 创建新幻灯片（复制所有属性）
                var newSlide = new Slide
                {
                    ProjectId = _currentTextProject.Id,
                    Title = $"{sourceSlide.Title} (副本)",
                    SortOrder = newSortOrder,
                    BackgroundColor = sourceSlide.BackgroundColor,
                    BackgroundImagePath = sourceSlide.BackgroundImagePath,
                    SplitMode = sourceSlide.SplitMode,  // 复制分割模式
                    SplitStretchMode = sourceSlide.SplitStretchMode,  // 复制拉伸模式
                    SplitRegionsData = sourceSlide.SplitRegionsData  // 复制区域数据
                };

                _dbContext.Slides.Add(newSlide);
                await _dbContext.SaveChangesAsync();

                // 复制所有文本元素
                foreach (var sourceElement in sourceElements)
                {
                    var newElement = new TextElement
                    {
                        SlideId = newSlide.Id,
                        X = sourceElement.X,
                        Y = sourceElement.Y,
                        Width = sourceElement.Width,
                        Height = sourceElement.Height,
                        Content = sourceElement.Content,
                        FontSize = sourceElement.FontSize,
                        FontFamily = sourceElement.FontFamily,
                        FontColor = sourceElement.FontColor,
                        IsBold = sourceElement.IsBold,
                        TextAlign = sourceElement.TextAlign,
                        ZIndex = sourceElement.ZIndex
                    };
                    _dbContext.TextElements.Add(newElement);
                }

                await _dbContext.SaveChangesAsync();

                // 先加载新幻灯片内容并生成缩略图,再刷新列表(避免闪烁)
                await Dispatcher.InvokeAsync(async () =>
                {
                    // 临时选中新幻灯片(不触发SelectionChanged)
                    var previousIndex = SlideListBox.SelectedIndex;
                    SlideListBox.SelectionChanged -= SlideListBox_SelectionChanged;
                    
                    // 手动加载新幻灯片内容
                    LoadSlide(newSlide);
                    
                    // 等待UI完全渲染
                    await Task.Delay(150);
                    
                    // 生成新幻灯片的缩略图
                    var thumbnailPath = SaveSlideThumbnail(newSlide.Id);
                    if (!string.IsNullOrEmpty(thumbnailPath))
                    {
                        newSlide.ThumbnailPath = thumbnailPath;
                    }
                    
                    // 恢复事件监听
                    SlideListBox.SelectionChanged += SlideListBox_SelectionChanged;
                    
                    // 刷新幻灯片列表(此时缩略图已生成)
                    LoadSlideList();
                    
                    // 选中新幻灯片
                    SlideListBox.SelectedItem = newSlide;
                    
                }, System.Windows.Threading.DispatcherPriority.Loaded);

                //System.Diagnostics.Debug.WriteLine($"✅ 复制幻灯片成功: 原ID={sourceSlide.Id}, 新ID={newSlide.Id}");
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 复制幻灯片失败: {ex.Message}");
                WpfMessageBox.Show($"复制幻灯片失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 删除幻灯片按钮点击事件（直接删除,不弹窗确认）
        /// </summary>
        private async void BtnDeleteSlide_Click(object sender, RoutedEventArgs e)
        {
            if (SlideListBox.SelectedItem is not Slide selectedSlide)
                return;

            try
            {
                _dbContext.Slides.Remove(selectedSlide);
                await _dbContext.SaveChangesAsync();

                // 刷新幻灯片列表
                LoadSlideList();

                //System.Diagnostics.Debug.WriteLine($"✅ 删除幻灯片成功: ID={selectedSlide.Id}, Title={selectedSlide.Title}");
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 删除幻灯片失败: {ex.Message}");
                WpfMessageBox.Show($"删除幻灯片失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 加载幻灯片列表
        /// </summary>
        private void LoadSlideList()
        {
            if (_currentTextProject == null)
                return;

            // 🆕 使用Include加载Elements集合，以便计算元素数量
            var slides = _dbContext.Slides
                .Include(s => s.Elements)
                .Where(s => s.ProjectId == _currentTextProject.Id)
                .OrderBy(s => s.SortOrder)
                .ToList();

            // 🆕 加载缩略图路径
            var thumbnailDir = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                "Thumbnails");
            
            foreach (var slide in slides)
            {
                var thumbnailPath = System.IO.Path.Combine(thumbnailDir, $"slide_{slide.Id}.png");
                if (System.IO.File.Exists(thumbnailPath))
                {
                    slide.ThumbnailPath = thumbnailPath;
                }
            }

            // 🆕 保存当前选中的索引
            int previousSelectedIndex = SlideListBox.SelectedIndex;
            
            SlideListBox.ItemsSource = slides;

            // 如果有幻灯片，恢复选中或默认选中第一个
            if (slides.Any())
            {
                // 如果之前有选中项，恢复选中；否则默认选中第一个
                int targetIndex = previousSelectedIndex >= 0 && previousSelectedIndex < slides.Count 
                    ? previousSelectedIndex 
                    : 0;
                
                // 🆕 先清空选中项，然后再设置，强制触发SelectionChanged事件
                SlideListBox.SelectedIndex = -1;
                SlideListBox.SelectedIndex = targetIndex;
                
                //System.Diagnostics.Debug.WriteLine($"🔄 强制选中幻灯片: Index={targetIndex}");
            }

            //System.Diagnostics.Debug.WriteLine($"✅ 加载幻灯片列表: Count={slides.Count}");
        }

        /// <summary>
        /// 刷新幻灯片列表（保持当前选中项）
        /// </summary>
        private void RefreshSlideList()
        {
            if (_currentTextProject == null)
                return;

            var currentSelectedSlide = SlideListBox.SelectedItem as Slide;
            var currentSelectedId = currentSelectedSlide?.Id;
            
            // 🔧 临时禁用SelectionChanged事件，避免重新加载当前幻灯片
            SlideListBox.SelectionChanged -= SlideListBox_SelectionChanged;
            
            try
            {
                // 🔧 先清空ItemsSource，强制UI重新绑定
                SlideListBox.ItemsSource = null;
                
                // 重新加载列表
                LoadSlideList();
                
                // 尝试恢复选中项（不会触发SelectionChanged）
                if (currentSelectedId.HasValue)
                {
                    var updatedSlide = (SlideListBox.ItemsSource as List<Slide>)?.FirstOrDefault(s => s.Id == currentSelectedId.Value);
                    if (updatedSlide != null)
                    {
                        SlideListBox.SelectedItem = updatedSlide;
                    }
                }
                
                //System.Diagnostics.Debug.WriteLine($"✅ 刷新幻灯片列表完成（未重新加载幻灯片内容）");
            }
            finally
            {
                // 恢复SelectionChanged事件
                SlideListBox.SelectionChanged += SlideListBox_SelectionChanged;
            }
        }

        /// <summary>
        /// 生成当前画布的缩略图
        /// </summary>
        private BitmapSource GenerateThumbnail()
        {
            try
            {
                // 获取画布的父Grid（包含背景图）
                var canvasParent = EditorCanvas.Parent as Grid;
                if (canvasParent == null)
                    return null;

                // 🎨 保存缩略图前：隐藏所有文本框的装饰元素（边框、拖拽手柄等）
                foreach (var textBox in _textBoxes)
                {
                    textBox.HideDecorations();
                }

                // 强制更新布局，确保隐藏效果生效
                canvasParent.UpdateLayout();

                // 获取实际尺寸
                int width = (int)canvasParent.ActualWidth;
                int height = (int)canvasParent.ActualHeight;
                
                // 如果尺寸无效，使用默认值
                if (width <= 0) width = 1080;
                if (height <= 0) height = 700;

                // 创建渲染目标
                var renderBitmap = new RenderTargetBitmap(
                    width, height,
                    96, 96,
                    PixelFormats.Pbgra32);

                // 渲染画布
                renderBitmap.Render(canvasParent);

                // 缩放到缩略图大小
                var thumbnail = new TransformedBitmap(renderBitmap, new ScaleTransform(0.1, 0.1));

                // 🎨 保存缩略图后：恢复所有文本框的装饰元素
                foreach (var textBox in _textBoxes)
                {
                    textBox.RestoreDecorations();
                }

                return thumbnail;
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 生成缩略图失败: {ex.Message}");
                
                // 🎨 异常时也要恢复装饰元素
                foreach (var textBox in _textBoxes)
                {
                    textBox.RestoreDecorations();
                }
                
                return null;
            }
        }

        /// <summary>
        /// 保存当前幻灯片的缩略图到临时文件
        /// </summary>
        private string SaveSlideThumbnail(int slideId)
        {
            try
            {
                var thumbnail = GenerateThumbnail();
                if (thumbnail == null)
                    return null;

                // 创建缩略图目录
                var thumbnailDir = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                    "Thumbnails");
                
                if (!System.IO.Directory.Exists(thumbnailDir))
                    System.IO.Directory.CreateDirectory(thumbnailDir);

                // 保存缩略图
                var thumbnailPath = System.IO.Path.Combine(thumbnailDir, $"slide_{slideId}.png");
                
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(thumbnail));
                
                using (var fileStream = new FileStream(thumbnailPath, FileMode.Create))
                {
                    encoder.Save(fileStream);
                }

                //System.Diagnostics.Debug.WriteLine($"✅ 缩略图已保存: {thumbnailPath}");
                return thumbnailPath;
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 保存缩略图失败: {ex.Message}");
                return null;
            }
        }

        #endregion
    }

    /// <summary>
    /// 字体项数据，用于ComboBox的Tag
    /// </summary>
    internal class FontItemData
    {
        /// <summary>
        /// 字体配置信息
        /// </summary>
        public CustomFont Config { get; set; }

        /// <summary>
        /// WPF FontFamily 对象（包含完整的字体路径和族名称）
        /// </summary>
        public System.Windows.Media.FontFamily FontFamily { get; set; }
    }
}

