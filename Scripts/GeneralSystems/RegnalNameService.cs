using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;

namespace EmpireCraft.Scripts.GeneralSystems;

// 西方帝国(古典共和、西方封建)的王号序数：同一帝国里，同氏族、同名的皇帝按即位先后称 I、II、III……
// 第二位出现时，第一位若仍在世也补上 I；已写进历史记录的名字不追改。
public static class RegnalNameService
{
    // Empire.NewEmperor 调用。recompute=true 用于帝国延续(ReplaceEmpire)后补齐前代记录再重算。
    public static void OnEmperorCrowned(Empire empire, Actor emperor, bool recompute = false)
    {
        if (empire?.data == null || emperor == null || emperor.isRekt()) return;
        if (!PersonalUnionService.IsUnionRegime(empire.CoreKingdom)) return;
        string givenName = emperor.GetModName()?.firstName;
        if (string.IsNullOrWhiteSpace(givenName)) return;
        long clanId = emperor.GetSpecificClan()?.id ?? -1L;

        List<RegnalRecord> records = empire.data.regnal_records ??= new List<RegnalRecord>();
        if (recompute) records.RemoveAll(record => record.actor_id == emperor.id);
        else if (records.Any(record => record.actor_id == emperor.id)) return;

        List<RegnalRecord> namesakes = records
            .Where(record => record.clan_id == clanId && record.given_name == givenName).ToList();
        records.Add(new RegnalRecord { actor_id = emperor.id, clan_id = clanId, given_name = givenName });
        int ordinal = namesakes.Count + 1;
        if (ordinal < 2) return;

        SetRegnalSuffix(emperor, ordinal);
        if (ordinal == 2)
        {
            Actor first = World.world.units.get(namesakes[0].actor_id);
            if (first != null && !first.isRekt() && first.isAlive()) SetRegnalSuffix(first, 1);
        }
    }

    private static void SetRegnalSuffix(Actor actor, int ordinal)
    {
        actor.GetOrCreate().regnal_suffix = ToRoman(ordinal);
        actor.GetModName()?.SetName(actor);
    }

    public static string ToRoman(int number)
    {
        if (number <= 0) return "";
        (int value, string symbol)[] numerals =
        {
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"),
            (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
        };
        var result = new global::System.Text.StringBuilder();
        foreach ((int value, string symbol) in numerals)
        {
            while (number >= value)
            {
                result.Append(symbol);
                number -= value;
            }
        }
        return result.ToString();
    }
}
