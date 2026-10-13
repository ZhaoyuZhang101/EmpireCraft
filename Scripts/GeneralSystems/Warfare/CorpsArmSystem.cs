using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 兵种：每支军成军时按本文化的科技定一个兵种(存档 CorpsData.arm)，决定兵牌图标、行军方式、军饷与(纯净地图下的)战力。
//   成军时的编成：古代步兵、弓兵，青铜铸造后有车兵，冶铁后改出骑兵；火绳枪后出火枪兵，火药后有炮兵，膛线后出步枪兵；
//   摩托化后出装甲车，装甲战出坦克，航空出直升机，战略航空出战斗机、轰炸机；义军一律是民兵。
//   同一时代各兵种按权重分配(按军号散列，固定不变)，一国几支军自然有步有骑有炮。
// 换装：研究出新装备后旧军不会自动换装——步兵、弓兵 → 火枪兵 → 步枪兵；车兵 → 骑兵 → 装甲车；直升机 → 战斗机。
//   换装费 = 换装后一年的军饷，从本国国库出；各国每月视财力换一支(国库至少留两倍费用)，前线的军优先；
//   军事页可以点"换装"直接换(照样花钱)。换装后要重新操练，组织度 -25。
// 军饷：装备越先进越贵(CorpsSystem.PayUpkeep 按兵种加权)：民兵 0.6 倍，步兵、弓兵 1，火枪 1.2，车兵 1.3，骑兵、步枪 1.5，
//   炮兵 1.8，装甲车 2.5，坦克 3.2，直升机 3.5，战斗机 4，轰炸机 4.5。
// 行军(CorpsSystem.SecondTick、StepTowardTarget)：
//   · 速度：步兵类 1 倍，民兵 0.8，炮兵 0.75，车兵 1.3，骑兵 1.6，装甲车 2，坦克 1.6，直升机 2.4，战斗机 3.2，轰炸机 2.8；
//   · 山地、沼泽、丛林：步兵类剩一半，骑兵剩四成，车兵、炮兵、装甲车、坦克剩三成；飞机不受地形影响；
//   · 优先走路网(见 RoadNetwork)：离开道路步兵类剩八成五、民兵九成，骑兵八成，车兵七成，炮兵六成五，坦克六成，装甲车五成五；
//     寻路按代价走(路上的地块代价 1，路外按兵种加价，山地沼泽丛林再加)，所以宁可绕一段路走大道；
//   · 飞机直线飞越高山与海面，不必去港口登船；陆军照旧绕开高山、跨海要登船。
// 战力(纯净地图模式，见 FrontSystem.Firepower)：人物、载具都是数据，不靠 WarBox 的实体载具，按兵种定火力倍数。
public static class CorpsArmSystem
{
    public enum Arm { Infantry, Archer, Chariot, Cavalry, Musket, Rifle, Artillery, Mechanized, Tank, Helicopter, Fighter, Bomber, Militia }

    private readonly struct Spec
    {
        public readonly float Speed, Rough, OffRoad, Upkeep, Power;
        public readonly bool Flies;
        public Spec(float speed, float rough, float offRoad, float upkeep, float power, bool flies = false)
        {
            Speed = speed;
            Rough = rough;
            OffRoad = offRoad;
            Upkeep = upkeep;
            Power = power;
            Flies = flies;
        }
    }

    private static readonly Dictionary<Arm, Spec> Specs = new()
    {
        [Arm.Infantry] = new Spec(1f, 0.5f, 0.85f, 1f, 1f),
        [Arm.Archer] = new Spec(1f, 0.5f, 0.85f, 1f, 1.05f),
        [Arm.Chariot] = new Spec(1.3f, 0.3f, 0.7f, 1.3f, 1.15f),
        [Arm.Cavalry] = new Spec(1.6f, 0.4f, 0.8f, 1.5f, 1.3f),
        [Arm.Musket] = new Spec(1f, 0.5f, 0.85f, 1.2f, 1.4f),
        [Arm.Rifle] = new Spec(1f, 0.5f, 0.85f, 1.5f, 1.8f),
        [Arm.Artillery] = new Spec(0.75f, 0.3f, 0.65f, 1.8f, 1.7f),
        [Arm.Mechanized] = new Spec(2f, 0.3f, 0.55f, 2.5f, 2.2f),
        [Arm.Tank] = new Spec(1.6f, 0.3f, 0.6f, 3.2f, 2.8f),
        [Arm.Helicopter] = new Spec(2.4f, 1f, 1f, 3.5f, 2.4f, true),
        [Arm.Fighter] = new Spec(3.2f, 1f, 1f, 4f, 2.6f, true),
        [Arm.Bomber] = new Spec(2.8f, 1f, 1f, 4.5f, 3f, true),
        [Arm.Militia] = new Spec(0.8f, 0.5f, 0.9f, 0.6f, 0.8f),
    };

