using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Models.Enums;
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
        
        //  性能优化：文件夹图片列表缓存
        private int? _cachedFolderId = null;
        private List<MediaFile> _cachedFolderImages = null;
        private MediaFile _cachedCurrentFile = null;

        public OriginalManager(DatabaseManager dbManager, Window mainWindow)
        {
            _dbManager = dbManager;
            _mainWindow = mainWindow;
        }
        
        /// <summary>
        /// 清除缓存（在扫描文件夹后调用）
        /// </summary>
        public void ClearCache()
        {
            _cachedFolderId = null;
            _cachedFolderImages = null;
            _cachedCurrentFile = null;
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
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"添加原图标记失败: {ex.Message}");
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
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"移除原图标记失败: {ex.Message}");
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
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"检查原图标记失败: {ex.Message}");
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
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"获取原图标记类型失败: {ex.Message}");
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

                //  性能优化：使用缓存的文件信息，避免重复数据库查询
                MediaFile mediaFile;
                if (_cachedCurrentFile != null && _cachedCurrentFile.Id == imageId)
                {
                    mediaFile = _cachedCurrentFile;
                }
                else
                {
                    mediaFile = _dbManager.GetMediaFileById(imageId);
                    _cachedCurrentFile = mediaFile;
                }
                
                if (mediaFile?.FolderId != null)
                {
                    return CheckOriginalMark(ItemType.Folder, mediaFile.FolderId.Value);
                }

                return false;
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"判断原图模式失败: {ex.Message}");
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
        /// 获取相似图片列表（用于预缓存）
        /// </summary>
        public List<(int id, string name, string path)> GetSimilarImages()
        {
            return new List<(int id, string name, string path)>(_similarImages);
        }

        /// <summary>
        /// 获取第一张相似图片
        /// </summary>
        public (bool success, int? firstImageId, string firstImagePath) GetFirstSimilarImage()
        {
            if (_similarImages.Count > 0)
            {
                var (id, name, path) = _similarImages[0];
                return (true, id, path);
            }
            return (false, null, null);
        }

        /// <summary>
        /// 查找与当前图片名称相似的其他图片
        /// </summary>
        public bool FindSimilarImages(int imageId)
        {
            try
            {
                // System.Diagnostics.Debug.WriteLine($" FindSimilarImages: imageId={imageId}");
                
                //  性能优化：使用缓存避免重复数据库查询
                MediaFile currentFile;
                
                // 检查是否是缓存的当前文件
                if (_cachedCurrentFile != null && _cachedCurrentFile.Id == imageId)
                {
                    currentFile = _cachedCurrentFile;
                }
                else
                {
                    // 不是缓存的文件，需要查询数据库
                    currentFile = _dbManager.GetMediaFileById(imageId);
                    _cachedCurrentFile = currentFile;
                }
                
                if (currentFile == null || currentFile.FolderId == null)
                {
                    // System.Diagnostics.Debug.WriteLine($" 无法找到图片或文件夹: imageId={imageId}");
                    _similarImages.Clear();
                    _currentSimilarIndex = 0;
                    return false;
                }

                // System.Diagnostics.Debug.WriteLine($" 当前文件: {currentFile.Name}, FolderId={currentFile.FolderId}");

                // 提取基本名称
                string baseName = ExtractBaseName(currentFile.Name);
                // System.Diagnostics.Debug.WriteLine($" 基本名称: {baseName}");

                //  性能优化：检查文件夹图片列表缓存
                List<MediaFile> allImages;
                
                if (_cachedFolderId == currentFile.FolderId && _cachedFolderImages != null)
                {
                    // 使用缓存的文件夹图片列表
                    allImages = _cachedFolderImages;
                }
                else
                {
                    // 缓存未命中，查询数据库并缓存结果
                    allImages = _dbManager.GetMediaFilesByFolder(currentFile.FolderId.Value, FileType.Image);
                    _cachedFolderId = currentFile.FolderId;
                    _cachedFolderImages = allImages;
                }
                
                // System.Diagnostics.Debug.WriteLine($" 文件夹中共有 {allImages.Count} 张图片 (缓存命中: {_cachedFolderId == currentFile.FolderId})");

                // 筛选出名称相似的图片
                _similarImages = allImages
                    .Where(img => IsSameSongSeries(currentFile.Name, img.Name))
                    .OrderBy(img => img.OrderIndex)
                    .Select(img => (img.Id, img.Name, img.Path))
                    .ToList();
                
                // System.Diagnostics.Debug.WriteLine($" 筛选后找到 {_similarImages.Count} 张相似图片");

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

                    // System.Diagnostics.Debug.WriteLine($" 找到 {_similarImages.Count} 张相似图片, 当前索引: {_currentSimilarIndex}");
                    return true;
                }

                _similarImages.Clear();
                _currentSimilarIndex = 0;
                return false;
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"查找相似图片失败: {ex.Message}");
                _similarImages.Clear();
                _currentSimilarIndex = 0;
                return false;
            }
        }

        /// <summary>
        /// 切换到下一张/上一张相似图片
        /// </summary>
        /// <returns>返回元组：(成功标志, 新图片ID, 新图片路径, 是否循环完成)</returns>
        public (bool success, int? newImageId, string newImagePath, bool isLoopCompleted) SwitchSimilarImage(bool isNext, int currentImageId)
        {
            // System.Diagnostics.Debug.WriteLine($" SwitchSimilarImage: isNext={isNext}, currentImageId={currentImageId}");
            
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

                // System.Diagnostics.Debug.WriteLine($" 切换模式: {(switchMode == MarkType.Loop ? "循环" : "顺序")}");

                // 顺序模式：在文件夹所有图片中按顺序切换
                if (switchMode == MarkType.Sequence)
                {
                    var seqResult = SwitchInSequenceMode(isNext, currentImageId);
                    return (seqResult.success, seqResult.newImageId, seqResult.newImagePath, false); // 顺序模式不会循环完成
                }
                
                // 循环模式：在相似图片列表中循环切换
                return SwitchInLoopMode(isNext, currentImageId);
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"切换图片失败: {ex.Message}");
                return (false, null, null, false);
            }
        }

        /// <summary>
        /// 顺序模式：在文件夹所有图片中按顺序切换
        /// </summary>
        private (bool success, int? newImageId, string newImagePath) SwitchInSequenceMode(bool isNext, int currentImageId)
        {
            try
            {
                var currentFile = _dbManager.GetMediaFileById(currentImageId);
                if (currentFile == null || !currentFile.FolderId.HasValue)
                {
                    //System.Diagnostics.Debug.WriteLine(" 当前图片不在文件夹中");
                    return (false, null, null);
                }

                int folderId = currentFile.FolderId.Value;
                
                // 获取文件夹中所有图片
                // 优先使用OrderIndex（自定义排序），如果没有OrderIndex则按文件名排序
                var allImages = _dbManager.GetMediaFilesByFolder(folderId)
                    .Where(f => f.FileType == FileType.Image)
                    .OrderBy(f => f.OrderIndex ?? int.MaxValue) // 优先使用OrderIndex
                    .ThenBy(f => f.Name) // OrderIndex相同时按文件名排序
                    .ToList();

                if (allImages.Count == 0)
                {
                    //System.Diagnostics.Debug.WriteLine(" 文件夹中没有图片");
                    return (false, null, null);
                }

                // 找到当前图片的索引
                int currentIndex = allImages.FindIndex(f => f.Id == currentImageId);
                if (currentIndex == -1)
                {
                    //System.Diagnostics.Debug.WriteLine(" 未找到当前图片在列表中的位置");
                    return (false, null, null);
                }

                // 计算下一张图片的索引（顺序模式：到边界时停止）
                int newIndex = isNext ? currentIndex + 1 : currentIndex - 1;
                
                if (newIndex < 0 || newIndex >= allImages.Count)
                {
                    // System.Diagnostics.Debug.WriteLine($" 顺序模式已到达边界 (当前索引: {currentIndex}, 总数: {allImages.Count})");
                    return (false, null, null);
                }

                var targetImage = allImages[newIndex];
                // System.Diagnostics.Debug.WriteLine($" 顺序切换: {targetImage.Name} (索引 {newIndex + 1}/{allImages.Count})");

                return (true, targetImage.Id, targetImage.Path);
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"顺序模式切换失败: {ex.Message}");
                return (false, null, null);
            }
        }

        /// <summary>
        /// 循环模式：在相似图片列表中循环切换
        /// </summary>
        private (bool success, int? newImageId, string newImagePath, bool isLoopCompleted) SwitchInLoopMode(bool isNext, int currentImageId)
        {
            try
            {
                // 如果相似图片列表为空，先查找相似图片
                if (_similarImages.Count == 0)
                {
                    // System.Diagnostics.Debug.WriteLine(" 相似图片列表为空，自动查找相似图片...");
                    bool found = FindSimilarImages(currentImageId);
                    
                    if (!found || _similarImages.Count == 0)
                    {
                        // System.Diagnostics.Debug.WriteLine(" 没有相似图片,无法切换");
                        return (false, null, null, false);
                    }
                    
                    // System.Diagnostics.Debug.WriteLine($" 已找到 {_similarImages.Count} 张相似图片");
                }

                // 保存当前索引，用于检测循环完成
                int oldIndex = _currentSimilarIndex;

                // 循环模式：到最后一张时回到第一张
                int newIndex;
                if (isNext)
                {
                    newIndex = (_currentSimilarIndex + 1) % _similarImages.Count;
                }
                else
                {
                    newIndex = (_currentSimilarIndex - 1 + _similarImages.Count) % _similarImages.Count;
                }

                //  检测循环完成：向下切换且回到第一张（索引0）
                bool isLoopCompleted = isNext && newIndex == 0 && _similarImages.Count > 1;
                
                if (isLoopCompleted)
                {
                    // System.Diagnostics.Debug.WriteLine($" 检测到循环完成: 从索引{oldIndex}回到索引0");
                }

                // 更新当前索引
                _currentSimilarIndex = newIndex;

                var (targetId, targetName, targetPath) = _similarImages[newIndex];
                
                // System.Diagnostics.Debug.WriteLine($" 循环切换: {targetName} (索引 {newIndex + 1}/{_similarImages.Count})");

                return (true, targetId, targetPath, isLoopCompleted);
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"循环模式切换失败: {ex.Message}");
                return (false, null, null, false);
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
                    //System.Diagnostics.Debug.WriteLine($" 切换到不同系列: {targetFile.Name}");
                    return (true, targetFile.Id, targetFile.Path);
                }

                string directionText = isNext ? "下一张" : "上一张";
                //System.Diagnostics.Debug.WriteLine($" 没有找到{directionText}图片");
                return (false, null, null);
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"切换到不同图片失败: {ex.Message}");
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
                        // 顺序原图标记 - 文件夹带顺序
                        return ("FolderDownload", "#FF6B35");
                    }
                    else
                    {
                        // 循环原图标记 - 文件夹带循环
                        return ("FolderSync", "#4ECDC4");
                    }
                }
                else
                {
                    // 无原图标记
                    return isManualSort ? ("FolderCog", "#FDB44B") : ("Folder", "#FDB44B");
                }
            }
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"获取文件夹图标失败: {ex.Message}");
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
            catch (Exception)
            {
                //System.Diagnostics.Debug.WriteLine($"获取图片图标失败: {ex.Message}");
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



