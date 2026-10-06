using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Compatibility;

// 核战略：WarBox 的 AI 只在同一场战争打满 50 年(嗜血/尚武君主 25 年)后才考虑核打击，
// 而本模组的战争节奏快得多(战争消耗、受降、开国速攻)，几乎等不到那一天。
// 这里按战局的危急程度替 AI 决策，决定动用后调用 WarBox 自己的核打击流程
// (NuclearStrikeSystem.TryPlayerNuclearStrike：照常花费国库、长期准备、每场战争对同一敌国只用一次)。
//
// 每年对每个掌握核裂变技术、正在打仗的国家评估一次"危急度"：
//   战争已打满 10 年 +1；敌军兵力达到本国 1.5 倍 +1；本国城市不到敌国一半 +1；
//   都城正被围攻 +2；君主嗜血或尚武、或执政理念为法西斯 +1；
//   敌国同样拥有核武器(相互确保毁灭) -2。
// 危急度 ≥ 3 时每年 25% 的机会下令准备核打击。
//
// 核威慑(影响开战)：原版 AI 只向"好感不好"的国家开战，这里给好感加一项核威慑修正——
//   对方有核、自己没有：畏惧核报复，好感 +DeterredOpinion，基本不会主动开战；
//   双方都有核：相互确保毁灭，好感 +MutualOpinion，开战更加谨慎；
//   自己有核、对方没有：有恃无恐，好感 -SuperiorityOpinion，更敢开战。
// 帝国的"对外扩张"诉求也不会挑有核而自己无核的国家下手。
//
// 施压弃核：每年对每个(不在战争中的)有核国家，统计国力至少是它 1.5 倍的其他有核大国
// (与它接壤，或国力达到它 3 倍的霸权国)：
//   · 施压国合计国力达到它 3 倍：每年 30% 的机会被迫弃核——销毁核武器、不能再动用；
//     若它是本文化的主体(拥有本文化一半以上城市)，整个文化停止核研究；
//   · 达到 1.5 倍：遭到制裁，国库每年损失一成；
//   · 弃核之后再没有大国压着：每年 10% 的机会重启核计划。
public static class NuclearDoctrineSystem
{
    private const string NuclearTech = "nuclear_fission";
    private const int Threshold = 3;
    private const float YearlyChance = 0.25f;

    public const int DeterredOpinion = 150;
    public const int MutualOpinion = 60;
    public const int SuperiorityOpinion = 40;

    private static MethodInfo _tryStrike;
    private static bool _resolved;
    private static object _scanWorld;
    private static double _lastScan = -1d;

    private static MethodInfo TryStrike
    {
        get
        {
            if (_resolved) return _tryStrike;
            _resolved = true;
            _tryStrike = AccessTools.TypeByName("WarMobilization.NuclearStrikeSystem")?
                .GetMethod("TryPlayerNuclearStrike", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            return _tryStrike;
        }
    }

    public static void TryYearlyScan()
    {
        if (World.world?.kingdoms == null || ModClass.IS_CLEAR || TryStrike == null) return;
        double now = World.world.getCurWorldTime();
        if (!ReferenceEquals(_scanWorld, World.world) || now < _lastScan)
        {
            _scanWorld = World.world;
            _lastScan = now;
            return;
        }
        if (Date.getYearsSince(_lastScan) < 1) return;
        _lastScan = now;
        try
        {
            UpdatePressure();
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 弃核施压结算失败: {exception.Message}");
        }
        foreach (Kingdom kingdom in World.world.kingdoms.ToList())
        {
            try
            {
                Evaluate(kingdom);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft] 核战略评估失败({kingdom?.name}): {exception.Message}");
            }
        }
    }

    // 装了 WarBox(有核打击可用)时核武器才存在
    public static bool Available => TryStrike != null;

    // 帝国成员按帝国核心是否有核
    private static Kingdom Realm(Kingdom kingdom) =>
        kingdom != null && kingdom.IsInEmpire() ? kingdom.GetEmpire()?.CoreKingdom ?? kingdom : kingdom;

    public static bool IsNuclearPower(Kingdom kingdom)
    {
        Kingdom realm = Realm(kingdom);
        return Available && realm != null && !realm.GetOrCreate().nuclear_renounced && HasNukes(realm);
    }

