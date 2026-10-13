using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 神圣罗马式的选帝制帝国(封建制、帝位由选帝侯选举——施行了金玺诏书)：
//   · 家族领地：当选的诸侯带着自家领国成为帝国中央，帝都随之搬到他的都城；原来的中央降为普通诸侯国、仍归原皇族。
//     选举换了家族不算篡位(不额外扣正统、旧皇族不出走、不改国号，见 Empire.NewEmperor)；
//   · 家族领地占帝国城数：不到一成正统 -8，二成五以上 +5，四成以上 +8；
//     不到一成时，城比中央多的诸侯不听调遣(见 EmpirePeacePatch.Obeys)；
//   · 选举让步：每次当选(含选出罗马人民的国王)随机让出一项——帝国税降一档、某选帝侯所在国恢复外交与开战权、
//     自治派系 +5、中央一座非都城的城封给最强的选帝侯；
//   · 选立储君(原型"罗马人民的国王")：皇帝在世、有继承人时，每年有两成可能让选帝侯先把继承人选为储君
//     (让出一项、花一成年度经常收入)；皇帝死后他直接即位，不再另选。同一家族连续当选记届数。
public static class HolyRomanSystem
{
    private const float KingOfRomansChance = 0.2f;
    private const float KingOfRomansCost = 0.1f;
    private const float WeakHouse = 0.1f, StrongHouse = 0.25f, DominantHouse = 0.4f;

    public static HolyRomanState State(Empire empire)
    {
        if (empire?.data == null) return null;
        return empire.data.holy_roman ??= new HolyRomanState();
    }

    public static bool IsElective(Empire empire)
    {
        Regime regime = empire?.CoreKingdom?.GetRegime();
        return regime?.type == RegimeType.Feudalism && regime.GetLeaderSelectMethod() == LeaderSelectMethod.Vote;
    }

    // 家族领地：中央(皇帝自家领国)的城占帝国城数的比例
    public static float HouseShare(Empire empire)
    {
        int total = empire?.countCities() ?? 0;
        return total <= 0 ? 1f : (empire.CoreKingdom?.cities?.Count ?? 0) / (float)total;
    }

    public static IEnumerable<(string key, int value)> LegitimacyItems(Empire empire)
    {
        if (!IsElective(empire)) yield break;
        float share = HouseShare(empire);
        if (share < WeakHouse) yield return ("mandate_house_weak", -8);
        else if (share >= DominantHouse) yield return ("mandate_house_dominant", 8);
        else if (share >= StrongHouse) yield return ("mandate_house_strong", 5);
    }

    // 家族领地薄弱时，比皇帝自家领国城多的诸侯不听调遣
    public static bool Defies(Empire empire, Kingdom member) =>
        IsElective(empire) && HouseShare(empire) < WeakHouse && member?.cities != null &&
        member.cities.Count > (empire.CoreKingdom?.cities?.Count ?? 0);

    public static Actor KingOfRomans(Empire empire)
    {
        HolyRomanState state = State(empire);
        Actor actor = state?.king_of_romans_id >= 0 ? World.world.units.get(state.king_of_romans_id) : null;
        return actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.kingdom?.GetEmpire() == empire ? actor : null;
    }

