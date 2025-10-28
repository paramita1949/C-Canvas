using System;
using System.Text.RegularExpressions;
using System.Windows;
using ImageColorChanger.Services;

namespace ImageColorChanger.UI
{
    public partial class RegisterWindow : Window
    {
        public bool RegisterSuccess { get; private set; }

        public RegisterWindow()
        {
            InitializeComponent();
            RegisterSuccess = false;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 聚焦到用户名输入框
            UsernameTextBox.Focus();
        }

        private async void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            var username = UsernameTextBox.Text.Trim();
            var password = PasswordBox.Password;
            var email = EmailTextBox.Text.Trim();

            // 验证用户名
            if (string.IsNullOrEmpty(username))
            {
                ShowStatus("请输入用户名", isError: true);
                UsernameTextBox.Focus();
                return;
            }

            if (username.Length < 3 || username.Length > 20)
            {
                ShowStatus("用户名长度为3-20个字符", isError: true);
                UsernameTextBox.Focus();
                return;
            }

            if (!Regex.IsMatch(username, @"^[a-zA-Z0-9_]+$"))
            {
                ShowStatus("用户名只能包含字母、数字和下划线", isError: true);
                UsernameTextBox.Focus();
                return;
            }

            // 验证密码
            if (string.IsNullOrEmpty(password))
            {
                ShowStatus("请输入密码", isError: true);
                PasswordBox.Focus();
                return;
            }

            if (password.Length < 6)
            {
                ShowStatus("密码至少6个字符", isError: true);
                PasswordBox.Focus();
                return;
            }

            // 验证邮箱（可选）
            if (!string.IsNullOrEmpty(email))
            {
                if (!Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                {
                    ShowStatus("邮箱格式不正确", isError: true);
                    EmailTextBox.Focus();
                    return;
                }
            }

            // 显示注册中状态
            ShowStatus("正在注册...", isError: false);
            RegisterButton.IsEnabled = false;
            CancelButton.IsEnabled = false;

            try
            {
                // 调用注册服务
                var (success, message) = await AuthService.Instance.RegisterAsync(
                    username, 
                    password, 
                    string.IsNullOrEmpty(email) ? null : email
                );

                if (success)
                {
                    RegisterSuccess = true;
                    ShowStatus("注册成功！等待管理员激活...", isError: false);
                    
                    // 延迟关闭窗口
                    await System.Threading.Tasks.Task.Delay(2000);
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
                ShowStatus($"注册异常: {ex.Message}", isError: true);
            }
            finally
            {
                RegisterButton.IsEnabled = true;
                CancelButton.IsEnabled = true;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
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

