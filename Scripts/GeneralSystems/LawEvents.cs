using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 律法与实事(用户 2026-11-06 "律法系统感觉很不完善"，选"罪名来自真实发生的事")：以前除了每年给官员、城主、有爵者随机掷罪，
// 只有税收贪墨、计谋揭发会定罪，谋反、篡位、叛国、煽动叛乱、临阵脱逃、违抗军令、抢掠平民、宣扬异端这些罪名从来不会发生。
// 现在把游戏里真发生的事接到律法上。律法开关、各政体有没有这条律、管辖权都由 EmpireLawSystem 判断，这里只挑人、定罪名与几率。
//   当场拿获(EmpireLawSystem.Prosecute：不掷发现骰，按律论处，记卷宗)：
//   · 叛乱兵败(守方胜，或议和时叛军已经灭亡)：叛军首领活着落到朝廷手里——人在守方或守方所在帝国境内、不再是一国之君——
//     按谋反论处；地方独立、附庸独立按叛国，举教起事按煽动叛乱，藩王争位按篡位(见 OnWarEnded；首领见 TrackRebelLeader)；
//   · 不奉诏之战朝廷获胜、抗命的嗣君被废：按谋反论处；
//   · 宫门之变、诸王争位兵败的皇子：先按篡位论处(律有夷三族的连坐妻小；律法关闭、本政体无此律的不论)，之后照旧伏诛
//     (见 PrinceStruggleSystem)；
//   · 大军(满编 BigCorps 以上，义军、骑士团的不算)大败又溃散而统领生还：RoutChance 机会论处——全军覆没只身逃还、守御溃散
//     按临阵脱逃，攻城大败按违抗军令；城破时趁乱出走的守城统领 CityFledChance 机会按临阵脱逃论处；
//     同一国 MilitaryCooldownYears 年内至多论处一员败将(见 BattleDrama)；君主、储君、叛军的统领不论；
//   · 迫害异教时仍奉被禁之教的城主：PersecutionChance 机会按宣扬异端论处，每回至多 PersecutionMax 人(见 ReligiousPersecution)；
//   · 纵兵烧杀同一帝国(或正从本帝国叛出)的城池：攻城统领 SackChance 机会按抢掠平民论处(见 ScorchedEarthSystem)；
//   暗中犯罪(RecordCrime，日后由每年的查案发现；同一罪名二十年内只记一次)：
//   · 纵兵烧杀外国城池：攻城统领 SackRecordChance 机会记抢掠平民；
//   · 朝议"赈济灾荒"被层层克扣(到手不足 SkimLine)：灾城主官按克扣的比例记贪污，每回至多 SkimMax 人(见 TempFac_赈济灾荒)；
//   · 构陷宿敌去职记陷害；遣刺客杀了同国的人记杀人，刺杀本国君主记谋反(见 PersonalTieSystem)；
//   · 世家兼并郡望之田：AnnexChance 机会给在任官员、城主的族人记非法侵占土地(见 GentrySystem.Kin)。
// 罪名对应的刑罚照 EmpireLawConfig(谋反、篡位是死刑、夷三族，临阵脱逃是死刑……)，各政体可在律法里删改。
public static class LawEvents
{
    private const float BigCorps = 3000f;
    private const float RoutChance = 0.04f, CityFledChance = 0.1f;
    private const int MilitaryCooldownYears = 2;
    private const float PersecutionChance = 0.35f;
    private const int PersecutionMax = 2;
    private const float SackChance = 0.3f, SackRecordChance = 0.5f;
    private const float SkimLine = 0.8f, SkimFactor = 0.5f;
    private const int SkimMax = 2;
    private const float AnnexChance = 0.05f;

    // 各国上次论处败将的时间(不存档；换了世界时间倒退的记录当作过期)
    private static readonly Dictionary<long, double> LastMilitaryCase = new();

    #region 通用

    // 同一国或同一帝国
    private static bool Within(Kingdom kingdom, Kingdom realm) =>
        kingdom?.data != null && realm?.data != null && !kingdom.isRekt() && !realm.isRekt() &&
        (kingdom == realm || kingdom.IsInSameEmpire(realm));

    private static bool Living(Actor actor) => actor?.data != null && !actor.isRekt() && actor.isAlive();

    private static bool Prosecute(Actor actor, LawType law, Kingdom court, string reasonKey)
    {
        if (!Living(actor) || court?.data == null || court.isRekt()) return false;
        string name = actor.getName();
        try
        {
            if (EmpireLawSystem.Prosecute(actor, law, court, reasonKey) == null) return false;
            LogService.LogInfo($"[律法][实事] {court.GetKingdomName()} 以{law}论处 {name}({reasonKey})");
            return true;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[律法][实事] 以{law}论处 {name} 失败: {exception.Message}");
            return false;
        }
    }

