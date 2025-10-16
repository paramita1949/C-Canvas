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
using ImageColorChanger.Core;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Managers;
using ImageColorChanger.UI.Controls;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 的文本编辑器功能分部类
    /// </summary>
    public partial class MainWindow
    {
        #region 字段

        private TextProjectManager _textProjectManager;
        private TextProject _currentTextProject;
        private List<DraggableTextBox> _textBoxes = new List<DraggableTextBox>();
        private DraggableTextBox _selectedTextBox;
        private string _currentTextColor = "#000000";

        // 辅助线相关
        private const double SNAP_THRESHOLD = 10.0; // 吸附阈值（像素）

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化文本编辑器
        /// </summary>
        private void InitializeTextEditor()
        {
            _textProjectManager = new TextProjectManager(dbManager.GetDbContext());
            
            // 加载系统字体
            LoadSystemFonts();
            
            System.Diagnostics.Debug.WriteLine("✅ 文本编辑器初始化完成");
        }

        /// <summary>
        /// 加载自定义字体库
        /// </summary>
        private void LoadSystemFonts()
        {
            try
            {
                var fontsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fonts");
                var configPath = Path.Combine(fontsPath, "fonts.json");

                if (!File.Exists(configPath))
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ 未找到 fonts.json，加载系统默认字体");
                    LoadSystemDefaultFonts();
                    return;
                }

                // 读取配置文件
                var json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<FontConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (config == null || config.FontCategories == null || config.FontCategories.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ fonts.json 配置为空，加载系统默认字体");
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
                                var fontFilePath = Path.Combine(fontsPath, font.File);
                                if (!File.Exists(fontFilePath))
                                {
                                    System.Diagnostics.Debug.WriteLine($"⚠️ 字体文件不存在: {fontFilePath}");
                                    continue;
                                }

                                // 从文件加载字体 - 使用基于应用程序目录的URI
                                try
                                {
                                    // 🔍 先使用GlyphTypeface读取字体文件的真实族名称
                                    string realFontFamily = font.Family;
                                    
                                    try
                                    {
                                        var absoluteFontUri = new Uri(fontFilePath, UriKind.Absolute);
                                        var glyphTypeface = new System.Windows.Media.GlyphTypeface(absoluteFontUri);
                                        if (glyphTypeface.FamilyNames.Count > 0)
                                        {
                                            // 优先使用中文名称，否则使用英文名称
                                            var zhCN = System.Globalization.CultureInfo.GetCultureInfo("zh-CN");
                                            var enUS = System.Globalization.CultureInfo.GetCultureInfo("en-US");
                                            
                                            if (glyphTypeface.FamilyNames.ContainsKey(zhCN))
                                                realFontFamily = glyphTypeface.FamilyNames[zhCN];
                                            else if (glyphTypeface.FamilyNames.ContainsKey(enUS))
                                                realFontFamily = glyphTypeface.FamilyNames[enUS];
                                            else
                                                realFontFamily = glyphTypeface.FamilyNames.Values.First();
                                            
                                            System.Diagnostics.Debug.WriteLine($"🔍 从字体文件读取到真实族名称: {realFontFamily}");
                                        }
                                    }
                                    catch (Exception glyphEx)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"⚠️ 无法读取字体族名称，使用配置值: {glyphEx.Message}");
                                    }
                                    
                                    // 🎯 使用基于应用程序目录的BaseUri + 相对路径
                                    // 这样无论程序在哪个目录都能正确加载字体
                                    var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                                    var baseUri = new Uri(appDirectory);
                                    var relativeFontPath = $"./Fonts/{font.File.Replace("\\", "/")}";
                                    
                                    // WPF FontFamily 构造函数：FontFamily(baseUri, familyName)
                                    // familyName 格式：相对路径#字体族名称
                                    fontFamily = new System.Windows.Media.FontFamily(baseUri, $"{relativeFontPath}#{realFontFamily}");
                                    
                                    // 更新配置中的Family（用于后续保存）
                                    font.Family = realFontFamily;
                                    
                                    // 🔍 输出字体的实际 FamilyNames，帮助调试
                                    var actualFamilies = string.Join(", ", fontFamily.FamilyNames.Values);
                                    System.Diagnostics.Debug.WriteLine($"✅ 字体加载成功: {font.Name}");
                                    System.Diagnostics.Debug.WriteLine($"   BaseUri: {baseUri}");
                                    System.Diagnostics.Debug.WriteLine($"   相对路径: {relativeFontPath}#{realFontFamily}");
                                    System.Diagnostics.Debug.WriteLine($"   完整Source: {fontFamily.Source}");
                                    System.Diagnostics.Debug.WriteLine($"   实际FamilyNames: {actualFamilies}");
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"❌ 字体加载失败: {font.Name}");
                                    System.Diagnostics.Debug.WriteLine($"   文件: {fontFilePath}");
                                    System.Diagnostics.Debug.WriteLine($"   错误: {ex.Message}");
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
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"⚠️ 加载字体失败 [{font.Name}]: {ex.Message}");
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

                System.Diagnostics.Debug.WriteLine($"✅ 加载自定义字体库完成: {totalFonts} 种字体，{config.FontCategories.Count} 个分类");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 加载自定义字体库失败: {ex.Message}");
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

                System.Diagnostics.Debug.WriteLine($"✅ 加载系统默认字体完成: {FontFamilySelector.Items.Count} 种");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 加载系统默认字体失败: {ex.Message}");
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 生成默认项目名称失败: {ex.Message}");
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
                // 创建项目
                _currentTextProject = await _textProjectManager.CreateProjectAsync(projectName);

                // 切换到编辑模式
                ShowTextEditor();

                // 清空画布
                ClearEditorCanvas();

                // 添加到导航树
                AddTextProjectToNavigationTree(_currentTextProject);

                // 🆕 新建项目后，保存按钮恢复为白色
                BtnSaveTextProject.Background = new SolidColorBrush(Colors.White);

                System.Diagnostics.Debug.WriteLine($"✅ 创建文本项目成功: {projectName}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 创建文本项目失败: {ex.Message}");
                WpfMessageBox.Show($"创建项目失败: {ex.Message}", "错误", 
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
                // 加载项目
                _currentTextProject = await _textProjectManager.LoadProjectAsync(projectId);

                // 切换到编辑模式
                ShowTextEditor();

                // 清空画布
                ClearEditorCanvas();

                // 加载背景图
                if (!string.IsNullOrEmpty(_currentTextProject.BackgroundImagePath) &&
                    System.IO.File.Exists(_currentTextProject.BackgroundImagePath))
                {
                    BackgroundImage.Source = new BitmapImage(new Uri(_currentTextProject.BackgroundImagePath));
                }

                // 加载所有文本元素
                foreach (var element in _currentTextProject.Elements.OrderBy(e => e.ZIndex))
                {
                    var textBox = new DraggableTextBox(element);
                    
                    // 🔧 修复字体：根据字体族名称查找并应用正确的FontFamily对象
                    var fontFamilyToApply = FindFontFamilyByName(element.FontFamily);
                    if (fontFamilyToApply != null)
                    {
                        textBox.ApplyFontFamily(fontFamilyToApply);
                        System.Diagnostics.Debug.WriteLine($"✅ 为文本框应用字体: {element.FontFamily} -> {fontFamilyToApply.Source}");
                    }
                    
                    AddTextBoxToCanvas(textBox);
                }

                // 🆕 加载完成后，保存按钮恢复为白色
                BtnSaveTextProject.Background = new SolidColorBrush(Colors.White);

                System.Diagnostics.Debug.WriteLine($"✅ 加载文本项目成功: {_currentTextProject.Name}, 元素数: {_currentTextProject.Elements.Count}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 加载文本项目失败: {ex.Message}");
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
        }

        /// <summary>
        /// 隐藏文本编辑器（返回图片模式）
        /// </summary>
        private void HideTextEditor()
        {
            TextEditorPanel.Visibility = Visibility.Collapsed;
            ImageScrollViewer.Visibility = Visibility.Visible;
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
                projectTreeItems.Add(projectNode);

                // 选中新创建的项目
                projectNode.IsSelected = true;

                System.Diagnostics.Debug.WriteLine($"✅ 项目已添加到导航树: {project.Name}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 添加项目到导航树失败: {ex.Message}");
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

            try
            {
                // 创建新元素
                var newElement = new TextElement
                {
                    ProjectId = _currentTextProject.Id,
                    X = 100 + (_textBoxes.Count * 20), // 阶梯式偏移
                    Y = 100 + (_textBoxes.Count * 20),
                    Width = 300,
                    Height = 100,
                    Content = "双击编辑文字",
                    FontSize = 20,  // 默认字号20（实际渲染时会放大2倍）
                    FontFamily = "Microsoft YaHei UI",
                    FontColor = "#000000",
                    ZIndex = _textBoxes.Count
                };

                // 保存到数据库
                await _textProjectManager.AddElementAsync(newElement);

                // 添加到画布
                var textBox = new DraggableTextBox(newElement);
                AddTextBoxToCanvas(textBox);
                
                // 选中新文本框
                textBox.Focus();
                textBox.FocusTextBox();

                System.Diagnostics.Debug.WriteLine($"✅ 添加文本框成功: ID={newElement.Id}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 添加文本框失败: {ex.Message}");
                WpfMessageBox.Show($"添加文本框失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 删除选中的文本框按钮
        /// </summary>
        private async void BtnDeleteText_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTextBox == null)
            {
                WpfMessageBox.Show("请先选择要删除的文本框！", "提示", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var result = WpfMessageBox.Show("确定要删除选中的文本框吗？", "确认删除", 
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result != MessageBoxResult.Yes)
                    return;

                // 从数据库删除
                await _textProjectManager.DeleteElementAsync(_selectedTextBox.Data.Id);

                // 从画布移除
                EditorCanvas.Children.Remove(_selectedTextBox);
                _textBoxes.Remove(_selectedTextBox);

                // 清除选中状态
                _selectedTextBox = null;

                // 标记已修改
                MarkContentAsModified();

                System.Diagnostics.Debug.WriteLine($"✅ 删除文本框成功");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 删除文本框失败: {ex.Message}");
                WpfMessageBox.Show($"删除文本框失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 背景按钮点击（显示菜单）
        /// </summary>
        private void BtnBackground_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTextProject == null)
                return;

            var contextMenu = new ContextMenu();
            contextMenu.FontSize = 14;

            // 选项1：导入图片
            var loadImageItem = new MenuItem 
            { 
                Header = "🖼 导入图片",
                Height = 36
            };
            loadImageItem.Click += BtnLoadBackgroundImage_Click;
            contextMenu.Items.Add(loadImageItem);

            // 选项2：选择颜色
            var selectColorItem = new MenuItem 
            { 
                Header = "🎨 选择颜色",
                Height = 36
            };
            selectColorItem.Click += BtnSelectBackgroundColor_Click;
            contextMenu.Items.Add(selectColorItem);

            // 选项3：清除背景
            contextMenu.Items.Add(new Separator());
            var clearItem = new MenuItem 
            { 
                Header = "🗑 清除背景",
                Height = 36
            };
            clearItem.Click += BtnClearBackground_Click;
            contextMenu.Items.Add(clearItem);

            contextMenu.PlacementTarget = sender as UIElement;
            contextMenu.IsOpen = true;
        }

        /// <summary>
        /// 导入背景图片
        /// </summary>
        private async void BtnLoadBackgroundImage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTextProject == null)
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
                    
                    await _textProjectManager.UpdateBackgroundImageAsync(_currentTextProject.Id, dialog.FileName);
                    
                    System.Diagnostics.Debug.WriteLine($"✅ 背景图加载成功: {dialog.FileName}");
                    MarkContentAsModified();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 加载背景图失败: {ex.Message}");
                    WpfMessageBox.Show($"加载背景图失败: {ex.Message}", "错误", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 选择背景颜色
        /// </summary>
        private void BtnSelectBackgroundColor_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTextProject == null)
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

                    // 设置Canvas背景色
                    EditorCanvas.Background = new SolidColorBrush(wpfColor);
                    
                    // 隐藏背景图片
                    BackgroundImage.Visibility = Visibility.Collapsed;
                    BackgroundImage.Source = null;
                    
                    // 清除数据库中的背景图片路径
                    _ = _textProjectManager.UpdateBackgroundImageAsync(_currentTextProject.Id, null);
                    
                    System.Diagnostics.Debug.WriteLine($"✅ 背景色设置成功: #{wpfColor.R:X2}{wpfColor.G:X2}{wpfColor.B:X2}");
                    MarkContentAsModified();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 设置背景色失败: {ex.Message}");
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
            if (_currentTextProject == null)
                return;

            try
            {
                // 清除背景图片
                BackgroundImage.Source = null;
                BackgroundImage.Visibility = Visibility.Collapsed;
                
                // 重置Canvas背景为白色
                EditorCanvas.Background = new SolidColorBrush(Colors.White);
                
                // 清除数据库中的背景图片路径
                await _textProjectManager.UpdateBackgroundImageAsync(_currentTextProject.Id, null);
                
                System.Diagnostics.Debug.WriteLine("✅ 背景已清除");
                MarkContentAsModified();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 清除背景失败: {ex.Message}");
                WpfMessageBox.Show($"清除背景失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
                
                System.Diagnostics.Debug.WriteLine($"✅ 字体已更改: {fontData.Config.Name}");
                System.Diagnostics.Debug.WriteLine($"   保存到数据库: {fontFamilyName}");
                System.Diagnostics.Debug.WriteLine($"   应用的FontFamily: {fontData.FontFamily.Source}");
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
                // 限制范围
                fontSize = Math.Max(20, Math.Min(200, fontSize));
                
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
                // 滚轮向上增大，向下减小，每次步进5
                int delta = e.Delta > 0 ? 5 : -5;
                int newSize = Math.Max(20, Math.Min(200, currentSize + delta));
                
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
                int newSize = Math.Max(20, currentSize - 5);
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
                int newSize = Math.Min(200, currentSize + 5);
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

                System.Diagnostics.Debug.WriteLine($"✅ 创建水平对称元素成功");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 创建对称元素失败: {ex.Message}");
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

                System.Diagnostics.Debug.WriteLine($"✅ 创建垂直对称元素成功");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 创建对称元素失败: {ex.Message}");
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
                // 批量更新所有元素
                await _textProjectManager.UpdateElementsAsync(_textBoxes.Select(tb => tb.Data));

                // 🆕 保存成功后，恢复按钮为白色
                BtnSaveTextProject.Background = new SolidColorBrush(Colors.White);
                
                System.Diagnostics.Debug.WriteLine($"✅ 保存项目成功: {_currentTextProject.Name}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 保存项目失败: {ex.Message}");
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
        /// 画布点击（取消选中）
        /// </summary>
        private void EditorCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == EditorCanvas || e.OriginalSource == BackgroundImage)
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
                System.Diagnostics.Debug.WriteLine($"文本内容改变: {content}");
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
        }

        /// <summary>
        /// 🆕 标记内容已修改（保存按钮变绿）
        /// </summary>
        private void MarkContentAsModified()
        {
            if (BtnSaveTextProject.Background is SolidColorBrush brush && brush.Color == Colors.LightGreen)
                return; // 已经是绿色，不重复设置

            BtnSaveTextProject.Background = new SolidColorBrush(Colors.LightGreen);
            System.Diagnostics.Debug.WriteLine("🟢 内容已修改，保存按钮变绿");
        }

        /// <summary>
        /// 根据选中的文本框更新工具栏状态
        /// </summary>
        private void UpdateToolbarFromSelection()
        {
            if (_selectedTextBox == null) return;

            // 更新字体选择器
            var fontFamily = _selectedTextBox.Data.FontFamily;
            System.Diagnostics.Debug.WriteLine($"🔍 同步字体选择器: {fontFamily}");
            
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
                        System.Diagnostics.Debug.WriteLine($"✅ 找到匹配字体（完整URI）: {fontData.Config.Name}");
                        break;
                    }
                    
                    // 情况2：旧数据格式匹配（只有字体族名称）
                    if (fontData.Config.Family == fontFamily)
                    {
                        FontFamilySelector.SelectedIndex = i;
                        System.Diagnostics.Debug.WriteLine($"✅ 找到匹配字体（族名称）: {fontData.Config.Name}");
                        
                        // 🔧 自动修复：更新文本框的字体为完整URI
                        _selectedTextBox.Data.FontFamily = fontSource;
                        System.Diagnostics.Debug.WriteLine($"🔧 自动修复字体URI: {fontSource}");
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
        /// 🆕 从Canvas更新投影（核心投影功能）
        /// </summary>
        private void UpdateProjectionFromCanvas()
        {
            if (!projectionManager.IsProjectionActive)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ 投影未开启，无法更新投影内容");
                WpfMessageBox.Show("请先开启投影！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine("🎨 开始渲染Canvas到投影...");
                System.Diagnostics.Debug.WriteLine($"   Canvas尺寸: {EditorCanvas.Width}x{EditorCanvas.Height}");
                System.Diagnostics.Debug.WriteLine($"   文本框数量: {_textBoxes.Count}");
                
                // 1. 将Canvas渲染为位图
                var renderBitmap = RenderCanvasToBitmap(EditorCanvas);
                System.Diagnostics.Debug.WriteLine($"   渲染位图: {renderBitmap.PixelWidth}x{renderBitmap.PixelHeight}");

                // 2. 转换为ImageSharp格式
                var image = ConvertBitmapToImageSharp(renderBitmap);
                System.Diagnostics.Debug.WriteLine($"   ImageSharp图像: {image.Width}x{image.Height}");

                // 3. 更新投影
                projectionManager.UpdateProjectionImage(image, false, 1.0, false);

                System.Diagnostics.Debug.WriteLine("✅ 投影更新成功");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 更新投影失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   堆栈: {ex.StackTrace}");
                WpfMessageBox.Show($"更新投影失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 将Canvas渲染为位图
        /// </summary>
        private RenderTargetBitmap RenderCanvasToBitmap(Canvas canvas)
        {
            // 确保Canvas已完成布局
            canvas.Measure(new System.Windows.Size(canvas.Width, canvas.Height));
            canvas.Arrange(new Rect(new System.Windows.Size(canvas.Width, canvas.Height)));
            canvas.UpdateLayout();

            // 渲染到位图
            var renderBitmap = new RenderTargetBitmap(
                (int)canvas.Width,
                (int)canvas.Height,
                96, 96,
                PixelFormats.Pbgra32);

            renderBitmap.Render(canvas);
            return renderBitmap;
        }

        /// <summary>
        /// 将WPF位图转换为ImageSharp格式
        /// </summary>
        private Image<Rgba32> ConvertBitmapToImageSharp(BitmapSource bitmap)
        {
            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;

            // 创建ImageSharp图片
            var image = new Image<Rgba32>(width, height);

            // 从WPF位图读取像素
            int stride = width * 4; // BGRA32 = 4 bytes per pixel
            byte[] pixels = new byte[height * stride];
            bitmap.CopyPixels(pixels, stride, 0);

            // 转换BGRA到RGBA并写入ImageSharp
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int offset = y * stride + x * 4;
                    byte b = pixels[offset];
                    byte g = pixels[offset + 1];
                    byte r = pixels[offset + 2];
                    byte a = pixels[offset + 3];

                    image[x, y] = new Rgba32(r, g, b, a);
                }
            }

            return image;
        }

        /// <summary>
        /// 重命名文本项目 - 进入内联编辑模式
        /// </summary>
        private void RenameTextProjectAsync(ProjectTreeItem item)
        {
            if (item == null || item.Type != TreeItemType.TextProject)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ 无法重命名: item null 或类型不匹配");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"📝 进入编辑模式: ID={item.Id}, Name={item.Name}");
            
            // 保存原始名称
            item.OriginalName = item.Name;
            
            // 进入编辑模式
            item.IsEditing = true;
            
            System.Diagnostics.Debug.WriteLine($"✅ IsEditing 已设置为 true, OriginalName={item.OriginalName}");
        }

        /// <summary>
        /// 完成内联重命名
        /// </summary>
        private async Task CompleteRenameAsync(ProjectTreeItem item, string newName)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"💾 完成重命名: OriginalName={item.OriginalName}, CurrentName={item.Name}, NewName={newName}");
                
                // 如果取消或输入为空，恢复原始名称
                if (string.IsNullOrWhiteSpace(newName))
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ 名称为空，恢复原始名称");
                    item.Name = item.OriginalName;
                    item.IsEditing = false;
                    return;
                }

                // 如果名称未改变，直接返回
                if (newName.Trim() == item.OriginalName)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ 名称未改变，取消编辑");
                    item.IsEditing = false;
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"🔄 开始保存项目: ID={item.Id}, {item.OriginalName} -> {newName.Trim()}");
                
                // 加载并更新项目
                var project = await _textProjectManager.LoadProjectAsync(item.Id);
                if (project != null)
                {
                    System.Diagnostics.Debug.WriteLine($"✅ 项目加载成功，更新名称");
                    
                    project.Name = newName.Trim();
                    await _textProjectManager.SaveProjectAsync(project);
                    
                    // 更新树节点（Name 已经通过绑定更新了，只需更新 OriginalName）
                    item.OriginalName = newName.Trim();
                    item.IsEditing = false;
                    
                    ShowStatus($"✅ 项目已重命名: {newName}");
                    System.Diagnostics.Debug.WriteLine($"✅ 项目已重命名: ID={item.Id}, NewName={newName}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 项目加载失败: ID={item.Id}，恢复原始名称");
                    item.Name = item.OriginalName;
                    item.IsEditing = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 重命名项目失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ 堆栈跟踪: {ex.StackTrace}");
                
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
                    System.Diagnostics.Debug.WriteLine($"✅ 已删除项目: ID={item.Id}, Name={item.Name}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 删除项目失败: {ex.Message}");
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
            System.Diagnostics.Debug.WriteLine($"🔍 TreeItemEditBox_Loaded 触发");
            
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                System.Diagnostics.Debug.WriteLine($"🔍 TextBox 实例: Text={textBox.Text}, Visibility={textBox.Visibility}");
                
                if (textBox.DataContext is ProjectTreeItem item)
                {
                    System.Diagnostics.Debug.WriteLine($"🔍 DataContext: Name={item.Name}, IsEditing={item.IsEditing}");
                    
                    // 只在编辑模式时才聚焦
                    if (!item.IsEditing)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ IsEditing=false，跳过聚焦");
                        return;
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"📝 编辑框加载: Text={textBox.Text}, IsEditing={item.IsEditing}");
                    
                    // 延迟聚焦，确保UI完全加载
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (item.IsEditing) // 再次检查，防止在延迟期间状态改变
                        {
                            bool focused = textBox.Focus();
                            textBox.CaretIndex = textBox.Text.Length; // 光标定位到末尾
                            System.Diagnostics.Debug.WriteLine($"✅ 编辑框已聚焦: Success={focused}, 光标位置: {textBox.CaretIndex}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"⚠️ 延迟检查时 IsEditing=false");
                        }
                    }), System.Windows.Threading.DispatcherPriority.Input);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ DataContext 不是 ProjectTreeItem: {textBox.DataContext?.GetType().Name}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ sender 不是 TextBox: {sender?.GetType().Name}");
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
                System.Diagnostics.Debug.WriteLine($"🔍 编辑框可见性改变: IsVisible={isVisible}, IsEditing={item.IsEditing}, Name={item.Name}");
                
                // 当变为可见且处于编辑模式时，聚焦并定位光标
                if (isVisible && item.IsEditing)
                {
                    System.Diagnostics.Debug.WriteLine($"📝 编辑框变为可见，准备聚焦");
                    
                    // 延迟聚焦，确保控件完全渲染
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (item.IsEditing && textBox.IsVisible)
                        {
                            bool focused = textBox.Focus();
                            textBox.CaretIndex = textBox.Text.Length;
                            System.Diagnostics.Debug.WriteLine($"✅ 编辑框已聚焦: Success={focused}, CaretIndex={textBox.CaretIndex}, IsFocused={textBox.IsFocused}");
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
            System.Diagnostics.Debug.WriteLine($"⚠️ 未找到字体: {fontFamilyName}，将使用默认字体");
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
            
            System.Diagnostics.Debug.WriteLine("状态: ✅ 已退出文本编辑器，返回图片/视频浏览模式");
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

