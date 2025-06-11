using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.Enums;
public enum OnomasticsType
{
    sex_male,
    sex_female,
    coin_flip,
    consonant_separator,
    vowel_separator,
    vowel_duplicator,
    space,
    silent_space,
    divider,
    group_1,
    group_2,
    group_3,
    group_4,
    group_5,
    group_6,
    group_7,
    group_8,
    group_9,
    group_10
}

public static class OnomasticsTypeExtensions
{
    public static string[] ToStringList(OnomasticsType[] typeList)
    {
        return typeList.Select(x => x.ToString()).ToArray();
    }
}
