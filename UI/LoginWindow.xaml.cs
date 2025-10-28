using System;
using System.Windows;
using System.Windows.Input;
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
            ShowStatus("正在验证...", isError: false);
            LoginButton.IsEnabled = false;

            try
            {
                // 调用验证服务
                var (success, message) = await AuthService.Instance.LoginAsync(username, password);

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
            catch (Exception ex)
            {
                ShowStatus($"登录异常: {ex.Message}", isError: true);
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
    }
}

