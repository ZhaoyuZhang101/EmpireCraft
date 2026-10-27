using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;

namespace EmpireCraft.Scripts.GeneralSystems;

// 西方帝国(古典共和、西方封建，以及罗马一系文化的帝国)的王号序数：同一帝国里，同氏族、同名的皇帝按即位先后称一世、二世……
// (用户 2026-10-27 "西方罗马一系的文化皇帝如果同一家族当上皇帝，要变成 xx x世"：以前只管古典共和、西方封建两种政体，
// 罗马一系行别的政体的帝国没有序数)；中文界面显示"二世"，别的语言罗马数字(存的照旧是罗马数字，见 Display)。
// 第二位出现时，第一位若仍在世也补上一世；已写进历史记录的名字不追改。
public static class RegnalNameService
{
    // Empire.NewEmperor 调用。recompute=true 用于帝国延续(ReplaceEmpire)后补齐前代记录再重算。
    public static void OnEmperorCrowned(Empire empire, Actor emperor, bool recompute = false)
    {
        if (empire?.data == null || emperor == null || emperor.isRekt()) return;
        if (!Applies(empire)) return;
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

    // 古典共和、西方封建，或帝国核心王国是罗马一系的文化
    private static bool Applies(Empire empire) =>
        PersonalUnionService.IsUnionRegime(empire.CoreKingdom) ||
        CultureService.GetRealmCulture(empire.CoreKingdom).GetInstitutionLine() == "Roma";

    // 名字后面显示的序数：中文"二世"，别的语言罗马数字
    public static string Display(string stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return "";
        string language = PlayerConfig.dict.TryGetValue("language", out PlayerOptionData option) ? option.stringVal : "";
        if (language != "cz" && language != "ch") return stored;
        int number = FromRoman(stored.Trim());
        return number <= 0 ? stored : ChineseNumber(number) + "世";
    }

    private static int FromRoman(string text)
    {
        int total = 0, previous = 0;
        for (int i = text.Length - 1; i >= 0; i--)
        {
            int value = text[i] switch { 'I' => 1, 'V' => 5, 'X' => 10, 'L' => 50, 'C' => 100, 'D' => 500, 'M' => 1000, _ => -1 };
            if (value < 0) return -1;
            total += value < previous ? -value : value;
            previous = global::System.Math.Max(previous, value);
        }
        return total;
    }

    private static string ChineseNumber(int number)
    {
        const string digits = "零一二三四五六七八九";
        if (number < 10) return digits[number].ToString();
        if (number < 100)
        {
            int tens = number / 10, ones = number % 10;
            return (tens == 1 ? "" : digits[tens].ToString()) + "十" + (ones == 0 ? "" : digits[ones].ToString());
        }
        return number.ToString();
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
