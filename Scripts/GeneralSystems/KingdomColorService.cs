using EmpireCraft.Scripts.Compatibility;

namespace EmpireCraft.Scripts.GeneralSystems;

public static class KingdomColorService
{
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
