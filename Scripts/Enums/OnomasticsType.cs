using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.Enums;
public enum OnomasticsType
{
    [Description("M")]
    sex_male, //男性标签，置入Group后面命名时对男性生效
    [Description("F")]
    sex_female, //女性标签，同上
    [Description("f")]
    coin_flip, //置入Group后方使其仅有百分之五十生效，可叠加，如果在group后面放置两次，那么这个词库就只会有百分之25的概率显示
    [Description("c")]
    consonant_separator, //横杠符号
    [Description("V")]
    vowel_separator,
    [Description("d")]
    vowel_duplicator,
    [Description("_")]
    space, //空格
    [Description(",")]
    silent_space,
    [Description("/")]
    divider,
    [Description("l")]
    clone_last,
    [Description("m")]
    mirror,
    [Description("w")]
    wild_6,
    [Description("o")]
    domino,
    [Description("r")]
    repeater,
    [Description("u")]
    upper,
    [Description("b")]
    backspace,
    [Description("D")]
    consonant_duplicator,
    [Description("e")]
    vowel_replacer,
    [Description("E")]
    consonant_replacer,
    [Description("V")]
    vowel_requirer,
    [Description("C")]
    consonant_requirer,
    [Description("-")]
    hyphen,
    [Description("#")]
    numbers,
    [Description("0")]
    group_1,
    [Description("1")]
    group_2,
    [Description("2")]
    group_3,
    [Description("3")]
    group_4,
    [Description("4")]
    group_5,
    [Description("5")]
    group_6,
    [Description("6")]
    group_7,
    [Description("7")]
    group_8,
    [Description("8")]
    group_9,
    [Description("9")]
    group_10
}

public static class OnomasticsTypeExtensions
{
    public static string[] ToStringList(OnomasticsType[] typeList)
    {
        return typeList.Select(x => x.ToString()).ToArray();
    }

    public static string GetDescription(this Enum value)
    {
        var field = value.GetType().GetField(value.ToString());
        var attribute = (DescriptionAttribute)Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute));
        return attribute == null ? value.ToString() : attribute.Description;
    }
}