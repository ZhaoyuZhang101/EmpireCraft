using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.Layer;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

public static class KingdomColorService
{
    // 周制(分封)：帝国(天子)用核心王国的颜色，诸侯各用各的颜色——不与天子同色，也尽量不与别的诸侯同色。
    // 分封出来的诸侯原先沿用宗主颜色，满图一片同色看不出列国
    public static bool KeepsOwnColor(Kingdom member)
    {
        var empire = member?.GetEmpire();
        Kingdom core = empire?.CoreKingdom;
        return core != null && core != member && core.GetRegime()?.type == Regimes.RegimeType.ZhouFeudalism;
    }

    public static void EnsureDistinctInZhou(Kingdom member)
    {
        if (!KeepsOwnColor(member) || AncientWarfareCompatibility.Owns(member)) return;
        // 周室：整个周室同一色系(见 ApplySphereFamily)；不是周室的分封帝国才走下面的"各不相同"
        if (ZhouEraSystem.Applies(member.GetEmpire()))
        {
            ApplySphereFamily(member.GetEmpire());
            return;
        }
        var empire = member.GetEmpire();
        Kingdom core = empire.CoreKingdom;
        ColorAsset current = member.getColor();
        bool clash = current == null || current == core.getColor() || current == empire.getColor();
        if (!clash)
            foreach (Kingdom other in empire.kingdoms_list)
                if (other != null && other != member && !other.isRekt() && other.getColor() == current &&
                    other.id < member.id)
                {
                    clash = true;
                    break;
                }
        if (!clash) return;
        var library = member.getColorLibrary();
        var colors = library?.list;
        if (colors == null || colors.Count == 0) return;
        var used = new global::System.Collections.Generic.HashSet<ColorAsset> { core.getColor(), empire.getColor() };
        foreach (Kingdom other in empire.kingdoms_list)
            if (other != null && other != member && !other.isRekt()) used.Add(other.getColor());
        ColorAsset next = null;
        int original = member.data.original_color_id;
        if (original >= 0 && original < colors.Count && colors[original] != null && !used.Contains(colors[original]))
            next = colors[original];
        if (next == null)
            foreach (ColorAsset candidate in colors)
                if (candidate != null && !used.Contains(candidate))
                {
                    next = candidate;
                    break;
                }
        // 颜色用尽：至少与天子不同色
        if (next == null)
            foreach (ColorAsset candidate in colors)
                if (candidate != null && candidate != core.getColor() && candidate != empire.getColor())
                {
                    next = candidate;
                    break;
                }
        if (next == null) return;
        int nextId = colors.IndexOf(next);
        member._cached_color = null;
        member.data.setColorID(nextId);
        member.updateColor(next);
        World.world?.zone_calculator?.dirtyAndClear();
    }

    private const float GraySaturation = 0.18f, DarkValue = 0.22f;

    // 同一周室同一色系(内部同一大色系、细微差别，像暖色系、冷色系，一眼看出是一家)：
    //   以天子的颜色为基准，诸侯(含战国离开帝国的)按色相离基准由近到远，依次取色板里还没被占用的颜色——
    //   诸侯少时都挤在基准色附近，多了才往两侧铺开；灰白与很暗的颜色看不出色相，除非天子本身是这类颜色，否则排到最后；
    //   别的周室(王畿与诸侯)在用的颜色不取，免得两个周室混成一片。
    //   已经在本色系里的诸侯不动，只给新来的、或色系外的换色，不会每月闪色。
    // 部落联盟(用户 2026-10-17 "共事的时候应该和诸侯国一样用统一色系")：共事同一共主的部落照此以共主的颜色为基准取同一色系；
    //   别的周室、别的部落联盟在用的颜色都不取。
    public static void ApplySphereFamily(Empire sphere)
    {
        if (!ZhouEraSystem.Applies(sphere)) return;
        ApplyFamily(sphere, ZhouEraSystem.Vassals(sphere));
    }

    // 每月(TribalFissionService.EnforceOneCity)：各部落联盟
    public static void ApplyTribalFamilies()
    {
        foreach (Empire confederation in TribalFissionService.Confederations())
            ApplyFamily(confederation, TribalFissionService.VassalsOf(confederation));
    }

    // 一家(周室或部落联盟)：基准帝国与它的诸侯、共事的部落
    private static IEnumerable<(Empire head, IEnumerable<Kingdom> members)> Families()
    {
        foreach (Empire empire in ModClass.EMPIRE_MANAGER?.ToList() ?? new List<Empire>())
        {
            if (empire?.CoreKingdom == null) continue;
            if (ZhouEraSystem.Applies(empire)) yield return (empire, ZhouEraSystem.Vassals(empire));
        }
        foreach (Empire confederation in TribalFissionService.Confederations())
            yield return (confederation, TribalFissionService.VassalsOf(confederation));
    }

