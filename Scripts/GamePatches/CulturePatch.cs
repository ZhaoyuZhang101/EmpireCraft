using UnityEngine;
using NeoModLoader.api;
using System.Text;
using NeoModLoader.services;
using HarmonyLib;
using NeoModLoader.api.attributes;
using NeoModLoader.General;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Collections;
using System.Linq;
using System.IO;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using System.Runtime.CompilerServices;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GeneralSystems;
namespace EmpireCraft.Scripts.GamePatches;

public class CulturePatch : GamePatch
{
    public ModDeclare declare { get; set; }
    public static string ModPath;
    public void Initialize()
    {
        ModPath = declare.FolderPath + "/Locales/";
        new Harmony(nameof(set_culture_name)).Patch(AccessTools.Method(typeof(Culture), nameof(Culture.createCulture)),
            postfix: new HarmonyMethod(GetType(), nameof(set_culture_name)));
        new Harmony(nameof(set_default_culture_name)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.createDefaultCultureAndLanguageAndClan)),
            postfix: new HarmonyMethod(GetType(), nameof(set_default_culture_name)));
        new Harmony(nameof(clone_culture_name)).Patch(AccessTools.Method(typeof(Culture), nameof(Culture.cloneAndEvolveOnomastics)),
            postfix: new HarmonyMethod(GetType(), nameof(clone_culture_name)));
        GuardVanillaCultureDecisions();
        try
        {
            new Harmony(nameof(BeforeMakeOwnKingdom)).Patch(AccessTools.Method(typeof(City), "makeOwnKingdom"),
                prefix: new HarmonyMethod(typeof(CulturePatch), nameof(BeforeMakeOwnKingdom)) { priority = Priority.First },
                finalizer: new HarmonyMethod(typeof(CulturePatch), nameof(AfterMakeOwnKingdom)));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 立国取封地文化的补丁未生效: {exception.Message}");
        }
    }

    // 某城自成一国期间记下这座城(锁定文化时新国取它的官方文化，见 CultureService.ApplyFounderCulture)
    private static void BeforeMakeOwnKingdom(City __instance, out City __state)
    {
        __state = CultureService.PendingSeat;
        CultureService.PendingSeat = __instance;
    }

    private static Exception AfterMakeOwnKingdom(Exception __exception, City __state)
    {
        CultureService.PendingSeat = __state;
        return __exception;
    }

    // 原版"国王把国家文化改成自己的""城主把城市文化改成自己的"两个决策只改原版的文化对象，模组的国家文化、
    // 城市主流文化另有一套：君主与国、城文化不同(或同一文化有两个原版对象)就来回互改，锁着文化也照改，
    // 原版窗口里的文化跟着君主变。国家这一条不再走(国家文化只随易俗、立国等改)；城市这一条锁定时不走(见 CultureService.AllowLeaderCityCulture)
    private static void GuardVanillaCultureDecisions()
    {
        try
        {
            DecisionAsset king = AssetManager.decisions_library?.get("king_change_kingdom_culture");
            if (king?.action_check_launch != null)
            {
                DecisionAction original = king.action_check_launch;
                king.action_check_launch = actor =>
                {
                    if (actor?.kingdom == null) return false;
                    // 古战模组的国家照原版
                    if (actor.kingdom.culture == null || AncientWarfareCompatibility.Owns(actor.kingdom)) return original(actor);
                    // 已被这条决策改成君主文化的(旧存档)，拉回国家文化
                    CultureService.SyncVanillaKingdomCulture(actor.kingdom);
                    return false;
                };
            }
            DecisionAsset leader = AssetManager.decisions_library?.get("leader_change_city_culture");
            if (leader?.action_check_launch != null)
            {
                DecisionAction original = leader.action_check_launch;
                leader.action_check_launch = actor => CultureService.AllowLeaderCityCulture(actor) && original(actor);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 原版改文化决策的拦截未生效: {exception.Message}");
        }
    }

    private static void set_default_culture_name(Actor __instance, string pCultureName)
    {
        if (__instance?.data == null || AncientWarfareCompatibility.Owns(__instance)) return;
        EnsureEmpireNaming(__instance?.culture);
        try
        {
            if (__instance.kingdom?.data == null || __instance.city?.data == null ||
                __instance.culture?.data == null || __instance.language?.data == null) return;
            string realmCulture = CultureService.GetMainCulture(__instance.city, initialize: false);
            if (!CultureService.IsValidCulture(realmCulture))
                realmCulture = GetInjectedCultureName(__instance.culture);
            if (CultureService.IsValidCulture(realmCulture))
                CultureService.SetRealmCulture(__instance.kingdom, realmCulture, updatePoliticalSystem: false);
            OnomasticsData kingdomNames = GetOnomasticDataSafe(__instance.culture, MetaType.Kingdom);
            OnomasticsData cityNames = GetOnomasticDataSafe(__instance.culture, MetaType.City);
            if (kingdomNames == null || cityNames == null) return;
            var beforeKingdomName = __instance.kingdom.data.name;
            // 原始阶段的部落、方国国号(见 PrehistoricClanSystem)
            __instance.kingdom.data.name = PrehistoricClanSystem.AncientKingdomName(realmCulture) ??
                                           kingdomNames.generateName().UseLocalizedNameSeparator();
            __instance.kingdom.RememberInitialRandomKingdomName(__instance.kingdom.data.name, overwrite: true);
            __instance.kingdom.GetOrCreate().core_name = "";
            __instance.kingdom.GetOrCreate().core_name_source = "";
            __instance.kingdom.EnsureKingdomCoreName();
            var afterKingdomName = __instance.kingdom.data.name;
            TranslateHelper.LogChangeKingdomName(__instance, __instance.kingdom, beforeKingdomName, afterKingdomName);
            var beforeCityName = __instance.city.data.name;
            __instance.city.data.name = cityNames.generateName()
                .UseLocalizedNameSeparator();
            __instance.city.GetOrCreate().core_name = "";
            __instance.city.GetOrCreate().core_name_source = "";
            __instance.city.EnsureCityCoreName();
            var afterCityName = __instance.city.data.name;
            TranslateHelper.LogChangeCityName(__instance, __instance.city, beforeCityName, afterCityName);
            __instance.language.data.name = __instance.kingdom.GetKingdomName() + LM.Get("Language") +
                                            __instance.city.GetCityName() + LM.Get("Dialect");
            SyncCultureDisplayName(__instance.culture, realmCulture);
            __instance.culture.data.creator_city_name = __instance.city.data.name;
            // 城邦国名默认用城市名
            CityStateService.ApplyCityName(__instance.kingdom);
        }
        catch (Exception e)
        {
            LogService.LogError($"文化命名失败: {e}");
        }

    }

    private static void set_culture_name(Culture __instance, Actor pActor)
    {
        if (__instance?.data == null || pActor?.data == null) return;
        if (AncientWarfareCompatibility.Owns(pActor)) return;
        string kingdomName = pActor.kingdom?.data == null ? "" : pActor.kingdom.GetKingdomName();
        string cityName = pActor.city?.data == null ? "" : pActor.city.GetCityName();
        __instance.data.name = kingdomName + "-" + cityName + LM.Get("Culture");
        setDefaultNameTemplate(__instance, pActor);
        EnsureEmpireNaming(__instance);
        
    }
    private static void clone_culture_name(Culture __instance, Culture pFrom)
    {
        if (__instance?.data == null) return;
        // Evolution copies the parent's naming data, so it must also copy the EC
        // binding. Creator species describes ancestry, not the culture being evolved.
        string inheritedCulture = GetInjectedCultureName(pFrom);
        if (CultureService.IsValidCulture(inheritedCulture))
        {
            NamingTemplateState state = GetNamingTemplateState(__instance);
            state.empireCraftCulture = inheritedCulture;
            state.signature = null;
        }
        if (SyncCultureDisplayName(__instance)) return;
        string kingdomName = ExtractStoredCoreName(__instance.data.creator_kingdom_name);
        string cityName = ExtractStoredCoreName(__instance.data.creator_city_name);
        __instance.data.name = kingdomName + "-" + cityName + LM.Get("EvolvedCulture");
    }

    private static string ExtractStoredCoreName(string storedName)
    {
        if (string.IsNullOrWhiteSpace(storedName)) return "";
        if (OverallHelperFunc.TryExtractEnglishPrefixedCountryName(storedName, out string prefixedName))
            return prefixedName;
        string[] parts = storedName.SplitNameParts();
        return parts.Length > 0 ? parts[0] : storedName.Trim();
    }
    private static void setDefaultNameTemplate(Culture culture, Actor founder)
    {
        string insertCulture = CultureService.GetMainCulture(founder?.city, initialize: false);
        if (!CultureService.IsValidCulture(insertCulture))
            insertCulture = CultureService.GetActorCulture(founder);
        if (!CultureService.IsValidCulture(insertCulture))
            insertCulture = OverallHelperFunc.GetCultureFromSpecies(culture.data.creator_species_id);
        insertCultureTemplate(culture, insertCulture);
    }

    private sealed class NamingTemplateState
    {
        public NamingTemplateState() { }
        internal string signature;
        internal object data;
        internal string empireCraftCulture;
    }
    private static ConditionalWeakTable<Culture, NamingTemplateState> NamingTemplates = new();

    public static void ResetWorldState()
    {
        NamingTemplates = new();
    }

    private static NamingTemplateState GetNamingTemplateState(Culture culture)
    {
        NamingTemplateState state = NamingTemplates.GetOrCreateValue(culture);
        // 原版会回收复用 Culture 对象；绑定只属于当前数据，不能跟随对象进入另一份存档。
        if (!ReferenceEquals(state.data, culture.data))
        {
            state.data = culture.data;
            state.signature = null;
            state.empireCraftCulture = null;
        }
        return state;
    }

    internal static void EnsureEmpireNaming(Culture culture)
    {
        if (!AncientWarfareCompatibility.Loaded || culture?.data == null) return;
        NamingTemplateState state = GetNamingTemplateState(culture);
        string cultureName = GetInjectedCultureName(culture);
        if (!OnomasticsRule.ALL_CULTURE_RULE.ContainsKey(cultureName)) return;
        string signature = cultureName + "/" + PlayerConfig.detectLanguage();
        state.empireCraftCulture = cultureName;
        if (state.signature == signature && ReferenceEquals(state.data, culture.data)) return;
        // Repair saved/previously AW-created templates, without adding EC political traits.
        insertCultureTemplate(culture, cultureName, false);
        state.signature = signature;
        state.data = culture.data;
    }

    public static string GetInjectedCultureName(Culture culture)
    {
        if (culture?.data == null) return "";
        NamingTemplateState state = GetNamingTemplateState(culture);
        if (!string.IsNullOrWhiteSpace(state.empireCraftCulture) &&
            OnomasticsRule.ALL_CULTURE_RULE.ContainsKey(state.empireCraftCulture))
        {
            return state.empireCraftCulture;
        }

        // Legacy cultures did not persist the injected key. Recover it from the culture's
        // own creator metadata, never from the current actor's race.
        // 更早的存档没有 creator_species_id，但仍保存了文化自身的 original_actor_asset。
        string species = !string.IsNullOrWhiteSpace(culture.data.creator_species_id)
            ? culture.data.creator_species_id
            : culture.data.original_actor_asset;
        string recovered = OverallHelperFunc.GetCultureFromSpecies(species);
        if (!OnomasticsRule.ALL_CULTURE_RULE.ContainsKey(recovered)) return "";
        state.empireCraftCulture = recovered;
        return recovered;
    }

    public static bool SyncCultureDisplayName(Culture culture, string cultureName = null)
    {
        if (culture?.data == null) return false;
        if (!CultureService.IsValidCulture(cultureName)) cultureName = GetInjectedCultureName(culture);
        if (!CultureService.IsValidCulture(cultureName)) return false;

        string displayName;
        try
        {
            displayName = cultureName.GetCultureTranslate();
        }
        catch (Exception)
        {
            displayName = cultureName;
        }

        if (string.IsNullOrWhiteSpace(displayName)) return false;
        culture.data.name = displayName;
        return true;
    }

    public static Dictionary<long, string> ExportCultureBindings()
    {
        Dictionary<long, string> bindings = new();
        if (World.world?.cultures == null) return bindings;
        foreach (Culture culture in World.world.cultures)
        {
            if (culture?.data == null) continue;
            string cultureName = GetInjectedCultureName(culture);
            if (OnomasticsRule.ALL_CULTURE_RULE.ContainsKey(cultureName))
                bindings[culture.id] = cultureName;
        }
        return bindings;
    }

    public static void ImportCultureBindings(IDictionary<long, string> bindings)
    {
        if (bindings == null || World.world?.cultures == null) return;
        foreach (KeyValuePair<long, string> binding in bindings)
        {
            if (!OnomasticsRule.ALL_CULTURE_RULE.ContainsKey(binding.Value)) continue;
            Culture culture = World.world.cultures.get(binding.Key);
            if (culture?.data == null) continue;
            GetNamingTemplateState(culture).empireCraftCulture = binding.Value;
            insertCultureTemplate(culture, binding.Value, addTraits: false);
        }
    }

    internal static OnomasticsData GetOnomasticDataSafe(Culture culture, MetaType type)
    {
        if (culture?.data == null) return null;
        try
        {
            return culture.getOnomasticData(type);
        }
        catch (NullReferenceException)
        {
            return null;
        }
    }

    public static void insertCultureTemplate(Culture culture, string cultureName, bool addTraits = true)
    {
        OnomasticsData kindomData = culture.getOnomasticData(MetaType.Kingdom);
        OnomasticsData clanData = culture.getOnomasticData(MetaType.Clan);
        OnomasticsData familyData = culture.getOnomasticData(MetaType.Family);
        OnomasticsData CityData = culture.getOnomasticData(MetaType.City);
        OnomasticsData unitData = culture.getOnomasticData(MetaType.Unit);


        if (!OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(cultureName, out Setting setting))
        {
            return;
        }
        GetNamingTemplateState(culture).empireCraftCulture = cultureName;
        SyncCultureDisplayName(culture, cultureName);
        FamilySetting familySetting = setting.Family;
        UnitSetting unitSetting = setting.Unit;
        KingdomSetting kingdomSetting = setting.Kingdom;
        ClanSetting clanSetting = setting.Clan;
        CitySetting citySetting = setting.City;
        List<string> traits = setting.traits;
        foreach (string trait in addTraits ? traits : new List<string>())
        {
            if (!culture.hasTrait(trait))
            {
                culture.addTrait(trait);
            }
        }


        OnomasticsHelper.Configure(
            kindomData,
            cultureName,
            kingdomSetting.rule,
            setGroup(kingdomSetting.groups, cultureName)
            );

        OnomasticsHelper.Configure(
            familyData,
            cultureName,
            familySetting.rule,
            setGroup(familySetting.groups, cultureName)
            );

        OnomasticsHelper.Configure(
            clanData,
            cultureName,
            clanSetting.rule,
            setGroup(clanSetting.groups, cultureName)
            );

        OnomasticsHelper.Configure(
            unitData,
            cultureName,
            unitSetting.rule,
            setGroup(unitSetting.groups, cultureName)
            );

        OnomasticsHelper.Configure(
            CityData,
            cultureName,
            citySetting.rule,
            setGroup(citySetting.groups, cultureName)
            );

    }

    public static (string groupName, string CharacterSetName, string definedContent)[] setGroup(Dictionary<string, string> groupPair, string culture)
    {
        (string groupName, string CharacterSetName, string definedContent)[] groups = Array.Empty<(string groupName, string CharacterSetName, string definedContent)>();
        foreach (KeyValuePair<string, string> group in groupPair)
        {
            string key = group.Key;
            string value = group.Value;

            // 姓名、氏族名模板用 U+200A 等空白分隔；它们是字面内容，不是词库名或翻译键。
            if (string.IsNullOrWhiteSpace(value))
            {
                groups = groups.Append((key, null, value ?? "")).ToArray();
                continue;
            }

            string ModPath = Path.Combine(ModClass._declare.FolderPath, "Locales");
            string culturePath = Path.Combine(ModPath, "Cultures", $"Culture_{culture}");
            string CharacterSetPath = Path.Combine(culturePath, String.Format("{0}{1}.csv", culture, value));
            if (File.Exists(CharacterSetPath))
            {
                groups = groups.Append((key, value, null)).ToArray();
            }
            else
            {
                groups = groups.Append((key, null, LM.Get(value))).ToArray();
            }

        }
        return groups;
    }
}
