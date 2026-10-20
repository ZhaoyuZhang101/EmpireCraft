using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 帝国改革(原型 1495 年沃尔姆斯帝国议会)：西方制度树「帝国改革」节点施行后，各帝国还要在帝国议会提案颁行
//   (帝国权威 60 以上才付议、四年一议，通过扣 30，见 Diet 的 AgendaReform；旧档节点已施行的读档后直接算已颁行)：
//   · 永久和平：此后诸侯不得私战(颁行之前诸侯可为法理打小仗，见 Feuds)，纠纷一律交议会与法院；
//   · 帝国法院：诸侯为法理城起诉，审理、宣判、抗判见 Feuds；
//   · 帝国大区：成员国按都城所在分成几个大区(每四国一区，至多十区)，大区诸侯应征、缴摊派更可靠(抗命几率减半)，
//     帝国禁令由被禁者所在大区最强的诸侯执行；
//   · 议会投票整体 +0.1(诸侯认这套规矩)，正统 +3。
// 分割继承(原型神圣罗马帝国的小邦林立)：封建制帝国里世袭诸侯死后，有几个儿子就有三成五可能把领国分给诸子
//   (长子承袭封国，其余城市按重要程度分给诸子，见 GraceEdictService)；选帝侯的领国不可分割(原型金玺诏书)。
public static partial class HolyRomanSystem
{
    public const string NodeImperialReform = "western_imperial_reform";
    private const float PartibleChance = 0.35f;
    private const int MembersPerCircle = 4, MaxCircles = 10, CircleRefreshYears = 10;
    public const int ReformLegitimacy = 3;

    // 本帝国已颁行(帝国级)；旧档还没起算权威时按文化节点算(第一次年结即迁移成颁行，见 EnsureAuthority)
    public static bool ReformEnacted(Empire empire)
    {
        HolyRomanState state = State(empire);
        if (state == null) return false;
        return state.reform_at >= 0d || state.authority == LegacyAuthority && ReformNodeEnacted(empire);
    }

    // 文化制度节点(颁行的前提)
    public static bool ReformNodeEnacted(Empire empire) =>
        empire?.data != null && InstitutionSystem.IsEnacted(empire, NodeImperialReform);

    // AI 选改革的加分(见 InstitutionSystem.TryStartAiReform)：有帝国议会的封建制帝国偏向帝国改革、避开与之互斥的
    // 中央集权君主制，选帝制更甚。两者同在第五档，中央集权所需的官僚、常备军技术先到，以前多半先推了它，帝国改革就永远推不了
    public const string NodeCentralizedMonarchy = "western_centralized_monarchy";

    public static float ReformBias(Empire empire, InstitutionNodeConfig node)
    {
        if (node == null || node.id != NodeImperialReform && node.id != NodeCentralizedMonarchy || !HasDiet(empire)) return 0f;
        float lean = IsElective(empire) ? 30f : 12f;
        return node.id == NodeImperialReform ? lean : -lean;
    }

    public static int Circle(Empire empire, Kingdom kingdom)
    {
        HolyRomanState state = State(empire);
        return state?.circles != null && kingdom != null && state.circles.TryGetValue(kingdom.id, out int circle) ? circle : -1;
    }

    // 大区诸侯：施行帝国改革后分了大区的成员国
    private static bool InCircle(Empire empire, Kingdom member) => ReformEnacted(empire) && Circle(empire, member) >= 0;

    public static int CircleCount(Empire empire) => State(empire)?.circles?.Values.Distinct().Count() ?? 0;

    private static void ReformYearly(Empire empire, HolyRomanState state)
    {
        if (!ReformEnacted(empire))
        {
            state.circles?.Clear();
            state.circles_at = -1d;
            return;
        }
        List<Kingdom> members = Members(empire).Where(member => member.capital != null).ToList();
        state.circles ??= new Dictionary<long, int>();
        bool changed = members.Count != state.circles.Count || members.Any(member => !state.circles.ContainsKey(member.id));
        if (changed || state.circles_at < 0d || Date.getYearsSince(state.circles_at) >= CircleRefreshYears)
            AssignCircles(state, members);
    }

    // 按都城位置分区(简单 k 均值，种子按国号顺序取，结果稳定)
    private static void AssignCircles(HolyRomanState state, List<Kingdom> members)
    {
        state.circles.Clear();
        state.circles_at = World.world.getCurWorldTime();
        if (members.Count == 0) return;
        int k = Mathf.Clamp(Mathf.RoundToInt(members.Count / (float)MembersPerCircle), 1, MaxCircles);
        List<Kingdom> ordered = members.OrderBy(member => member.id).ToList();
        var centers = new List<Vector2>();
        for (int i = 0; i < k; i++) centers.Add(ordered[i * ordered.Count / k].capital.city_center);
        var assignment = new Dictionary<Kingdom, int>();
        for (int round = 0; round < 6; round++)
        {
            foreach (Kingdom member in ordered)
            {
                Vector2 at = member.capital.city_center;
                int best = 0;
                for (int i = 1; i < k; i++)
                    if ((centers[i] - at).sqrMagnitude < (centers[best] - at).sqrMagnitude) best = i;
                assignment[member] = best;
            }
            for (int i = 0; i < k; i++)
            {
                List<Vector2> points = assignment.Where(pair => pair.Value == i).Select(pair => pair.Key.capital.city_center).ToList();
                if (points.Count > 0) centers[i] = new Vector2(points.Average(p => p.x), points.Average(p => p.y));
            }
        }
        foreach (var pair in assignment) state.circles[pair.Key.id] = pair.Value;
    }

    // 分割继承(GraceEdictService.TryHandleVacancy 调用，每次出缺只掷一次)；掷中后还要有两个以上的儿子、都城外有城才分
    public static bool RollPartible(Empire empire, Kingdom fief)
    {
        if (!HasDiet(empire) || fief?.data == null) return false;
        string blocked = fief.GetRegime()?.type != RegimeType.Feudalism ? "不是封建制"
            : Estate(fief) != EstateNone ? "自由城市或主教领"
            : fief.cities.Count < 2 ? "只有一城"
            : Seats(State(empire)).Contains(fief.id) ||
              empire.data.CabinetMembers != null && empire.GetCabinetMembers().Any(actor => actor?.kingdom == fief) ? "选帝侯领国不可分"
            : null;
        bool rolled = blocked == null && UnityEngine.Random.value < PartibleChance;
        PartibleTally.TryGetValue(empire.data.id, out var tally);
        PartibleTally[empire.data.id] = (tally.vacant + 1, tally.rolled + (rolled ? 1 : 0),
            rolled ? tally.blocked : blocked ?? "没掷中(三成五)");
        return rolled;
    }

    // 分割继承的年度计数(诊断用，不存档)：帝国 → (诸侯出缺几次, 掷中几次, 最近一次没分的缘由)
    private static readonly Dictionary<long, (int vacant, int rolled, string blocked)> PartibleTally = new();
}
