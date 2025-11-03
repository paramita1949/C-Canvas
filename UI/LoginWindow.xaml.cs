using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using ImageColorChanger.Services;

namespace ImageColorChanger.UI
{
    public partial class LoginWindow : Window
    {
        public bool LoginSuccess { get; private set; }

        public LoginWindow()
        {
            InitializeComponent();
            LoginSuccess = false;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 聚焦到用户名输入框
            UsernameTextBox.Focus();
            
            // 检查是否有新版本
            CheckForUpdateNotification();
        }
        
        /// <summary>
        /// 检查更新通知
        /// </summary>
        private void CheckForUpdateNotification()
        {
            try
            {
                var versionInfo = UpdateService.GetLastCheckedVersionInfo();
                if (versionInfo != null)
                {
                    // 显示更新通知文字
                    UpdateVersionRun.Text = $"V{versionInfo.Version}";
                    UpdateNotificationText.Visibility = System.Windows.Visibility.Visible;
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"[LoginWindow] 显示更新提示: V{versionInfo.Version}");
#endif
                }
            }
            catch (Exception)
            {
#if DEBUG
                // 静默失败，不影响用户登录体验
#endif
            }
        }
        
        /// <summary>
        /// 点击更新按钮
        /// </summary>
        private void BtnShowUpdate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var versionInfo = UpdateService.GetLastCheckedVersionInfo();
                if (versionInfo != null)
                {
                    var updateWindow = new UpdateWindow(versionInfo);
                    updateWindow.Owner = this;
                    updateWindow.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"打开更新窗口失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            var username = UsernameTextBox.Text.Trim();
            var password = PasswordBox.Password;

            // 验证输入
            if (string.IsNullOrEmpty(username))
            {
                ShowStatus("请输入用户名", isError: true);
                UsernameTextBox.Focus();
                return;
            }

            if (string.IsNullOrEmpty(password))
            {
                ShowStatus("请输入密码", isError: true);
                PasswordBox.Focus();
                return;
            }

            // 显示登录中状态
            ShowStatus("正在验证,不要关闭窗口", isError: false);
            LoginButton.IsEnabled = false;

            try
            {
                // 设置超时：60秒
                var loginTask = AuthService.Instance.LoginAsync(username, password);
                var timeoutTask = System.Threading.Tasks.Task.Delay(60000); // 60秒超时

                var completedTask = await System.Threading.Tasks.Task.WhenAny(loginTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    // 超时
                    ShowStatus("连接超时：网络不佳或服务器无响应，请检查网络后重试", isError: true);
                    return;
                }

                // 获取登录结果
                var (success, message) = await loginTask;

                if (success)
                {
                    LoginSuccess = true;
                    ShowStatus(message, isError: false);
                    
                    // 延迟关闭窗口，让用户看到成功消息
                    await System.Threading.Tasks.Task.Delay(1000);
                    DialogResult = true;
                    Close();
                }
                else
                {
                    ShowStatus(message, isError: true);
                }
            }
            catch (System.Net.Http.HttpRequestException)
            {
                // 网络请求异常
                ShowStatus($"网络错误：无法连接到服务器，请检查网络", isError: true);
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                // 任务取消或超时
                ShowStatus("请求超时：服务器响应太慢，请稍后重试", isError: true);
            }
            catch (Exception ex)
            {
                ShowStatus($"登录异常：{ex.Message}", isError: true);
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [登录] 未知异常: {ex}");
                #endif
            }
            finally
            {
                LoginButton.IsEnabled = true;
            }
        }

        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            // 打开注册窗口
            var registerWindow = new RegisterWindow
            {
                Owner = this
            };

            var result = registerWindow.ShowDialog();

            if (result == true && registerWindow.RegisterSuccess)
            {
                // 注册成功，显示提示信息
                ShowStatus("注册成功！请等待管理员激活后登录。", isError: false);
            }
        }

        private void PaymentButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 打开联系窗口（显示微信二维码和付费信息）
                var contactWindow = new ContactWindow();
                contactWindow.Owner = this;
                contactWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"打开窗口失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PasswordBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                LoginButton_Click(sender, e);
            }
        }

        private void ShowStatus(string message, bool isError)
        {
            StatusText.Text = message;
            StatusText.Foreground = isError 
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red)
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
            StatusText.Visibility = Visibility.Visible;
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                // 使用默认浏览器打开链接
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"无法打开浏览器: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ForgotPassword_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 打开密码重置窗口
                var resetWindow = new ResetPasswordWindow
                {
                    Owner = this
                };

                var result = resetWindow.ShowDialog();

                if (result == true)
                {
                    // 密码重置成功，显示提示信息
                    ShowStatus("密码已重置，请使用新密码登录", isError: false);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"打开窗口失败: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

