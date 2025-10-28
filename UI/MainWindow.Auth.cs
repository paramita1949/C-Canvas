using System;
using System.Windows;
using System.Windows.Media;
using ImageColorChanger.Services;
using MessageBox = System.Windows.MessageBox;
using Color = System.Windows.Media.Color;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 的认证相关功能
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// 初始化认证服务
        /// </summary>
        private void InitializeAuthService()
        {
            // 订阅认证状态变化事件
            AuthService.Instance.AuthenticationChanged += OnAuthenticationChanged;
            
            // 初始化UI状态
            UpdateAuthUI();
        }

        /// <summary>
        /// 认证状态改变事件处理
        /// </summary>
        private void OnAuthenticationChanged(object sender, AuthService.AuthenticationChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateAuthUI();
                
                // 不再弹窗提示登录成功，信息已显示在标题栏
            });
        }

        /// <summary>
        /// 更新认证UI状态
        /// </summary>
        private void UpdateAuthUI()
        {
            if (AuthService.Instance.IsAuthenticated)
            {
                // 已登录状态
                BtnLogin.Content = "用户";
                
                // 显示重置次数信息
                int resetCount = AuthService.Instance.ResetDeviceCount;
                string resetInfo = resetCount > 0 ? $"可解绑{resetCount}次" : "解绑次数已用完";
                BtnLogin.ToolTip = $"用户管理 - {resetInfo}";
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"🎨 [UpdateAuthUI] 更新UI - 解绑次数: {resetCount}");
                System.Diagnostics.Debug.WriteLine($"🎨 [UpdateAuthUI] Tooltip: {BtnLogin.ToolTip}");
                #endif
            }
            else
            {
                // 未登录状态
                BtnLogin.Content = "登录";
                BtnLogin.ToolTip = "登录账号";
            }
            
            // 窗口标题统一显示，不显示登录信息
            this.Title = "Canvas Cast V5.1.6";
        }

        /// <summary>
        /// 用户按钮点击事件
        /// </summary>
        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            if (AuthService.Instance.IsAuthenticated)
            {
                // 已登录，显示用户菜单
                ShowUserMenu();
            }
            else
            {
                // 未登录，显示登录窗口
                var loginWindow = new LoginWindow
                {
                    Owner = this
                };
                
                loginWindow.ShowDialog();
                
                // 登录窗口关闭后更新UI
                UpdateAuthUI();
            }
        }

        /// <summary>
        /// 显示用户管理菜单（手动刷新）
        /// </summary>
        private void ShowUserMenu()
        {
            // 获取当前缓存的用户信息
            string username = AuthService.Instance.Username;
            int remainingDays = AuthService.Instance.RemainingDays;
            DateTime? expiresAt = AuthService.Instance.ExpiresAt;
            int resetCount = AuthService.Instance.ResetDeviceCount;
            var deviceInfo = AuthService.Instance.DeviceBindingInfo;
            
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"🎨 [ShowUserMenu] 显示用户菜单");
            System.Diagnostics.Debug.WriteLine($"   用户名: {username}");
            System.Diagnostics.Debug.WriteLine($"   剩余天数: {remainingDays}");
            System.Diagnostics.Debug.WriteLine($"   解绑次数: {resetCount}");
            System.Diagnostics.Debug.WriteLine($"   设备信息: {(deviceInfo != null ? $"已绑定{deviceInfo.BoundDevices}/{deviceInfo.MaxDevices}, 剩余{deviceInfo.RemainingSlots}" : "null")}");
            #endif
            
            // 创建自定义用户信息窗口
            var userWindow = new System.Windows.Window
            {
                Title = "用户信息",
                Width = 480,
                Height = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(245, 245, 245))
            };
            
            var mainPanel = new System.Windows.Controls.StackPanel
            {
                Margin = new Thickness(0)
            };
            
            // 标题栏
            var headerPanel = new System.Windows.Controls.Border
            {
                Background = new System.Windows.Media.LinearGradientBrush(
                    Color.FromRgb(102, 126, 234),
                    Color.FromRgb(118, 75, 162),
                    90),
                Padding = new Thickness(20, 15, 20, 15)
            };
            
            var headerText = new System.Windows.Controls.TextBlock
            {
                Text = "👤 个人中心",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White
            };
            headerPanel.Child = headerText;
            mainPanel.Children.Add(headerPanel);
            
            // 信息内容区域
            var contentPanel = new System.Windows.Controls.StackPanel
            {
                Margin = new Thickness(20, 20, 20, 20)
            };
            
            // 用户名
            AddInfoBlock(contentPanel, "用户名", username, "👤");
            
            // 账号有效期（只显示日期，不显示具体时间）
            string expireInfo = $"{expiresAt?.ToString("yyyy年MM月dd日") ?? "未知"}  (剩余 {remainingDays} 天)";
            AddInfoBlock(contentPanel, "有效期", expireInfo, "⏰");
            
            // 设备绑定 - 显示剩余可绑定数量
            string deviceBindInfo = deviceInfo != null 
                ? $"剩余可绑定 {deviceInfo.RemainingSlots} 台  (已绑定 {deviceInfo.BoundDevices} / {deviceInfo.MaxDevices})"
                : "未知";
            AddInfoBlock(contentPanel, "设备绑定", deviceBindInfo, "📱");
            
            // 解绑次数
            string resetInfo = $"{resetCount} 次";
            AddInfoBlock(contentPanel, "解绑次数", resetInfo, "🔓");
            
            mainPanel.Children.Add(contentPanel);
            
            // 按钮区域
            var buttonPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20)
            };
            
            // 刷新按钮
            var refreshBtn = CreateStyledButton("🔄 刷新", Color.FromRgb(40, 167, 69));
            refreshBtn.Click += async (s, e) =>
            {
                var btn = s as System.Windows.Controls.Button;
                var originalContent = btn.Content;
                btn.IsEnabled = false;
                btn.Content = "刷新中...";
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"🔄 [手动刷新] 用户点击刷新按钮");
                #endif
                
                bool success = await AuthService.Instance.RefreshAccountInfoAsync();
                
                btn.IsEnabled = true;
                btn.Content = originalContent;
                
                if (success)
                {
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"✅ [手动刷新] 刷新成功，重新显示窗口");
                    #endif
                    
                    // 更新标题栏
                    UpdateAuthUI();
                    
                    // 关闭当前窗口，重新打开以显示最新数据
                    userWindow.Close();
                    ShowUserMenu();
                }
                else
                {
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"❌ [手动刷新] 刷新失败");
                    #endif
                    
                    MessageBox.Show("无法连接到服务器，当前显示为本地缓存数据", "刷新失败", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            };
            
            // 退出按钮
            var logoutBtn = CreateStyledButton("退出", Color.FromRgb(108, 117, 125));
            logoutBtn.Click += (s, e) =>
            {
                userWindow.Close();
                if (MessageBox.Show("确定要退出登录吗？", "确认退出", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    AuthService.Instance.Logout();
                    UpdateAuthUI();
                }
            };
            
            // 解绑设备按钮
            var unbindBtn = CreateStyledButton("解绑设备", Color.FromRgb(220, 53, 69));
            unbindBtn.Click += (s, e) =>
            {
                userWindow.Close();
                UnbindDeviceWithConfirm();
            };
            
            buttonPanel.Children.Add(refreshBtn);
            buttonPanel.Children.Add(logoutBtn);
            buttonPanel.Children.Add(unbindBtn);
            mainPanel.Children.Add(buttonPanel);
            
            userWindow.Content = mainPanel;
            userWindow.ShowDialog();
        }
        
        /// <summary>
        /// 添加信息块
        /// </summary>
        private void AddInfoBlock(System.Windows.Controls.StackPanel parent, string label, string value, string icon)
        {
            var block = new System.Windows.Controls.Border
            {
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                BorderThickness = new Thickness(1),
                CornerRadius = new System.Windows.CornerRadius(8),
                Padding = new Thickness(15, 12, 15, 12),
                Margin = new Thickness(0, 0, 0, 12)
            };
            
            var grid = new System.Windows.Controls.Grid();
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            
            var labelText = new System.Windows.Controls.TextBlock
            {
                Text = $"{icon} {label}",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                VerticalAlignment = VerticalAlignment.Center
            };
            System.Windows.Controls.Grid.SetColumn(labelText, 0);
            
            var valueText = new System.Windows.Controls.TextBlock
            {
                Text = value,
                FontSize = 14,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(33, 33, 33)),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            System.Windows.Controls.Grid.SetColumn(valueText, 1);
            
            grid.Children.Add(labelText);
            grid.Children.Add(valueText);
            block.Child = grid;
            parent.Children.Add(block);
        }
        
        /// <summary>
        /// 创建样式化按钮
        /// </summary>
        private System.Windows.Controls.Button CreateStyledButton(string text, Color bgColor)
        {
            var button = new System.Windows.Controls.Button
            {
                Content = text,
                Width = 140,
                Height = 40,
                FontSize = 15,
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(8, 0, 8, 0),
                Background = new SolidColorBrush(bgColor),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            
            // 保存原始颜色
            var originalColor = bgColor;
            
            // 计算悬停时的颜色（变暗20%）
            var hoverColor = Color.FromRgb(
                (byte)(bgColor.R * 0.8),
                (byte)(bgColor.G * 0.8),
                (byte)(bgColor.B * 0.8)
            );
            
            // 添加鼠标进入事件（悬停变色）
            button.MouseEnter += (s, e) =>
            {
                button.Background = new SolidColorBrush(hoverColor);
            };
            
            // 添加鼠标离开事件（恢复原色）
            button.MouseLeave += (s, e) =>
            {
                button.Background = new SolidColorBrush(originalColor);
            };
            
            // 添加圆角样式
            var template = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Button));
            var border = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            border.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new System.Windows.CornerRadius(8));
            border.SetValue(System.Windows.Controls.Border.BackgroundProperty, new System.Windows.TemplateBindingExtension(System.Windows.Controls.Control.BackgroundProperty));
            
            var contentPresenter = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
            contentPresenter.SetValue(System.Windows.Controls.ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
            contentPresenter.SetValue(System.Windows.Controls.ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            
            border.AppendChild(contentPresenter);
            template.VisualTree = border;
            button.Template = template;
            
            return button;
        }
        
        /// <summary>
        /// 解绑设备（带二次确认）
        /// </summary>
        private void UnbindDeviceWithConfirm()
        {
            var resetCount = AuthService.Instance.ResetDeviceCount;
            
            if (resetCount <= 0)
            {
                MessageBox.Show(
                    "解绑次数已用完，无法解绑设备。\n请联系管理员。",
                    "无法解绑",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            
            // 二次确认
            var confirmResult = MessageBox.Show(
                $"⚠️ 确认解绑设备\n\n" +
                $"解绑后将会：\n" +
                $"• 清除所有已绑定的设备\n" +
                $"• 当前账号自动退出登录\n" +
                $"• 需要重新登录使用\n\n" +
                $"剩余解绑次数：{resetCount} 次\n" +
                $"解绑后剩余：{resetCount - 1} 次\n\n" +
                $"确定要解绑设备吗？",
                "二次确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            
            if (confirmResult == MessageBoxResult.Yes)
            {
                UnbindDevice();
            }
        }

        /// <summary>
        /// 设备解绑（重置绑定设备）
        /// </summary>
        private async void UnbindDevice()
        {
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"🔄 [UnbindDevice] 开始解绑流程");
            #endif
            
            // 要求输入密码确认
            var passwordDialog = new System.Windows.Window
            {
                Title = "🔒 密码确认",
                Width = 460,
                Height = 260,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(245, 245, 245))
            };
            
            var mainPanel = new System.Windows.Controls.StackPanel
            {
                Margin = new Thickness(0)
            };
            
            // 标题栏
            var headerPanel = new System.Windows.Controls.Border
            {
                Background = new System.Windows.Media.LinearGradientBrush(
                    Color.FromRgb(220, 53, 69),
                    Color.FromRgb(189, 46, 60),
                    90),
                Padding = new Thickness(20, 15, 20, 15)
            };
            
            var headerText = new System.Windows.Controls.TextBlock
            {
                Text = "🔒 密码验证",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White
            };
            headerPanel.Child = headerText;
            mainPanel.Children.Add(headerPanel);
            
            // 内容区域
            var contentPanel = new System.Windows.Controls.StackPanel
            {
                Margin = new Thickness(25, 20, 25, 20)
            };
            
            // 提示文本
            var promptText = new System.Windows.Controls.TextBlock
            {
                Text = "⚠️ 解绑设备需要验证您的账号密码",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                Margin = new Thickness(0, 0, 0, 15)
            };
            contentPanel.Children.Add(promptText);
            
            // 密码输入框容器
            var passwordContainer = new System.Windows.Controls.Border
            {
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                BorderThickness = new Thickness(2),
                CornerRadius = new System.Windows.CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 20)
            };
            
            var passwordBox = new System.Windows.Controls.PasswordBox
            {
                FontSize = 15,
                Height = 35,
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                PasswordChar = '●'
            };
            
            // 密码框获得焦点时改变边框颜色
            passwordBox.GotFocus += (s, e) =>
            {
                passwordContainer.BorderBrush = new SolidColorBrush(Color.FromRgb(220, 53, 69));
                passwordContainer.BorderThickness = new Thickness(2);
            };
            
            passwordBox.LostFocus += (s, e) =>
            {
                passwordContainer.BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200));
                passwordContainer.BorderThickness = new Thickness(2);
            };
            
            passwordContainer.Child = passwordBox;
            contentPanel.Children.Add(passwordContainer);
            
            // 按钮区域
            var buttonPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            
            var okButton = CreateStyledButton("确认解绑", Color.FromRgb(220, 53, 69));
            okButton.Width = 120;
            okButton.Height = 38;
            
            var cancelButton = CreateStyledButton("取消", Color.FromRgb(108, 117, 125));
            cancelButton.Width = 120;
            cancelButton.Height = 38;
            
            bool? dialogResult = null;
            okButton.Click += (s, e) => { dialogResult = true; passwordDialog.Close(); };
            cancelButton.Click += (s, e) => { dialogResult = false; passwordDialog.Close(); };
            
            // 支持回车确认
            passwordBox.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    dialogResult = true;
                    passwordDialog.Close();
                }
            };
            
            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(okButton);
            contentPanel.Children.Add(buttonPanel);
            
            mainPanel.Children.Add(contentPanel);
            passwordDialog.Content = mainPanel;
            
            // 窗口加载后自动聚焦到密码框
            passwordDialog.Loaded += (s, e) => passwordBox.Focus();
            
            passwordDialog.ShowDialog();
            
            if (dialogResult != true || string.IsNullOrEmpty(passwordBox.Password))
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"🔄 [UnbindDevice] 用户取消或密码为空");
                #endif
                return;
            }
            
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"🔄 [UnbindDevice] 调用 ResetDevicesAsync");
            #endif
            
            // 执行解绑
            var (success, message, remaining) = await AuthService.Instance.ResetDevicesAsync(passwordBox.Password);
            
            #if DEBUG
            System.Diagnostics.Debug.WriteLine($"🔄 [UnbindDevice] ResetDevicesAsync 返回:");
            System.Diagnostics.Debug.WriteLine($"   success: {success}");
            System.Diagnostics.Debug.WriteLine($"   message: {message}");
            System.Diagnostics.Debug.WriteLine($"   remaining: {remaining}");
            #endif
            
            if (success)
            {
                // 解绑成功，自动退出登录
                MessageBox.Show(
                    $"✅ {message}\n\n剩余解绑次数：{remaining}次\n\n当前账号已自动退出，请重新登录。",
                    "解绑成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                
                // 自动退出登录
                AuthService.Instance.Logout();
                UpdateAuthUI();
            }
            else
            {
                MessageBox.Show(
                    $"❌ {message}",
                    "解绑失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 清理认证服务
        /// </summary>
        private void CleanupAuthService()
        {
            AuthService.Instance.AuthenticationChanged -= OnAuthenticationChanged;
        }
    }
}

