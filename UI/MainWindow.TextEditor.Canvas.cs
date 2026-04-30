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
using System.Windows.Media.Media3D;
using System.Windows.Media.Animation;
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
    /// MainWindow TextEditor Canvas Events
    /// </summary>
    public partial class MainWindow
    {
        #region 画布事件

        /// <summary>
        /// 画布点击（处理编辑区域内的空白点击）
        /// </summary>
        private void EditorCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_isSelectionToolActive && e.LeftButton == MouseButtonState.Pressed)
            {
                DependencyObject source = e.OriginalSource as DependencyObject;
                while (source != null)
                {
                    if (source is DraggableTextBox)
                    {
                        return;
                    }
                    source = GetDependencyParent(source);
                }

                StartMarqueeSelection(e.GetPosition(EditorCanvas));
                e.Handled = true;
                return;
            }

            //System.Diagnostics.Debug.WriteLine($"[EditorCanvas_MouseDown] 开始处理");
            //System.Diagnostics.Debug.WriteLine($"   - OriginalSource: {e.OriginalSource?.GetType().Name}");

            // 这个事件只有在EditorCanvas区域内点击时才会触发
            // 所以只要不是点击在文本框内，就取消选中
            bool clickedOnTextBox = false;

            // 检查是否点击在任意文本框内
            foreach (var textBox in _textBoxes)
            {
                var position = e.GetPosition(textBox);
                //System.Diagnostics.Debug.WriteLine($"   - 检查文本框 {textBox.Name}，位置: ({position.X:F1}, {position.Y:F1})");

                if (position.X >= 0 && position.Y >= 0 &&
                    position.X <= textBox.ActualWidth &&
                    position.Y <= textBox.ActualHeight)
                {
                    clickedOnTextBox = true;
                    //System.Diagnostics.Debug.WriteLine($"   -  点击在文本框内，保持选中状态");
                    break;
                }
            }

            // 如果点击在编辑区域内但没有点击在文本框内，则取消所有文本框选中状态
            if (!clickedOnTextBox)
            {
                //System.Diagnostics.Debug.WriteLine($"   -  点击在编辑区域空白位置，取消所有文本框选中状态");
                DeselectAllTextBoxes(true); // 关闭侧边面板（点击空白区域时关闭）
            }

            //System.Diagnostics.Debug.WriteLine($"[EditorCanvas_MouseDown] 处理完成");
        }

        private void EditorCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isMarqueeSelecting || _marqueeSelectionRect == null)
            {
                return;
            }

            var currentPoint = e.GetPosition(EditorCanvas);
            UpdateMarqueeSelectionVisual(currentPoint);
            UpdateMarqueeSelectionResult();
            e.Handled = true;
        }

        private void EditorCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isMarqueeSelecting || e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            EndMarqueeSelection();
            e.Handled = true;
        }

        private void StartMarqueeSelection(System.Windows.Point startPoint)
        {
            _marqueeStartPoint = startPoint;
            _isMarqueeSelecting = true;

            DeselectAllTextBoxes(false);

            _marqueeSelectionRect = new WpfRectangle
            {
                Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243)),
                StrokeThickness = 1.5,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(48, 33, 150, 243)),
                StrokeDashArray = new DoubleCollection { 4, 2 },
                IsHitTestVisible = false
            };

            Canvas.SetLeft(_marqueeSelectionRect, startPoint.X);
            Canvas.SetTop(_marqueeSelectionRect, startPoint.Y);
            System.Windows.Controls.Panel.SetZIndex(_marqueeSelectionRect, 5000);
            EditorCanvas.Children.Add(_marqueeSelectionRect);
            EditorCanvas.CaptureMouse();
        }

        private void UpdateMarqueeSelectionVisual(System.Windows.Point currentPoint)
        {
            if (_marqueeSelectionRect == null)
            {
                return;
            }

            double left = Math.Min(_marqueeStartPoint.X, currentPoint.X);
            double top = Math.Min(_marqueeStartPoint.Y, currentPoint.Y);
            double width = Math.Abs(currentPoint.X - _marqueeStartPoint.X);
            double height = Math.Abs(currentPoint.Y - _marqueeStartPoint.Y);

            Canvas.SetLeft(_marqueeSelectionRect, left);
            Canvas.SetTop(_marqueeSelectionRect, top);
            _marqueeSelectionRect.Width = width;
            _marqueeSelectionRect.Height = height;
        }

        private void UpdateMarqueeSelectionResult()
        {
            if (_marqueeSelectionRect == null)
            {
                return;
            }

            var rect = new Rect(
                Canvas.GetLeft(_marqueeSelectionRect),
                Canvas.GetTop(_marqueeSelectionRect),
                _marqueeSelectionRect.Width,
                _marqueeSelectionRect.Height);

            var hits = _textBoxes.Where(tb => rect.IntersectsWith(GetTextBoxBounds(tb))).ToList();
            SelectMultipleTextBoxes(hits);
        }

        private void EndMarqueeSelection()
        {
            _isMarqueeSelecting = false;
            EditorCanvas.ReleaseMouseCapture();

            if (_marqueeSelectionRect != null)
            {
                EditorCanvas.Children.Remove(_marqueeSelectionRect);
                _marqueeSelectionRect = null;
            }

            if (_selectedTextBoxes.Count == 0)
            {
                HideBibleFloatingToolbar();
            }
        }
        
        /// <summary>
        /// 画布键盘事件（处理DEL快捷键）
        /// </summary>
        private async void EditorCanvas_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            //#if DEBUG
            //System.Diagnostics.Debug.WriteLine($"[EditorCanvas_KeyDown] 按键: {e.Key}");
            //System.Diagnostics.Debug.WriteLine($"   IsInSplitMode: {IsInSplitMode()}");
            //System.Diagnostics.Debug.WriteLine($"   _selectedRegionIndex: {_selectedRegionIndex}");
            //System.Diagnostics.Debug.WriteLine($"   _regionImages.Count: {_regionImages.Count}");
            //System.Diagnostics.Debug.WriteLine($"   包含选中区域图片: {_regionImages.ContainsKey(_selectedRegionIndex)}");
            //#endif
            
            // DEL键：只清除选中区域的图片（仅在分割模式下且有图片时）
            if (e.Key == System.Windows.Input.Key.Delete)
            {
                //#if DEBUG
                //System.Diagnostics.Debug.WriteLine($"[DEL键] 检测到 Delete 键");
                //#endif
                
                if (IsInSplitMode())
                {
                    //#if DEBUG
                    //System.Diagnostics.Debug.WriteLine($" [DEL键] 在分割模式下");
                    //#endif
                    
                    if (_selectedRegionIndex >= 0)
                    {
                        //#if DEBUG
                        //System.Diagnostics.Debug.WriteLine($" [DEL键] 有选中区域: {_selectedRegionIndex}");
                        //#endif
                        
                        if (_regionImages.ContainsKey(_selectedRegionIndex))
                        {
                            //#if DEBUG
                            //System.Diagnostics.Debug.WriteLine($" [DEL键] 区域有图片，执行清空");
                            //#endif
                            
                            await ClearSelectedRegionImage();
                            e.Handled = true;
                        }
                        //#if DEBUG
                        //else
                        //{
                        //    //System.Diagnostics.Debug.WriteLine($" [DEL键] 区域没有图片");
                        //}
                        //#endif
                    }
                    //#if DEBUG
                    //else
                    //{
                    //    //System.Diagnostics.Debug.WriteLine($" [DEL键] 没有选中区域");
                    //}
                    //#endif
                }
                else
                {
                    await DeleteSelectedTextBoxesAsync();
                    e.Handled = true;
                }
                //#if DEBUG
                //else
                //{
                //    //System.Diagnostics.Debug.WriteLine($" [DEL键] 不在分割模式");
                //}
                //#endif
            }
        }

        /// <summary>
        /// 画布右键菜单（空白区域支持粘贴）
        /// </summary>
        private void EditorCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            ShowEditorCanvasPasteContextMenu(e);
        }

        /// <summary>
        /// 画布右键弹起（兜底，避免某些情况下Down事件被占用）
        /// </summary>
        private void EditorCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            ShowEditorCanvasPasteContextMenu(e);
        }

        private void ShowEditorCanvasPasteContextMenu(MouseButtonEventArgs e)
        {
            if (EditorCanvas?.ContextMenu != null && EditorCanvas.ContextMenu.IsOpen)
            {
                e.Handled = true;
                return;
            }

            // 点击在文本框上时交给文本框自身右键菜单处理
            DependencyObject source = e.OriginalSource as DependencyObject;
            while (source != null)
            {
                if (source is DraggableTextBox)
                {
                    return;
                }
                source = GetDependencyParent(source);
            }

            var contextMenu = new ContextMenu
            {
                MinWidth = 176,
                FontSize = 14
            };
            try
            {
                contextMenu.Style = (Style)this.FindResource("NoBorderContextMenuStyle");
            }
            catch
            {
            }

            var selected = GetActiveSelectedTextBoxes();
            if (selected.Count > 0)
            {
                var copySelectedItem = CreateIconMenuItem(
                    selected.Count > 1 ? $"复制 {selected.Count} 项" : "复制",
                    "IconLucideCopy2",
                    async () => { await CopyTextBoxToClipboardAsync(null); });
                contextMenu.Items.Add(copySelectedItem);

                var deleteSelectedItem = CreateIconMenuItem(
                    selected.Count > 1 ? $"删除 {selected.Count} 项" : "删除",
                    "IconLucideX",
                    async () => { await DeleteSelectedTextBoxesAsync(); });
                contextMenu.Items.Add(deleteSelectedItem);
                contextMenu.Items.Add(new Separator());
            }

            var pasteItem = CreateIconMenuItem("粘贴", "IconLucideFileText", async () =>
            {
                await PasteTextBoxFromClipboardAsync(null);
            });
            pasteItem.IsEnabled = _textBoxClipboardElement != null;
            contextMenu.Items.Add(pasteItem);

            var insertNoticeItem = CreateIconMenuItem("插入通知", "IconLucideBookPlus", async () =>
            {
                await InsertNoticeComponentAsync();
            });
            insertNoticeItem.IsEnabled = _currentSlide != null;
            contextMenu.Items.Add(insertNoticeItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(CreateEditorCanvasLayoutMenuItem());
            contextMenu.Items.Add(CreateEditorCanvasThemeMenuItem());

            EditorCanvas.ContextMenu = contextMenu;
            contextMenu.PlacementTarget = EditorCanvas;
            contextMenu.IsOpen = true;
            e.Handled = true;
        }

        private static DependencyObject GetDependencyParent(DependencyObject source)
        {
            if (source == null)
            {
                return null;
            }

            if (source is Visual || source is Visual3D)
            {
                return VisualTreeHelper.GetParent(source);
            }

            if (source is System.Windows.Documents.TextElement textElement)
            {
                return textElement.Parent;
            }

            if (source is FrameworkContentElement frameworkContent)
            {
                return frameworkContent.Parent;
            }

            if (source is ContentElement contentElement)
            {
                return ContentOperations.GetParent(contentElement) as DependencyObject
                    ?? LogicalTreeHelper.GetParent(contentElement);
            }

            return LogicalTreeHelper.GetParent(source);
        }

        private MenuItem CreateEditorCanvasLayoutMenuItem()
        {
            var layoutRoot = new MenuItem
            {
                Header = BuildEditorCanvasMenuHeader("应用布局", "IconLucideLayoutGrid"),
                FontSize = 14
            };

            ConfigureLayoutThumbnailItemsPanel(layoutRoot);
            PopulateLayoutThumbnailMenuItems(layoutRoot.Items);
            return layoutRoot;
        }

        private ContextMenu CreateDirectLayoutThumbnailContextMenu(FrameworkElement anchor)
        {
            var layoutMenu = new ContextMenu
            {
                PlacementTarget = anchor,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                Background = (System.Windows.Media.Brush)FindResource("BrushMenuSurface"),
                Foreground = (System.Windows.Media.Brush)FindResource("BrushMenuText"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("BrushMenuBorder"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
                FontSize = 14
            };

            try
            {
                var menuItemStyle = (Style)FindResource("CompactContextMenuItemStyle");
                layoutMenu.Resources.Add(typeof(MenuItem), menuItemStyle);
            }
            catch
            {
                // 忽略样式缺失，退回系统默认样式
            }

            ConfigureLayoutThumbnailItemsPanel(layoutMenu);
            PopulateLayoutThumbnailMenuItems(layoutMenu.Items);
            return layoutMenu;
        }

        private MenuItem CreateEditorCanvasThemeMenuItem()
        {
            var themeRoot = new MenuItem
            {
                Header = BuildEditorCanvasMenuHeader("主题", "IconLucidePalette"),
                FontSize = 14
            };

            var darkItem = new MenuItem
            {
                Header = "黑底白字",
                IsCheckable = true,
                IsChecked = _slideThemeMode == SlideThemeMode.Dark
            };
            darkItem.Click += async (_, _) =>
            {
                await ApplySlideThemeAsync(SlideThemeMode.Dark);
            };

            var lightItem = new MenuItem
            {
                Header = "白底黑字",
                IsCheckable = true,
                IsChecked = _slideThemeMode == SlideThemeMode.Light
            };
            lightItem.Click += async (_, _) =>
            {
                await ApplySlideThemeAsync(SlideThemeMode.Light);
            };

            themeRoot.Items.Add(darkItem);
            themeRoot.Items.Add(lightItem);
            return themeRoot;
        }

        private static void ConfigureLayoutThumbnailItemsPanel(ItemsControl host)
        {
            if (host == null)
            {
                return;
            }

            var itemsPanelFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.Primitives.UniformGrid));
            itemsPanelFactory.SetValue(System.Windows.Controls.Primitives.UniformGrid.ColumnsProperty, 3);
            itemsPanelFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(2));
            host.ItemsPanel = new ItemsPanelTemplate(itemsPanelFactory);
        }

        private void PopulateLayoutThumbnailMenuItems(ItemCollection items)
        {
            if (items == null)
            {
                return;
            }

            items.Clear();
            items.Add(CreateLayoutPresetThumbnailMenuItem(SlideLayoutPreset.TitleSubtitle));
            items.Add(CreateLayoutPresetThumbnailMenuItem(SlideLayoutPreset.SectionTitleCentered));
            items.Add(CreateLayoutPresetThumbnailMenuItem(SlideLayoutPreset.TitleBody));
            items.Add(CreateLayoutPresetThumbnailMenuItem(SlideLayoutPreset.TitleTopOnly));
            items.Add(CreateLayoutPresetThumbnailMenuItem(SlideLayoutPreset.BodyKeyPoints));
        }

        private MenuItem CreateLayoutPresetThumbnailMenuItem(SlideLayoutPreset preset)
        {
            var previewHost = new Border
            {
                Width = 180,
                Height = 92,
                Background = new SolidColorBrush(WpfColor.FromRgb(252, 252, 252)),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(184, 184, 184)),
                BorderThickness = new Thickness(1)
            };

            previewHost.Child = new Viewbox
            {
                Stretch = Stretch.Fill,
                Child = BuildSlideLayoutPreviewVisual(preset)
            };

            var header = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Vertical,
                Margin = new Thickness(0, 2, 0, 2)
            };
            header.Children.Add(previewHost);
            header.Children.Add(new TextBlock
            {
                Text = GetSlideLayoutDisplayName(preset),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold
            });

            var item = new MenuItem
            {
                Header = header,
                Width = 210,
                Padding = new Thickness(6),
                FontSize = 13
            };

            item.Click += async (_, _) =>
            {
                await ApplySlideLayoutAsync(preset);
            };

            return item;
        }

        private object BuildEditorCanvasMenuHeader(string text, string iconResourceKey)
        {
            var headerPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal
            };

            try
            {
                if (TryFindResource(iconResourceKey) is Geometry iconGeometry)
                {
                    var icon = new System.Windows.Shapes.Path
                    {
                        Data = iconGeometry,
                        Width = 16,
                        Height = 16,
                        Margin = new Thickness(0, 0, 8, 0)
                    };

                    if (TryFindResource("LucideIconPathStyle") is Style iconStyle)
                    {
                        icon.Style = iconStyle;
                    }

                    headerPanel.Children.Add(icon);
                }
            }
            catch
            {
            }

            headerPanel.Children.Add(new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center
            });

            return headerPanel;
        }

        #endregion

    }
}


