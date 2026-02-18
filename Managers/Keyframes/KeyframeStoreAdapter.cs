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
            return keyframe.Id;
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
            int orderIndex = keyframe.OrderIndex ?? 0;

            await _keyframeRepository.DeleteAsync(keyframe);
            await _keyframeRepository.SaveChangesAsync();

            var subsequent = (await _keyframeRepository.GetKeyframesByImageIdAsync(imageId))
                .Where(k => (k.OrderIndex ?? 0) > orderIndex)
                .ToList();

            foreach (var item in subsequent)
            {
                item.OrderIndex = (item.OrderIndex ?? 0) - 1;
                await _keyframeRepository.UpdateAsync(item);
            }

            await _keyframeRepository.SaveChangesAsync();
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
    }
}