    public static bool IsRenounced(Kingdom kingdom) => Realm(kingdom)?.GetOrCreate().nuclear_renounced == true;

    // 文化的核研究是否被禁(本文化的主体已被迫弃核)；年度扫描时更新
    private static readonly HashSet<string> BannedCultures = new();

    public static bool IsResearchBanned(string culture, string techId) =>
        techId == NuclearTech && !string.IsNullOrEmpty(culture) && BannedCultures.Contains(culture);

    // 科技门槛(见 TechnologyPatch.ModGate)：弃核国家不能再动用核武器
    public static bool BlocksNuclearUse(string techId, object[] args)
    {
        if (techId != NuclearTech || args == null) return false;
        foreach (object arg in args)
        {
            Kingdom kingdom = arg switch
            {
                Actor actor => actor.kingdom,
                Kingdom k => k,
                City city => city.kingdom,
                _ => null
            };
            if (kingdom != null) return IsRenounced(kingdom);
        }
        return false;
    }

    // pMain 对 pTarget 的核威慑好感修正(同一帝国内不算)
    public static int DeterrenceOpinion(Kingdom main, Kingdom target)
    {
        if (!Available || main == null || target == null || main == target || main.IsInSameEmpire(target)) return 0;
        bool own = IsNuclearPower(main);
        bool theirs = IsNuclearPower(target);
        if (theirs && own) return MutualOpinion;
        if (theirs) return DeterredOpinion;
        return own ? -SuperiorityOpinion : 0;
    }

    // 进攻方无核而目标有核：被核威慑吓住
    public static bool IsDeterred(Kingdom attacker, Kingdom target) =>
        IsNuclearPower(target) && !IsNuclearPower(attacker);

    private static bool HasNukes(Kingdom kingdom) =>
        kingdom != null && (!TechnologySystem.GatesActive ||
                              TechnologySystem.HasTech(TechnologySystem.GetCultureOf(kingdom), NuclearTech));

    private static void Evaluate(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || !kingdom.isCiv() || !kingdom.hasKing() ||
            kingdom.king.hasPlot() || !kingdom.hasEnemies() || !IsNuclearPower(kingdom) ||
            AncientWarfareCompatibility.Owns(kingdom)) return;
        // 帝国成员由帝国核心决策
        if (kingdom.IsInEmpire() && !kingdom.IsEmpire()) return;

