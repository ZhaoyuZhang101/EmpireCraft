using System.Collections.Generic;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 两人的亲属称谓(世系图、即位记事用；用户 2026-10-16："就不能老老实实的把世系表标全吗")：
// 沿父母两边往上找最近的共同祖先，按两边各隔几代、经不经过母亲一环(表亲、舅、甥)、长幼定称谓——
// 子孙、兄弟、侄、叔伯、舅、堂(从)兄弟、表兄弟、堂侄、堂叔、叔祖、再从兄弟……再远的写族兄弟、族叔、族侄；
// 六代以内找不到共同祖先的写宗室。以前只看父系三种关系，其余一律写"旁支入继"
public static class LineageKinship
{
    private const int MaxGenerations = 6;

    // person 是 elder 的什么人(elder 一般是先君，person 是新君)
    public static string Term(PersonalClanIdentity elder, PersonalClanIdentity person)
    {
        if (elder == null || person == null) return "";
        Dictionary<long, (int up, bool maternal)> fromElder = Ups(elder), fromPerson = Ups(person);
        int bestA = -1, bestB = -1;
        bool maternalA = false, maternalB = false;
        foreach (KeyValuePair<long, (int up, bool maternal)> pair in fromElder)
        {
            if (!fromPerson.TryGetValue(pair.Key, out var other)) continue;
            int score = pair.Value.up + other.up;
            int bestScore = bestA < 0 ? int.MaxValue : bestA + bestB;
            bool cleaner = !pair.Value.maternal && !other.maternal && (maternalA || maternalB);
            if (score > bestScore || score == bestScore && !cleaner) continue;
            bestA = pair.Value.up;
            bestB = other.up;
            maternalA = pair.Value.maternal;
            maternalB = other.maternal;
        }
        if (bestA < 0) return Text("lineage_rel_clan");
        bool female = person.sex == ActorSex.Female, older = Birth(person) < Birth(elder);
        bool maternal = maternalA || maternalB;
        int a = bestA, b = bestB;
        // 直系
        if (a == 0)
            return b switch
            {
                1 => Text(female ? "lineage_rel_daughter" : "lineage_rel_son"),
                2 => Text(maternalB ? "lineage_rel_maternal_grandson" : "lineage_rel_grandson"),
                3 => Text("lineage_rel_great_grandson"),
                _ => Text("lineage_rel_descendant")
            };
        if (b == 0) return Text(a == 1 ? female ? "lineage_rel_mother" : "lineage_rel_father" : "lineage_rel_grandfather");
        // 旁系
        switch (a, b)
        {
            case (1, 1):
                return Text(female ? older ? "lineage_rel_elder_sister" : "lineage_rel_sister"
                    : older ? "lineage_rel_elder_brother" : "lineage_rel_brother");
            case (1, 2):
                return Text(maternalB ? female ? "lineage_rel_sister_daughter" : "lineage_rel_sister_son"
                    : female ? "lineage_rel_niece" : "lineage_rel_nephew");
            case (1, 3):
                return Text("lineage_rel_grandnephew");
            case (2, 1):
                if (maternalA) return Text(female ? "lineage_rel_maternal_aunt" : "lineage_rel_maternal_uncle");
                return Text(female ? "lineage_rel_aunt" : ElderThanParent(elder, person) ? "lineage_rel_uncle_elder" : "lineage_rel_uncle");
            case (2, 2):
                return Text(maternal ? older ? "lineage_rel_cousin_maternal_elder" : "lineage_rel_cousin_maternal"
                    : older ? "lineage_rel_cousin_elder" : "lineage_rel_cousin");
            case (2, 3):
                return Text(maternal ? "lineage_rel_cousin_nephew_maternal" : "lineage_rel_cousin_nephew");
            case (3, 2):
                return Text(maternal ? "lineage_rel_cousin_uncle_maternal" : "lineage_rel_cousin_uncle");
            case (3, 1):
                return Text("lineage_rel_granduncle");
            case (3, 3):
                return Text(older ? "lineage_rel_second_cousin_elder" : "lineage_rel_second_cousin");
            case (3, 4):
                return Text("lineage_rel_second_nephew");
            case (4, 3):
                return Text("lineage_rel_second_uncle");
        }
        int generations = a - b;
        return Text(generations switch
        {
            0 => older ? "lineage_rel_clan_elder" : "lineage_rel_clan_younger",
            1 => "lineage_rel_clan_uncle",
            >= 2 => "lineage_rel_clan_grandfather",
            -1 => "lineage_rel_clan_nephew",
            _ => "lineage_rel_clan_grandson"
        });
    }

    private static string Text(string key) => LM.Get(key) ?? key;

    // 往上各代祖先 → 隔几代、这条路上有没有经过母亲一环(同一代优先记不经过母亲的)
    private static Dictionary<long, (int up, bool maternal)> Ups(PersonalClanIdentity person)
    {
        var result = new Dictionary<long, (int up, bool maternal)>();
        var queue = new Queue<(PersonalClanIdentity person, int up, bool maternal)>();
        queue.Enqueue((person, 0, false));
        while (queue.Count > 0)
        {
            (PersonalClanIdentity current, int up, bool maternal) = queue.Dequeue();
            if (current == null) continue;
            if (result.TryGetValue(current.id, out var known) && (known.up < up || known.up == up && (!known.maternal || maternal))) continue;
            result[current.id] = (up, maternal);
            if (up >= MaxGenerations) continue;
            if (current.father > 0) queue.Enqueue((SpecificClanManager.getPerson(current.father), up + 1, maternal));
            if (current.mother > 0) queue.Enqueue((SpecificClanManager.getPerson(current.mother), up + 1, true));
        }
        return result;
    }

    // 叔伯：比先君的父亲年长是伯，否则是叔
    private static bool ElderThanParent(PersonalClanIdentity elder, PersonalClanIdentity uncle)
    {
        PersonalClanIdentity father = elder.father > 0 ? SpecificClanManager.getPerson(elder.father) : null;
        return father != null && Birth(uncle) < Birth(father);
    }

    // 出生先后(生日"11,062年5月20日")；解析不了按编号
    private static long Birth(PersonalClanIdentity person)
    {
        var match = global::System.Text.RegularExpressions.Regex.Match(person?.birthday ?? "", @"([\d,]+)年(\d+)月(\d+)日");
        if (!match.Success || !long.TryParse(match.Groups[1].Value.Replace(",", ""), out long year))
            return long.MaxValue / 2 + (person?.id ?? 0) % 1000000;
        return year * 10000 + int.Parse(match.Groups[2].Value) * 100 + int.Parse(match.Groups[3].Value);
    }
}
