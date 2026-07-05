using System;
using System.Collections.Generic;

namespace ImageColorChanger.UI.Modules
{
    internal static class TextBoxZIndexPolicy
    {
        public static int GetNextFrontZIndex(IEnumerable<(int DataZIndex, int PanelZIndex)> siblingLayers)
        {
            int maxZ = 0;

            if (siblingLayers == null)
            {
                return maxZ + 1;
            }

            foreach (var layer in siblingLayers)
            {
                maxZ = Math.Max(maxZ, Math.Max(layer.DataZIndex, layer.PanelZIndex));
            }

            return maxZ + 1;
        }
    }
}
