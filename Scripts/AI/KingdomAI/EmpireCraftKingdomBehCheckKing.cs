using System;
using System.Linq;
using ai.behaviours;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckKing : GameAIKingdomBase
{
    public override Type OriginalBeh => typeof(KingdomBehCheckKing);

    // 帝国核心帝位虚悬起始的时间(不存档：读档、换世界时清掉重新计满一年，见 ResetWorldState)
    private static readonly global::System.Collections.Generic.Dictionary<long, double> VacantSince = new();

    public static void ResetWorldState() => VacantSince.Clear();
    private const double VacantYears = 1d;

    private static bool VacantLongEnough(Kingdom kingdom)
    {
        double now = World.world.getCurWorldTime();
        if (!VacantSince.TryGetValue(kingdom.id, out double since) || now < since)
        {
            VacantSince[kingdom.id] = now;
            return false;
        }
        return Date.getYearsSince(since) >= VacantYears;
    }
    public override BehResult execute(Kingdom pKingdom)
    {
        if (pKingdom.data.timer_new_king > 0f)
        {
            return BehResult.Continue;
        }
        if (pKingdom.hasKing())
        {
            VacantSince.Remove(pKingdom.id);
            Actor king = pKingdom.king;
            if (king.isAlive())
            {
                TryToGiveGoldenTooth(king);
                king.CheckSpecificClan();
                return BehResult.Continue;
            }
        }
        // 第二直隶：储君就藩，没有储君就空着，不选任(见 MultiCapitalSystem.Zhili)
        if (MultiCapitalSystem.HoldsVacancy(pKingdom)) return BehResult.Continue;
        // 穿越政权：执委会主席出缺由元老院从元老里选，不走继承(见 TransmigrantSystem.TryHandleVacancy)
        if (TransmigrantSystem.TryHandleVacancy(pKingdom)) return BehResult.Continue;
        // 教宗国：教宗出缺由枢机推举或世俗强立，不传子孙(见 PapalElection)
        if (PapalElection.TryHandleVacancy(pKingdom)) return BehResult.Continue;
        if (ConstitutionalSuccessionSystem.TryHandleVacancy(pKingdom)) return BehResult.Continue;
        // 共主联盟：城邦跟随盟主新君、封建共主的王国按长幼分给子女
        if (PersonalUnionService.TryHandleVacancy(pKingdom))
        {
            return BehResult.Continue;
        }
        // 推恩令：封国国王去世后先按推恩令分城或收归郡县，处理过就不再走常规继承
        if (!pKingdom.hasKing() && GraceEdictService.TryHandleVacancy(pKingdom))
        {
            return BehResult.Continue;
        }
        if (NeedSuccession(pKingdom))
        {
            EmpireCraftKingdomBehCheckHeir.PrepareForSuccession(pKingdom);
            // 无小人模式：实体族人里没有继承人时，从虚拟族谱里落成一位(见 VirtualGenealogySystem.RealizeHeir)
            if (!pKingdom.HasHeir() && !SuccessionCouncilSystem.InSession(pKingdom))
            {
                Actor virtualHeir = VirtualGenealogySystem.RealizeHeir(pKingdom);
                if (virtualHeir != null) pKingdom.SetHeir(virtualHeir);
            }
            if (pKingdom.HasHeir())
            {
                ChooseKingFromHeir(pKingdom);  
                return BehResult.Continue;
            }

            if (pKingdom.IsEmpire())
            {
                // 帝位虚悬满一年、又不在议立(继承会议已放弃或无从议立)：无人可立，帝国降为王国，
                // 之后照普通王国从官员里选、从人口里落成一位国君(用户 2026-10-17 "无皇帝都多少年了"，选"无人可立就降为王国")
                if (!VacantLongEnough(pKingdom) || SuccessionCouncilSystem.InSession(pKingdom)) return BehResult.Continue;
                Empire empire = pKingdom.GetEmpire();
                // 还有成员国(郡县、藩国)的大帝国：撤了帝号各国就散了——照普通王国从官员、人口里落成一位，不撤
                if (empire.kingdoms_list?.Any(member => member != null && member != pKingdom && !member.isRekt()) != true)
                {
                string content = string.Format(LM.Get("empire_vacant_demoted"), empire.GetEmpireName(), pKingdom.GetKingdomName());
                empire.RecordHistory(directContent: content, kingdomId: pKingdom.id);
                TranslateHelper.LogEventOnly(content, pKingdom);
                ModClass.EMPIRE_MANAGER.dissolveEmpire(empire);
                }
                VacantSince.Remove(pKingdom.id);
            }
        } 
        // 选举制国家的行政区：省长出缺由本省执政党递补
        if (ProvincialPoliticsSystem.TryFillGovernorVacancy(pKingdom)) return BehResult.Continue;
        // 主教领：主教不世袭，出缺时按叙任权另派(见 HolyRomanSystem.Members)
        if (HolyRomanSystem.TryAppointBishop(pKingdom)) return BehResult.Continue;
        // 有帝国议会的选帝制帝国：帝位空缺由选帝侯会选(拉票、双重选举、大空位，见 HolyRomanSystem.Election)
        if (HolyRomanSystem.TryHoldElection(pKingdom)) return BehResult.Continue;
        OfficeObject office = pKingdom.GetOffice();
        if (office == null) return BehResult.Continue;
        office.is_local = true;
        office.meta_object = pKingdom;
        office.Select(pKingdom, "国家");
        // 无小人模式：选不出人(城里实体单位很少)时，从都城人口中生成一人出任
        if (!pKingdom.hasKing() && CityPopulationSystem.AbstractPopulationEnabled && pKingdom.capital != null)
        {
            Actor ruler = CityPopulationSystem.SpawnOfficial(pKingdom.capital, pKingdom);
            // 华夏一系不立女主：落成的是女子就改成男子(刚从人口里落成，还没有家室)
            if (EmpireCraftKingdomBehCheckHeir.BarredBySex(pKingdom, ruler)) ruler.data.sex = ActorSex.Male;
            if (ruler != null) office.SetActor(ruler);
        }
        return BehResult.Continue;
    }

    public static bool NeedSuccession(Kingdom pKingdom)
    {
        if (pKingdom.IsEmpire() && ConstitutionalSuccessionSystem.ThroneBound(pKingdom.GetEmpire())) return true;
        // 帝国核心的君主世袭(旧档改制时被重置成"科举选任"的修回来，见 EnsureHereditaryThrone)
        if (pKingdom.IsEmpire()) pKingdom.EnsureHereditaryThrone();
        Regime regime = pKingdom.GetRegime();
        if (regime == null) return false;
        var method = regime.GetLeaderSelectMethod();
        return (!pKingdom.IsEmpire() && method == LeaderSelectMethod.Succession) ||
               (pKingdom.IsEmpire() && method == LeaderSelectMethod.Succession);
    }

    public void ChooseKingFromHeir(Kingdom pKingdom)
    {
        pKingdom.clearKingData();
        if (!pKingdom.HasHeir()) return;
        var heir = pKingdom.GetHeir();
        if (heir == null || heir.isRekt()) return;
        Kingdom lastKingdom = null;
        if (heir.isKing() && heir.kingdom != null && !heir.kingdom.isRekt())
        {
            lastKingdom = heir.kingdom;
        }

        // 继承人已是别国君主而按规矩不该入继(华夏，或是别的帝国的皇帝，见 EmpireCraftKingdomBehCheckHeir.ForeignRuler)：
        // 不即位、不并国，重新择立继承人
        // 华夏一系的继承人是女子(不该有，见 EmpireCraftKingdomBehCheckHeir.BarredBySex)：不即位，重新择立
        if (lastKingdom != null && EmpireCraftKingdomBehCheckHeir.ForeignRuler(pKingdom, heir) ||
            EmpireCraftKingdomBehCheckHeir.BarredBySex(pKingdom, heir))
        {
            pKingdom.RemoveHeir();
            pKingdom.StartToChooseHeir();
            return;
        }

        // 西方封建制/古典共和：继承人本就是别国君主时由他兼领(共主联盟)，不把两国合并
        if (lastKingdom != null && !pKingdom.IsEmpire() && PersonalUnionService.IsUnionRegime(lastKingdom) &&
            PersonalUnionService.IsUnionRegime(pKingdom))
        {
            if (pKingdom.GetRegime()?.type == RegimeType.Feudalism &&
                PersonalUnionService.IsAtFeudalRealmLimit(heir))
            {
                if (!PersonalUnionService.CrownLocalVassal(pKingdom, lastKingdom))
                    pKingdom.GetOrCreate().union_partition_heir_id = heir.id;
                return;
            }
            PersonalUnionService.CrownInUnion(pKingdom, heir);
            return;
        }
        // 注意要用 "or" 模式：RegimeType 的值是 0,1,2... 连续编号，LvLing|ZhouFeudalism 按位或
        // 等于 2(=ZhouFeudalism)，之前这样写导致律令制的藩王永远走不到下面"封国留给儿子"的分支。
        if (lastKingdom != null && !(lastKingdom.GetRegime() is {type: RegimeType.LvLing or RegimeType.ZhouFeudalism}))
        {
            Empire succeeded = null;
            if (pKingdom.IsEmpire())
            {
                Empire empire = pKingdom.GetEmpire();
                if (empire != null && !empire.isRekt() && !empire.IsArchived())
                {
                    // 继承人的本国在帝国之外(非华夏可由外国君主入继，见 ForeignRuler)：先编入帝国再立为中央。
                    // 以前直接改中央，新中央不是成员，旧中央的城并过去后帝国名存实亡，地图上看已经没了
                    if (!empire.hasKingdom(lastKingdom))
                        empire.join(lastKingdom, pForce: true, pLegitimacyTransfer: true);
                    if (!empire.hasKingdom(lastKingdom))
                    {
                        pKingdom.RemoveHeir();
                        pKingdom.StartToChooseHeir();
                        return;
                    }
                    empire.CoreKingdom = lastKingdom;
                    // 继承人带着自己的封国入继大统：国号不随他的封国改(换了皇族的，由 Empire.NewEmperor 的篡位分支另定国号)
                    empire.KeepNameOnNewCore();
                    succeeded = empire;
                }
            }
            pKingdom.cities.ForEach(c=>{ if (c != null && !c.isRekt()) c.joinAnotherKingdom(lastKingdom); });
            // 中央改成了他的国，不经 setKing：即位后续(登记、汗位更替之乱、兀鲁思分封……)在这里补上，见 Empire.CompleteSuccession。
            // 放在旧中央的城并过来之后(旧中央已是没有君主的空壳，不会被当成兀鲁思据地自立)；原版各国的城市名单要到下一帧才重排，
            // 先排好，即位分封按新中央的全部城来分
            if (succeeded != null)
            {
                World.world.kingdoms.beginChecksCities();
                succeeded.CompleteSuccession(heir);
            }
            return;
        }

        // 成员君主入继大统、封国传给子女的政体读政体配置(keep_member_roles)：律令制、周制
        // 储君领着第二直隶：即位后直隶空出等新储君，不传子女(见 MultiCapitalSystem.Zhili)
        if (lastKingdom != null && !MultiCapitalSystem.IsZhili(lastKingdom) &&
            RegimeManager.GetTemplate(lastKingdom.GetRegime()?.type)?.keep_member_roles == true)
        {
            var children = heir.getChildren();
            if (children != null && children.ToList().FindAll(a=>a != null && !a.isKing()).Any())
            {
                MakeKingAndMoveToCapital(lastKingdom, children.ToList().Find(a=>a != null && !a.isKing()));
            }
            else
            {
                lastKingdom.StartToChooseHeir();
            }
        }
        MakeKingAndMoveToCapital(pKingdom, heir);
        OfficeObject office = pKingdom.GetOffice();
        if (office == null)
        {
            pKingdom.InitialRegime();
            office = pKingdom.GetOffice();
        }
        if (office != null)
        {
            office.meta_object = pKingdom;
            office.SetActor(heir);
        }

        // 帝国换皇帝后的即位分封统一在 KingdomPatch 的 setKing 补丁里处理(EnfeoffmentHelper.OnEmperorSucceeded)，
        // 这样选举、拥立等不经过这里的即位方式也会分封。

        // 帝国下的诸侯/附庸国换了国王：按宗主权威与封国自主判定册封、请封还是自立。
        if (!pKingdom.IsEmpire()) VassalInvestitureService.OnVassalSuccession(pKingdom, heir);
    }
    public void TryToGiveGoldenTooth(Actor pActor)
    {
        if (pActor.getAge() > 45 && Randy.randomChance(0.05f))
        {
            pActor.addTrait("golden_tooth");
        }
    }
    
    public void MakeKingAndMoveToCapital(Kingdom pKingdom, Actor pNewKing)
    {
        if (pNewKing.hasCity())
        {
            pNewKing.removeFromArmy();
            if (pNewKing.isCityLeader())
            {
                pNewKing.city.removeLeader();
            }
        }
    }
}