    private static void ApplyFamily(Empire sphere, IEnumerable<Kingdom> members)
    {
        Kingdom core = sphere?.CoreKingdom;
        if (core?.data == null || core.isRekt() || AncientWarfareCompatibility.Owns(core)) return;
        List<Kingdom> vassals = members
            .Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && kingdom != core && !AncientWarfareCompatibility.Owns(kingdom))
            .OrderBy(kingdom => kingdom.id).ToList();
        if (vassals.Count == 0) return;
        List<ColorAsset> colors = core.getColorLibrary()?.list;
        ColorAsset royal = sphere.getColor() ?? core.getColor();
        if (colors == null || colors.Count == 0 || royal == null) return;
        Color.RGBToHSV(royal.getColorMain(), out float hue, out float saturation, out float value);
        bool grayBase = saturation < GraySaturation || value < DarkValue;
        var excluded = new HashSet<ColorAsset> { royal, core.getColor() };
        foreach ((Empire other, IEnumerable<Kingdom> otherMembers) in Families())
        {
            if (other == sphere) continue;
            excluded.Add(other.getColor());
            excluded.Add(other.CoreKingdom.getColor());
            foreach (Kingdom kingdom in otherMembers) excluded.Add(kingdom?.getColor());
        }
        List<ColorAsset> family = colors.Where(color => color != null && !excluded.Contains(color))
            .OrderBy(color => Distance(color, hue, grayBase)).Take(vassals.Count).ToList();
        var palette = new HashSet<ColorAsset>(family);
        var kept = new HashSet<ColorAsset>();
        var pending = new List<Kingdom>();
        foreach (Kingdom vassal in vassals)
        {
            ColorAsset current = vassal.getColor();
            if (current != null && palette.Contains(current) && kept.Add(current)) continue;
            pending.Add(vassal);
        }
        bool changed = false;
        foreach (Kingdom vassal in pending)
        {
            ColorAsset next = family.FirstOrDefault(color => !kept.Contains(color));
            if (next == null) break;
            kept.Add(next);
            vassal._cached_color = null;
            vassal.data.setColorID(colors.IndexOf(next));
            vassal.updateColor(next);
            changed = true;
        }
        if (changed) World.world?.zone_calculator?.dirtyAndClear();
    }

    // 与基准色相的距离(0~0.5，按色环取近的一侧)；灰白黑排在最后(天子是灰色系时反过来)
    private static float Distance(ColorAsset color, float hue, bool grayBase)
    {
        Color.RGBToHSV(color.getColorMain(), out float h, out float s, out float v);
        bool gray = s < GraySaturation || v < DarkValue;
        if (grayBase) return gray ? s : 1f + s;
        if (gray) return 2f;
        float d = Mathf.Abs(h - hue);
        return Mathf.Min(d, 1f - d);
    }

    // Native generateColor changes data.color_id without clearing _cached_color.
    // Resolve from persisted IDs and use updateColor so banners and map readers agree.
    public static void Restore(Kingdom kingdom, int preferredColorId = -1,
        ColorAsset formerOverlordColor = null, bool useOriginalColor = true)
    {
        if (kingdom?.data == null || kingdom.isRekt() || AncientWarfareCompatibility.Owns(kingdom)) return;
        var library = kingdom.getColorLibrary();
        var colors = library?.list;
        if (colors == null || colors.Count == 0) return;
        int index = preferredColorId;
        if (useOriginalColor && (index < 0 || index >= colors.Count || colors[index] == null))
            index = kingdom.data.original_color_id;
        ColorAsset next = index >= 0 && index < colors.Count ? colors[index] : null;
        if (next == null || next == formerOverlordColor)
        {
            next = library.getNextColor(kingdom.getActorAsset());
            if (next == null || next == formerOverlordColor)
            {
                next = null;
                foreach (ColorAsset candidate in colors)
                    if (candidate != null && candidate != formerOverlordColor)
                    {
                        next = candidate;
                        break;
                    }
            }
        }
        if (next == null) return;
        int nextId = colors.IndexOf(next);
        if (nextId < 0) return;
        bool changed = kingdom.data.color_id != nextId ||
                       kingdom._cached_color != null && kingdom._cached_color != next;
        // Also repair a legacy mismatch where the cached color already equals the target,
        // but the persisted color ID points elsewhere (updateColor would early-return).
        kingdom._cached_color = null;
        if (kingdom.data.color_id < 0 || kingdom.data.color_id >= colors.Count)
            kingdom.data.setColorID(nextId);
        kingdom.updateColor(next);
        if (kingdom.data.original_color_id < 0 || kingdom.data.original_color_id >= colors.Count)
            kingdom.data.original_color_id = nextId;
        if (changed) World.world?.zone_calculator?.dirtyAndClear();
    }
}
