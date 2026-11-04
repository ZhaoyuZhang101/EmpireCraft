using System;

namespace EmpireCraft.Scripts.UI.Components
{
    public static class TerritoryLabelProjection
    {
        public const int ReferenceFontSize = 128;
        // 描边粗细(参考字号的单位，128 号字上 3 个单位)；屏幕上至少 MinOutlinePixels(见 TerritoryLabelRenderer)。
        // 用户 2026-10-27"文字黑边能不能加粗"：以前 1 个单位，小字号铭牌上不到一像素
        public const float OutlineDistance = 3f;
        public const float MinOutlinePixels = 1.2f;

        // 铭牌字体逐档的拼字像素：字形在图集里实际占的大小，和 Text 的字号、画布缩放无关(见 RasterFontSize)
        public static readonly int[] RasterSteps = { 384, 256, 192, 160, 128, 96, 64 };
        public const int AtlasSize = 4096;
        // 每字按拼字像素见方、四周各留 1 像素，只算六成：留给小字行、粗体与装箱空隙。
        // 按真实字形装箱估过：方块字还剩三成多余量；本机隶书字形扁，实际装得下这里算的两倍(256 像素约 300 字、128 像素约 1240 字)
        private const float AtlasUse = 0.6f;

        public static int AtlasCapacity(int rasterPixels)
        {
            float perSide = (float)AtlasSize / (rasterPixels + 2);
            return (int)(AtlasUse * perSide * perSide);
        }

        // 同一帧要拼 glyphs 个字该用第几档：只往下降，到最低档为止
        public static int RasterStepFor(int glyphs, int currentStep)
        {
            int step = currentStep;
            while (step < RasterSteps.Length - 1 && glyphs > AtlasCapacity(RasterSteps[step])) step++;
            return step;
        }

        // Text 给动态字体拼字的实际像素 = 字号 × 画布缩放(Text.pixelsPerUnit)：要拼 rasterPixels 像素，字号就除回来
        public static int RasterFontSize(int rasterPixels, float pixelsPerUnit) =>
            Math.Max(1, (int)Math.Round(rasterPixels / Math.Max(0.01f, pixelsPerUnit)));

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
