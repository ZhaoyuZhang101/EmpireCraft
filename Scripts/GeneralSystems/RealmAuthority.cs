using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 君威：独立王国/部落推制度改革时代替正统(帝国的正统见 MonarchyLegitimacy)。算式见 RealmAuthorityRules；
// 这里只取已结算的数(国库、腐败、苛政快照、君主资质与声望、天命、战争)，不扫人口，界面频繁读也不卡
public static class RealmAuthority
{
    private const float CacheSeconds = 1f;
    private static readonly Dictionary<long, (int value, float at)> Cache = new();
    private static object _cacheWorld;

    public static int Get(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || World.world == null) return 0;
        if (!ReferenceEquals(_cacheWorld, World.world))
        {
            _cacheWorld = World.world;
            Cache.Clear();
        }
        float now = Time.unscaledTime;
        if (Cache.TryGetValue(kingdom.id, out var cached) && now >= cached.at && now - cached.at < CacheSeconds)
            return cached.value;
        int value = RealmAuthorityRules.Clamp(Breakdown(kingdom).Sum(item => item.value));
        Cache[kingdom.id] = (value, now);
        return value;
    }

    // 君威的各项来源(界面悬停明细用)；键复用正统明细里已有的说法，君威独有的另起
    public static List<(string key, int value)> Breakdown(Kingdom kingdom)
    {
        var items = new List<(string key, int value)>();
        if (kingdom?.data == null || kingdom.isRekt()) return items;
        items.Add(("realm_authority_base", RealmAuthorityRules.Base));
        Actor ruler = kingdom.king;
        bool reigning = ruler != null && !ruler.isRekt() && ruler.isAlive();
        items.Add(reigning ? RulerQualitySystem.LegitimacyItem(ruler) : ("mandate_interregnum", RealmAuthorityRules.Interregnum));
        int treasury = RealmAuthorityRules.Treasury(kingdom.GetTreasuryBalance());
        if (treasury != 0) items.Add((treasury > 0 ? "mandate_solvent_treasury" : "mandate_debt", treasury));
        int corruption = RealmAuthorityRules.Corruption(CorruptionSystem.GetRate(kingdom));
        if (corruption != 0) items.Add(("mandate_corruption", corruption));
        int misrule = RealmAuthorityRules.Misrule(HarshRuleSystem.GetBurden(kingdom));
        if (misrule != 0) items.Add(("mandate_misrule", misrule));
        int prestige = reigning ? RealmAuthorityRules.Prestige(ruler.data?.renown ?? 0) : 0;
        if (prestige != 0) items.Add(("realm_authority_prestige", prestige));
        // 受着某个天下的天命(天下共主，见 TianxiaSystem)
        if (TianxiaSystem.MandateOf(kingdom) != null) items.Add(("mandate_tianxia_holder", RealmAuthorityRules.MandateHolder));
        if (InRebellion(kingdom)) items.Add(("realm_authority_rebellion", RealmAuthorityRules.Rebellion));
        return items;
    }

    // 叛乱未平：本国正在起兵(派系、地方叛乱)，或正被叛军攻打
    public static bool InRebellion(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return false;
        if (kingdom.IsFactionRebelling() || kingdom.IsLocalRebelling()) return true;
        foreach (War war in kingdom.getWars())
            if (war != null && !war.hasEnded() && war.main_defender == kingdom && MusterSystem.IsRebellion(war))
                return true;
        return false;
    }

    public static void Invalidate(Kingdom kingdom = null)
    {
        if (kingdom == null) Cache.Clear();
        else Cache.Remove(kingdom.id);
    }
}
