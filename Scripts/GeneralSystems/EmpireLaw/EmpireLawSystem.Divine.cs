using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems.EmpireLaw;

// 量刑天平上的神力添加(用户 2026-11-10 "量刑玩家也要能添加，比如添加罪名、添加保释条件什么的"；问答全选添加罪名、保释条件、
// 自定砝码、当场开审)：
//   · 添加罪名：给这人记一桩未发之罪(贪墨类带赃额)，年审或开审时按律论处；
//   · 保释：交保金(从他私财出 BailBond 两) ×1.25、有人担保(派系首领或父亲；是官员、宗室的 ×1.35，否则 ×1.2)、软禁在家 ×1.15、
//     戴罪立功 ×1.3，都是情面砝码；取保时在押的当即出狱；保释期间再犯(记了新罪)取保作废，罪责加"违保" ×1.5；
//     担保人死了或下了狱，担保那一项不算，罪责加"保人出事" ×1.15；开审了结后保释随之解除；
//   · 自定砝码：预设名目(民愤、上意从严、情节恶劣 / 民心所向、上意从宽、功臣之后)× 三档倍数，记在身上，开审了结后清掉；
//   · 当场开审：这人在本朝的未发之罪逐桩按律论处(走完整的量刑天平)，不等年审。
public static partial class EmpireLawSystem
{
    public const int BailBond = 200;
    public static readonly string[] CustomGuiltKeys = { "public_outrage", "imperial_harsh", "heinous" };
    public static readonly string[] CustomMercyKeys = { "public_sympathy", "imperial_mercy", "merit_lineage" };
    public static readonly float[] CustomFactors = { 1.25f, 1.5f, 2f };
    public static readonly string[] BailConditions = { "bond", "guarantor", "house", "redeem" };
    private const float BondMercy = 1.25f, GuarantorMercy = 1.2f, NotableGuarantorMercy = 1.35f, HouseMercy = 1.15f,
        RedeemMercy = 1.3f, RevokedGuilt = 1.5f, GuarantorLostGuilt = 1.15f;

    // 天平上加神力的砝码与保释(GetScale 调)
    private static void AddDivineWeights(PersonalClanIdentity identity, List<LawFactor> guilt, List<LawFactor> mercy)
    {
        if (identity == null) return;
        foreach (LawCustomWeight weight in identity.law_custom_weights ?? new List<LawCustomWeight>())
            if (weight != null && Mathf.Abs(weight.factor - 1f) > 0.001f)
                (weight.guilt ? guilt : mercy).Add(new LawFactor("law_custom_" + weight.key, weight.factor));
        LawBail bail = identity.law_bail;
        if (bail == null) return;
        if (bail.revoked)
        {
            guilt.Add(new LawFactor("law_scale_bail_revoked", RevokedGuilt));
            return;
        }
        foreach (string condition in bail.conditions ?? new List<string>())
        {
            switch (condition)
            {
                case "bond":
                    mercy.Add(new LawFactor("law_scale_bail_bond", BondMercy, bail.bond.ToString()));
                    break;
                case "guarantor":
                    PersonalClanIdentity guarantor = SpecificClanManager.getPerson(bail.guarantor);
                    if (guarantor == null || !guarantor.is_alive || IsImprisoned(guarantor))
                        guilt.Add(new LawFactor("law_scale_bail_guarantor_lost", GuarantorLostGuilt, guarantor?.name ?? ""));
                    else
                        mercy.Add(new LawFactor("law_scale_bail_guarantor", NotableGuarantor(guarantor) ? NotableGuarantorMercy : GuarantorMercy,
                            guarantor.name ?? ""));
                    break;
                case "house":
                    mercy.Add(new LawFactor("law_scale_bail_house", HouseMercy));
                    break;
                case "redeem":
                    mercy.Add(new LawFactor("law_scale_bail_redeem", RedeemMercy));
                    break;
            }
        }
    }

    private static bool NotableGuarantor(PersonalClanIdentity guarantor)
    {
        Actor actor = guarantor.actor_id >= 0 ? World.world?.units?.get(guarantor.actor_id) : null;
        return actor?.data != null && !actor.isRekt() && (actor.IsOnOffice() || IsRoyalKin(actor, actor.kingdom) || actor.isKing());
    }

    // 保释期间再犯(RecordCrime 调)：取保作废
    private static void BreakBail(Actor actor, PersonalClanIdentity identity)
    {
        if (identity?.law_bail == null || identity.law_bail.revoked) return;
        identity.law_bail.revoked = true;
        if (actor?.kingdom != null)
            TranslateHelper.LogEventOnly(string.Format(LM.Get("law_bail_revoked_log"), actor.getName()), actor.kingdom);
    }

    #region 神力

    // 未发之罪(本朝管得着、还没了结的)
    public static List<CrimeRecord> PendingCrimes(Actor actor)
    {
        PersonalClanIdentity identity = actor?.GetPersonalIdentity();
        Kingdom kingdom = actor?.kingdom;
        if (identity?.crime_records == null || kingdom == null) return new List<CrimeRecord>();
        return identity.crime_records.Where(record => record != null && !record.resolved && InJurisdiction(record, kingdom)).ToList();
    }

    // 记得上的罪名：本朝律里有的(过于强大那一条另有专门的判法，不在此列)
    public static List<LawType> AddableCrimes(Actor actor)
    {
        Kingdom kingdom = actor?.kingdom;
        if (kingdom == null || !CanEnforceLawInEmpireScope(actor, kingdom)) return new List<LawType>();
        return Enum.GetValues(typeof(LawType)).Cast<LawType>().Where(type => type != LawType.过于强大 && kingdom.HasLaw(type)).ToList();
    }

