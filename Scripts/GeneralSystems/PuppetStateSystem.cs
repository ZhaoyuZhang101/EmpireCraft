using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 傀儡国(用户 2026-11-10 "剧情模式加上日军侵华剧本""现代政体如果要做的符合历史但是受限于机制的话优先完善机制")：
// 伪满洲国、蒙疆、汪伪政府这类由占领者扶植、名义上自成一国的政权。和封建附庸、朝贡、殖民地都不同：
//   · 主国替它定战和：傀儡不自行宣战(WarGate、WarTargetPatch)，主国(连同主国所在帝国的中央)的仗它每月补进同一方；
//   · 主国与傀儡、同一主国的傀儡之间互不开战(WarGate.Forbidden)；
//   · 傀儡不卷入现代建政、军阀时期与和平统一(ModernStateFormationSystem、NationalUnificationSystem)，政体照立国时定下的，
//     不随本文化的废君、建政改走(满洲国称帝、汪伪称国民政府)；
//   · 主国亡了就不再是傀儡(记一笔"失去依附")；剧情事件可把傀儡的城献给别国(cede)了结它；
//   · 理念跟主国(用户 2026-11-10 "扶持傀儡政权只能扶持自己国家主导政党的同理念政府")：傀儡的元首与要员奉行主国执政党的理念，
//     主国执政理念变了(日本 1940 改大政翼赞会)傀儡跟着变；近代占领挑领头人先挑当地同理念的(见 OccupationPuppetSystem)。
// 只由剧本(关系 puppet)或剧情事件(puppet)扶立，AI 不自行扶植傀儡。
public static class PuppetStateSystem
{
    public static bool IsPuppet(Kingdom kingdom) => Master(kingdom) != null;

    public static Kingdom Master(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return null;
        long id = kingdom.GetOrCreate().puppet_master_id;
        if (id < 0 || World.world?.kingdoms == null) return null;
        Kingdom master = World.world.kingdoms.get(id);
        return master?.data == null || master.isRekt() || master == kingdom ? null : master;
    }

    public static IEnumerable<Kingdom> PuppetsOf(Kingdom master) =>
        master == null || World.world?.kingdoms == null ? Enumerable.Empty<Kingdom>()
            : World.world.kingdoms.list.Where(kingdom => kingdom?.data != null && kingdom.GetOrCreate().puppet_master_id == master.data.id &&
                                                         Master(kingdom) == master);

    // 主国一方：主国在帝国里时按帝国中央(日本帝国的核心)，仗多半是中央在打
    private static Kingdom Realm(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        return empire?.CoreKingdom != null && !empire.isRekt() ? empire.CoreKingdom : kingdom;
    }

    private static bool SameRealm(Kingdom a, Kingdom b) => a != null && b != null && (a == b || a.IsInSameEmpire(b));

    // 主国与傀儡、同一主国的两个傀儡：互不开战
    public static bool Bound(Kingdom a, Kingdom b)
    {
        if (a == null || b == null || a == b) return false;
        Kingdom ma = Master(a), mb = Master(b);
        if (ma != null && (SameRealm(ma, b) || mb != null && SameRealm(ma, mb))) return true;
        return mb != null && SameRealm(mb, a);
    }

    // 立为傀儡；做不了返回原因(语言键)
    public static string Establish(Kingdom master, Kingdom puppet, bool record = true)
    {
        if (master?.data == null || puppet?.data == null || master == puppet || master.isRekt() || puppet.isRekt())
            return "diplomacy_reason_invalid";
        // 帝国的成员(行政区、藩镇)由帝国管，不另作傀儡；傀儡不能再有傀儡
        if (puppet.IsInEmpire() && !puppet.IsEmpire()) return "puppet_reason_member";
        if (IsPuppet(master) || PuppetsOf(puppet).Any()) return "puppet_reason_chain";
        if (SameRealm(master, puppet)) return "puppet_reason_member";
        DiplomacyActions.EndWars(master, puppet);
        DiplomacyActions.EndWars(Realm(master), puppet);
        KingdomExtension.KingdomExtraData data = puppet.GetOrCreate();
        data.puppet_master_id = master.data.id;
        data.puppet_since = World.world.getCurWorldTime();
        // 政体照立国时的：不随本文化的政体同步、废君、建政改走(见 InstitutionSystem、RepublicSystem.CanAbolish)
        data.regime_manually_selected = true;
        data.scenario_monarchy = true;
        SyncIdeology(puppet, master);
        JoinWars(puppet, master);
        // 地图上用宗主的颜色(见 VassalColorService)
        VassalColorService.Apply(puppet, master);
        if (record)
        {
            string text = string.Format(LM.Get("puppet_established_history"), puppet.GetKingdomName(), master.GetKingdomName());
            EventRecorder.Record(master, text);
            TranslateHelper.LogEventMessage(text, master);
        }
        LogService.LogInfo($"[EmpireCraft][傀儡] {master.GetKingdomName()} 立 {puppet.GetKingdomName()} 为傀儡");
        return null;
    }

