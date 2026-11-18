using System;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;
using EmpireCraft.Scripts.System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 幕府(用户 2026-10-16 选定"新增幕府政体，其余复用换名")：日本制度线施行"幕府"后政体改为 Bakufu——
//   · 开幕：天皇照旧在位，成为傀儡；征夷大将军以权臣掌实权、在将军家承袭(用户 2026-10-19 选"改用权臣机制"，
//     见 MilitaryRegency；以前将军坐上君位、执政宗族换成将军家，天皇只记个名字)；将军家衰微或被拉下马时另择一家、
//     将军更替时诸藩可能举兵自立(用户 2026-10-21 选"幕府会倒、应仁之乱"，见 MilitaryRegency)；
//   · 大政奉还(明治维新等离开幕府政体时)：不再有将军，天皇亲政；
//   · 废藩置县：大政奉还后改行律令(明治维新)，世袭的藩一律改为朝廷选任的县(用户 2026-10-19 选"补……明治废藩"；
//     以前换政体时各藩照旧世袭、有外交权，明治之后仍是一国诸藩)。
public static class BakufuSystem
{
    public static bool IsBakufu(Empire empire) => empire?.CoreKingdom?.GetRegime()?.type == RegimeType.Bakufu;

    public static void OnRegimeChanged(Empire empire, RegimeType? previous, RegimeType current)
    {
        if (empire?.data == null || empire.CoreKingdom == null || previous == current) return;
        try
        {
            if (current == RegimeType.Bakufu) MilitaryRegency.Refresh(empire);
            else if (previous == RegimeType.Bakufu)
            {
                // 旧档(将军坐在君位上)先请天皇家复位
                if (empire.data.court_clan_id >= 0) Restore(empire);
                MilitaryRegency.Dissolve(empire, "bakufu_restoration_history");
                if (current == RegimeType.LvLing) AbolishHan(empire);
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][幕府] {empire.GetEmpireName()} 政权交接失败: {exception.Message}");
        }
    }

    private static void Restore(Empire empire)
    {
        Kingdom core = empire.CoreKingdom;
        SpecificClan court = SpecificClanManager.Get(empire.data.court_clan_id);
        Actor emperor = Emperor(empire);
        empire.data.court_clan_id = -1L;
        empire.data.court_emperor_id = -1L;
        if (court == null || emperor == null || emperor == core.king) return;
        Actor shogun = core.king;
        // 先把皇室记回天皇家(万世一系只许皇室一系坐皇位，见 MilitaryRegency.Migrate)
        long shogunHouse = empire.data.empire_specific_clan;
        empire.data.empire_specific_clan = court.id;
        core.GetOffice().meta_object = core;
        core.GetOffice().SetActor(emperor);
        if (core.king != emperor)
        {
            empire.data.empire_specific_clan = shogunHouse;
            return;
        }
        Record(empire, string.Format(LM.Get("bakufu_restoration_history"), empire.GetEmpireName(), shogun?.getName() ?? "",
            emperor.getName()));
    }

    // 废藩置县：世袭或有外交权的成员(藩)改为不世袭、无外交权、朝廷选任的县；羁縻的不动
    private static void AbolishHan(Empire empire)
    {
        int abolished = 0;
        foreach (Kingdom member in empire.kingdoms_list?.ToList() ?? new global::System.Collections.Generic.List<Kingdom>())
        {
            if (member?.data == null || member.isRekt() || member == empire.CoreKingdom ||
                JimiSystem.IsJimiAdministration(member)) continue;
            Regime regime = member.GetRegime();
            if (regime == null || regime.type != RegimeType.LvLing ||
                !regime.IsAllowDiplomacy() && regime.GetLeaderSelectMethod() != LeaderSelectMethod.Succession) continue;
            regime.SetAllowDiplomacy(false);
            regime.SetLeaderSelectMethod(LeaderSelectMethod.Exam);
            EmpireCraft.Scripts.AI.KingdomAI.EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(member);
            abolished++;
        }
        if (abolished > 0)
            Record(empire, string.Format(LM.Get("bakufu_han_abolished_history"), empire.GetEmpireName(), abolished));
    }

    // 旧档(将军坐在君位上)的天皇：开幕时的天皇在世就是他；否则天皇家在世族人里辈分最高、年纪最大的男子(没有男子取女子)
    public static Actor Emperor(Empire empire)
    {
        if (empire?.data == null || empire.data.court_clan_id < 0) return null;
        // 记着的天皇按族谱找回(并回人口、落成换了编号的，见 PersonRef；之后作天皇留在地图上)
        long recordedId = empire.data.court_emperor_id, recordedIdentity = -1L;
        Actor recorded = recordedId > 0 ? PersonRef.Resolve(ref recordedId, ref recordedIdentity) : null;
        if (recorded != null)
        {
            empire.data.court_emperor_id = recorded.getID();
            return recorded;
        }
        SpecificClan court = SpecificClanManager.Get(empire.data.court_clan_id);
        Actor heir = court?.AllAliveMembers?
            .Where(actor => actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.GetPersonalIdentity()?.is_main == true)
            .OrderByDescending(actor => actor.isSexMale())
            .ThenBy(actor => actor.GetPersonalIdentity()?.generation ?? int.MaxValue)
            .ThenByDescending(actor => actor.getAge()).FirstOrDefault();
        // 天皇家只剩虚拟族人(纯净地图下多是这样，以前就找不到天皇)：照同样的先后落成一位
        heir ??= KinRoster.RealizeFirst(court?.LivingPeople
                     .Where(person => KinRoster.Virtual(person) && person.is_main)
                     .OrderByDescending(person => person.sex == ActorSex.Male).ThenBy(person => person.generation)
                     .ThenByDescending(person => person.age),
            actor => actor.isAlive() && !actor.isRekt(), 2);
        if (heir != null) empire.data.court_emperor_id = heir.getID();
        return heir;
    }

    private static void Record(Empire empire, string content)
    {
        LogService.LogInfo($"[EmpireCraft][幕府] {content}");
        empire.RecordHistory(directContent: content, kingdomId: empire.CoreKingdom.id);
        TranslateHelper.LogEventMessage(content, empire.CoreKingdom);
    }
}