    // 帝位选出的人(OfficeObject.SetActor 调用)：是别的成员国的君主时，带着自家领国成为中央，不调进原中央
    public static bool TryElectHouse(Kingdom seat, Actor elected)
    {
        try
        {
            Empire empire = seat?.GetEmpire();
            if (empire == null || empire.CoreKingdom != seat || !IsElective(empire) || elected?.data == null ||
                elected.isRekt() || !elected.isKing()) return false;
            Kingdom house = elected.kingdom;
            if (house == null || house == seat || house.GetEmpire() != empire || house.king != elected || !house.hasCapital())
                return false;
            string former = seat.GetKingdomName();
            if (!empire.TransferCoreTo(house)) return false;
            HolyRomanState state = State(empire);
            Elected(empire, state, elected);
            Concede(empire, state);
            Record(empire, string.Format(LM.Get("hre_election_history"), elected.getName(), house.GetKingdomName(),
                house.capital?.GetCityName() ?? "", former));
            return true;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][神罗] 选举换家族失败: {exception.Message}");
            return false;
        }
    }

    private static void Elected(Empire empire, HolyRomanState state, Actor emperor)
    {
        long clan = emperor.GetSpecificClan()?.id ?? -1L;
        state.house_terms = clan >= 0 && clan == state.house_clan_id ? state.house_terms + 1 : 1;
        state.house_clan_id = clan;
        state.last_election_at = World.world.getCurWorldTime();
    }

    // 每年(ConstitutionalEconomySystem 年度结算)：罗马人民的国王即位与选立
    public static void Yearly(Empire empire)
    {
        try
        {
            if (!IsElective(empire)) return;
            HolyRomanState state = State(empire);
            Actor emperor = empire.Emperor;
            if (emperor == null) return;
            // 罗马人民的国王已经即位
            if (state.king_of_romans_id == emperor.id)
            {
                state.king_of_romans_id = -1L;
                Elected(empire, state, emperor);
            }
            if (KingOfRomans(empire) != null) return;
            state.king_of_romans_id = -1L;
            Actor heir = empire.CoreKingdom.HasHeir() ? empire.CoreKingdom.GetHeir() : null;
            if (heir?.data == null || heir.isRekt() || !heir.isAlive() || UnityEngine.Random.value >= KingOfRomansChance) return;
            long cost = Math.Min(Math.Max(0L, (long)empire.CoreKingdom.GetMoney()),
                (long)(StateDebtSystem.AnnualStableIncome(empire.CoreKingdom) * KingOfRomansCost));
            if (cost > 0) empire.CoreKingdom.SubMoney((int)cost, TreasuryCategory.Gift);
            state.king_of_romans_id = heir.id;
            Concede(empire, state);
            Record(empire, string.Format(LM.Get("hre_king_of_romans_history"), heir.getName(), emperor.getName()));
            heir.RecordPersonalHistory(LM.Get("personal_history_king_of_romans"));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][神罗] 年度结算失败: {exception.Message}");
        }
    }

    // 选举让步：随机让出一项(能让的才让)
    private static void Concede(Empire empire, HolyRomanState state)
    {
        var options = new List<Func<string>> { LowerTax, RestoreWarRight, Autonomy, GrantFief };
        while (options.Count > 0)
        {
            int index = UnityEngine.Random.Range(0, options.Count);
            Func<string> option = options[index];
            options.RemoveAt(index);
            string text = option();
            if (string.IsNullOrEmpty(text)) continue;
            state.concessions ??= new List<string>();
            state.concessions.Add(text);
            if (state.concessions.Count > 20) state.concessions.RemoveAt(0);
            Record(empire, string.Format(LM.Get("hre_concession_history"), text));
            return;
        }

        string LowerTax()
        {
            if (empire.data.TaxRate <= 0.021f) return null;
            empire.data.TaxRate = Mathf.Max(0.02f, empire.data.TaxRate - 0.02f);
            return LM.Get("hre_concession_tax");
        }

        string RestoreWarRight()
        {
            Kingdom member = ElectorRealms(empire).FirstOrDefault(kingdom => kingdom.GetRegime()?.IsAllowDiplomacy() == false);
            if (member == null) return null;
            member.GetRegime().SetAllowDiplomacy(true);
            return string.Format(LM.Get("hre_concession_war_right"), member.GetKingdomName());
        }

        string Autonomy()
        {
            FixedFaction faction = empire.CoreKingdom.GetRegime()?.GetPlayerFactions()
                ?.FirstOrDefault(item => item.Type == FactionType.自治);
            if (faction == null || !empire.CoreKingdom.TryIncreaseFactionRatio(faction, 5)) return null;
            return LM.Get("hre_concession_autonomy");
        }

        string GrantFief()
        {
            Kingdom core = empire.CoreKingdom;
            City city = core.cities?.Where(item => item != null && !item.isRekt() && item != core.capital)
                .OrderBy(item => item.zones?.Count ?? 0).FirstOrDefault();
            Kingdom elector = ElectorRealms(empire).OrderByDescending(kingdom => kingdom.cities.Count).FirstOrDefault();
            if (city == null || elector == null || core.cities.Count < 3) return null;
            city.joinAnotherKingdom(elector);
            return string.Format(LM.Get("hre_concession_fief"), city.GetCityName(), elector.GetKingdomName());
        }
    }

    // 选帝侯所在的成员国(不含中央)
    public static List<Kingdom> ElectorRealms(Empire empire) =>
        empire.GetCabinetMembers().Where(actor => actor?.kingdom != null && !actor.isRekt())
            .Select(actor => actor.kingdom)
            .Where(kingdom => kingdom != empire.CoreKingdom && kingdom.GetEmpire() == empire && !kingdom.isRekt())
            .Distinct().ToList();

    private static void Record(Empire empire, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        empire.RecordHistory(directContent: text, actorId: empire.Emperor?.id ?? -1L, kingdomId: empire.CoreKingdom?.id ?? -1L);
        TranslateHelper.LogEventMessage(text, empire.CoreKingdom);
    }
}
