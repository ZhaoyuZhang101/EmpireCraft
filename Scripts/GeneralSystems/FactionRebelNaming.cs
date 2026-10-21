using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 派系起兵的称呼(用户 2026-10-22 见几个响应的行省同叫"世族"分不清，问"有没有更符合历史的称呼"，
// 选"统兵者官爵""按首领与家数")：
//   · 叛军：统兵者官爵——本国官职的称呼(见 OfficeObject.GetName)：宗室封王称王号(燕王)、节度使称镇(淮西节度使)、
//     行省称官衔(山东巡抚)、州郡称官(某州刺史)；起兵那一刻取，停战改回原名；没有官职的称"地名+军"；
//   · 战争：一家起兵，宗室王称"某王之乱"，余称首领姓名(王敦之乱、侯景之乱式)；两家合称，王取封号首字(燕赵)、余取两人之姓(安史)；
//     三家以上按数称(七国、八王、三藩式)，字按多数人的官称：王、镇、省、州，余称国。
public static class FactionRebelNaming
{
    private enum Kind { Prince, Garrison, Province, Prefecture, Realm }

    private static readonly string[] Digits = { "", "一", "二", "三", "四", "五", "六", "七", "八", "九" };

    // 叛军的称呼(起兵前调：要按本国原来的名号与官职算)
    public static string RebelName(Kingdom kingdom)
    {
        string title = Title(kingdom);
        if (!string.IsNullOrWhiteSpace(title)) return title;
        string front = kingdom.GetKingdomName();
        if (string.IsNullOrWhiteSpace(front)) front = kingdom.capital?.GetCityName() ?? kingdom.data?.name ?? "";
        return string.Format(LM.Get("faction_rebel_army_name"), front);
    }

    // 这场派系起兵的战争名
    public static string WarName(IReadOnlyList<Kingdom> rebels)
    {
        List<Kingdom> list = rebels?.Where(kingdom => kingdom?.data != null).Distinct().ToList() ?? new List<Kingdom>();
        if (list.Count == 0) return null;
        if (list.Count == 1)
        {
            Kingdom only = list[0];
            string title = Title(only);
            string who = KindOf(title) == Kind.Prince ? title : only.king?.getName();
            if (string.IsNullOrWhiteSpace(who)) who = !string.IsNullOrWhiteSpace(title) ? title : only.GetKingdomName();
            return string.Format(LM.Get("faction_rebellion_war_name"), who);
        }
        if (list.Count == 2)
        {
            string a = Short(list[0]), b = Short(list[1]);
            if (!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && a != b)
                return string.Format(LM.Get("faction_war_pair"), a, b);
        }
        Kind kind = list.Select(kingdom => KindOf(Title(kingdom))).GroupBy(k => k).OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key).First().Key;
        return string.Format(LM.Get("faction_war_many"), Number(list.Count), LM.Get("faction_war_unit_" + kind.ToString().ToLowerInvariant()));
    }

    // 本国官职的称呼(地名 + 官称)
    private static string Title(Kingdom kingdom)
    {
        try
        {
            return kingdom?.data == null ? null : kingdom.GetOffice()?.GetName(kingdom);
        }
        catch
        {
            return null;
        }
    }

    // 两家合称时的一字：王取封号首字，余取首领之姓
    private static string Short(Kingdom kingdom)
    {
        string title = Title(kingdom);
        if (KindOf(title) == Kind.Prince && title.Length > 1) return title.Substring(0, 1);
        Actor king = kingdom.king;
        string surname = king == null ? null : SurnameInheritanceSystem.SurnameOf(king.GetPersonalIdentity());
        if (string.IsNullOrWhiteSpace(surname)) surname = king?.getName()?.Substring(0, 1);
        return surname;
    }

    private static Kind KindOf(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return Kind.Realm;
        if (title.EndsWith("王")) return Kind.Prince;
        if (title.Contains("节度") || title.EndsWith("军")) return Kind.Garrison;
        if (title.Contains("巡抚") || title.Contains("总督") || title.Contains("布政") || title.Contains("行省")) return Kind.Province;
        if (title.Contains("刺史") || title.Contains("太守") || title.Contains("知州") || title.Contains("知府") || title.Contains("州牧"))
            return Kind.Prefecture;
        return Kind.Realm;
    }

    // 家数：中文界面用汉字(七国、八王)，其余用阿拉伯数字
    private static string Number(int count)
    {
        string language = PlayerConfig.dict != null && PlayerConfig.dict.TryGetValue("language", out PlayerOptionData option)
            ? option.stringVal : "";
        if (language != "cz" && language != "ch") return count.ToString();
        if (count < 10) return Digits[count];
        if (count < 20) return "十" + Digits[count % 10];
        if (count < 100) return Digits[count / 10] + "十" + Digits[count % 10];
        return count.ToString();
    }
}
