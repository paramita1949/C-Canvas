using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models.Enums;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 相似图切换与智能预缓存
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// 切换相似图片
        /// </summary>
        private bool SwitchSimilarImage(bool isNext)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var switchStart = sw.ElapsedMilliseconds;
            var result = _originalManager.SwitchSimilarImage(isNext, _currentImageId);
            var switchTime = sw.ElapsedMilliseconds - switchStart;
            _ = switchTime;

            if (result.success && result.newImageId.HasValue)
            {
                int fromImageId = _currentImageId;
                int toImageId = result.newImageId.Value;

                _currentImageId = toImageId;

                var loadStart = sw.ElapsedMilliseconds;
                LoadImage(result.newImagePath);
                var loadTotalTime = sw.ElapsedMilliseconds - loadStart;
                _ = loadTotalTime;

                _ = TriggerSmartPreload();
                _ = OnSimilarImageSwitched(fromImageId, toImageId, result.isLoopCompleted);

                sw.Stop();
                string direction = isNext ? "下一张" : "上一张";
                ShowStatus($"已切换到{direction}相似图片: {Path.GetFileName(result.newImagePath)}");
                return true;
            }

            sw.Stop();
            return false;
        }

        /// <summary>
        /// 智能预缓存：根据当前模式自动触发精准预缓存
        /// </summary>
        private async Task TriggerSmartPreload()
        {
            try
            {
                if (_preloadCacheManager == null || _currentImageId <= 0)
                    return;

                var currentFile = DatabaseManagerService.GetMediaFileById(_currentImageId);
                if (currentFile == null)
                    return;

                if (_originalMode)
                {
                    var markType = _originalManager.GetOriginalMarkType(ItemType.Image, _currentImageId);

                    if (markType == null && currentFile.FolderId.HasValue)
                    {
                        markType = _originalManager.GetOriginalMarkType(ItemType.Folder, currentFile.FolderId.Value);
                    }

                    if (markType == MarkType.Loop)
                    {
                        if (!_originalManager.HasSimilarImages())
                        {
                            _originalManager.FindSimilarImages(_currentImageId);
                        }

                        var similarImages = GetSimilarImagesFromOriginalManager();
                        await _preloadCacheManager.PreloadForLoopModeAsync(_currentImageId, similarImages);
                    }
                    else if (markType == MarkType.Sequence)
                    {
                        if (currentFile.FolderId.HasValue)
                        {
                            await _preloadCacheManager.PreloadForSequenceModeAsync(_currentImageId, currentFile.FolderId.Value);
                        }
                    }
                }
                else
                {
                    await _preloadCacheManager.PreloadForKeyframeModeAsync(_currentImageId);
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 从OriginalManager获取相似图片列表
        /// </summary>
        private List<(int id, string name, string path)> GetSimilarImagesFromOriginalManager()
        {
            try
            {
                return _originalManager.GetSimilarImages();
            }
            catch (Exception)
            {
                return new List<(int id, string name, string path)>();
            }
        }

        /// <summary>
        /// 切换到下一张相似图片 (公共方法,供投影窗口调用)
        /// </summary>
        public void SwitchToNextSimilarImage()
        {
            if (_originalMode && _currentImageId > 0)
            {
                if (!_originalManager.HasSimilarImages())
                {
                    _originalManager.FindSimilarImages(_currentImageId);
                }
            }

            SwitchSimilarImage(true);
        }

        /// <summary>
        /// 切换到上一张相似图片 (公共方法,供投影窗口调用)
        /// </summary>
        public void SwitchToPreviousSimilarImage()
        {
            if (_originalMode && _currentImageId > 0)
            {
                if (!_originalManager.HasSimilarImages())
                {
                    _originalManager.FindSimilarImages(_currentImageId);
                }
            }

            SwitchSimilarImage(false);
        }
    }
}


