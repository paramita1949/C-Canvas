using System.Collections.ObjectModel;
using System.ComponentModel;
using ImageColorChanger.Database.Models;

namespace ImageColorChanger.UI
{
    public class ProjectTreeItem : INotifyPropertyChanged
    {
        public int Id { get; set; }
        
        private string _name;
        public string Name 
        { 
            get => _name; 
            set 
            { 
                if (_name != value) 
                { 
                    _name = value; 
                    OnPropertyChanged(nameof(Name)); 
                } 
            } 
        }
        
        public string Icon { get; set; }
        private string _iconKind;
        public string IconKind 
        { 
            get => _iconKind; 
            set 
            { 
                if (_iconKind != value) 
                { 
                    _iconKind = value; 
                    OnPropertyChanged(nameof(IconKind)); 
                } 
            } 
        }

        private string _iconColor = "#666666";
        public string IconColor 
        { 
            get => _iconColor; 
            set 
            { 
                if (_iconColor != value) 
                { 
                    _iconColor = value; 
                    OnPropertyChanged(nameof(IconColor)); 
                } 
            } 
        }

        private string _iconImagePath;
        public string IconImagePath
        {
            get => _iconImagePath;
            set
            {
                if (_iconImagePath != value)
                {
                    _iconImagePath = value;
                    OnPropertyChanged(nameof(IconImagePath));
                    OnPropertyChanged(nameof(HasIconImagePath));
                }
            }
        }

        public bool HasIconImagePath => !string.IsNullOrWhiteSpace(_iconImagePath);
        public TreeItemType Type { get; set; }
        public string Path { get; set; }
        public FileType FileType { get; set; }
        public object Tag { get; set; }  // 通用标签，用于存储额外数据（如圣经书卷ID和章节）
        public ObservableCollection<ProjectTreeItem> Children { get; set; } = new ObservableCollection<ProjectTreeItem>();
        
        // 文件夹标签（用于在搜索结果中显示所属文件夹）
        public string FolderName { get; set; }  // 所属文件夹名称
        public string FolderColor { get; set; } = "#666666";  // 文件夹标记颜色
        public bool ShowFolderTag { get; set; } = false;  // 是否显示文件夹标签

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged(nameof(IsExpanded));
                }
            }
        }

        private bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing != value)
                {
                    _isEditing = value;
                    OnPropertyChanged(nameof(IsEditing));
                }
            }
        }

        // 编辑前的原始名称
        public string OriginalName { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum TreeItemType
    {
        Project,
        Folder,
        File,
        Image,
        Video,
        Audio,
        TextProject,     // 文本项目
        LyricsRoot,      // 歌词库根节点
        LyricsGroup,     // 歌词分组
        LyricsSong,      // 歌曲歌词
        BibleTestament,  // 圣经约（旧约/新约）
        BibleBook,       // 圣经书卷
        BibleChapter     // 圣经章节
    }


}
