using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 叛乱初起的集结期：叛军先动手，被叛的一方要先集结大军，叛军有机会在大军到来前打下一个省。
//   · 叛乱类战争(原版起义、派系/地方/民族/宗教叛乱、地方独立、附庸独立、清君侧)开打时掷定集结期 0~5 个月：
//     基础 0~3 个月(按战争定，存档稳定)；叛军都城离被叛方都城远(超过 120 格)+1；帝国正统不足 40 +1；
//     被叛方已在举国总动员 -1；
//   · 集结期间，被叛方对这伙叛军：不反攻(不分兵去打叛军占的城)；只有当时就在受攻城附近(六块地块以内)的军能赶来守城，
//     远处的军还在集结；仓促应战，守方战力 ×0.75；
//   · 开打与集结完毕各在战线纪事里记一笔；集结期满后照常平叛。
// 叛军是攻方、被叛方是守方(起义与各类叛乱都是叛军宣战)。
public static class MusterSystem
{
    public const int LocalZones = 6;
    private const float SurprisedDefense = 0.75f;
    private const int MaxMonths = 5;
    private const float FarDistance = 120f;
    private const float WeakMandate = 40f;

    private static readonly HashSet<EmpireWarType> Rebellions = new()
    {
        EmpireWarType.派系叛乱, EmpireWarType.地方叛乱, EmpireWarType.民族叛乱, EmpireWarType.宗教叛乱,
        EmpireWarType.地方独立, EmpireWarType.附庸独立, EmpireWarType.清君侧
    };

    public static bool IsRebellion(War war) =>
        war?.data != null && (war.getAsset() == WarTypeLibrary.rebellion || Rebellions.Contains(war.GetEmpireWarType()));

    // 这场叛乱的集结期(月)，第一次问到时掷定并记在战争上
    public static int Months(War war)
    {
        var data = war.GetOrCreate();
        if (data.muster_months >= 0) return data.muster_months;
        int months = (int)(Math.Abs(war.data.id * 7919L) % 4);
        Kingdom rebel = war.main_attacker, loyal = FrontSystem.Realm(war.main_defender);
        if (rebel?.capital != null && loyal?.capital != null &&
            Vector2.Distance(rebel.capital.city_center, loyal.capital.city_center) > FarDistance) months++;
        if (loyal?.GetEmpire() is Layer.Empire empire && empire.Mandate < WeakMandate) months++;
        if (loyal != null && WarScopeSystem.Level(loyal) >= WarScopeSystem.Total) months--;
        data.muster_months = Mathf.Clamp(months, 0, MaxMonths);
        return data.muster_months;
    }

    private static bool Underway(War war) =>
        war?.data != null && war.isAlive() && !war.hasEnded() && IsRebellion(war) &&
        Date.getMonthsSince(war.data.created_time) < Months(war);

    // loyal 一方还在集结、对 rebel 这伙叛军不能全力应战
    public static bool Mustering(Kingdom loyal, Kingdom rebel)
    {
        if (loyal?.data == null || rebel?.data == null || loyal == rebel) return false;
        Kingdom realm = FrontSystem.Realm(loyal);
        foreach (War war in rebel.getWars())
        {
            if (!Underway(war)) continue;
            bool loyalDefends = war.isDefender(loyal) || war.isDefender(realm);
            if (loyalDefends && war.isAttacker(rebel)) return true;
        }
        return false;
    }

    // 守方仓促应战的战力系数
    public static float DefenseFactor(Kingdom defender, Kingdom attacker) =>
        Mustering(defender, attacker) ? SurprisedDefense : 1f;

    // 每月(战线结算前)：新起的叛乱记"仓促应战"，集结期满记"大军集结完毕"
    public static void MonthTick()
    {
        foreach (War war in World.world.wars.ToList())
        {
            if (war?.data == null || !war.isAlive() || war.hasEnded() || !IsRebellion(war)) continue;
            var data = war.GetOrCreate();
            Kingdom rebel = war.main_attacker, loyal = FrontSystem.Realm(war.main_defender);
            if (rebel?.data == null || loyal?.data == null) continue;
            bool fresh = data.muster_months < 0;
            int months = Months(war);
            if (fresh && months > 0)
                FrontSystem.Log(loyal, string.Format(LM.Get("corps_log_muster_start"), rebel.GetKingdomName(),
                    loyal.GetKingdomName(), months));
            if (months > 0 && !data.muster_done && Date.getMonthsSince(war.data.created_time) >= months)
            {
                data.muster_done = true;
                FrontSystem.Log(loyal, string.Format(LM.Get("corps_log_muster_done"), loyal.GetKingdomName(),
                    rebel.GetKingdomName()));
            }
        }
    }
}