    // 暗中犯罪：君主不记(律法给君主记罪只加暴虐，这里都是臣下的事)
    private static bool Record(Actor actor, LawType law)
    {
        if (!Living(actor) || actor.isKing()) return false;
        try
        {
            return actor.RecordCrime(law);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[律法][实事] 给 {actor.getName()} 记{law}失败: {exception.Message}");
            return false;
        }
    }

    #endregion

    #region 叛乱兵败

    // 叛乱一类的仗(攻方是起事的一方)按什么罪论处；不是叛乱返回 null。
    // 不奉诏(朝廷是攻方)、伐不臣、征伐不服这些朝廷讨伐的仗不在这里
    private static LawType? RebelLaw(War war, EmpireWarType type)
    {
        switch (type)
        {
            case EmpireWarType.藩王索取皇位:
                return LawType.篡位;
            case EmpireWarType.地方独立:
            case EmpireWarType.附庸独立:
                return LawType.叛国;
            case EmpireWarType.宗教叛乱:
                return LawType.煽动叛乱;
            case EmpireWarType.派系叛乱:
            case EmpireWarType.地方叛乱:
            case EmpireWarType.民族叛乱:
            case EmpireWarType.清君侧:
                return LawType.谋反;
            case EmpireWarType.None:
                return war.getAsset() == WarTypeLibrary.rebellion ? LawType.谋反 : (LawType?)null;
            default:
                return null;
        }
    }

    private static string RebelReason(LawType law) => law switch
    {
        LawType.篡位 => "law_reason_failed_claim",
        LawType.叛国 => "law_reason_failed_secession",
        LawType.煽动叛乱 => "law_reason_failed_sect_uprising",
        _ => "law_reason_failed_rebellion"
    };

    // WarPatch.update 里每次更新：记下叛军眼下的首领(叛军灭亡后国王没了，战后要按记下的找人)。
    // 每帧都走，出错也不能抛出去(会让原版 War.update 每帧报错)
    public static void TrackRebelLeader(War war)
    {
        try
        {
            Actor leader = war?.data == null || war.hasEnded() ? null : war.getMainAttacker()?.king;
            if (!Living(leader)) return;
            WarExtension.WarExtraData data = war.GetOrCreate();
            if (data == null || data.rebel_leader_id == leader.id || RebelLaw(war, data.empireWarType) == null) return;
            data.rebel_leader_id = leader.id;
        }
        catch (Exception)
        {
            // 记不上首领只是战后不论处，不影响战争
        }
    }

    // 战争结束(WarPatch.end_war，各项结局结算之后)：叛乱兵败、不奉诏平定后论处首领
    public static void OnWarEnded(War war, WarWinner winner)
    {
        if (war?.data == null || World.world?.units == null || !EmpireLawSystem.Enabled) return;
        try
        {
            WarExtension.WarExtraData data = war.GetOrCreate();
            if (data == null) return;
            if (data.empireWarType == EmpireWarType.不奉诏)
            {
                Defiance(war, data, winner);
                return;
            }
            LawType? law = RebelLaw(war, data.empireWarType);
            if (law == null || !RebelsLost(war, winner)) return;
            long id = data.rebel_leader_id >= 0 ? data.rebel_leader_id : data.republic_revolution_leader_id;
            Actor leader = id >= 0 ? World.world.units.get(id) : null;
            Kingdom loyalist = war.getMainDefender();
            if (Captive(leader, loyalist)) Prosecute(leader, law.Value, loyalist, RebelReason(law.Value));
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[律法][实事] 战后论处叛乱首领失败({war.data.name}): {exception.Message}");
        }
    }

    // 守方胜；或者议和、作罢时叛军已经灭亡(没有城了)
    private static bool RebelsLost(War war, WarWinner winner)
    {
        if (winner == WarWinner.Defenders) return true;
        if (winner != WarWinner.Nobody) return false;
        Kingdom rebel = war.getMainAttacker();
        return rebel?.data == null || rebel.isRekt() || !rebel.isAlive() || rebel.countCities() == 0;
    }

    // 落到朝廷手里：还活着，人在胜方(或胜方所在帝国)境内，不再是一国之君(还据着一国的不算被擒)
    private static bool Captive(Actor actor, Kingdom victor)
    {
        if (!Living(actor) || victor?.data == null || victor.isRekt()) return false;
        Kingdom held = actor.kingdom;
        return held?.data != null && held.king != actor && Within(held, victor);
    }

