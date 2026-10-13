using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GamePatches;

// 原版的战争标记(箭头 + 刀盾图标)从主攻方都城画到主守方都城：大国的都城离前线很远，
// 箭头横跨大半张地图，看起来像隔着别国开战。改为画在两边离得最近的两座城之间
// (帝国一方连同成员国的城一起算)，刀盾图标也放在这两座城上。最近的一对城每两秒重找一次。
public class WarMarkerPatch : GamePatch
{
    public ModDeclare declare { get; set; }

    private const float RefreshSeconds = 2f;
    private static readonly Dictionary<long, (City from, City to, float at)> Ends = new();
    private static bool _failed;

    public void Initialize()
    {
        new Harmony(nameof(WarMarkerPatch)).Patch(AccessTools.Method(typeof(QuantumSpriteLibrary), "drawWars"),
            prefix: new HarmonyMethod(typeof(WarMarkerPatch), nameof(Before)));
    }

    private static bool Before(QuantumSpriteAsset pAsset)
    {
        if (_failed) return true;
        try
        {
            Draw(pAsset);
            return false;
        }
        catch (Exception exception)
        {
            _failed = true;
            LogService.LogWarning($"[EmpireCraft][战争标记] 改用原版画法: {exception.Message}");
            return true;
        }
    }

    // 同原版 drawWars，只把两端换成离得最近的两座城
    private static void Draw(QuantumSpriteAsset pAsset)
    {
        if (!PlayerConfig.optionBoolEnabled("marks_wars")) return;
        QuantumSpriteLibrary._wars_pos_sword_main.Clear();
        QuantumSpriteLibrary._wars_pos_shields_main.Clear();
        if (World.world.wars.Count == 0) return;
        Kingdom hovered = null;
        foreach (Kingdom kingdom in World.world.kingdoms)
            if (kingdom.isCursorOver())
            {
                hovered = kingdom;
                break;
            }
        foreach (War war in World.world.wars)
        {
            if (war.hasEnded() || war.isTotalWar()) continue;
            float alpha = 1f;
            if (hovered != null)
            {
                bool involved = war.hasKingdom(hovered);
                pAsset.base_scale = involved ? 1f : 0.2f;
                alpha = involved ? 1f : 0.1f;
            }
            else pAsset.base_scale = 0.5f;
            Kingdom attacker = war.main_attacker, defender = war.main_defender;
            if (attacker.isRekt() || defender.isRekt() || !attacker.hasCapital() || !defender.hasCapital()) continue;
            (City from, City to) = Endpoints(war, attacker, defender);
            if (from == null || to == null) continue;
            Vector3 start = from.city_center, end = to.city_center;
            start.y -= 20f;
            end.y -= 20f;
            start.z = pAsset.base_scale;
            end.z = pAsset.base_scale;
            QuantumSpriteLibrary._wars_pos_sword_main.Add(start);
            QuantumSpriteLibrary._wars_pos_shields_main.Add(end);
            pAsset.base_scale *= 0.6f;
            QuantumSpriteArrows arrows = QuantumSpriteLibrary.drawArrowQuantumSprite(pAsset, start, end, ref Toolbox.color_white);
            Color color = attacker.getColor().getColorMainSecond();
            color.a = alpha;
            if (arrows != null)
            {
                arrows.spriteArrowMiddle.color = color;
                arrows.spriteArrowMiddle.sortingOrder = -1;
            }
        }
    }

    private static (City from, City to) Endpoints(War war, Kingdom attacker, Kingdom defender)
    {
        float now = Time.realtimeSinceStartup;
        long id = war.data.id;
        if (Ends.TryGetValue(id, out var cached) && now - cached.at < RefreshSeconds && Valid(cached.from, attacker) &&
            Valid(cached.to, defender))
            return (cached.from, cached.to);
        List<City> ours = Cities(attacker), theirs = Cities(defender);
        City bestFrom = attacker.capital, bestTo = defender.capital;
        float best = float.MaxValue;
        foreach (City a in ours)
        foreach (City b in theirs)
        {
            float distance = ((Vector2)a.city_center - (Vector2)b.city_center).sqrMagnitude;
            if (distance >= best) continue;
            best = distance;
            bestFrom = a;
            bestTo = b;
        }
        if (Ends.Count > 256) Ends.Clear();
        Ends[id] = (bestFrom, bestTo, now);
        return (bestFrom, bestTo);
    }

    private static bool Valid(City city, Kingdom side) =>
        city?.data != null && !city.isRekt() && city.kingdom != null && Realm(city.kingdom) == Realm(side);

    private static Kingdom Realm(Kingdom kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;

    // 这一方的城：帝国中央连同成员国，其余就是本国
    private static List<City> Cities(Kingdom kingdom)
    {
        var result = new List<City>();
        Empire empire = kingdom.GetEmpire();
        IEnumerable<Kingdom> members = empire?.CoreKingdom == kingdom ? empire.kingdoms_list : new[] { kingdom };
        foreach (Kingdom member in members)
        {
            if (member?.cities == null || member.isRekt()) continue;
            foreach (City city in member.cities)
                if (city?.data != null && !city.isRekt() && city.isValidTargetForWar()) result.Add(city);
        }
        return result;
    }
}
