using System.Collections.Generic;
using ImageColorChanger.Database.Models;

namespace ImageColorChanger.Database.Repositories
{
    public interface IKeyframeRepository
    {
        Keyframe AddKeyframe(int imageId, double position, int yPosition);
        List<Keyframe> GetKeyframes(int imageId);
        void DeleteKeyframe(int keyframeId);
        void ClearKeyframes(int imageId);
    }
}
