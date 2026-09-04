using System;

namespace EmpireCraft.Scripts.UI.Components
{
    public static class TerritoryLabelProjection
    {
        public const int ReferenceFontSize = 128;
        public const float OutlineDistance = 1f;

        public static float FitScale(float width, float height, float textWidth, float textHeight,
            float sizeMultiplier, int maxFontSize)
        {
            if (width <= 0 || height <= 0 || textWidth <= 0 || textHeight <= 0 || maxFontSize <= 0) return 0;
            float desired = Math.Min(height * Math.Max(0.2f, Math.Min(1f, sizeMultiplier)), maxFontSize) / ReferenceFontSize;
            // Include the outline in the fit, so smoothing never enlarges text beyond its territory.
            float scale = Math.Min(desired, Math.Min(width / (textWidth + 2 * OutlineDistance),
                height / (textHeight + 2 * OutlineDistance))) * 0.98f;
            return float.IsNaN(scale) || float.IsInfinity(scale) ? 0 : Math.Max(0, scale);
        }

        public static float Visibility(float scale, int minFontSize)
        {
            float t = Math.Max(0f, Math.Min(1f, (scale * ReferenceFontSize - minFontSize) / 2f));
            return t * t * (3f - 2f * t);
        }
    }
}
