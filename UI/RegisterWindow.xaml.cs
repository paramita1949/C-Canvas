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

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            CheckPasswordMatch();
        }

        private void ConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            CheckPasswordMatch();
        }

        private void CheckPasswordMatch()
        {
            var password = PasswordBox.Password;
            var confirmPassword = ConfirmPasswordBox.Password;

            // 如果两个密码框都为空，不显示提示
            if (string.IsNullOrEmpty(password) && string.IsNullOrEmpty(confirmPassword))
            {
                PasswordMatchHint.Text = "";
                PasswordMatchHint.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
                return;
            }

            // 如果重复密码框为空，显示提示
            if (string.IsNullOrEmpty(confirmPassword))
            {
                PasswordMatchHint.Text = "请再次输入密码";
                PasswordMatchHint.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
                return;
            }

            // 检查两次密码是否一致
            if (password == confirmPassword)
            {
                PasswordMatchHint.Text = "✓ 两次密码一致";
                PasswordMatchHint.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
            }
            else
            {
                PasswordMatchHint.Text = "✗ 两次密码不一致";
                PasswordMatchHint.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
            }
        }

        private async void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            var username = UsernameTextBox.Text.Trim();
            var password = PasswordBox.Password;
            var confirmPassword = ConfirmPasswordBox.Password;
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

            // 验证邮箱（必填）
            if (string.IsNullOrEmpty(email))
            {
                ShowStatus("请输入邮箱", isError: true);
                EmailTextBox.Focus();
                return;
            }

            if (!Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            {
                ShowStatus("邮箱格式不正确", isError: true);
                EmailTextBox.Focus();
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

            // 验证重复密码
            if (string.IsNullOrEmpty(confirmPassword))
            {
                ShowStatus("请再次输入密码", isError: true);
                ConfirmPasswordBox.Focus();
                return;
            }

            // 验证两次密码是否一致
            if (password != confirmPassword)
            {
                ShowStatus("两次输入的密码不一致，请重新输入", isError: true);
                ConfirmPasswordBox.Focus();
                ConfirmPasswordBox.Clear();
                return;
            }

            // 显示注册中状态
            ShowStatus("正在注册...", isError: false);
            RegisterButton.IsEnabled = false;
            CancelButton.IsEnabled = false;

            try
            {
                // 设置超时：60秒
                var registerTask = AuthService.Instance.RegisterAsync(
                    username, 
                    password, 
                    email  // 邮箱必填
                );
                var timeoutTask = System.Threading.Tasks.Task.Delay(60000); // 60秒超时

                var completedTask = await System.Threading.Tasks.Task.WhenAny(registerTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    // 超时
                    ShowStatus("连接超时：网络不佳或服务器无响应，请检查网络后重试", isError: true);
                    return;
                }

                // 获取注册结果
                var (success, message) = await registerTask;

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
                ShowStatus($"注册异常：{ex.Message}", isError: true);
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [注册] 未知异常: {ex}");
                #endif
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

