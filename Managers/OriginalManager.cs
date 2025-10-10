using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.UI;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 原图模式管理器
    /// 负责管理原图标记、相似图片查找、原图模式切换等功能
    /// </summary>
    public class OriginalManager
    {
        private readonly DatabaseManager _dbManager;
        private readonly Window _mainWindow;

        // 相似图片切换功能的状态变量
        private List<(int id, string name, string path)> _similarImages = new List<(int, string, string)>();
        private int _currentSimilarIndex = 0;

        // 预缓存状态跟踪
        private HashSet<string> _cachedImageGroups = new HashSet<string>();

        public OriginalManager(DatabaseManager dbManager, Window mainWindow)
        {
            _dbManager = dbManager;
            _mainWindow = mainWindow;
        }

        #region 原图标记管理

        /// <summary>
        /// 添加原图标记
        /// </summary>
        public bool AddOriginalMark(ItemType itemType, int itemId, MarkType markType = MarkType.Loop)
        {
            try
            {
                var mark = new OriginalMark
                {
                    ItemType = itemType,
                    ItemId = itemId,
                    MarkType = markType,
                    CreatedTime = DateTime.Now
                };

                return _dbManager.AddOriginalMark(mark);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"添加原图标记失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 移除原图标记
        /// </summary>
        public bool RemoveOriginalMark(ItemType itemType, int itemId)
        {
            try
            {
                return _dbManager.RemoveOriginalMark(itemType, itemId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"移除原图标记失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 检查是否有原图标记
        /// </summary>
        public bool CheckOriginalMark(ItemType itemType, int itemId)
        {
            try
            {
                return _dbManager.CheckOriginalMark(itemType, itemId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"检查原图标记失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取原图标记类型
        /// </summary>
        public MarkType? GetOriginalMarkType(ItemType itemType, int itemId)
        {
            try
            {
                return _dbManager.GetOriginalMarkType(itemType, itemId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取原图标记类型失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 判断图片是否应该使用原图模式
        /// </summary>
        public bool ShouldUseOriginalMode(int imageId)
        {
            try
            {
                // 检查图片本身是否有标记
                if (CheckOriginalMark(ItemType.Image, imageId))
                    return true;

                // 检查图片所在文件夹是否有标记
                var mediaFile = _dbManager.GetMediaFileById(imageId);
                if (mediaFile?.FolderId != null)
                {
                    return CheckOriginalMark(ItemType.Folder, mediaFile.FolderId.Value);
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"判断原图模式失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 相似图片查找和切换

        /// <summary>
        /// 检查是否有相似图片
        /// </summary>
        public bool HasSimilarImages()
        {
            return _similarImages.Count > 0;
        }

        /// <summary>
        /// 查找与当前图片名称相似的其他图片
        /// </summary>
        public bool FindSimilarImages(int imageId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🔍 FindSimilarImages: imageId={imageId}");
                
                var currentFile = _dbManager.GetMediaFileById(imageId);
                if (currentFile == null || currentFile.FolderId == null)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 无法找到图片或文件夹: imageId={imageId}");
                    _similarImages.Clear();
                    _currentSimilarIndex = 0;
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"📁 当前文件: {currentFile.Name}, FolderId={currentFile.FolderId}");

                // 提取基本名称
                string baseName = ExtractBaseName(currentFile.Name);
                System.Diagnostics.Debug.WriteLine($"📝 基本名称: {baseName}");

                // 查找同一文件夹中的所有图片
                var allImages = _dbManager.GetMediaFilesByFolder(currentFile.FolderId.Value, FileType.Image);
                System.Diagnostics.Debug.WriteLine($"📂 文件夹中共有 {allImages.Count} 张图片");

                // 筛选出名称相似的图片
                _similarImages = allImages
                    .Where(img => IsSameSongSeries(currentFile.Name, img.Name))
                    .OrderBy(img => img.OrderIndex)
                    .Select(img => (img.Id, img.Name, img.Path))
                    .ToList();
                
                System.Diagnostics.Debug.WriteLine($"🔎 筛选后找到 {_similarImages.Count} 张相似图片");

                if (_similarImages.Count > 0)
                {
                    // 找到当前图片在相似图片列表中的索引
                    for (int i = 0; i < _similarImages.Count; i++)
                    {
                        if (_similarImages[i].id == imageId)
                        {
                            _currentSimilarIndex = i;
                            break;
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"✅ 找到 {_similarImages.Count} 张相似图片, 当前索引: {_currentSimilarIndex}");
                    return true;
                }

                _similarImages.Clear();
                _currentSimilarIndex = 0;
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"查找相似图片失败: {ex.Message}");
                _similarImages.Clear();
                _currentSimilarIndex = 0;
                return false;
            }
        }

        /// <summary>
        /// 切换到下一张/上一张相似图片
        /// </summary>
        public (bool success, int? newImageId, string newImagePath) SwitchSimilarImage(bool isNext, int currentImageId)
        {
            System.Diagnostics.Debug.WriteLine($"🔍 SwitchSimilarImage: isNext={isNext}, currentImageId={currentImageId}, _similarImages.Count={_similarImages.Count}");
            
            if (_similarImages.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("❌ 没有相似图片,无法切换");
                return (false, null, null);
            }

            try
            {
                // 获取当前图片的标记类型来决定切换模式
                MarkType switchMode = MarkType.Loop; // 默认循环模式

                // 检查图片本身的标记类型
                var markType = GetOriginalMarkType(ItemType.Image, currentImageId);
                if (markType != null)
                {
                    switchMode = markType.Value;
                }
                else
                {
                    // 检查图片所在文件夹的标记类型
                    var mediaFile = _dbManager.GetMediaFileById(currentImageId);
                    if (mediaFile != null && mediaFile.FolderId.HasValue)
                    {
                        var folderMarkType = GetOriginalMarkType(ItemType.Folder, mediaFile.FolderId.Value);
                        if (folderMarkType.HasValue)
                        {
                            switchMode = folderMarkType.Value;
                        }
                    }
                }

                // 计算新的索引
                int newIndex;
                if (switchMode == MarkType.Loop)
                {
                    // 循环模式：到最后一张时回到第一张
                    if (isNext)
                    {
                        newIndex = (_currentSimilarIndex + 1) % _similarImages.Count;
                    }
                    else
                    {
                        newIndex = (_currentSimilarIndex - 1 + _similarImages.Count) % _similarImages.Count;
                    }
                }
                else
                {
                    // 顺序模式：按照顺序切换，到边界时返回false
                    newIndex = isNext ? _currentSimilarIndex + 1 : _currentSimilarIndex - 1;
                    
                    if (newIndex < 0 || newIndex >= _similarImages.Count)
                    {
                        // 顺序模式下到达边界，需要切换到不同系列的图片
                        return SwitchToDifferentImage(isNext, currentImageId);
                    }
                }

                // 更新当前索引
                _currentSimilarIndex = newIndex;

                var (targetId, targetName, targetPath) = _similarImages[newIndex];
                
                string modeText = switchMode == MarkType.Loop ? "循环" : "顺序";
                System.Diagnostics.Debug.WriteLine($"📷 {modeText}切换: {targetName} (索引 {newIndex}/{_similarImages.Count})");

                return (true, targetId, targetPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"切换相似图片失败: {ex.Message}");
                return (false, null, null);
            }
        }

        /// <summary>
        /// 切换到下一个/上一个不同系列的图片
        /// </summary>
        private (bool success, int? newImageId, string newImagePath) SwitchToDifferentImage(bool isNext, int currentImageId)
        {
            try
            {
                var currentFile = _dbManager.GetMediaFileById(currentImageId);
                if (currentFile == null || !currentFile.FolderId.HasValue)
                    return (false, null, null);

                int folderId = currentFile.FolderId.Value;

                // 根据方向查找下一个/上一个图片
                MediaFile targetFile;
                if (isNext)
                {
                    targetFile = _dbManager.GetNextMediaFile(folderId, currentFile.OrderIndex, FileType.Image);
                }
                else
                {
                    targetFile = _dbManager.GetPreviousMediaFile(folderId, currentFile.OrderIndex, FileType.Image);
                }

                if (targetFile != null)
                {
                    System.Diagnostics.Debug.WriteLine($"📷 切换到不同系列: {targetFile.Name}");
                    return (true, targetFile.Id, targetFile.Path);
                }

                string directionText = isNext ? "下一张" : "上一张";
                System.Diagnostics.Debug.WriteLine($"⚠️ 没有找到{directionText}图片");
                return (false, null, null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"切换到不同图片失败: {ex.Message}");
                return (false, null, null);
            }
        }

        #endregion

        #region 名称匹配逻辑

        /// <summary>
        /// 提取图片名称的基本部分（去除数字后缀）
        /// </summary>
        private string ExtractBaseName(string name)
        {
            // 处理类似 "第0001首 圣哉三一1" 和 "第0001首 圣哉三一2" 的情况
            var match = Regex.Match(name, @"(第\d+首\s+[^\d]+)\d*$");
            if (match.Success)
                return match.Groups[1].Value;

            // 处理类似 "001.圣哉三一歌1" 和 "001.圣哉三一歌2" 的情况
            match = Regex.Match(name, @"(\d+\.[^0-9]+)\d*$");
            if (match.Success)
                return match.Groups[1].Value;

            // 如果没有匹配到特定格式，尝试去掉末尾的数字
            match = Regex.Match(name, @"(.+?)\d+$");
            if (match.Success)
                return match.Groups[1].Value;

            // 如果没有数字后缀，返回完整名称
            return name;
        }

        /// <summary>
        /// 判断两个图片是否属于同一首歌的不同页
        /// </summary>
        private bool IsSameSongSeries(string name1, string name2)
        {
            // 处理类似 "第0001首 圣哉三一1" 和 "第0001首 圣哉三一2" 的情况
            var pattern1 = @"第(\d+)首\s+([^\d]+)(\d*)$";
            var match1 = Regex.Match(name1, pattern1);
            var match2 = Regex.Match(name2, pattern1);

            if (match1.Success && match2.Success)
            {
                string songNum1 = match1.Groups[1].Value;
                string baseName1 = match1.Groups[2].Value;
                string songNum2 = match2.Groups[1].Value;
                string baseName2 = match2.Groups[2].Value;

                // 必须是相同的歌曲编号
                if (songNum1 == songNum2)
                {
                    return baseName1.Trim() == baseName2.Trim();
                }
            }

            // 处理类似 "001.圣哉三一歌1" 和 "001.圣哉三一歌2" 的情况
            var pattern2 = @"(\d+)\.([^0-9]+)(\d*)$";
            match1 = Regex.Match(name1, pattern2);
            match2 = Regex.Match(name2, pattern2);

            if (match1.Success && match2.Success)
            {
                string songNum1 = match1.Groups[1].Value;
                string baseName1 = match1.Groups[2].Value;
                string songNum2 = match2.Groups[1].Value;
                string baseName2 = match2.Groups[2].Value;

                // 必须是相同的歌曲编号
                if (songNum1 == songNum2)
                {
                    return baseName1.Trim() == baseName2.Trim();
                }
            }

            // 如果没有特定格式，则比较去掉末尾数字后的名称
            string base1 = Regex.Replace(name1, @"\d+$", "").Trim();
            string base2 = Regex.Replace(name2, @"\d+$", "").Trim();

            // 只有当基本名称完全相同且不为空时才认为是相似的
            return !string.IsNullOrEmpty(base1) && base1 == base2;
        }

        #endregion

        #region 项目树图标更新

        /// <summary>
        /// 获取文件夹的 Material Design 图标类型
        /// </summary>
        public (string iconKind, string color) GetFolderIconKind(int folderId, bool isManualSort)
        {
            try
            {
                bool hasMark = CheckOriginalMark(ItemType.Folder, folderId);
                
                if (hasMark)
                {
                    var markType = GetOriginalMarkType(ItemType.Folder, folderId);
                    if (markType == MarkType.Sequence)
                    {
                        // 顺序原图标记 - 使用 PlayArrow 图标
                        return isManualSort ? ("FolderPlay", "#FF6B35") : ("PlayArrow", "#FF6B35");
                    }
                    else
                    {
                        // 循环原图标记 - 使用 Repeat 图标
                        return isManualSort ? ("FolderSync", "#4ECDC4") : ("Repeat", "#4ECDC4");
                    }
                }
                else
                {
                    // 无原图标记
                    return isManualSort ? ("FolderCog", "#FDB44B") : ("Folder", "#FDB44B");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取文件夹图标失败: {ex.Message}");
                return ("Folder", "#FDB44B");
            }
        }

        /// <summary>
        /// 获取独立图片的 Material Design 图标类型
        /// </summary>
        public (string iconKind, string color) GetImageIconKind(int imageId)
        {
            try
            {
                bool hasMark = CheckOriginalMark(ItemType.Image, imageId);
                // 有标记使用 Star，无标记使用 Image
                return hasMark ? ("Star", "#FFD700") : ("Image", "#95E1D3");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取图片图标失败: {ex.Message}");
                return ("Image", "#95E1D3");
            }
        }

        // 保留旧方法以兼容
        public string GetFolderIcon(int folderId, bool isManualSort)
        {
            var (iconKind, _) = GetFolderIconKind(folderId, isManualSort);
            return iconKind;
        }

        public string GetImageIcon(int imageId)
        {
            var (iconKind, _) = GetImageIconKind(imageId);
            return iconKind;
        }

        #endregion
    }
}

