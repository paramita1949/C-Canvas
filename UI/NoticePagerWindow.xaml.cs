using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace ImageColorChanger.UI
{
    public partial class NoticePagerWindow : Window
    {
        public class NoticeDisplayItem
        {
            public string Title { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
        }

        private readonly List<NoticeDisplayItem> _items;
        private int _index;

        public NoticePagerWindow(IEnumerable<NoticeDisplayItem> notices)
        {
            InitializeComponent();
            _items = notices?.Where(n => n != null).ToList() ?? new List<NoticeDisplayItem>();

            if (_items.Count == 0)
            {
                _items.Add(new NoticeDisplayItem
                {
                    Title = "系统通知",
                    Message = "暂无消息内容。"
                });
            }

            _index = 0;
            RenderCurrent();
        }

        private void RenderCurrent()
        {
            var item = _items[_index];
            TitleTextBlock.Text = string.IsNullOrWhiteSpace(item.Title) ? "系统通知" : item.Title.Trim();
            MessageTextBlock.Text = item.Message?.Trim() ?? string.Empty;
            PageTextBlock.Text = $"第 {_index + 1} 条 / 共 {_items.Count} 条";

            PrevButton.IsEnabled = _index > 0;
            NextButton.IsEnabled = _index < _items.Count - 1;
        }

        private void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            if (_index <= 0) return;
            _index--;
            RenderCurrent();
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_index >= _items.Count - 1) return;
            _index++;
            RenderCurrent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

