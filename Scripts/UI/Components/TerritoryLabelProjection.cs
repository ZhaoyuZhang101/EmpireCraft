using System;

namespace EmpireCraft.Scripts.UI.Components
{
    public static class TerritoryLabelProjection
    {
        public const int ReferenceFontSize = 128;
        public const float OutlineDistance = 1f;

        public static float FitScale(float width, float height, float textWidth, float textHeight,
            float sizeMultiplier, int maxFontSize, float outlineDistance = OutlineDistance)
        {
            if (width <= 0 || height <= 0 || textWidth <= 0 || textHeight <= 0 || maxFontSize <= 0) return 0;
            float multiplier = Math.Max(0.2f, Math.Min(2f, sizeMultiplier));
            outlineDistance = Math.Max(0f, outlineDistance);
            float desired = Math.Min(height, maxFontSize) / ReferenceFontSize;
            // Fit the base glyph box first, then apply the style's intentional display multiplier.
            float scale = Math.Min(desired, Math.Min(width / (textWidth + 2 * outlineDistance),
                height / (textHeight + 2 * outlineDistance))) * multiplier * 0.98f;
            return float.IsNaN(scale) || float.IsInfinity(scale) ? 0 : Math.Max(0, scale);
        }

        public static float Visibility(float scale, int minFontSize)
        {
            float t = Math.Max(0f, Math.Min(1f, (scale * ReferenceFontSize - minFontSize) / 2f));
            return t * t * (3f - 2f * t);
        }

        // Large map labels should become contextual instead of obscuring the map.
        public static float ScreenCoverageVisibility(float widthCoverage, float heightCoverage)
        {
            float coverage = Math.Max(widthCoverage, heightCoverage);
            float t = Math.Max(0f, Math.Min(1f, (coverage - 0.70f) / 0.30f));
            float smooth = t * t * (3f - 2f * t);
            return 1f - 0.82f * smooth;
        }
    }
}
