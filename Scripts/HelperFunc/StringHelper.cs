using System;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.HelperFunc;

public static class StringHelper
{
    /// <summary>
    /// 将颜色赋值于文本
    /// </summary>
    public static string ColorString(this string content, string colorText= "", Color pColor = default)
    {
        if (String.IsNullOrEmpty(colorText))
        {
            return $"<color={pColor.ToHexString()}>{content}</color>";
        }
        return $"<color={colorText}>{content}</color>";
    }
    /// <summary>
    /// 格式化本地化文件中的语句
    /// </summary>
    public static string LocalFormat(this string content, params object[] additions)
    {
        return string.Format(LM.Get(content), additions);
    }
}