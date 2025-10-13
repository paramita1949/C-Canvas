using CommunityToolkit.Mvvm.ComponentModel;

namespace ImageColorChanger.ViewModels
{
    /// <summary>
    /// ViewModel基类
    /// 使用CommunityToolkit.Mvvm提供的ObservableObject
    /// </summary>
    public abstract class ViewModelBase : ObservableObject
    {
        // 基类提供通用的属性变化通知功能
        // 继承自ObservableObject，自动实现INotifyPropertyChanged
    }
}

