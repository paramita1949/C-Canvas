using System;
using System.Text.RegularExpressions;
using System.Windows;
using ImageColorChanger.Services;
using ImageColorChanger.Services.Interfaces;

namespace ImageColorChanger.UI
{
    public partial class ResetPasswordWindow : Window
    {
        private readonly IAuthFacade _authFacade;
        private int _countdown = 0;
        private System.Windows.Threading.DispatcherTimer _countdownTimer;
        private bool _showCountdownInStatus = false;
        private bool _countdownStatusIsError = false;

        public ResetPasswordWindow(IAuthFacade authFacade)
        {
            _authFacade = authFacade ?? throw new ArgumentNullException(nameof(authFacade));
            InitializeComponent();

            // 订阅服务器切换事件
            _authFacade.ServerSwitching += OnServerSwitching;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 聚焦到用户名输入框
            UsernameTextBox.Focus();
        }

        protected override void OnClosed(EventArgs e)
        {
            // 取消订阅事件
            _authFacade.ServerSwitching -= OnServerSwitching;

            // 清理定时器
            if (_countdownTimer != null)
            {
                _countdownTimer.Stop();
                _countdownTimer = null;
            }

            base.OnClosed(e);
        }

        /// <summary>
        /// 服务器切换事件处理
        /// </summary>
        private void OnServerSwitching(object sender, ServerSwitchEventArgs e)
        {
            // 在UI线程上更新状态
            Dispatcher.Invoke(() =>
            {
                ShowStatus(e.Message, isError: false);
            });
        }

        private async void SendCodeButton_Click(object sender, RoutedEventArgs e)
        {
            var username = UsernameTextBox.Text.Trim();
            var email = EmailTextBox.Text.Trim();

            // 验证输入
            if (string.IsNullOrEmpty(username))
            {
                ShowStatus("请输入用户名", isError: true);
                UsernameTextBox.Focus();
                return;
            }

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

            // 禁用发送按钮
            SendCodeButton.IsEnabled = false;
            ShowStatus("正在发送验证码...", isError: false);

            try
            {
                var (success, message) = await _authFacade.SendVerificationCodeAsync(username, email);

                if (success)
                {
                    ShowStatus("验证码已发送到您的邮箱，10分钟内有效", isError: false);
                    
                    // 开始60秒倒计时
                    StartCountdown(60);
                    
                    // 聚焦到验证码输入框
                    CodeTextBox.Focus();
                }
                else
                {
                    ShowStatus(message, isError: true);

                    if (TryExtractRetryAfterSeconds(message, out var retrySeconds))
                    {
                        StartCountdown(retrySeconds, showStatus: true, statusIsError: true);
                    }
                    else
                    {
                        SendCodeButton.IsEnabled = true;
                    }
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"发送失败：{ex.Message}", isError: true);
                SendCodeButton.IsEnabled = true;
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($" [重置密码] 发送验证码异常: {ex}");
                #endif
            }
        }

        private async void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            var email = EmailTextBox.Text.Trim();
            var code = CodeTextBox.Text.Trim();
            var newPassword = NewPasswordBox.Password;

            // 验证输入
            if (string.IsNullOrEmpty(email))
            {
                ShowStatus("请输入邮箱", isError: true);
                EmailTextBox.Focus();
                return;
            }

            if (string.IsNullOrEmpty(code))
            {
                ShowStatus("请输入验证码", isError: true);
                CodeTextBox.Focus();
                return;
            }

            if (code.Length != 6 || !Regex.IsMatch(code, @"^\d{6}$"))
            {
                ShowStatus("验证码格式不正确（6位数字）", isError: true);
                CodeTextBox.Focus();
                return;
            }

            if (string.IsNullOrEmpty(newPassword))
            {
                ShowStatus("请输入新密码", isError: true);
                NewPasswordBox.Focus();
                return;
            }

            if (newPassword.Length < 6)
            {
                ShowStatus("新密码至少6个字符", isError: true);
                NewPasswordBox.Focus();
                return;
            }

            // 禁用按钮
            ResetButton.IsEnabled = false;
            CancelButton.IsEnabled = false;
            ShowStatus("正在重置密码...", isError: false);

            try
            {
                var (success, message) = await _authFacade.ResetPasswordAsync(email, code, newPassword);

                if (success)
                {
                    ShowStatus("密码重置成功！", isError: false);
                    
                    // 延迟关闭窗口
                    await System.Threading.Tasks.Task.Delay(1500);
                    
                    System.Windows.MessageBox.Show(
                        "密码重置成功，请使用新密码登录。",
                        "成功",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                    
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
                ShowStatus($"重置失败：{ex.Message}", isError: true);
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($" [重置密码] 异常: {ex}");
                #endif
            }
            finally
            {
                ResetButton.IsEnabled = true;
                CancelButton.IsEnabled = true;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void StartCountdown(int seconds, bool showStatus = false, bool statusIsError = false)
        {
            _countdown = seconds;
            _showCountdownInStatus = showStatus;
            _countdownStatusIsError = statusIsError;

            UpdateCountdownButton();

            if (_showCountdownInStatus)
            {
                ShowStatus($"请在 {_countdown} 秒后重试", _countdownStatusIsError);
            }

            if (_countdownTimer == null)
            {
                _countdownTimer = new System.Windows.Threading.DispatcherTimer();
                _countdownTimer.Interval = TimeSpan.FromSeconds(1);
                _countdownTimer.Tick += CountdownTimer_Tick;
            }

            _countdownTimer.Start();
        }

        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            _countdown--;

            if (_countdown <= 0)
            {
                _countdownTimer.Stop();
                SendCodeButton.IsEnabled = true;
                SendCodeButton.Content = "发送验证码";

                if (_showCountdownInStatus)
                {
                    ShowStatus("现在可以重新发送验证码", isError: false);
                    _showCountdownInStatus = false;
                }
            }
            else
            {
                UpdateCountdownButton();

                if (_showCountdownInStatus)
                {
                    ShowStatus($"请在 {_countdown} 秒后重试", _countdownStatusIsError);
                }
            }
        }

        private void UpdateCountdownButton()
        {
            SendCodeButton.Content = $"重新发送 ({_countdown}s)";
            SendCodeButton.IsEnabled = false;
        }

        private void ShowStatus(string message, bool isError)
        {
            StatusText.Text = message;
            StatusText.Foreground = isError
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red)
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
            StatusText.Visibility = Visibility.Visible;
        }

        private bool TryExtractRetryAfterSeconds(string message, out int seconds)
        {
            seconds = 0;

            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            var jsonMatch = Regex.Match(message, "\\\"retry_after\\\"\\s*:\\s*(\\d+)");
            if (jsonMatch.Success && int.TryParse(jsonMatch.Groups[1].Value, out seconds) && seconds > 0)
            {
                return true;
            }

            var textMatch = Regex.Match(message, @"请在\s*(\d+)\s*秒后重试");
            if (textMatch.Success && int.TryParse(textMatch.Groups[1].Value, out seconds) && seconds > 0)
            {
                return true;
            }

            return false;
        }
    }
}


