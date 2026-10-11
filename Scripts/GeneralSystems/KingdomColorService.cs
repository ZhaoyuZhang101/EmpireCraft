using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Compatibility;

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
