using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 制空权、战略轰炸与导弹(用户 2026-11-24 "现代之后就很无聊了"，五批里的第五批)。以前战斗机、轰炸机只是火力高一点的"军"。
// 每年把各场战争过一遍：
//   · 空中力量：一方各国的战斗机军兵力(轰炸机算一半、直升机三成)合计，研究出喷气发动机的 ×1.3；
//   · 制空权：一方的空中力量达到对方一倍半，夺得制空权——它的陆军在战线上火力 +15%，对方 −10%(见 FrontSystem.Firepower)；
//   · 战略轰炸：握有制空权又有轰炸机的一方每年轰炸对方工业最多的那座城——那座城的工厂一年里减产(轰炸机越多减得越多，至多四成)，
//     对方劳工、市民怨气上涨，有机会炸毁一座厂的一个档次；
//   · 导弹：研究出火箭技术的一方轰炸力 +30%，洲际导弹再 +30%；没有制空权但有火箭技术，照样能打，只是效果减半。
// 末代的可重复研究"军事精进"也在这里乘进火力(见 TechnologySystem.RepeatFactor)。只影响数值战线，不放实体飞机。
public static class AirPowerSystem
{
    public const string JetTech = "jet_engines", RocketTech = "rocketry", IcbmTech = "icbm";
    private const float SuperiorityRatio = 1.5f, MinAir = 200f, WinFire = 1.15f, LoseFire = 0.9f;
    private const float BombBase = 0.15f, BombPerIntensity = 0.25f, FullBombers = 2000f, RazeChance = 0.1f;

    private static readonly Dictionary<long, float> Fire = new();
    private static readonly Dictionary<long, (double until, float cut)> Bombed = new();

    public static void ResetWorldState()
    {
        Fire.Clear();
        Bombed.Clear();
    }

    private static bool Has(Kingdom kingdom, string tech) => TechnologySystem.HasTech(TechnologySystem.GetCultureOf(kingdom), tech);

    // 每年(WorldOrderSystem.Scan)
    internal static void Yearly(double now)
    {
        Fire.Clear();
        var fighters = new Dictionary<long, float>();
        var bombers = new Dictionary<long, float>();
        foreach ((Kingdom kingdom, CorpsData corps) in CorpsSystem.All())
        {
            if (kingdom?.data == null || corps == null || corps.militia) continue;
            CorpsArmSystem.Arm arm = CorpsArmSystem.Of(corps);
            float air = arm switch
            {
                CorpsArmSystem.Arm.Fighter => corps.strength,
                CorpsArmSystem.Arm.Bomber => corps.strength * 0.5f,
                CorpsArmSystem.Arm.Helicopter => corps.strength * 0.3f,
                _ => 0f
            };
            if (air <= 0f) continue;
            if (Has(kingdom, JetTech)) air *= 1.3f;
            fighters[kingdom.id] = (fighters.TryGetValue(kingdom.id, out float sum) ? sum : 0f) + air;
            if (arm == CorpsArmSystem.Arm.Bomber)
                bombers[kingdom.id] = (bombers.TryGetValue(kingdom.id, out float load) ? load : 0f) + corps.strength;
        }
        foreach (War war in World.world.wars.list.ToList())
        {
            if (war == null || war.hasEnded()) continue;
            List<Kingdom> attackers = Alive(war._list_attackers), defenders = Alive(war._list_defenders);
            if (attackers.Count == 0 || defenders.Count == 0) continue;
            float Sum(List<Kingdom> side, Dictionary<long, float> table) =>
                side.Sum(kingdom => table.TryGetValue(kingdom.id, out float value) ? value : 0f);
            float mine = Sum(attackers, fighters), theirs = Sum(defenders, fighters);
            int side = mine >= MinAir && mine >= theirs * SuperiorityRatio ? 1
                : theirs >= MinAir && theirs >= mine * SuperiorityRatio ? -1 : 0;
            if (side != 0)
            {
                List<Kingdom> winners = side > 0 ? attackers : defenders, losers = side > 0 ? defenders : attackers;
                foreach (Kingdom kingdom in winners) Scale(kingdom, WinFire);
                foreach (Kingdom kingdom in losers) Scale(kingdom, LoseFire);
                Note(war, 1, "air_superiority_history", winners[0], losers[0], null);
                Strike(war, winners, losers, Sum(winners, bombers), 1f, now);
                // 落了下风的一方有火箭技术，照样拿导弹还手
                if (losers.Any(kingdom => Has(kingdom, RocketTech))) Strike(war, losers, winners, 0f, 0.5f, now);
            }
            else
            {
                foreach ((List<Kingdom> from, List<Kingdom> to) in new[] { (attackers, defenders), (defenders, attackers) })
                    if (from.Any(kingdom => Has(kingdom, RocketTech))) Strike(war, from, to, 0f, 0.5f, now);
            }
        }
        foreach (long city in Bombed.Where(pair => pair.Value.until <= now).Select(pair => pair.Key).ToList()) Bombed.Remove(city);
    }

