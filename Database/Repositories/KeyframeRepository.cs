using System.Collections.Generic;
using System.Linq;
using ImageColorChanger.Database.Models;

namespace ImageColorChanger.Database.Repositories
{
    public sealed class KeyframeRepository : IKeyframeRepository
    {
        private readonly CanvasDbContext _context;

        public KeyframeRepository(CanvasDbContext context)
        {
            _context = context;
        }

        public Keyframe AddKeyframe(int imageId, double position, int yPosition)
        {
            var maxOrder = _context.Keyframes
                .Where(k => k.ImageId == imageId)
                .Max(k => (int?)k.OrderIndex) ?? -1;

            var keyframe = new Keyframe
            {
                ImageId = imageId,
                Position = position,
                YPosition = yPosition,
                OrderIndex = maxOrder + 1
            };

            _context.Keyframes.Add(keyframe);
            _context.SaveChanges();
            return keyframe;
        }

        public List<Keyframe> GetKeyframes(int imageId)
        {
            return _context.Keyframes
                .Where(k => k.ImageId == imageId)
                .OrderBy(k => k.OrderIndex)
                .ToList();
        }

        public void DeleteKeyframe(int keyframeId)
        {
            var keyframe = _context.Keyframes.Find(keyframeId);
            if (keyframe != null)
            {
                _context.Keyframes.Remove(keyframe);
                _context.SaveChanges();
            }
        }

        public void ClearKeyframes(int imageId)
        {
            var keyframes = _context.Keyframes.Where(k => k.ImageId == imageId);
            _context.Keyframes.RemoveRange(keyframes);
            _context.SaveChanges();
        }
    }
}