    // 立一个傀儡政权：seat 作治所，leader 领头(另立门户，离了原来的官职)，cities 里别的城一并交过去，followers 随他改投；
    // name 国号(锁定)；culture 写了改新国文化，否则取当地之俗。剧情事件(扶植傀儡政权)与近代占领(OccupationPuppetSystem)共用
    public static Kingdom Found(Kingdom master, City seat, IEnumerable<City> cities, Actor leader, string name, string culture = null,
        IEnumerable<Actor> followers = null)
    {
        if (master?.data == null || master.isRekt() || seat?.data == null || seat.isRekt() || leader?.data == null || leader.isRekt()) return null;
        Detach(leader);
        if (leader.city != seat) leader.joinCity(seat);
        Kingdom puppet = seat.makeOwnKingdom(leader, pRebellion: false);
        if (puppet?.data == null) return null;
        if (puppet.IsInEmpire()) puppet.GetEmpire()?.leave(puppet, true, true);
        if (CultureService.IsValidCulture(culture)) RealmCultureSystem.Convert(puppet, culture, wholeEmpire: false);
        else CultureService.ApplyLocalCulture(puppet, seat);
        puppet.ClearAdministrativeTitle();
        puppet.SetKingdomType(KingdomType.default_country_post);
        puppet.GetRegime()?.SetAllowDiplomacy(true);
        puppet.GetRegime()?.SetAllowArmy(true);
        puppet.GetRegime()?.SetAllowSupportCenterArmy(false);
        foreach (City city in (cities ?? Enumerable.Empty<City>()).Where(city => city?.data != null && !city.isRekt() && city != seat).ToList())
            if (city.kingdom != puppet) city.joinAnotherKingdom(puppet);
        if (!string.IsNullOrWhiteSpace(name))
        {
            puppet.SetKingdomCoreName(name, name);
            puppet.SetCustomCountryName(name);
        }
        foreach (Actor follower in (followers ?? Enumerable.Empty<Actor>())
                 .Where(actor => actor?.data != null && !actor.isRekt() && actor != leader && actor.isAdult() && !actor.isKing()).ToList())
        {
            Detach(follower);
            if (follower.kingdom != puppet) follower.joinKingdom(puppet);
            if (follower.city?.kingdom != puppet) follower.joinCity(seat);
        }
        string failed = Establish(master, puppet, record: false);
        if (failed != null) LogService.LogWarning($"[EmpireCraft][傀儡] {name} 立为 {master.GetKingdomName()} 的傀儡失败({failed})");
        if (CorpsSystem.Enabled) CorpsSystem.Prefill(puppet);
        return puppet;
    }

    // 主国执政党的理念：有执政党按执政党；共和国按建国理念；君主国没有政党的按国家主理念；都没有按君主本人的
    public static PartyIdeology MasterIdeology(Kingdom master)
    {
        Empire empire = master?.GetEmpire();
        if (empire != null && !empire.isRekt())
        {
            FixedFaction party = PartySystem.GetGovernmentParty(empire);
            if (party != null) return party.Ideology;
            return WarlordEraSystem.GovernmentIdeology(empire);
        }
        return master?.king != null ? IdeologyPopulationSystem.Get(master.king) : PartyIdeology.Centrism;
    }