    private static List<Kingdom> Alive(IEnumerable<Kingdom> side) =>
        side?.Where(kingdom => kingdom?.data != null && !kingdom.isRekt()).ToList() ?? new List<Kingdom>();

    private static void Scale(Kingdom kingdom, float factor) =>
        Fire[kingdom.id] = Mathf.Clamp((Fire.TryGetValue(kingdom.id, out float value) ? value : 1f) * factor, 0.8f, 1.3f);

    // 每场战争每样事只记一回
    private static void Note(War war, int bit, string key, Kingdom from, Kingdom to, City city)
    {
        var data = war.GetOrCreate();
        if ((data.air_noted & bit) != 0) return;
        data.air_noted |= bit;
        string text = string.Format(LM.Get(key), from.GetKingdomFullName(), to.GetKingdomFullName(), city?.GetCityName() ?? "");
        EventRecorder.Record(from, text);
        if (to.GetEmpire() is { } other) other.RecordHistory(directContent: text, kingdomId: other.CoreKingdom?.id ?? -1L);
    }

    // 轰炸：打对方工业最多的那座城
    private static void Strike(War war, List<Kingdom> from, List<Kingdom> to, float bomberStrength, float effect, double now)
    {
        float power = bomberStrength;
        bool rockets = from.Any(kingdom => Has(kingdom, RocketTech));
        if (rockets) power = Mathf.Max(power, FullBombers * 0.3f) * 1.3f;
        if (from.Any(kingdom => Has(kingdom, IcbmTech))) power *= 1.3f;
        if (power <= 0f) return;
        City target = null;
        int best = -1;
        foreach (Kingdom kingdom in to)
            foreach (City city in kingdom.cities)
            {
                if (city?.data == null || city.isRekt()) continue;
                int units = FactorySystem.Units(city) + (city == kingdom.capital ? 1 : 0);
                if (units <= best) continue;
                best = units;
                target = city;
            }
        if (target == null) return;
        float intensity = Mathf.Clamp01(power / FullBombers) * effect;
        float cut = BombBase * effect + BombPerIntensity * intensity;
        Bombed[target.id] = (now + 12d * Date.getMonthTime(), Mathf.Max(cut, Bombed.TryGetValue(target.id, out var old) ? old.cut : 0f));
        if (target.kingdom?.GetEmpire() is { } victim)
        {
            ClaimPolicySystem.AddGrievance(victim, SocialClass.Labour, 3f * intensity);
            ClaimPolicySystem.AddGrievance(victim, SocialClass.Citizen, 4f * intensity);
        }
        if (UnityEngine.Random.value < RazeChance * intensity)
            foreach (FactorySite site in FactorySystem.InCity(target))
            {
                int index = site.units.FindIndex(unit => unit.level > 1);
                if (index < 0) continue;
                FactorySystem.SetLevel(site, index, site.units[index].level - 1);
                break;
            }
        Note(war, bomberStrength > 0f ? 2 : 4, bomberStrength > 0f ? "air_bombing_history" : "air_missile_history", from[0], target.kingdom ?? to[0], target);
    }

    // 陆军火力(见 FrontSystem.Firepower)：制空权，再乘末代的"军事精进"
    public static float FirepowerFactor(Kingdom kingdom) => kingdom?.data == null ? 1f
        : (Fire.TryGetValue(kingdom.id, out float value) ? value : 1f) *
          TechnologySystem.RepeatFactor(TechnologySystem.GetCultureOf(kingdom), "military");

    // 挨了轰炸的城工厂减产(见 FactorySystem.Produce)
    public static float BombedFactor(City city) =>
        city != null && Bombed.TryGetValue(city.id, out var hit) && World.world.getCurWorldTime() < hit.until ? 1f - hit.cut : 1f;
}
