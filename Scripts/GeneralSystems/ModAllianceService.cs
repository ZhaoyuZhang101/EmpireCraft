using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;

namespace EmpireCraft.Scripts.GeneralSystems;

// 模组同盟的标记：共主联盟、城邦同盟、反帝同盟等由模组机制建立的同盟记在 ModClass.MOD_ALLIANCE_IDS 里(随存档保存)。
// 世界规则"禁止原版结盟"打开时，解散所有未标记的同盟；玩家用神力强制结成的同盟也保留。
public static class ModAllianceService
{
    public static void Mark(Alliance alliance)
    {
        if (alliance == null || !alliance.isAlive()) return;
        ModClass.MOD_ALLIANCE_IDS.Add(alliance.id);
    }

    public static bool IsModAlliance(Alliance alliance) =>
        alliance != null && ModClass.MOD_ALLIANCE_IDS.Contains(alliance.id);

    public static bool IsVanillaAllianceBanned() =>
        EmpireCraftWorldLawLibrary.empirecraft_law_ban_vanilla_alliance?.isEnabled() == true;

    // 旧存档里模组同盟还没有标记：成员中有两国同属一位君主的(共主联盟、城邦同盟)视为模组同盟并补上标记
    private static bool LooksLikeUnionAlliance(Alliance alliance)
    {
        List<Actor> kings = alliance.kingdoms_hashset
            .Where(kingdom => kingdom != null && kingdom.isAlive() && kingdom.hasKing())
            .Select(kingdom => kingdom.king).ToList();
        return kings.Count != kings.Distinct().Count();
    }

    // 帝国与同盟不兼容：帝国本身及其成员国一律退出同盟(同盟剩不足两国时原版会自行解散)
    public static void LeaveAllianceForEmpire(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt() || !kingdom.hasAlliance()) return;
        kingdom.getAlliance().leave(kingdom);
    }

    // 定期调用：清掉已在帝国里(或本身是帝国)的同盟成员
    public static void RemoveEmpireMembersFromAlliances()
    {
        if (World.world?.alliances == null) return;
        foreach (Alliance alliance in World.world.alliances.list.ToList())
        {
            if (alliance == null || !alliance.isAlive()) continue;
            // 同盟里有国家成了帝国：整个同盟解散(与称帝时的处理一致)
            if (alliance.kingdoms_hashset.Any(member => member != null && !member.isRekt() && member.IsEmpire()))
            {
                World.world.alliances.dissolveAlliance(alliance);
                continue;
            }
            foreach (Kingdom member in alliance.kingdoms_hashset.ToList())
                if (member != null && !member.isRekt() && member.IsInEmpire())
                    alliance.leave(member);
        }
    }

    // 世界规则打开时调用：解散原版同盟
    public static void DissolveVanillaAlliances()
    {
        if (World.world?.alliances == null) return;
        foreach (Alliance alliance in World.world.alliances.list.ToList())
        {
            if (alliance == null || !alliance.isAlive() || alliance.isForcedType()) continue;
            if (IsModAlliance(alliance)) continue;
            if (LooksLikeUnionAlliance(alliance))
            {
                Mark(alliance);
                continue;
            }
            World.world.alliances.dissolveAlliance(alliance);
        }
        ModClass.MOD_ALLIANCE_IDS.RemoveWhere(id => World.world.alliances.get(id) == null);
    }
}
