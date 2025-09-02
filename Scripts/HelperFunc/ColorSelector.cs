using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.HelperFunc;

public static class ColorSelector
{
    public static string NextColor()
    {
        Color randomColor = new Color(Random.value, Random.value, Random.value);
        Color pastel = Color.Lerp(randomColor, Color.white, 0.5f);
        return pastel.ToHexString();
    }
    /// <summary>
    /// 将 Color 转为 "#RRGGBBAA" 格式的十六进制字符串
    /// </summary>
    public static string ToHexString(this Color color, bool includeAlpha = true)
    {
        // ColorUtility 会返回不带 “#” 的纯十六进制码
        string hex = includeAlpha
            ? ColorUtility.ToHtmlStringRGBA(color)  // 包含透明度
            : ColorUtility.ToHtmlStringRGB(color);  // 不包含透明度

        return "#" + hex;
    }
}