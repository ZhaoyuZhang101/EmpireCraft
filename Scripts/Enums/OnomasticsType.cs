using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.Enums;
public enum OnomasticsType
{
    sex_male, //男性标签，置入Group后面命名时对男性生效
    sex_female, //女性标签，同上
    coin_flip, //置入Group后方使其仅有百分之五十生效，可叠加，如果在group后面放置两次，那么这个词库就只会有百分之25的概率显示
    consonant_separator, //横杠符号
    vowel_separator,
    vowel_duplicator,
    space, //空格
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
