using System.Collections.Generic;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.System;
using HarmonyLib;
using NeoModLoader.api;

namespace EmpireCraft.Scripts.GeneralSystems;

// 近亲不婚：直系血亲(祖孙、父女……任意代)与三代以内的旁系血亲(同一祖父母之下：兄弟姐妹、
// 同父异母/同母异父、叔伯姑舅与侄甥、堂表兄妹)不结为夫妻、不纳为妾。
// 按模组族谱里每个人记下的父母推算；原版只认同一户、父母子女，分了家的兄弟姐妹、堂表亲都拦不住。
// 用在原版恋爱(Actor.canFallInLoveWith)、纳妾(EmpireCraftActorAddLover)、天子选后妃(OfficeSelector.TryGetHarem)
public static class KinshipRules
{
    private const int DirectDepth = 6;
    private const int CollateralDepth = 2;

    public static bool AreCloseKin(Actor a, Actor b)
    {
        if (a == null || b == null) return false;
        if (a == b) return true;
        if (a.isParentOf(b) || b.isParentOf(a)) return true;
        return AreCloseKin(a.GetPersonalIdentity(), b.GetPersonalIdentity());
    }

    public static bool AreCloseKin(PersonalClanIdentity a, PersonalClanIdentity b)
    {
        if (a == null || b == null) return false;
        if (a.id == b.id) return true;
        Dictionary<long, int> up = Ancestors(a);
        if (up.ContainsKey(b.id)) return true;
        Dictionary<long, int> other = Ancestors(b);
        if (other.ContainsKey(a.id)) return true;
        foreach (KeyValuePair<long, int> pair in up)
            if (pair.Value <= CollateralDepth && other.TryGetValue(pair.Key, out int depth) && depth <= CollateralDepth)
                return true;
        return false;
    }

    // 祖先 → 隔了几代(父母为 1)
    private static Dictionary<long, int> Ancestors(PersonalClanIdentity person)
    {
        var result = new Dictionary<long, int>();
        var frontier = new List<PersonalClanIdentity> { person };
        for (int depth = 1; depth <= DirectDepth && frontier.Count > 0; depth++)
        {
            var next = new List<PersonalClanIdentity>();
            foreach (PersonalClanIdentity current in frontier)
            {
                Add(current.father, depth, result, next);
                Add(current.mother, depth, result, next);
            }
            frontier = next;
        }
        return result;
    }

    private static void Add(long id, int depth, Dictionary<long, int> result, List<PersonalClanIdentity> next)
    {
        if (id < 0 || result.ContainsKey(id)) return;
        result[id] = depth;
        PersonalClanIdentity parent = SpecificClanManager.getPerson(id);
        if (parent != null) next.Add(parent);
    }
}

public class KinshipPatch : GamePatches.GamePatch
{
    public ModDeclare declare { get; set; }

    public void Initialize()
    {
        new Harmony(nameof(KinshipPatch)).Patch(AccessTools.Method(typeof(Actor), nameof(Actor.canFallInLoveWith)),
            postfix: new HarmonyMethod(typeof(KinshipPatch), nameof(AfterCanFallInLove)));
    }

    public static void AfterCanFallInLove(Actor __instance, Actor pTarget, ref bool __result)
    {
        if (__result && KinshipRules.AreCloseKin(__instance, pTarget)) __result = false;
    }
}