    // 换装费 = 换装后几年的军饷；换装后组织度损失；国库至少留几倍费用才换
    private const float RefitYears = 1f;
    private const float RefitOrgLoss = 25f;
    private const float RefitReserve = 2f;

    // 本文化眼下的成军编成(兵种, 权重)；每两秒(真实时间)重算一次
    private const float RefreshSeconds = 2f;
    private static readonly Dictionary<string, (float at, List<(Arm arm, int weight)> mix, int total)> Mixes = new();

    public static Arm Of(CorpsData corps)
    {
        if (corps == null) return Arm.Infantry;
        if (corps.militia) return Arm.Militia;
        if (corps.arm >= 0 && Enum.IsDefined(typeof(Arm), corps.arm)) return (Arm)corps.arm;
        // 新成军(或旧存档第一次读到)：按本文化眼下的编成定下，之后不再自动变
        Arm picked = Pick(corps);
        corps.arm = (int)picked;
        return picked;
    }

    private static Arm Pick(CorpsData corps)
    {
        var (_, mix, total) = Mix(Culture(corps));
        ulong hash = (ulong)corps.id * 11400714819323198485UL;
        int roll = (int)((hash >> 33) % (ulong)total);
        foreach ((Arm arm, int weight) in mix)
        {
            if (roll < weight) return arm;
            roll -= weight;
        }
        return mix[0].Item1;
    }

    private static string Culture(CorpsData corps)
    {
        Kingdom kingdom = World.world?.kingdoms?.get(corps.kingdom_id);
        return kingdom == null ? "" : TechnologySystem.GetCultureOf(kingdom) ?? "";
    }

    private static bool Has(string culture, string tech) =>
        !string.IsNullOrEmpty(culture) && TechnologySystem.IsActiveFor(culture) && TechnologySystem.HasTech(culture, tech);

    private static (float, List<(Arm, int)>, int) Mix(string culture)
    {
        float now = Time.unscaledTime;
        if (Mixes.TryGetValue(culture, out var cached) && now - cached.at < RefreshSeconds && now >= cached.at) return cached;
        var mix = new List<(Arm, int)>();
        bool rifles = Has(culture, "rifling");
        mix.Add((rifles ? Arm.Rifle : Arm.Infantry, rifles ? 40 : 45));
        if (!rifles) mix.Add((Has(culture, "matchlock") ? Arm.Musket : Arm.Archer, 25));
        if (Has(culture, "motorization")) mix.Add((Arm.Mechanized, 20));
        else if (Has(culture, "ironworking")) mix.Add((Arm.Cavalry, 20));
        else if (Has(culture, "bronze_casting")) mix.Add((Arm.Chariot, 15));
        if (Has(culture, "gunpowder")) mix.Add((Arm.Artillery, 12));
        if (Has(culture, "armored_warfare")) mix.Add((Arm.Tank, 20));
        if (Has(culture, "strategic_aviation"))
        {
            mix.Add((Arm.Fighter, 10));
            mix.Add((Arm.Bomber, 8));
        }
        else if (Has(culture, "aviation")) mix.Add((Arm.Helicopter, 10));
        int total = mix.Sum(entry => entry.Item2);
        var entry = (now, mix, Mathf.Max(1, total));
        Mixes[culture] = entry;
        return entry;
    }

    // 本文化的装备能把这一兵种换成什么(换不了返回原兵种)
    public static Arm Upgrade(Arm arm, string culture)
    {
        switch (arm)
        {
            case Arm.Infantry:
            case Arm.Archer:
            case Arm.Musket:
                if (Has(culture, "rifling")) return Arm.Rifle;
                return arm != Arm.Musket && Has(culture, "matchlock") ? Arm.Musket : arm;
            case Arm.Chariot:
            case Arm.Cavalry:
                if (Has(culture, "motorization")) return Arm.Mechanized;
                return arm == Arm.Chariot && Has(culture, "ironworking") ? Arm.Cavalry : arm;
            case Arm.Helicopter:
                return Has(culture, "strategic_aviation") ? Arm.Fighter : arm;
            default:
                return arm;
        }
    }

