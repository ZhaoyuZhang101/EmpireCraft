using System;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GameClassExtensions;

public static class StringExtensions
{
    /// <summary>
    /// 将颜色赋值于文本
    /// </summary>
    public static string ColorString(this string content, string color= "", Color pColor = default)
    {
        if (String.IsNullOrEmpty(color))
        {
            return $"<color={pColor.ToHexString()}>{content}</color>";
        }
        return $"<color={color}>{content}</color>";
    }
    /// <summary>
    /// 格式化本地化文件中的语句
    /// </summary>
    public static string LocalFormat(this string content, params object[] additions)
    {
        return string.Format(LM.Get(content), additions);
    }
}