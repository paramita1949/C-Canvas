using System;
using System.Windows;
using System.Windows.Media;

namespace ImageColorChanger.UI
{
    public partial class StartupSplashWindow : Window
    {
        public StartupSplashWindow(string initialStatus, string scriptureText)
        {
            InitializeComponent();
            SetStatus(initialStatus);
            SetScripture(scriptureText);
            LoadSplashImage();
        }

        public void SetStatus(string status)
        {
            StatusText.Text = string.IsNullOrWhiteSpace(status)
                ? "正在启动..."
                : status.Trim();
        }

        public void SetScripture(string scriptureText)
        {
            string displayText = string.IsNullOrWhiteSpace(scriptureText)
                ? StartupScriptureVerseProvider.GetRandomVerseText()
                : scriptureText.Trim();
            StartupScriptureVerseProvider.SplitDisplayText(displayText, out string text, out string reference);
            string referenceCaption = StartupScriptureVerseProvider.FormatReferenceCaption(reference);

            CenterScriptureText.Text = text;
            CenterScriptureReferenceText.Text = referenceCaption;
            CenterScriptureReferenceText.Visibility = string.IsNullOrWhiteSpace(referenceCaption)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void LoadSplashImage()
        {
            try
            {
                if (StartupSplashImageResolver.TryLoad(out ImageSource imageSource))
                {
                    SplashImage.Source = imageSource;
                }
            }
            catch
            {
                // 开屏图加载失败不能阻断应用启动。
            }
        }
    }
}
