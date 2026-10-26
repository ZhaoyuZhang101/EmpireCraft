using HarmonyLib;
using System;
using NeoModLoader.services;
using NeoModLoader.General;
using NeoModLoader.api;
using System.Text.RegularExpressions;
using System.Collections;
using EmpireCraft.Scripts.HelperFunc;
using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;

namespace EmpireCraft.Scripts.GamePatches;
public class ReligionPatch : GamePatch
{
    public ModDeclare declare { get; set; }
    public static string ModPath;
    public void Initialize()
    {
        ModPath = declare.FolderPath + "/Locales/";
        new Harmony(nameof(set_religion_name)).Patch(AccessTools.Method(typeof(Religion), nameof(Religion.newReligion)),
        postfix: new HarmonyMethod(GetType(), nameof(set_religion_name)));
        GuardVanillaReligionDecisions();
        LogService.LogInfo("宗教Patch加载成功");
    }

    // 原版"国王把国教改成自己的""城主把城的宗教改成自己的"两个决策：国教就跟着君主私人信仰跑，新城主一到整城改宗，
    // 模组的改宗(背景人口传教、统治者皈依、宗教同化)全被冲掉。国教不再随君主私人信仰改——有国教的，君主改奉国教
    // (君主皈依另见 ReligionConversionSystem)；无小人模式下城的宗教是城中背景人口所信，城主不改
    private static void GuardVanillaReligionDecisions()
    {
        try
        {
            DecisionAsset king = AssetManager.decisions_library?.get("king_change_kingdom_religion");
            if (king?.action_check_launch != null)
            {
                DecisionAction original = king.action_check_launch;
                king.action_check_launch = actor =>
                {
                    Kingdom kingdom = actor?.kingdom;
                    if (kingdom == null) return false;
                    if (!kingdom.hasReligion() || EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(kingdom))
                        return original(actor);
                    if (actor.isAlive() && actor.religion != kingdom.religion) actor.setReligion(kingdom.religion);
                    return false;
                };
            }
            DecisionAsset leader = AssetManager.decisions_library?.get("leader_change_city_religion");
            if (leader?.action_check_launch != null)
            {
                DecisionAction original = leader.action_check_launch;
                leader.action_check_launch = actor =>
                    (!BackgroundReligionSystem.Active || actor?.city == null || !actor.city.hasReligion() ||
                     EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(actor.city)) && original(actor);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft] 拦截原版宗教决策失败: {exception.Message}");
        }
    }

    private static void set_religion_name(Religion __instance, Actor pActor, WorldTile pTile, bool pAddDefaultTraits)
    {
        if (__instance?.data == null) return;
        string culture = CultureService.GetMainCulture(pActor?.city, initialize: false);
        if (!CultureService.IsValidCulture(culture)) culture = CultureService.GetActorCulture(pActor);
        LogService.LogInfo("当前模组文化: " + culture);
        if (CultureService.IsValidCulture(culture))
        {
            // 宗教类型按创教者文化定(见 ReligionFaith)；有名库的类型从名库取名，多神民间用本文化名库
            FaithType faith = ReligionFaith.OfCulture(culture);
            ReligionFaith.Record(__instance, faith);
            string faithName = ReligionFaith.PickName(faith);
            if (faithName != null) __instance.data.name = faithName;
            else InsertReligionNameTemplate(__instance, culture);
            ReligionSphere.Record(__instance, culture);
        }
        City birthplace = pActor?.city ?? pActor?.current_tile?.zone_city;
        if (pActor != null && birthplace?.data != null)
            ReligionChronicle.Record(__instance, ReligionChronicle.KindFounding, string.Format(LM.Get("religion_founded_log"),
                pActor.getName(), birthplace.GetCityName(), __instance.data.name));
        else
        {
            InsertReligionNameTemplate(__instance, "Western");
        }
        __instance.SetCity(pActor?.city??pActor?.current_tile?.zone_city);

    }
    public static void InsertReligionNameTemplate(Religion religion, string cultureName)
    {
        if (OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(cultureName, out var setting))
        {
            if (!string.IsNullOrEmpty(setting.Religion))
            {
                var nameSet = setting.Religion.Split(' ');
                religion.data.name = nameSet.GetRandom();
                LogService.LogInfo(cultureName + "宗教名称: " + religion.data.name);
                return;
            }
        }
        string culturePath = ModPath + $"Cultures/Culture_{cultureName}/";
        string religionNamePath = culturePath + $"{cultureName}ReligionNames.csv";
        List<string> religionKeys = OnomasticsHelper.getKeysFromPath(religionNamePath);
        religion.data.name = LM.Get(religionKeys[UnityEngine.Random.Range(0, religionKeys.Count)]);
        LogService.LogInfo(cultureName + "宗教名称: " + religion.data.name);
    }
}
