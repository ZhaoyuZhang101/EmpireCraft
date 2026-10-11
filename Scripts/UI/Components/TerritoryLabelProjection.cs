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

        public static bool OrientedRectanglesOverlap(float firstX, float firstY,
            float firstHalfWidth, float firstHalfHeight, float firstRadians,
            float secondX, float secondY, float secondHalfWidth, float secondHalfHeight,
            float secondRadians, float separationPadding = 0f)
        {
            if (firstHalfWidth <= 0f || firstHalfHeight <= 0f ||
                secondHalfWidth <= 0f || secondHalfHeight <= 0f) return false;

            double firstCos = Math.Cos(firstRadians);
            double firstSin = Math.Sin(firstRadians);
            double secondCos = Math.Cos(secondRadians);
            double secondSin = Math.Sin(secondRadians);
            double deltaX = secondX - firstX;
            double deltaY = secondY - firstY;
            double padding = Math.Max(0f, separationPadding);

            return OverlapsOnAxis(firstCos, firstSin) &&
                   OverlapsOnAxis(-firstSin, firstCos) &&
                   OverlapsOnAxis(secondCos, secondSin) &&
                   OverlapsOnAxis(-secondSin, secondCos);

            bool OverlapsOnAxis(double axisX, double axisY)
            {
                double distance = Math.Abs(deltaX * axisX + deltaY * axisY);
                double firstRadius = firstHalfWidth * Math.Abs(firstCos * axisX + firstSin * axisY) +
                                     firstHalfHeight * Math.Abs(-firstSin * axisX + firstCos * axisY);
                double secondRadius = secondHalfWidth * Math.Abs(secondCos * axisX + secondSin * axisY) +
                                      secondHalfHeight * Math.Abs(-secondSin * axisX + secondCos * axisY);
                return distance < Math.Max(0d, firstRadius + secondRadius - padding);
            }
        }
    }
}
