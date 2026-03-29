using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Repositories.Interfaces;

namespace ImageColorChanger.Managers.Keyframes
{
    /// <summary>
    /// 基于统一仓储实现的关键帧存储适配器。
    /// </summary>
    public sealed class KeyframeStoreAdapter : IKeyframeStore
    {
        private readonly IKeyframeRepository _keyframeRepository;

        public KeyframeStoreAdapter(IKeyframeRepository keyframeRepository)
        {
            _keyframeRepository = keyframeRepository ?? throw new ArgumentNullException(nameof(keyframeRepository));
        }

        public async Task<int> AddKeyframeAsync(int imageId, double position, int yPosition)
        {
            try
            {
                var keyframes = await _keyframeRepository.GetKeyframesByImageIdAsync(imageId);
                if (keyframes.Any(k => Math.Abs(k.YPosition - yPosition) < 50))
                {
                    return -1;
                }

                int maxOrder = keyframes.Max(k => (int?)k.OrderIndex) ?? -1;
                var keyframe = new Keyframe
                {
                    ImageId = imageId,
                    Position = position,
                    YPosition = yPosition,
                    OrderIndex = maxOrder + 1
                };

                await _keyframeRepository.AddAsync(keyframe);
                await _keyframeRepository.SaveChangesAsync();
                await NormalizeOrderIndexesByPositionAsync(imageId);
                return keyframe.Id;
            }
            catch (Exception ex)
            {
                _ = ex;
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"[关键帧][Store.Add][ERROR] imageId={imageId}, position={position:F4}, y={yPosition}");
                System.Diagnostics.Debug.WriteLine($"[关键帧][Store.Add][ERROR] {ex}");
                if (ex.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[关键帧][Store.Add][INNER] {ex.InnerException}");
                }
                if (ex.Message?.Contains("no such column", StringComparison.OrdinalIgnoreCase) == true)
                {
                    System.Diagnostics.Debug.WriteLine("[关键帧][Store.Add][HINT] 检测到缺失列异常，请检查 keyframes 表是否包含 auto_pause 列。");
                }
                #endif
                throw;
            }
        }

        public List<Keyframe> GetKeyframesByImageId(int imageId)
        {
            return _keyframeRepository.GetKeyframesByImageId(imageId);
        }

        public async Task<List<Keyframe>> GetKeyframesAsync(int imageId)
        {
            return await _keyframeRepository.GetKeyframesByImageIdAsync(imageId);
        }

        public async Task<bool> DeleteKeyframeAsync(int keyframeId)
        {
            var keyframe = await _keyframeRepository.GetByIdAsync(keyframeId);
            if (keyframe == null)
            {
                return false;
            }

            int imageId = keyframe.ImageId;
            await _keyframeRepository.DeleteAsync(keyframe);
            await _keyframeRepository.SaveChangesAsync();
            await NormalizeOrderIndexesByPositionAsync(imageId);
            return true;
        }

        public async Task<bool> ClearKeyframesAsync(int imageId)
        {
            var keyframes = await _keyframeRepository.GetKeyframesByImageIdAsync(imageId);
            if (keyframes.Count == 0)
            {
                return false;
            }

            await _keyframeRepository.DeleteRangeAsync(keyframes);
            await _keyframeRepository.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdateLoopCountAsync(int keyframeId, int? loopCount)
        {
            var keyframe = await _keyframeRepository.GetByIdAsync(keyframeId);
            if (keyframe == null)
            {
                return false;
            }

            keyframe.LoopCount = loopCount;
            await _keyframeRepository.UpdateAsync(keyframe);
            await _keyframeRepository.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdateAutoPauseAsync(int keyframeId, bool autoPause)
        {
            var keyframe = await _keyframeRepository.GetByIdAsync(keyframeId);
            if (keyframe == null)
            {
                return false;
            }

            keyframe.AutoPause = autoPause;
            await _keyframeRepository.UpdateAsync(keyframe);
            await _keyframeRepository.SaveChangesAsync();
            return true;
        }

        private async Task NormalizeOrderIndexesByPositionAsync(int imageId)
        {
            var ordered = (await _keyframeRepository.GetKeyframesByImageIdAsync(imageId))
                .OrderBy(k => k.YPosition)
                .ThenBy(k => k.Id)
                .ToList();

            bool hasChange = false;
            for (int i = 0; i < ordered.Count; i++)
            {
                if ((ordered[i].OrderIndex ?? -1) != i)
                {
                    ordered[i].OrderIndex = i;
                    await _keyframeRepository.UpdateAsync(ordered[i]);
                    hasChange = true;
                }
            }

            if (hasChange)
            {
                await _keyframeRepository.SaveChangesAsync();
            }
        }
    }
}