    public static Arm UpgradeOf(CorpsData corps) => corps == null || corps.militia ? Arm.Militia : Upgrade(Of(corps), Culture(corps));

    public static bool CanRefit(CorpsData corps) => corps != null && !corps.militia && UpgradeOf(corps) != Of(corps);

    public static int RefitCost(CorpsData corps)
    {
        if (!CanRefit(corps)) return 0;
        double annual = CityStabilityRules.CorpsAnnual(Mathf.Max(0f, corps.strength) * Upkeep(UpgradeOf(corps)), false);
        return (int)Math.Min(int.MaxValue, Math.Ceiling(annual * RefitYears));
    }

    // 换装：从本国国库出钱；钱不够不换
    public static bool Refit(Kingdom kingdom, CorpsData corps)
    {
        if (kingdom?.data == null || kingdom.isRekt() || !CanRefit(corps)) return false;
        int cost = RefitCost(corps);
        if (kingdom.GetMoney() < cost) return false;
        Arm target = UpgradeOf(corps);
        if (cost > 0) kingdom.SubMoney(cost, TreasuryCategory.Military);
        corps.arm = (int)target;
        corps.org = Mathf.Max(0f, corps.org - RefitOrgLoss);
        FrontSystem.Log(kingdom, string.Format(LM.Get("corps_log_refit"), CorpsSystem.Name(corps), Name(target),
            MoneyDisplay.Format(cost)));
        return true;
    }

    // 每月：视财力换装一支，前线的军优先，其次兵多的(骑士团的军由骑士团自己管)
    public static void Monthly(Kingdom kingdom, List<CorpsData> list)
    {
        if (kingdom?.data == null || list == null || list.Count == 0) return;
        CorpsData pick = list.Where(corps => corps != null && corps.order_id < 0 && !corps.embarked && CanRefit(corps))
            .OrderByDescending(corps => corps.task is "attack" or "defend")
            .ThenByDescending(corps => corps.strength).FirstOrDefault();
        if (pick == null || kingdom.GetMoney() < RefitCost(pick) * RefitReserve) return;
        Refit(kingdom, pick);
    }

    public static float Speed(Arm arm) => Specs[arm].Speed;

    // 山地、沼泽、丛林里剩几成速度
    public static float Rough(Arm arm) => Specs[arm].Rough;

    // 离开道路剩几成速度(见 RoadNetwork)；寻路时路外每块的代价是它的倒数
    public static float OffRoad(Arm arm) => Specs[arm].OffRoad;

    public static bool Flies(Arm arm) => Specs[arm].Flies;

    public static bool Flies(CorpsData corps) => Flies(Of(corps));

    // 军饷倍数(见 CorpsSystem.PayUpkeep)
    public static float Upkeep(Arm arm) => Specs[arm].Upkeep;

    // 战力倍数(纯净地图模式，见 FrontSystem.Firepower)
    public static float Power(Arm arm) => Specs[arm].Power;

    public static string Name(Arm arm) => LM.Get("corps_arm_" + arm.ToString().ToLowerInvariant());

    // 兵牌图标(Tools/IconForge/ship_forge.py 的 ARMS)
    public static string IconPath(Arm arm) => "ui/icons/corps/arm_" + arm.ToString().ToLowerInvariant();

    // 科技窗口：这项技术让本文化新成军时可以有哪些兵种
    public static IEnumerable<Arm> UnlockedBy(string techId)
    {
        switch (techId)
        {
            case "bronze_casting": yield return Arm.Chariot; break;
            case "ironworking": yield return Arm.Cavalry; break;
            case "matchlock": yield return Arm.Musket; break;
            case "rifling": yield return Arm.Rifle; break;
            case "gunpowder": yield return Arm.Artillery; break;
            case "motorization": yield return Arm.Mechanized; break;
            case "armored_warfare": yield return Arm.Tank; break;
            case "aviation": yield return Arm.Helicopter; break;
            case "strategic_aviation":
                yield return Arm.Fighter;
                yield return Arm.Bomber;
                break;
        }
    }

    public static void ResetWorldState() => Mixes.Clear();
}
