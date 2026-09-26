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
using NeoModLoader.services;

namespace EmpireCraft.Scripts.AI.KingdomAI;

public class EmpireCraftKingdomBehCheckKing : GameAIKingdomBase
{
    public override Type OriginalBeh => typeof(KingdomBehCheckKing);
    public override BehResult execute(Kingdom pKingdom)
    {
        if (pKingdom.data.timer_new_king > 0f)
        {
            return BehResult.Continue;
        }
        if (pKingdom.hasKing())
        {
            Actor king = pKingdom.king;
            if (king.isAlive())
            {
                TryToGiveGoldenTooth(king);
                king.CheckSpecificClan();
                return BehResult.Continue;
            }
        }
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
            if (pKingdom.HasHeir())
            {
                ChooseKingFromHeir(pKingdom);  
                return BehResult.Continue;
            }

            if (pKingdom.IsEmpire())
            {
                return BehResult.Continue;
            }
        } 
        OfficeObject office = pKingdom.GetOffice();
        if (office == null) return BehResult.Continue;
        office.is_local = true;
        office.meta_object = pKingdom;
        office.Select(pKingdom, "国家");
        return BehResult.Continue;
    }

    public static bool NeedSuccession(Kingdom pKingdom)
    {
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
            if (pKingdom.IsEmpire())
            {
                Empire empire = pKingdom.GetEmpire();
                if (empire != null && !empire.isRekt() && !empire.IsArchived())
                {
                    empire.CoreKingdom = lastKingdom;
                }
            }
            pKingdom.cities.ForEach(c=>{ if (c != null && !c.isRekt()) c.joinAnotherKingdom(lastKingdom); });
            return;
        }

        if (lastKingdom != null && lastKingdom.GetRegime() is {type: RegimeType.LvLing or RegimeType.ZhouFeudalism})
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