    // 傀儡奉行主国的理念：元首与京城的官员、名人随之改；傀儡自己有政党的，由这一理念的党一党执政
    public static void SyncIdeology(Kingdom puppet, Kingdom master)
    {
        if (puppet?.data == null || master?.data == null) return;
        PartyIdeology ideology = MasterIdeology(master);
        KingdomExtension.KingdomExtraData data = puppet.GetOrCreate();
        if (data.puppet_ideology == ideology.ToString() && (puppet.king == null || IdeologyPopulationSystem.Get(puppet.king) == ideology)) return;
        data.puppet_ideology = ideology.ToString();
        if (puppet.king != null) IdeologyPopulationSystem.Set(puppet.king, ideology);
        foreach (Actor actor in puppet.capital?.units?.ToList() ?? new List<Actor>())
            if (actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.kingdom == puppet && CityPopulationSystem.IsNotable(actor))
                IdeologyPopulationSystem.Set(actor, ideology);
        Empire empire = puppet.GetEmpire();
        if (empire != null && empire.CoreKingdom == puppet && PartySystem.IsActive(empire))
        {
            FixedFaction ruling = PartySystem.ScenarioFoundParty(empire, ideology, puppet.king, null);
            if (ruling != null) PartyBanSystem.Close(empire, ruling, Enumerable.Empty<FixedFaction>(), "party_ban_one_party_history");
        }
    }

    // 离了原来的官职、城主之位
    private static void Detach(Actor actor)
    {
        if (actor.IsOnOffice())
        {
            actor.GetOffice()?.RemoveActor();
            actor.EndOffice();
        }
        if (actor.isCityLeader() && actor.city != null) actor.city.removeLeader();
    }

    public static void Release(Kingdom puppet, string historyKey)
    {
        if (puppet?.data == null) return;
        KingdomExtension.KingdomExtraData data = puppet.GetOrCreate();
        if (data.puppet_master_id < 0) return;
        Kingdom master = World.world?.kingdoms?.get(data.puppet_master_id);
        data.puppet_master_id = -1L;
        data.puppet_since = -1d;
        data.regime_manually_selected = false;
        VassalColorService.Clear(puppet);
        if (puppet.isRekt() || string.IsNullOrEmpty(historyKey)) return;
        string text = string.Format(LM.Get(historyKey), puppet.GetKingdomName(), master?.data?.name ?? LM.Get("label_none"));
        EventRecorder.Record(puppet, text);
        TranslateHelper.LogEventOnly(text, puppet);
    }

    // 主国(连同主国所在帝国的中央)正在打的仗：傀儡补进同一方
    private static void JoinWars(Kingdom puppet, Kingdom master)
    {
        var sides = new HashSet<Kingdom> { master, Realm(master) };
        foreach (Kingdom side in sides.ToList())
        {
            if (side?.data == null || side.isRekt()) continue;
            foreach (War war in side.getWars().ToList())
            {
                if (war?.data == null || war.hasEnded() || war.hasKingdom(puppet)) continue;
                try
                {
                    if (war.isAttacker(side)) war.joinAttackers(puppet);
                    else if (war.isDefender(side)) war.joinDefenders(puppet);
                }
                catch (Exception exception)
                {
                    LogService.LogWarning($"[EmpireCraft][傀儡] {puppet.GetKingdomName()} 随主国参战失败: {exception.Message}");
                }
            }
        }
    }

    private static object _world;
    private static double _lastMonth = -1d;

    public static void ResetWorldState()
    {
        _world = null;
        _lastMonth = -1d;
    }

    // 每月：主国还在的随主国参战，主国亡了的放开
    public static void Tick()
    {
        MapBox world = World.world;
        if (world?.kingdoms == null || ModClass.IS_CLEAR) return;
        if (!ReferenceEquals(_world, world))
        {
            _world = world;
            _lastMonth = -1d;
        }
        double now = world.getCurWorldTime();
        if (_lastMonth >= 0d && now >= _lastMonth && Date.getMonthsSince(_lastMonth) < 1) return;
        _lastMonth = now;
        foreach (Kingdom kingdom in world.kingdoms.list.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.GetOrCreate().puppet_master_id < 0) continue;
            try
            {
                Kingdom master = Master(kingdom);
                if (master == null) Release(kingdom, "puppet_master_gone_history");
                // 城都交出去了(并进别的傀儡、战后献城)：只剩个空名，不再随主国参战
                else if ((kingdom.cities?.Count ?? 0) == 0) Release(kingdom, null);
                else
                {
                    SyncIdeology(kingdom, master);
                    JoinWars(kingdom, master);
                }
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][傀儡] {kingdom.data.name} 月结失败: {exception.Message}");
            }
        }
    }
}