    // 不奉诏：朝廷(攻方)获胜、改立了新君，被废的抗命嗣君以谋反论处
    private static void Defiance(War war, WarExtension.WarExtraData data, WarWinner winner)
    {
        if (winner != WarWinner.Attackers || data.investiture_heir_id < 0) return;
        Actor heir = World.world.units.get(data.investiture_heir_id);
        Kingdom core = war.getMainAttacker();
        if (Captive(heir, core)) Prosecute(heir, LawType.谋反, core, "law_reason_defiance_crushed");
    }

    // 宫门之变、诸王争位兵败(PrinceStruggleSystem)：以篡位论处；之后照旧伏诛(已被律法处死的不再杀)
    public static void FailedUsurper(Actor prince, Kingdom core, string reasonKey)
    {
        if (!EmpireLawSystem.Enabled) return;
        Prosecute(prince, LawType.篡位, core, reasonKey);
    }

    #endregion

    #region 败将

    private static bool MilitaryCaseDue(Kingdom kingdom)
    {
        if (World.world == null) return false;
        double now = World.world.getCurWorldTime();
        return !LastMilitaryCase.TryGetValue(kingdom.id, out double at) || now < at ||
               Date.getYearsSince(at) >= MilitaryCooldownYears;
    }

    private static void MarkMilitaryCase(Kingdom kingdom)
    {
        if (World.world != null) LastMilitaryCase[kingdom.id] = World.world.getCurWorldTime();
    }

    // 可以追究的败将：本国大军(不是义军、骑士团)的统领，不是君主、储君，本国不是正在起事的叛军，近两年没论处过败将
    private static bool Accountable(Kingdom kingdom, Actor general, CorpsData corps) =>
        kingdom?.data != null && !kingdom.isRekt() && Living(general) && general.kingdom == kingdom && corps != null &&
        !corps.militia && corps.order_id < 0 && corps.full >= BigCorps && !general.isKing() &&
        !(kingdom.HasHeir() && kingdom.GetHeir() == general) && !CorpsSystem.Insurgent(kingdom) && MilitaryCaseDue(kingdom);

    // 战场结算(BattleDrama.Settle)：大败又溃散，统领没阵亡、没被俘、没有突围之功
    public static void Routed(Kingdom kingdom, Actor general, CorpsData corps, bool attacking)
    {
        if (UnityEngine.Random.value >= RoutChance || !EmpireLawSystem.Enabled || !Accountable(kingdom, general, corps)) return;
        bool lost = corps.strength < 1f;
        LawType law = lost || !attacking ? LawType.临阵脱逃 : LawType.违抗军令;
        string reason = lost ? "law_reason_army_lost" : attacking ? "law_reason_rash_defeat" : "law_reason_army_routed";
        if (Prosecute(general, law, kingdom, reason)) MarkMilitaryCase(kingdom);
    }

    // 城破(BattleDrama.CityFell)：守城统领没殉国、没被俘，趁乱出走
    public static void CityFled(Kingdom kingdom, Actor general, CorpsData corps)
    {
        if (UnityEngine.Random.value >= CityFledChance || !EmpireLawSystem.Enabled || !Accountable(kingdom, general, corps)) return;
        if (Prosecute(general, LawType.临阵脱逃, kingdom, "law_reason_city_abandoned")) MarkMilitaryCase(kingdom);
    }

    #endregion

    #region 纵兵烧杀

    // 城破时纵兵烧杀(ScorchedEarthSystem.OnCityCaptured)：攻城的统领担这一笔
    public static void Sacked(City city, Kingdom victim, Kingdom conqueror)
    {
        if (city?.data == null || victim?.data == null || conqueror?.data == null || !EmpireLawSystem.Enabled) return;
        try
        {
            Actor general = SackCommander(city, conqueror);
            if (!Living(general) || general.isKing() || general.kingdom != conqueror) return;
            Empire empire = conqueror.GetEmpire();
            // 烧杀的是本帝国的城(或正从本帝国叛出的城)：朝廷当场追究；外国的城只暗中记一笔
            bool own = empire != null && !empire.isRekt() &&
                       (victim.GetEmpire() == empire ||
                        (victim.IsLocalRebelling() || victim.IsFactionRebelling()) && victim.RebellionOriginEmpire() == empire);
            if (own)
            {
                if (UnityEngine.Random.value < SackChance)
                    Prosecute(general, LawType.抢掠平民, empire.CoreKingdom ?? conqueror, "law_reason_sack");
            }
            else if (UnityEngine.Random.value < SackRecordChance) Record(general, LawType.抢掠平民);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[律法][实事] 烧杀论处失败({city.data.name}): {exception.Message}");
        }
    }