        Kingdom target = null;
        int best = int.MinValue;
        foreach (War war in kingdom.getWars().Where(war => war != null && !war.hasEnded()))
        {
            Kingdom enemy = war.isAttacker(kingdom) ? war.getMainDefender() : war.getMainAttacker();
            if (enemy == null || enemy == kingdom || enemy.isRekt() || !enemy.isCiv() || enemy.cities.Count == 0) continue;
            int score = Desperation(kingdom, enemy, war);
            if (score > best)
            {
                best = score;
                target = enemy;
            }
        }
        if (target == null || best < Threshold || UnityEngine.Random.value >= YearlyChance) return;
        object[] args = { kingdom, target, null, 0 };
        TryStrike.Invoke(null, args);
    }

    #region 施压弃核

    private const float PressureShare = 1.5f;
    private const float HegemonShare = 3f;
    private const float RenounceChance = 0.3f;
    private const float RestartChance = 0.1f;
    private const float SanctionLoss = 0.1f;

    // 各个独立的政权：帝国按核心国，未加入帝国的王国按本国
    private static List<Kingdom> Realms() => World.world.kingdoms
        .Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && kingdom.isCiv() &&
                          (kingdom.IsEmpire() || !kingdom.IsInEmpire()) && !AncientWarfareCompatibility.Owns(kingdom))
        .ToList();

    private static List<Kingdom> Members(Kingdom realm)
    {
        Empire empire = realm.IsEmpire() ? realm.GetEmpire() : null;
        return empire?.kingdoms_list?.Where(kingdom => kingdom != null && !kingdom.isRekt()).ToList() ??
               new List<Kingdom> { realm };
    }

    private static bool Borders(Kingdom a, Kingdom b)
    {
        var other = new HashSet<Kingdom>(Members(b));
        return Members(a).Any(kingdom => kingdom.cities.Any(city =>
            city?.neighbours_cities != null && city.neighbours_cities.Any(n => n?.kingdom != null && other.Contains(n.kingdom))));
    }

    private static double Power(Kingdom realm) => Math.Max(1d, realm.GetNationalPower());

    private static void UpdatePressure()
    {
        List<Kingdom> realms = Realms();
        List<Kingdom> nuclear = realms.Where(IsNuclearPower).ToList();
        foreach (Kingdom realm in realms)
        {
            KingdomExtension.KingdomExtraData data = realm.GetOrCreate();
            bool renounced = data.nuclear_renounced;
            if (!renounced && !nuclear.Contains(realm)) continue;
            double power = Power(realm);
            List<Kingdom> pressurers = nuclear.Where(other => other != realm && !other.IsInSameEmpire(realm) &&
                                                              Power(other) >= power * PressureShare &&
                                                              (Power(other) >= power * HegemonShare || Borders(realm, other)))
                .ToList();
            string names = string.Join("、", pressurers.Take(3).Select(other => other.GetKingdomFullName()));
            if (renounced)
            {
                if (pressurers.Count == 0 && UnityEngine.Random.value < RestartChance)
                {
                    data.nuclear_renounced = false;
                    TranslateHelper.LogEventMessage(string.Format(LM.Get("nuclear_restart_log"),
                        realm.GetKingdomFullName()), realm);
                }
                continue;
            }
            if (pressurers.Count == 0 || realm.hasEnemies())
            {
                data.nuclear_sanctioned = false;
                continue;
            }
            double ratio = pressurers.Sum(Power) / power;
            if (ratio >= HegemonShare && UnityEngine.Random.value < RenounceChance)
            {
                data.nuclear_renounced = true;
                data.nuclear_renounced_at = World.world.getCurWorldTime();
                data.nuclear_sanctioned = false;
                TranslateHelper.LogEventMessage(string.Format(LM.Get("nuclear_renounced_log"),
                    realm.GetKingdomFullName(), names), realm);
                continue;
            }
            int loss = (int)(realm.GetMoney() * SanctionLoss);
            if (loss > 0) realm.SubMoney(loss);
            if (data.nuclear_sanctioned) continue;
            data.nuclear_sanctioned = true;
            TranslateHelper.LogEventMessage(string.Format(LM.Get("nuclear_sanction_log"), names,
                realm.GetKingdomFullName()), realm);
        }

        // 本文化的主体被迫弃核：整个文化停止核研究
        BannedCultures.Clear();
        foreach (IGrouping<string, Kingdom> culture in realms.GroupBy(TechnologySystem.GetCultureOf))
        {
            if (string.IsNullOrEmpty(culture.Key)) continue;
            int total = culture.Sum(realm => Members(realm).Sum(kingdom => kingdom.cities.Count));
            int banned = culture.Where(realm => realm.GetOrCreate().nuclear_renounced)
                .Sum(realm => Members(realm).Sum(kingdom => kingdom.cities.Count));
            if (total > 0 && banned * 2 >= total) BannedCultures.Add(culture.Key);
        }
    }

    #endregion

    private static int Desperation(Kingdom kingdom, Kingdom enemy, War war)
    {
        int score = 0;
        if (Date.getCurrentYear() - war.getYearStarted() >= 10) score++;
        int ownSoldiers = Math.Max(1, kingdom.countTotalWarriors());
        if (enemy.countTotalWarriors() >= ownSoldiers * 1.5f) score++;
        if (kingdom.cities.Count * 2 < enemy.cities.Count) score++;
        if (kingdom.capital != null && !kingdom.capital.isRekt() && kingdom.capital.isGettingCaptured()) score += 2;
        Actor king = kingdom.king;
        Empire empire = kingdom.GetEmpire();
        if (king.hasTrait("bloodlust") || king.hasTrait("militarist") ||
            empire != null && PartySystem.GetGovernmentParty(empire)?.Ideology == PartyIdeology.Fascism) score++;
        if (IsNuclearPower(enemy)) score -= 2;
        return score;
    }
}
