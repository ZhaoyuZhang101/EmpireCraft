using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 本族自行定居分封与主动采用西方封建制度是两条路径。后者才有教廷、选帝侯、教会诸侯等配套。
public static class NomadFeudalismSystem
{
    private const string WesternNode = "western_feudalization";

    private static bool HasWesternInstitutions(Kingdom kingdom)
    {
        InstitutionRealm realm = InstitutionRealm.Of(kingdom);
        return realm?.State?.applied_node_ids?.Contains(WesternNode) == true ||
               InstitutionSystem.IsEnacted(realm?.Culture ?? CultureService.GetRealmCulture(kingdom), WesternNode);
    }

    public static RegimeType FeudalizationTarget(Kingdom kingdom) =>
        HasWesternInstitutions(kingdom) ? RegimeType.Feudalism : RegimeType.NomadFeudalism;

    // 只修明确记为水土出路的旧版改制；真正采纳外制、非游牧文化及复合帝国不动。
    public static bool NeedsLegacyRepair(Kingdom kingdom) =>
        kingdom?.data != null && !kingdom.isRekt() && !AncientWarfareCompatibility.Owns(kingdom) &&
        kingdom.GetRegime()?.type == RegimeType.Feudalism &&
        kingdom.GetOrCreate().shuitu_regime == (int)RegimeType.Feudalism &&
        CultureService.GetRealmCulture(kingdom).IsNomadCulture() && !HasWesternInstitutions(kingdom) &&
        !RepublicSystem.IsRegimeLocked(kingdom) && !CompositeEmpireService.IsComposite(kingdom.GetEmpire());

    public static void RepairAfterLoad()
    {
        if (World.world?.kingdoms == null || RegimeManager.GetTemplate(RegimeType.NomadFeudalism) == null) return;
        foreach (Kingdom kingdom in World.world.kingdoms.ToList())
        {
            if (!NeedsLegacyRepair(kingdom)) continue;
            try
            {
                Repair(kingdom);
            }
            catch (Exception exception)
            {
                LogService.LogError($"[EmpireCraft][汗国分封] 修复 {kingdom.id} 失败: {exception}");
            }
        }
    }

    private static void Repair(Kingdom kingdom)
    {
        Regime previous = kingdom.GetRegime();
        List<FixedFaction> factions = previous.GetPlayerFactions().Where(faction => faction != null).ToList();
        var ratios = new Dictionary<FixedFaction, int>(kingdom.GetFactionRatio());
        var options = previous.options.ToDictionary(pair => pair.Key, pair => pair.Value[0]);
        kingdom.SetRegimeType(RegimeType.NomadFeudalism);
        kingdom.LoadRegime();
        Regime current = kingdom.GetRegime();
        if (current?.type != RegimeType.NomadFeudalism) return;
        // 旧默认派系改成本族对应势力；自定义名字、手选理念、成员、领袖、ID 和中央占比保留。
        foreach (FixedFaction faction in factions)
        {
            FixedFaction oldTemplate = previous.Factions?.FirstOrDefault(template => template.Type == faction.Type);
            FactionType? type = faction.Type switch
            {
                FactionType.中央 => FactionType.尊王,
                FactionType.自治 => FactionType.诸侯,
                FactionType.神权 => FactionType.融入,
                _ => null
            };
            FixedFaction replacement = type.HasValue ? current.Factions?.FirstOrDefault(template => template.Type == type) : null;
            if (oldTemplate == null || replacement == null || IdeologyFamilies.HasExplicitIdeology(faction) ||
                (faction.NameMode != FactionEditor.NameCustom && faction.NameMode != FactionEditor.NameDefault) ||
                (faction.Name != oldTemplate.Name && faction.Name != LM.Get(oldTemplate.Name))) continue;
            faction.Type = replacement.Type;
            faction.Name = replacement.Name;
            faction._requiredTraits = replacement._requiredTraits.ToList();
            faction.RequiredTraits = replacement._requiredTraits.ToList();
            faction.ClassAffinities.Clear();
            faction.TemporaryFactionTypesRecord = (faction.TemporaryFactionTypes ?? new()).ToList();
            faction.TemporaryFactions = new();
            faction.ClaimCatalogVersion = FixedFaction.CurrentClaimCatalogVersion;
            faction.FixMissedTemporaryFactions();
        }
        current.PlayerFactions = factions;
        foreach (var pair in options)
            if (current.options.TryGetValue(pair.Key, out int[] option) && option.Length > 0)
                option[0] = pair.Value;
        // 汗国分封的税级只有"无/低"两档(见 Regime.MaxTaxLevel)：旧封建制带过来的中、高压回"低"
        current.ClampTaxLevel();
        kingdom.GetOrCreate().FactionRatio = ratios;
        kingdom.ReconcileFactionRatios(factions);
        kingdom.GetOrCreate().shuitu_regime = (int)RegimeType.NomadFeudalism;
        kingdom.GetOrCreate().last_kingdom_status_ts = -1d;
        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
        if (kingdom.IsEmpire()) kingdom.GetEmpire()?.data?.centerOffice?.Init(kingdom);
        LogService.LogInfo($"[EmpireCraft][汗国分封] {kingdom.GetKingdomName()} 的旧水土封建化已改成本族分封制度");
    }
}
