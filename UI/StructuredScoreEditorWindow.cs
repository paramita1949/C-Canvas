using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Markup;
using ImageColorChanger.Services.OnlineScores;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Brush = System.Windows.Media.Brush;

namespace ImageColorChanger.UI
{
    /// <summary>谱文自带分页优先，紧凑数字控制手动分页；不执行输入代码。</summary>
    public sealed class StructuredScoreEditorWindow : Window
    {
        private readonly TextBox _title, _notation, _pageMarks;
        private readonly ComboBox _layout, _textColor;
        private string _textColorHex;
        private readonly string _defaultTextColor;
        [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
        private sealed record ColorChoice(string Name, string Hex)
        {
            public string Swatch => Hex == null || Hex == "custom" ? "#94A3B8" : Hex;
            public override string ToString() => Name;
        }
        private readonly TextBlock _status, _pageSummary, _pageNumber, _pageHint, _previewLabel, _codeLineCount;
        private readonly Button _save;
        private readonly Grid _body, _labels;
        private readonly Border _codePane;
        private readonly GridSplitter _codeSplitter;
        private readonly Button _expandCode;
        private GridLength _codeWidth, _previewWidth;
        private bool _codeExpanded;
        private readonly Border _previewPane;
        private readonly Image _preview;
        private readonly StructuredScoreDocument _original;
        private readonly Func<StructuredScoreDocument, Task> _applyCuts;
        private readonly DispatcherTimer _editTimer;
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private CancellationTokenSource _analysisCts;
        private int[][] _pageLines;
        private int[] _combinedPageLines;
        private int[][] _defaultPageLines;
        private int[] _defaultCombinedPageLines;
        private bool? _pageLinesManual, _combinedPageLinesManual;
        private int _totalLines, _previewPage;
        private bool _suppressChanges, _busy, _needsAnalysis, _numbersDirty, _writing, _closed, _closingEdit;
        private string _analyzedNotation;
        private long _editVersion;
        private IReadOnlyList<string> _previewPages;
        public StructuredScoreDocument Result { get; private set; }

        public StructuredScoreEditorWindow(StructuredScoreDocument document) : this(document, null) { }
        public StructuredScoreEditorWindow(StructuredScoreDocument document, Func<StructuredScoreDocument, Task> applyCuts) : this(document, applyCuts, "#DAA520") { }
        public StructuredScoreEditorWindow(StructuredScoreDocument document, Func<StructuredScoreDocument, Task> applyCuts, string defaultTextColor)
        {
            _original = document; _applyCuts = applyCuts; _textColorHex = document.TextColor; _defaultTextColor = defaultTextColor;
            _pageLines = document.PageLines?.Select(p => p.ToArray()).ToArray();
            _combinedPageLines = document.CombinedPageLines?.ToArray();
            _pageLinesManual = document.PageLinesManual; _combinedPageLinesManual = document.CombinedPageLinesManual;
            Title = "结构化歌谱"; Width = 1100; Height = 730; MinWidth = 840; MinHeight = 520;
            FontFamily = new FontFamily("Microsoft YaHei"); FontSize = 13;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            InitializeEditorStyles(); Background = EditorBrush("#F3F4F7"); Foreground = EditorBrush("#242B38");
            var root = new DockPanel { Margin = new Thickness(24, 16, 24, 16), Background = Background }; Content = root;
            var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions);
            var close = MakeButton(applyCuts == null ? "取消" : "关闭"); close.IsCancel = true; actions.Children.Add(close);
            _save = MakeButton("保存"); _save.Background = EditorBrush("#2563EB"); _save.Foreground = Brushes.White;
            _save.Click += async (_, _) => await SaveScoreAsync(); actions.Children.Add(_save);
            _status = new TextBlock { Foreground = EditorBrush("#64748B"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            footer.Children.Add(_status);
            _title = new TextBox { Text = document.Title, FontSize = 22, Height = 36, FontWeight = FontWeights.SemiBold,
                Style = (Style)Resources["ScoreEditorTitle"], Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(4, 1, 8, 1),
                Margin = new Thickness(-4, 0, 0, 4), ToolTip = "点击编辑歌谱标题", VerticalContentAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(_title, "歌谱标题");
            DockPanel.SetDock(_title, Dock.Top); root.Children.Add(_title);
            var tools = new DockPanel { Margin = new Thickness(0, 0, 0, 8), Height = 36 };
            DockPanel.SetDock(tools, Dock.Top); root.Children.Add(tools);
            var viewTools = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(viewTools, Dock.Right); tools.Children.Add(viewTools);
            _layout = new ComboBox { Width = 96, ItemsSource = new[] { "分页", "合页", "长页" }, SelectedIndex = (int)document.Layout,
                Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(8, 0, 0, 0), ToolTip = "显示版式" };
            System.Windows.Automation.AutomationProperties.SetName(_layout, "显示版式");
            var choices = new List<ColorChoice> { new("跟随显示", null), new("黄色", "#FFFF00"), new("秋麒麟", "#DAA520"), new("白色", "#FFFFFF"), new("黑色", "#000000") };
            if (_textColorHex != null && !choices.Any(c => c.Hex == _textColorHex)) choices.Add(new("自定义", _textColorHex));
            choices.Add(new("更多色盘…", "custom"));
            _textColor = new ComboBox { Width = 132, ItemsSource = choices, SelectedItem = choices.First(c => c.Hex == _textColorHex),
                ItemTemplate = (DataTemplate)Resources["ScoreEditorColorChoice"],
                ToolTip = "直接绘制谱字、歌词及符号；颜色随歌谱保存。" };
            System.Windows.Automation.AutomationProperties.SetName(_textColor, "歌谱字色");
            viewTools.Children.Add(_textColor); viewTools.Children.Add(_layout);
            var numberRow = new StackPanel { Orientation = Orientation.Horizontal }; tools.Children.Add(numberRow);
            numberRow.Children.Add(new TextBlock { Text = "分页脚本", Foreground = EditorBrush("#5C6474"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            _pageMarks = new TextBox { Width = 172, MaxWidth = 220, AcceptsReturn = false, FontFamily = new FontFamily("Consolas"), FontSize = 17,
                Background = EditorBrush("#E7EAF0"), BorderThickness = new Thickness(0), Padding = new Thickness(4, 5, 4, 5), VerticalContentAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, ToolTip = "分页脚本：默认使用谱文分页；手动如333，末页自动收余。兼容空格，多位行数如12 4。" };
            System.Windows.Automation.AutomationProperties.SetName(_pageMarks, "每页谱行数");
            var marksHost = new Grid(); marksHost.Children.Add(_pageMarks);
            _pageHint = new TextBlock { IsHitTestVisible = false, Foreground = EditorBrush("#929CAA"), FontFamily = new FontFamily("Consolas"), FontSize = 17,
                Margin = new Thickness(7, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            marksHost.Children.Add(_pageHint); numberRow.Children.Add(marksHost);
            var auto = MakeButton("默认"); auto.ToolTip = "恢复原谱分页；无标记时自动分配"; auto.Background = Brushes.Transparent; auto.Foreground = EditorBrush("#2563EB"); numberRow.Children.Add(auto);
            _pageSummary = new TextBlock { Foreground = EditorBrush("#64748B"), MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            numberRow.Children.Add(_pageSummary);
            var labels = new Grid { Margin = new Thickness(0, 0, 0, 8) }; _labels = labels; labels.ColumnDefinitions.Add(new ColumnDefinition()); labels.ColumnDefinitions.Add(new ColumnDefinition());
            labels.ColumnDefinitions[0].Width = new GridLength(0.88, GridUnitType.Star); labels.ColumnDefinitions[1].Width = new GridLength(1.12, GridUnitType.Star);
            labels.ColumnDefinitions.Insert(1, new ColumnDefinition { Width = new GridLength(14) });
            DockPanel.SetDock(labels, Dock.Top); root.Children.Add(labels);
            var codeLabel = new DockPanel();
            _expandCode = MakeButton("⛶"); _expandCode.Padding = new Thickness(4, 0, 4, 0); _expandCode.MinHeight = 22; _expandCode.FontSize = 16;
            _expandCode.Background = Brushes.Transparent; _expandCode.ToolTip = "展开编辑"; _expandCode.Click += (_, _) => ToggleCodeExpanded();
            System.Windows.Automation.AutomationProperties.SetName(_expandCode, "展开或收起代码编辑区");
            DockPanel.SetDock(_expandCode, Dock.Right); codeLabel.Children.Add(_expandCode);
            var codeHeading = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            codeHeading.Children.Add(new TextBlock { Text = "歌谱代码", Foreground = EditorBrush("#64748B") });
            _codeLineCount = new TextBlock { Foreground = EditorBrush("#64748B"), Margin = new Thickness(6, 0, 0, 0) };
            codeHeading.Children.Add(_codeLineCount); codeLabel.Children.Add(codeHeading); labels.Children.Add(codeLabel);
            var previewLabel = new TextBlock { Text = "预览", Foreground = EditorBrush("#64748B") }; _previewLabel = previewLabel; Grid.SetColumn(previewLabel, 2); labels.Children.Add(previewLabel);
            _body = new Grid(); _body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.88, GridUnitType.Star), MinWidth = 220 });
            _body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            _body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.12, GridUnitType.Star), MinWidth = 260 }); root.Children.Add(_body);
            _notation = new TextBox { Text = document.Notation, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas, Microsoft YaHei"), FontSize = 14, Padding = new Thickness(4),
                BorderThickness = new Thickness(0), Background = Brushes.White };
            System.Windows.Automation.AutomationProperties.SetName(_notation, "歌谱代码");
            var codePane = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(16), Padding = new Thickness(14), Child = _notation }; _codePane = codePane;
            _body.Children.Add(codePane);
            _codeSplitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch,
                Background = Brushes.Transparent, ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext, ShowsPreview = true, Cursor = System.Windows.Input.Cursors.SizeWE };
            Grid.SetColumn(_codeSplitter, 1); _body.Children.Add(_codeSplitter);
            _codeSplitter.DragCompleted += (_, _) => { _labels.ColumnDefinitions[0].Width = _body.ColumnDefinitions[0].Width; _labels.ColumnDefinitions[2].Width = _body.ColumnDefinitions[2].Width; };
            _previewPane = new Border { Visibility = Visibility.Visible, Background = Brushes.Black,
                BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(16), ClipToBounds = true };
            _previewPane.SizeChanged += (_, _) => _previewPane.Clip = new RectangleGeometry(new Rect(0, 0, _previewPane.ActualWidth, _previewPane.ActualHeight), 16, 16);
            Grid.SetColumn(_previewPane, 2); _body.Children.Add(_previewPane);
            var previewRoot = new Grid(); _previewPane.Child = previewRoot;
            var paging = new StackPanel { Orientation = Orientation.Horizontal };
            var pagingCapsule = new Border { Child = paging, Background = EditorBrush("#282B34"), CornerRadius = new CornerRadius(20), Padding = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12) };
            var previous = MakeButton("‹"); previous.Style = (Style)Resources["ScoreEditorPagingButton"]; previous.Margin = new Thickness(0);
            previous.ToolTip = "上一页"; System.Windows.Automation.AutomationProperties.SetName(previous, "预览上一页");
            previous.Click += (_, _) => ShowPreviewPage(_previewPage - 1); paging.Children.Add(previous);
            _pageNumber = new TextBlock { Foreground = EditorBrush("#4ADE80"), FontWeight = FontWeights.Bold, FontSize = 16,
                Margin = new Thickness(12, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center }; paging.Children.Add(_pageNumber);
            var next = MakeButton("›"); next.Style = (Style)Resources["ScoreEditorPagingButton"];
            next.ToolTip = "下一页"; System.Windows.Automation.AutomationProperties.SetName(next, "预览下一页");
            next.Click += (_, _) => ShowPreviewPage(_previewPage + 1); paging.Children.Add(next);
            _preview = new Image { Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top };
            previewRoot.Children.Add(new ScrollViewer { Content = _preview, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8, 8, 8, 64) });
            previewRoot.Children.Add(pagingCapsule);
            _editTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
            _editTimer.Tick += async (_, _) => { _editTimer.Stop(); if (_needsAnalysis) await AnalyzeAsync(true); else if (_numbersDirty) await ApplyNumbersAsync(); };
            _notation.TextChanged += (_, _) => ScheduleEdit(true);
            _title.TextChanged += (_, _) => ScheduleEdit(false);
            _pageMarks.TextChanged += (_, _) => { UpdatePageHintVisibility(); ScheduleEdit(false); };
            _pageMarks.GotKeyboardFocus += (_, _) => UpdatePageHintVisibility();
            _pageMarks.LostKeyboardFocus += (_, _) => UpdatePageHintVisibility();
            _layout.SelectionChanged += (_, _) => ScheduleEdit(true);
            auto.Click += async (_, _) =>
            {
                _suppressChanges = true; _pageMarks.Clear(); _suppressChanges = false;
                if (_analyzedNotation != _notation.Text) await AnalyzeAsync(true);
                if (_analyzedNotation == _notation.Text) await ApplyNumbersAsync();
            };
            _textColor.SelectionChanged += (_, _) =>
            {
                if (_suppressChanges) return;
                var selected = (ColorChoice)_textColor.SelectedItem;
                if (selected?.Hex == "custom")
                {
                    using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
                    var color = StructuredScoreDocument.ParseTextColor(_textColorHex ?? _defaultTextColor);
                    dialog.Color = System.Drawing.Color.FromArgb(color.Red, color.Green, color.Blue);
                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        _textColorHex = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
                        var custom = new ColorChoice("自定义", _textColorHex); choices.Insert(choices.Count - 1, custom);
                        _suppressChanges = true; _textColor.Items.Refresh(); _textColor.SelectedItem = custom; _suppressChanges = false;
                    }
                    else { _suppressChanges = true; _textColor.SelectedItem = choices.First(c => c.Hex == _textColorHex); _suppressChanges = false; return; }
                }
                else _textColorHex = selected?.Hex;
                ScheduleEdit(false);
            };
            WriteMarks();
            Loaded += async (_, _) => await AnalyzeAsync(false);
            Closing += async (_, e) =>
            {
                if (_writing || _closingEdit) { e.Cancel = true; _status.Text = "正在自动保存，请稍候…"; return; }
                if (_numbersDirty && !_needsAnalysis && _applyCuts != null && _notation.Text == _original.Notation)
                {
                    e.Cancel = true; _closingEdit = true;
                    await ApplyNumbersAsync(); _closingEdit = false;
                    // 同步完成的保存也不能在Closing事件栈内再次Close，交给下一轮Dispatcher。
                    if (_save.IsEnabled) _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Close));
                }
            };
            Closed += (_, _) => { _closed = true; ++_editVersion; _editTimer.Stop(); _analysisCts?.Cancel(); };
        }

        private static Brush EditorBrush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex);
        private void ToggleCodeExpanded()
        {
            if (!_codeExpanded)
            {
                _codeWidth = _body.ColumnDefinitions[0].Width; _previewWidth = _body.ColumnDefinitions[2].Width;
                Grid.SetColumnSpan(_codePane, 3); _previewPane.Visibility = Visibility.Collapsed; _codeSplitter.Visibility = Visibility.Collapsed;
                _previewLabel.Visibility = Visibility.Collapsed;
                _expandCode.ToolTip = "返回预览";
            }
            else
            {
                Grid.SetColumnSpan(_codePane, 1); _body.ColumnDefinitions[0].Width = _codeWidth; _body.ColumnDefinitions[2].Width = _previewWidth;
                _previewPane.Visibility = Visibility.Visible; _codeSplitter.Visibility = Visibility.Visible; _expandCode.ToolTip = "展开编辑";
                _previewLabel.Visibility = Visibility.Visible;
                if (_analyzedNotation == _notation.Text && !_busy)
                {
                    try { _ = RefreshPreviewAsync(ReadEditor()); }
                    catch (ScoreIntegrationException) { /* 编辑未完成时保持当前预览，不打断展开/收起。 */ }
                }
            }
            _codeExpanded = !_codeExpanded;
            _notation.Focus();
        }
        private Button MakeButton(string text) => new() { Content = text, Style = (Style)Resources["ScoreEditorButton"], Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(8, 0, 0, 0) };
        private void InitializeEditorStyles()
        {
            // 局部轻量模板隔离应用全局深色Material主题，不改变其它窗口，也不引入动画和图片装饰。
            Resources = (ResourceDictionary)XamlReader.Parse(@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <Style x:Key='ScoreEditorButton' TargetType='Button'>
  <Setter Property='Background' Value='#E7EAF0'/><Setter Property='Foreground' Value='#334155'/>
  <Setter Property='BorderBrush' Value='Transparent'/><Setter Property='BorderThickness' Value='1'/>
  <Setter Property='Cursor' Value='Hand'/><Setter Property='FontWeight' Value='SemiBold'/><Setter Property='MinHeight' Value='36'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
   <Border x:Name='Surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='10' Padding='{TemplateBinding Padding}'>
    <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
   </Border>
   <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Opacity' Value='0.85'/></Trigger>
    <Trigger Property='IsPressed' Value='True'><Setter TargetName='Surface' Property='Opacity' Value='0.65'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Surface' Property='Opacity' Value='0.45'/></Trigger>
    <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#2563EB'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='ScoreEditorPagingButton' TargetType='Button' BasedOn='{StaticResource ScoreEditorButton}'>
  <Setter Property='Background' Value='Transparent'/><Setter Property='Foreground' Value='White'/><Setter Property='FontSize' Value='20'/>
  <Setter Property='MinWidth' Value='32'/><Setter Property='MinHeight' Value='30'/><Setter Property='Padding' Value='8,0'/>
 </Style>
 <Style TargetType='TextBox'>
  <Setter Property='Background' Value='White'/><Setter Property='Foreground' Value='#172338'/><Setter Property='CaretBrush' Value='#172338'/>
  <Setter Property='SelectionBrush' Value='#BFDBFE'/><Setter Property='BorderBrush' Value='Transparent'/><Setter Property='BorderThickness' Value='0'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'>
   <Border x:Name='Surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='10'>
    <ScrollViewer x:Name='PART_ContentHost'/>
   </Border>
   <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#CCD3DE'/></Trigger>
    <Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#2563EB'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Surface' Property='Opacity' Value='0.5'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style x:Key='ScoreEditorTitle' TargetType='TextBox' BasedOn='{StaticResource {x:Type TextBox}}'>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'>
   <Border x:Name='Underline' BorderBrush='Transparent' BorderThickness='0,0,0,1'>
    <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}'/>
   </Border>
   <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Underline' Property='BorderBrush' Value='#CCD3DE'/></Trigger>
    <Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Underline' Property='BorderBrush' Value='#2563EB'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='ScrollBar'>
  <Setter Property='Width' Value='8'/><Setter Property='Background' Value='Transparent'/><Setter Property='Foreground' Value='#929CAA'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
   <Track x:Name='PART_Track' IsDirectionReversed='True' Orientation='{TemplateBinding Orientation}'>
    <Track.DecreaseRepeatButton><RepeatButton x:Name='Decrease' Command='ScrollBar.PageUpCommand' Focusable='False'>
     <RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template>
    </RepeatButton></Track.DecreaseRepeatButton>
    <Track.Thumb><Thumb>
     <Thumb.Template><ControlTemplate TargetType='Thumb'><Border Background='#929CAA' CornerRadius='3' Margin='1' Opacity='0.55'/></ControlTemplate></Thumb.Template>
    </Thumb></Track.Thumb>
    <Track.IncreaseRepeatButton><RepeatButton x:Name='Increase' Command='ScrollBar.PageDownCommand' Focusable='False'>
     <RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template>
    </RepeatButton></Track.IncreaseRepeatButton>
   </Track>
   <ControlTemplate.Triggers>
    <Trigger Property='Orientation' Value='Horizontal'><Setter TargetName='PART_Track' Property='IsDirectionReversed' Value='False'/><Setter TargetName='Decrease' Property='Command' Value='{x:Static ScrollBar.PageLeftCommand}'/><Setter TargetName='Increase' Property='Command' Value='{x:Static ScrollBar.PageRightCommand}'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
  <Style.Triggers><Trigger Property='Orientation' Value='Horizontal'><Setter Property='Width' Value='Auto'/><Setter Property='Height' Value='8'/></Trigger></Style.Triggers>
 </Style>
 <DataTemplate x:Key='ScoreEditorColorChoice'>
  <StackPanel Orientation='Horizontal'>
   <Border Width='12' Height='12' CornerRadius='6' Background='{Binding Swatch}' BorderBrush='#8A93A3' BorderThickness='1' Margin='0,0,8,0' VerticalAlignment='Center'/>
   <TextBlock Text='{Binding Name}' VerticalAlignment='Center'/>
  </StackPanel>
 </DataTemplate>
 <Style TargetType='ComboBoxItem'>
  <Setter Property='Foreground' Value='#172338'/><Setter Property='Background' Value='White'/><Setter Property='Padding' Value='12,9'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBoxItem'>
   <Border x:Name='Item' Padding='{TemplateBinding Padding}' Background='{TemplateBinding Background}' CornerRadius='6'><ContentPresenter/></Border>
   <ControlTemplate.Triggers><Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Item' Property='Background' Value='#EFF6FF'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
 <Style TargetType='ComboBox'>
  <Setter Property='Foreground' Value='#172338'/><Setter Property='Background' Value='Transparent'/><Setter Property='BorderBrush' Value='Transparent'/><Setter Property='BorderThickness' Value='0'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBox'>
   <Grid>
    <ToggleButton Style='{x:Null}' OverridesDefaultStyle='True' HorizontalAlignment='Stretch' VerticalAlignment='Stretch' MinWidth='0' MinHeight='0' Padding='0' Focusable='False' IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}' ClickMode='Press'>
     <ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border Background='{Binding Background, RelativeSource={RelativeSource AncestorType=ComboBox}}' CornerRadius='10'/></ControlTemplate></ToggleButton.Template>
    </ToggleButton>
    <ContentPresenter Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' Margin='10,6,28,6' VerticalAlignment='Center' IsHitTestVisible='False'/>
    <Path Data='M0 0L4 4L8 0' Stroke='#64748B' StrokeThickness='1.5' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,14,0' IsHitTestVisible='False'/>
    <Border x:Name='FocusRing' CornerRadius='10' BorderBrush='Transparent' BorderThickness='1' IsHitTestVisible='False'/>
    <Popup x:Name='PART_Popup' IsOpen='{TemplateBinding IsDropDownOpen}' Placement='Bottom' AllowsTransparency='True' Focusable='False'>
     <Border Background='White' BorderBrush='#DCE3EC' BorderThickness='1' CornerRadius='12' Padding='4' MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}' Margin='0,4,0,0'>
      <ScrollViewer MaxHeight='240'><ItemsPresenter/></ScrollViewer>
     </Border>
    </Popup>
   </Grid>
   <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='FocusRing' Property='BorderBrush' Value='#CCD3DE'/></Trigger>
    <Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='FocusRing' Property='BorderBrush' Value='#2563EB'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
   </ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
 </Style>