    public static int DefaultLoot(LawType type) => type is LawType.贪污 or LawType.受贿 or LawType.盗窃 or LawType.诈骗 ? 300 : 0;

    public static bool GodAddCrime(Actor actor, LawType type)
    {
        if (actor?.data == null || actor.isRekt() || !AddableCrimes(actor).Contains(type)) return false;
        actor.RecordCrime(type, DefaultLoot(type));
        return PendingCrimes(actor).Any(record => record.law_type == (int)type);
    }

    // 担保人：本派首领，没有就父亲(都得在世、不在押、不是本人)
    public static PersonalClanIdentity FindGuarantor(Actor actor)
    {
        PersonalClanIdentity self = actor?.GetPersonalIdentity();
        Actor leader = actor?.GetFaction()?.GetLeader();
        PersonalClanIdentity candidate = leader != null && leader != actor && !leader.isRekt() ? leader.GetPersonalIdentity() : null;
        if (candidate == null || !candidate.is_alive || IsImprisoned(candidate))
        {
            candidate = self != null && self.father >= 0 ? SpecificClanManager.getPerson(self.father) : null;
            if (candidate == null || !candidate.is_alive || IsImprisoned(candidate)) return null;
        }
        return candidate == self ? null : candidate;
    }

    // 保释条件为什么加不上(null 为加得上)
    public static string CannotBail(Actor actor, string condition)
    {
        PersonalClanIdentity identity = actor?.GetPersonalIdentity();
        if (identity == null) return "law_divine_no_identity";
        if (identity.law_bail?.revoked == true) return "law_divine_bail_revoked";
        return condition switch
        {
            "bond" => actor.money < BailBond ? "law_divine_bail_no_money" : null,
            "guarantor" => FindGuarantor(actor) == null ? "law_divine_bail_no_guarantor" : null,
            _ => null
        };
    }

    // 加或去掉一项保释条件；加上时在押的当即出狱
    public static bool GodBail(Actor actor, string condition)
    {
        PersonalClanIdentity identity = actor?.GetPersonalIdentity();
        if (identity == null || !BailConditions.Contains(condition)) return false;
        LawBail bail = identity.law_bail;
        if (bail?.conditions != null && bail.conditions.Contains(condition))
        {
            bail.conditions.Remove(condition);
            if (bail.conditions.Count == 0 && !bail.revoked) identity.law_bail = null;
            return true;
        }
        if (CannotBail(actor, condition) != null) return false;
        identity.law_bail = bail ??= new LawBail { since = World.world?.getCurWorldTime() ?? 0d };
        bail.conditions ??= new List<string>();
        switch (condition)
        {
            case "bond":
                actor.addMoney(-BailBond);
                bail.bond = BailBond;
                break;
            case "guarantor":
                bail.guarantor = FindGuarantor(actor)?.id ?? -1L;
                break;
        }
        bail.conditions.Add(condition);
        if (IsImprisoned(identity))
        {
            identity.law_imprisonment = null;
            if (actor.kingdom != null)
                TranslateHelper.LogEventOnly(string.Format(LM.Get("law_bail_released_log"), actor.getName()), actor.kingdom);
        }
        return true;
    }

    public static void GodRevokeBail(Actor actor)
    {
        PersonalClanIdentity identity = actor?.GetPersonalIdentity();
        if (identity != null) identity.law_bail = null;
    }

    // 自定砝码：同名目点同一档去掉，点别的档换档
    public static void GodCustomWeight(Actor actor, string key, bool guilt, float factor)
    {
        PersonalClanIdentity identity = actor?.GetPersonalIdentity();
        if (identity == null) return;
        identity.law_custom_weights ??= new List<LawCustomWeight>();
        LawCustomWeight existing = identity.law_custom_weights.FirstOrDefault(weight => weight.key == key);
        if (existing != null && Mathf.Abs(existing.factor - factor) < 0.001f)
        {
            identity.law_custom_weights.Remove(existing);
            return;
        }
        if (existing == null) identity.law_custom_weights.Add(new LawCustomWeight { key = key, guilt = guilt, factor = factor });
        else existing.factor = factor;
    }

    public static float CustomWeight(Actor actor, string key) =>
        actor?.GetPersonalIdentity()?.law_custom_weights?.FirstOrDefault(weight => weight.key == key)?.factor ?? 1f;

    // 当场开审：未发之罪逐桩按律论处；了结后保释与自定砝码清掉。返回审了几桩
    public static int GodTrial(Actor actor)
    {
        if (actor?.data == null || actor.isRekt() || !actor.isAlive()) return 0;
        Kingdom kingdom = actor.kingdom;
        PersonalClanIdentity identity = actor.GetPersonalIdentity();
        int tried = 0;
        foreach (CrimeRecord record in PendingCrimes(actor))
        {
            if (actor.isRekt() || !actor.isAlive()) break;
            if (!CanResolveCrimeRecord(actor, kingdom, record)) continue;
            record.discovered = true;
            record.discovered_timestamp = World.world.getCurWorldTime();
            record.discovered_date = Date.getDate(record.discovered_timestamp);
            LawEnforcementContext context = TryEnforceLaw(actor, (LawType)record.law_type, kingdom, ApplyRecordedOptionalPunishments,
                record.crime_date, record.amount);
            if (context == null) continue;
            record.resolved = true;
            tried++;
        }
        if (tried > 0 && identity != null)
        {
            identity.law_bail = null;
            identity.law_custom_weights = null;
            if (kingdom != null)
                TranslateHelper.LogEventOnly(string.Format(LM.Get("law_divine_trial_log"), actor.getName(), tried), kingdom);
        }
        return tried;
    }

    #endregion
}