    // 攻下这座城的统领：本国打这座城的各军里兵力最多、有统领实体的那支
    private static Actor SackCommander(City city, Kingdom conqueror)
    {
        Actor best = null;
        float most = 0f;
        foreach (CorpsData corps in CorpsSystem.Of(conqueror))
        {
            if (corps == null || corps.sector_city_id != city.id || corps.general_id < 0 || corps.strength <= most) continue;
            Actor general = World.world.units.get(corps.general_id);
            if (!Living(general)) continue;
            best = general;
            most = corps.strength;
        }
        return best;
    }

    #endregion

    #region 赈灾克扣

    // 灾城的主官：城的官职(州郡长官)在任者，没有就是城主
    private static Actor Steward(City city)
    {
        Actor officer = city?.GetOffice()?.GetActor();
        if (Living(officer) && !officer.isKing()) return officer;
        Actor leader = city?.leader;
        return Living(leader) && !leader.isKing() ? leader : null;
    }

    // 朝议"赈济灾荒"发粮(TempFac_赈济灾荒)：到手不足 SkimLine 时，灾城主官按克扣的比例暗中记贪污
    public static void ReliefSkimmed(IEnumerable<City> cities, float efficiency)
    {
        if (cities == null || efficiency >= SkimLine || !EmpireLawSystem.Enabled) return;
        float chance = (1f - efficiency) * SkimFactor;
        int recorded = 0;
        foreach (City city in cities.ToList())
        {
            if (recorded >= SkimMax) break;
            if (city?.data == null || city.isRekt() || UnityEngine.Random.value >= chance) continue;
            if (Record(Steward(city), LawType.贪污)) recorded++;
        }
    }

    #endregion

    #region 迫害异教

    // 迫害(ReligiousPersecution.Persecute)：没有改宗的各城，城主仍奉被禁之教的，以宣扬异端论处
    public static void HereticLeaders(Kingdom realm, Religion faith, IEnumerable<City> cities)
    {
        if (realm?.data == null || faith == null || cities == null || !EmpireLawSystem.Enabled) return;
        int charged = 0;
        foreach (City city in cities.ToList())
        {
            if (charged >= PersecutionMax) break;
            Actor leader = city?.data == null || city.isRekt() ? null : city.leader;
            if (!Living(leader) || leader.isKing() || leader.religion != faith || !Within(leader.kingdom, realm)) continue;
            if (UnityEngine.Random.value >= PersecutionChance) continue;
            if (Prosecute(leader, LawType.宣扬异端, realm, "law_reason_persecution")) charged++;
        }
    }

    #endregion

    #region 个人恩怨、兼并

    // 构陷宿敌去职得逞(PersonalTieSystem.Frame)
    public static void Framed(Actor actor)
    {
        if (EmpireLawSystem.Enabled) Record(actor, LawType.陷害);
    }

    // 遣刺客得手(PersonalTieSystem.Revenge，在行刺之前判定)：victimKingdom 是死者所在的国，同国或同一帝国才记；
    // 刺的是本国君主记谋反
    public static void Assassinated(Actor actor, Kingdom victimKingdom, bool regicide)
    {
        if (!EmpireLawSystem.Enabled || !Living(actor) || !Within(victimKingdom, actor.kingdom)) return;
        Record(actor, regicide ? LawType.谋反 : LawType.杀人);
    }

    // 世家兼并郡望之田(GentrySystem.Annex 实际占到了地)：在任官员、城主的族人仗势占田，暗中记非法侵占土地
    public static void ClanAnnexed(SpecificClan clan, City seat)
    {
        if (clan == null || seat?.kingdom == null || UnityEngine.Random.value >= AnnexChance || !EmpireLawSystem.Enabled) return;
        Actor culprit = clan.LivingPeople?
            .Where(person => person != null && !person.is_virtual)
            .Select(person => person._actor)
            .Where(actor => Living(actor) && actor.isAdult() && !actor.isKing() && (actor.IsOnOffice() || actor.isCityLeader()) &&
                            Within(actor.kingdom, seat.kingdom))
            .OrderByDescending(actor => actor.city == seat)
            .FirstOrDefault();
        Record(culprit, LawType.非法侵占土地);
    }

    #endregion
}
