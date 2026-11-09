using System;
using System.Collections.Generic;

namespace EmpireCraft.Scripts.GeneralSystems.WorldMap;

// 剧情人物要活到自己的戏份(见 ScenarioEventSystem.Shield)：不碰游戏对象，单独测
public static class ScenarioPlotRules
{
    // 事件迟迟等不到条件(岳飞收复了汴京，不再议和)也不能永远不死：过了预定年份这么多年就放手
    public const int GraceYears = 8;

    public static readonly char[] NameSeparators = { '、', ',', '，' };

    // 称帝的国号写成"大某|Great X|大某"的(大顺、大西)："大"是美号，本名是"某"，三种文字各去掉"大"、"Great "。
    // 中文以"大"起头、英文以"Great "起头才算(大理这类"大"是本名的一部分，英文也不写 Great)；不是的原样返回 false
    public static bool SplitGrand(string parts, out string plain)
    {
        plain = parts ?? "";
        string[] p = plain.Split('|');
        if (p.Length < 2 || p[0].Length < 2 || p[0][0] != '大' || !p[1].StartsWith("Great ", StringComparison.Ordinal)) return false;
        p[0] = p[0].Substring(1);
        p[1] = p[1].Substring("Great ".Length);
        if (p.Length > 2 && p[2].Length >= 2 && p[2][0] == '大') p[2] = p[2].Substring(1);
        plain = string.Join("|", p);
        return true;
    }

    public static IEnumerable<string> Names(string field)
    {
        if (string.IsNullOrWhiteSpace(field)) yield break;
        foreach (string part in field.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            string name = part.Trim();
            if (name.Length > 0) yield return name;
        }
    }

    // 还没发生的事里点了名的人：编号 → 护到开局后第几年；一个人有几场戏按最晚的算
    public static Dictionary<long, int> ProtectUntil(IEnumerable<(int after, bool fired, IEnumerable<string> personFields)> events,
        IReadOnlyDictionary<string, long> people)
    {
        var until = new Dictionary<long, int>();
        foreach ((int after, bool fired, IEnumerable<string> personFields) in events)
        {
            if (fired) continue;
            foreach (string field in personFields)
            foreach (string name in Names(field))
            {
                if (!people.TryGetValue(name, out long id)) continue;
                int year = after + GraceYears;
                if (!until.TryGetValue(id, out int have) || year > have) until[id] = year;
            }
        }
        return until;
    }

    public static bool Protected(IReadOnlyDictionary<long, int> until, long id, int years) =>
        until.TryGetValue(id, out int year) && years < year;

    public const string AlreadyAtWar = "diplomacy_reason_at_war";

    // 外交结果没做成：停战未满、关系不对这类过一阵可能就成了，每年再试，试到预定年份过后 GraceYears 年。
    // 以前没做成也记成触发过，一件事只有一次机会(清军入关没开成，后面入关的事全断了)。已经在打的不算失败
    public static bool Retriable(string stepKind, string failed) =>
        stepKind == "relation" && failed != null && failed != AlreadyAtWar &&
        failed.StartsWith("diplomacy_reason_", StringComparison.Ordinal) ||
        stepKind == "submit" && failed == NotDesperate;

    // 走投无路才来投(用户 2026-11-10 "难道不应该是大家走投无路才会主动加入南明吗""大清连整个山东都没拿下呢"
    // "至少也得达到中国南方或者把其他起义军逼到没退路才会触发"；以前李自成一死，占着整个北方、清还没打过来的顺就整个并进了明)：
    // 正和入侵者交战、入侵者兵力是自己的 DesperateOdds 倍以上，并且入侵者已占同族之城的 SouthReachedPressure(兵锋到了南方)，
    // 或自己的城丢了一半以上(被逼得没退路)。号召归附、南明来投、剧本的奉表归附都看它；剧本的没到这步每年再试
    public const float DesperateOdds = 2f, SouthReachedPressure = 0.5f;
    public const string NotDesperate = "还没走投无路";

    public static bool Desperate(bool hostile, double invaderPower, double ownPower, float pressure, int cities, int peakCities) =>
        hostile && invaderPower >= ownPower * DesperateOdds &&
        (pressure >= SouthReachedPressure || peakCities > 0 && cities * 2 <= peakCities);

    // 继承名单：没写城的立为储君，排最前；写了城的是候补，排最后；同一人只留一处
    public static void AddHeir(List<long> heirs, long id, bool reserve)
    {
        heirs.Remove(id);
        if (reserve) heirs.Add(id);
        else heirs.Insert(0, id);
    }

    // 名单里头一位还能继位的；排在他前面不行的(死了、被俘、已经即位)划掉。没有人返回 -1
    public static long NextHeir(List<long> heirs, Func<long, bool> valid)
    {
        while (heirs.Count > 0)
        {
            if (valid(heirs[0])) return heirs[0];
            heirs.RemoveAt(0);
        }
        return -1L;
    }

    public const float RallyMinPressure = 0.05f, RallyBase = 0.6f, RallyNearby = 1.5f, RallyAtWar = 1.5f, RallyMaxChance = 0.5f;

    // 归附的机会(一家一年)：入侵者占的同族之城越多(pressure，0~1)越容易；挨着入侵者、正和入侵者打仗的各再加一半。没什么压力时没人来投
    public static float RallyChance(float pressure, bool bordersConqueror, bool atWarWithConqueror)
    {
        if (pressure < RallyMinPressure) return 0f;
        float chance = pressure * RallyBase;
        if (bordersConqueror) chance *= RallyNearby;
        if (atWarWithConqueror) chance *= RallyAtWar;
        return Math.Min(chance, RallyMaxChance);
    }

    public enum RebelFate { Stay, Wait, Return, Execute }

    // 起兵的领头人落到朝廷手里：城破时城里的人连君主都归破城的一方(原版)，以前张献忠就这样成了明臣，当上辅政大臣又篡了位。
    // 本国还在就逃回去，本国暂时没有都城先等着，本国亡了就伏诛；人不在朝廷这边(投了别家、还在本国)不管
    public static RebelFate FateOfRebelLeader(bool inRebelRealm, bool withOrigin, bool rebelAlive, bool rebelHasCapital)
    {
        if (inRebelRealm || !withOrigin) return RebelFate.Stay;
        if (!rebelAlive) return RebelFate.Execute;
        return rebelHasCapital ? RebelFate.Return : RebelFate.Wait;
    }
}
