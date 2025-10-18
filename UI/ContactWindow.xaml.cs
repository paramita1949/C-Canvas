using System.Windows;
using System.Windows.Media.Imaging;
using ImageColorChanger.Core;

namespace ImageColorChanger.UI
{
    public partial class ContactWindow : Window
    {
        public ContactWindow()
        {
            InitializeComponent();
            LoadImages();
        }
        
        /// <summary>
        /// 从PAK或文件系统加载图片资源
        /// </summary>
        private void LoadImages()
        {
            // 加载微信二维码
            var weixinImage = ResourceLoader.LoadImage("weixin.png");
            if (weixinImage != null && WeixinImage != null)
            {
                WeixinImage.Source = weixinImage;
            }
            
            // 加载赞助二维码
            var payImage = ResourceLoader.LoadImage("pay.png");
            if (payImage != null && PayImage != null)
            {
                PayImage.Source = payImage;
            }
        }
    }
}