</ResourceDictionary>");
        }
        private void ScheduleEdit(bool analyze)
        {
            if (_suppressChanges || _closed) return;
            ++_editVersion; _analysisCts?.Cancel();
            _needsAnalysis |= analyze; _numbersDirty |= !analyze;
            _save.IsEnabled = false; _editTimer.Stop(); _editTimer.Start();
            if (!analyze) UpdateSummary();
        }
        private void WriteMarks()
        {
            _suppressChanges = true;
            var layout = (StructuredScoreLayout)_layout.SelectedIndex;
            var values = layout == StructuredScoreLayout.Paginated ? _pageLines?.SelectMany(p => p).ToArray() : _combinedPageLines;
            bool manual = layout == StructuredScoreLayout.Paginated ? _pageLinesManual == true : _combinedPageLinesManual == true;
            _pageMarks.Text = layout == StructuredScoreLayout.LongPage || values == null || !manual ? "" : FormatNumbers(values);
            var defaults = layout == StructuredScoreLayout.Paginated ? _defaultPageLines?.SelectMany(p => p).ToArray() : _defaultCombinedPageLines;
            _pageHint.Text = layout == StructuredScoreLayout.LongPage ? "" : FormatNumbers(defaults ?? values);
            UpdatePageHintVisibility();
            _pageMarks.IsEnabled = layout != StructuredScoreLayout.LongPage;
            _suppressChanges = false; UpdateSummary();
        }
        private void UpdatePageHintVisibility()
        {
            _pageHint.Visibility = string.IsNullOrWhiteSpace(_pageMarks.Text) && !_pageMarks.IsKeyboardFocusWithin
                ? Visibility.Visible : Visibility.Collapsed;
        }
        private static string FormatNumbers(int[] values) => values == null ? "" : string.Join(values.All(n => n < 10) ? "" : " ", values);
        private int[] ReadNumbers()
        {
            if (string.IsNullOrWhiteSpace(_pageMarks.Text))
            {
                var defaults = (StructuredScoreLayout)_layout.SelectedIndex == StructuredScoreLayout.Paginated ? _defaultPageLines?.SelectMany(p => p).ToArray() : _defaultCombinedPageLines;
                if (defaults == null) throw new ScoreIntegrationException("谱文正在分析，请稍候。");
                return defaults.ToArray();
            }
            var values = ParsePageNumbers(_pageMarks.Text, _totalLines);
            if (values.Length > 6000 || values.Sum(n => (long)n) != _totalLines)
                throw new ScoreIntegrationException($"本版式共 {_totalLines} 行，当前合计 {values.Sum(n => (long)n)} 行；请完整分配，不漏行、不重复。");
            return values;
        }
        private static int[] ParsePageNumbers(string text, int totalLines)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ScoreIntegrationException("填写分页脚本，或点击自动分配。");
            if (text.Length > 30000) throw new ScoreIntegrationException("分页脚本过长。");
            var tokens = Regex.Split(text.Trim(), @"\s+");
            if (tokens.Any(t => t.Any(c => c < '0' || c > '9')))
                throw new ScoreIntegrationException("分页脚本只接受正整数，可连续输入或用空格隔开。");
            var spaced = tokens.Select(t => int.TryParse(t, out int value) && value > 0 && value <= 6000 ? value : 0).ToArray();
            // 完整旧方案保持原义；有空格且各段不超过总行数时，兼容12 4这样的多位行数。
            // 4444、4 423仍分别是4 4 4 4、4 4 2 3。只在编辑器解析末页收余，文件仍保存完整方案。
            if (spaced.All(n => n > 0) && spaced.Sum(n => (long)n) == totalLines) return spaced;
            if (tokens.Where((t, i) => t.Contains('0') && spaced[i] == 0).Any())
                throw new ScoreIntegrationException("每页行数必须大于0，且不能超过6000。");
            var compact = tokens.SelectMany((t, i) => t.Contains('0') ? new[] { spaced[i] } : t.Select(c => c - '0')).ToArray();
            if (compact.Length > 6000) throw new ScoreIntegrationException("分页脚本过长。");
            var requested = tokens.Length > 1 && spaced.All(n => n > 0 && n <= totalLines) ? spaced : compact;
            if (totalLines <= 0) return requested;
            var pages = new List<int>(); int remaining = totalLines;
            for (int i = 0; i < requested.Length && remaining > 0; i++)
            {
                int count = i == requested.Length - 1 ? remaining : Math.Min(requested[i], remaining);
                if (count > 6000) throw new ScoreIntegrationException("每页行数必须大于0，且不能超过6000。");
                pages.Add(count); remaining -= count;
            }
            return pages.ToArray();
        }
        private void UpdateSummary()
        {
            if ((StructuredScoreLayout)_layout.SelectedIndex == StructuredScoreLayout.LongPage)
            { _pageSummary.Text = "长页"; return; }
            try
            {
                var values = ReadNumbers();
                long total = values.Sum(n => (long)n);
                string kind = !string.IsNullOrWhiteSpace(_pageMarks.Text) ? "手动" : Regex.IsMatch(_notation.Text, @"^\s*\*\s*$", RegexOptions.Multiline) ? "原谱" : "自动";
                _pageSummary.Text = $"{kind} · {values.Length} 页 · {total} 行";
                _pageSummary.Foreground = _totalLines > 0 && total != _totalLines ? Brushes.Firebrick : Brushes.DimGray;
            }
            catch (ScoreIntegrationException) { _pageSummary.Text = "待分析"; }
        }
        private StructuredScoreDocument ReadEditor(bool readNumbers = true)
        {
            if (_analyzedNotation != _notation.Text) throw new ScoreIntegrationException("谱文正在分析或分析失败，请先完成自动分析。");
            string title = _title.Text.Trim(), notation = _notation.Text;
            var heading = new Regex(@"^T(?:<[^>]*>)?[:：]([^\r\n]*)", RegexOptions.Multiline);
            var match = heading.Match(notation);
            if (!match.Success) notation = "T:" + title + "\n" + notation;
            else if (match.Groups[1].Value.Trim() != title)
                notation = heading.Replace(notation, _ => "T:" + title, 1);
            var layout = (StructuredScoreLayout)_layout.SelectedIndex;
            var document = new StructuredScoreDocument { Id = _original.Id, Title = title, Notation = notation, ShigebenId = _original.ShigebenId,
                Lyrics = _original.Lyrics, LyricsFromNotation = _original.LyricsFromNotation,
                NotationType = _original.NotationType, TextColor = _textColorHex, Layout = layout, Page = 0, RenderRevision = _original.RenderRevision,
                PageLinesManual = _pageLinesManual, CombinedPageLinesManual = _combinedPageLinesManual,
                PageLines = _pageLines?.Select(p => p.ToArray()).ToArray(), CombinedPageLines = _combinedPageLines?.ToArray() };
            if (readNumbers && layout != StructuredScoreLayout.LongPage)
            {
                var numbers = ReadNumbers();
                bool manual = !string.IsNullOrWhiteSpace(_pageMarks.Text);
                if (layout == StructuredScoreLayout.Paginated)
                {
                    document.PageLinesManual = manual;
                    document.PageLines = manual ? new[] { numbers } : _defaultPageLines.Select(p => p.ToArray()).ToArray();
                }
                else { document.CombinedPageLinesManual = manual; document.CombinedPageLines = numbers; }
            }
            return document;
        }
        private async Task AnalyzeAsync(bool reset)
        {
            _editTimer.Stop(); _needsAnalysis = false; _numbersDirty = false;
            _analysisCts?.Cancel(); var cts = new CancellationTokenSource(); _analysisCts = cts;
            long version = ++_editVersion; _busy = true; _save.IsEnabled = false;
            string source = _notation.Text; bool sourceChanged = source != _analyzedNotation;
            if (sourceChanged) _codeLineCount.Text = "";
            string titleBeforeAnalysis = _title.Text;
            var layout = sourceChanged && reset ? StructuredScoreLayout.Paginated : (StructuredScoreLayout)_layout.SelectedIndex;
            _status.Text = "正在分析谱文…";
            try
            {
                var result = await Task.Run(() =>
                {
                    int[][] plan;
                    try { plan = StructuredScoreLayoutRenderer.CreatePageLines(source, layout == StructuredScoreLayout.LongPage ? StructuredScoreLayout.Paginated : layout, cts.Token); }
                    catch (ScoreIntegrationException) when (sourceChanged && reset && layout == StructuredScoreLayout.Paginated)
                    { layout = StructuredScoreLayout.Combined; plan = StructuredScoreLayoutRenderer.CreatePageLines(source, layout, cts.Token); }
                    return plan;
                }, cts.Token);
                if (version != _editVersion || _closed) return;
                if (result.Length == 0 || result.Sum(p => p.Length) == 0) throw new ScoreIntegrationException("谱文无效，没有可显示的完整谱行。");
                _suppressChanges = true;
                if (sourceChanged)
                {
                    var title = Regex.Match(source, @"^T(?:<[^>]*>)?[:：]([^\r\n]*)", RegexOptions.Multiline);
                    if (title.Success && !string.IsNullOrWhiteSpace(title.Groups[1].Value) && _title.Text == titleBeforeAnalysis) _title.Text = title.Groups[1].Value.Trim();
                }
                _layout.SelectedIndex = (int)layout; _suppressChanges = false;
                _totalLines = result.Sum(p => p.Sum());
                _codeLineCount.Text = $"· {_totalLines} 行";
                if (sourceChanged && reset)
                {
                    _pageLines = null; _combinedPageLines = null; _pageLinesManual = false; _combinedPageLinesManual = false;
                }
                if (layout == StructuredScoreLayout.Combined)
                {
                    _defaultCombinedPageLines = result[0];
                    if (_combinedPageLinesManual == null) _combinedPageLinesManual = _combinedPageLines != null && !_combinedPageLines.SequenceEqual(result[0]);
                    if (_combinedPageLinesManual != true || _combinedPageLines == null) _combinedPageLines = result[0];
                }
                else
                {
                    _defaultPageLines = result;
                    if (_pageLinesManual == null) _pageLinesManual = _pageLines != null && System.Text.Json.JsonSerializer.Serialize(_pageLines) != System.Text.Json.JsonSerializer.Serialize(result);
                    if (_pageLinesManual != true || _pageLines == null) _pageLines = result;
                }
                _analyzedNotation = source; WriteMarks();
                _save.IsEnabled = true; _status.Text = "已自动分配";
                if (_previewPane.Visibility == Visibility.Visible) await RefreshPreviewAsync(ReadEditor());
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (version == _editVersion && !_closed) _status.Text = "分析失败，原文件不变：" + ex.Message; }
            finally { if (version == _editVersion) _busy = false; cts.Dispose(); if (_analysisCts == cts) _analysisCts = null; }
        }
        private async Task ApplyNumbersAsync()
        {
            _editTimer.Stop(); _numbersDirty = false;
            long version = ++_editVersion; _busy = true; _save.IsEnabled = false;
            try
            {
                var document = ReadEditor(); document.Validate();
                bool persist = _applyCuts != null && document.Notation == _original.Notation && document.Title == _original.Title;
                await _writeGate.WaitAsync();
                try
                {
                    if (version != _editVersion || _closed) return;
                    if (persist)
                    {
                        _writing = true; _status.Text = "正在自动保存分页脚本…";
                        await _applyCuts(document);
                        _original.PageLines = document.PageLines?.Select(p => p.ToArray()).ToArray();
                        _original.CombinedPageLines = document.CombinedPageLines?.ToArray();
                        _original.PageLinesManual = document.PageLinesManual; _original.CombinedPageLinesManual = document.CombinedPageLinesManual;
                        _original.TextColor = document.TextColor;
                    }
                }
                finally { _writing = false; _writeGate.Release(); }
                if (version != _editVersion || _closed) return;
                _pageLines = document.PageLines; _combinedPageLines = document.CombinedPageLines;
                _pageLinesManual = document.PageLinesManual; _combinedPageLinesManual = document.CombinedPageLinesManual;
                _save.IsEnabled = true; UpdateSummary();
                _status.Text = persist ? "已自动保存" : "已调整";
                if (_previewPane.Visibility == Visibility.Visible) await RefreshPreviewAsync(document);
            }
            catch (Exception ex) { if (version == _editVersion && !_closed) _status.Text = "未保存，原方案保留：" + ex.Message; }
            finally { if (version == _editVersion) _busy = false; }
        }
        private async Task SaveScoreAsync()
        {
            if (_busy || _writing) return;
            _save.IsEnabled = false;
            try
            {
                _editTimer.Stop();
                if (_needsAnalysis) await AnalyzeAsync(true);
                var document = ReadEditor();
                await _writeGate.WaitAsync();
                try
                {
                    _writing = true;
                    await Task.Run(() => document.Render(CancellationToken.None));
                    Result = document; _numbersDirty = false; _needsAnalysis = false;
                }
                finally { _writing = false; _writeGate.Release(); }
                DialogResult = true;
            }
            catch (Exception ex) { _status.Text = "未保存：" + ex.Message; }
            finally { _writing = false; _save.IsEnabled = true; }
        }
        private async Task RefreshPreviewAsync(StructuredScoreDocument document)
        {
            long version = _editVersion;
            try
            {
                var previewColor = StructuredScoreDocument.ParseTextColor(document.TextColor ?? _defaultTextColor);
                var pages = await Task.Run(() => document.RenderWithColors(previewColor, StructuredScoreDocument.TextBackground(previewColor), CancellationToken.None));
                if (version != _editVersion || _closed || _previewPane.Visibility != Visibility.Visible) return;
                int page = Math.Min(_previewPage, pages.Count - 1);
                var bitmap = await Task.Run(() => LoadBitmap(pages[page]));
                if (version != _editVersion || _closed) return;
                _previewPages = pages; _previewPage = page; _preview.Source = bitmap; _pageNumber.Text = $"{page + 1} / {pages.Count}";
                var background = StructuredScoreDocument.TextBackground(previewColor);
                _previewPane.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(background.Red, background.Green, background.Blue));
                var diagnostics = await Task.Run(() => ExtendedScoreNotationRenderer.GetDiagnostics(document.Notation, CancellationToken.None));
                if (version != _editVersion || _closed) return;
                _status.ToolTip = diagnostics.Count > 0 || document.PaginationNotice != null ? string.Join("\n", diagnostics.Concat(document.PaginationNotice == null ? Array.Empty<string>() : new[] { document.PaginationNotice })) : null;
                if (diagnostics.Count > 0) _status.Text = $"可预览 · {diagnostics.Count} 项谱文提示";
                else if (document.PaginationWasUpdated) _status.Text = "已兼容旧分页脚本";
            }
            catch (Exception ex) { if (version == _editVersion && !_closed) _status.Text = "预览失败，原文件不变：" + ex.Message; }
        }
        private async void ShowPreviewPage(int page)
        {
            if (_previewPages == null || _previewPages.Count == 0) return;
            page = (page % _previewPages.Count + _previewPages.Count) % _previewPages.Count;
            _previewPage = page; var pages = _previewPages;
            try
            {
                var bitmap = await Task.Run(() => LoadBitmap(pages[page]));
                if (_closed || pages != _previewPages || page != _previewPage) return;
                _preview.Source = bitmap; _pageNumber.Text = $"{page + 1} / {pages.Count}";
            }
            catch (Exception ex) { if (!_closed) _status.Text = "预览失败：" + ex.Message; }
        }
        internal static BitmapSource LoadBitmap(string path)
        {
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            using (var stream = File.OpenRead(path)) { bitmap.StreamSource = stream; bitmap.EndInit(); }
            bitmap.Freeze(); return bitmap;
        }
    }
}
