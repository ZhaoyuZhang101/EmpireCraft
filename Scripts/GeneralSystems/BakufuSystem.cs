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
//   · 开幕：天皇家(开幕时的皇族宗族)退居朝廷，天皇成为虚君；实权交给征夷大将军——
//     帝国里不属天皇一系、领地最大的成员国君主(镇守府、藩国优先)，找不到就由朝中最高的武官出任；
//     将军一系成为帝国的执政宗族(继承在将军家传承)。找不到合适人选时由在位者兼任将军，天皇家仍记下；
//   · 天皇：天皇家在世族人里辈分最高、年纪最大的男子(开幕时的天皇在世就是他)，帝国窗口显示；
//   · 大政奉还(明治维新等离开幕府政体时)：天皇家的天皇重新执政，执政宗族改回天皇家。
public static class BakufuSystem
{
    public static bool IsBakufu(Empire empire) => empire?.CoreKingdom?.GetRegime()?.type == RegimeType.Bakufu;

    public static void OnRegimeChanged(Empire empire, RegimeType? previous, RegimeType current)
    {
        if (empire?.data == null || empire.CoreKingdom == null || previous == current) return;
        try
        {
            if (current == RegimeType.Bakufu) Establish(empire);
            else if (previous == RegimeType.Bakufu) Restore(empire);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][幕府] {empire.GetEmpireName()} 政权交接失败: {exception.Message}");
        }
    }

    private static void Establish(Empire empire)
    {
        Kingdom core = empire.CoreKingdom;
        SpecificClan court = empire.EmpireSpecificClan;
        Actor emperor = core.king;
        empire.data.court_clan_id = court?.id ?? -1L;
        empire.data.court_emperor_id = emperor?.data != null ? emperor.getID() : -1L;
        Actor shogun = PickShogun(empire, court);
        if (shogun == null || shogun == emperor)
        {
            Record(empire, string.Format(LM.Get("bakufu_established_self_history"), empire.GetEmpireName(),
                emperor?.getName() ?? ""));
            return;
        }
        core.GetOffice().meta_object = core;
        core.GetOffice().SetActor(shogun);
        if (core.king != shogun)
        {
            Record(empire, string.Format(LM.Get("bakufu_established_self_history"), empire.GetEmpireName(),
                emperor?.getName() ?? ""));
            return;
        }
        if (shogun.GetSpecificClan() is SpecificClan shogunClan) empire.data.empire_specific_clan = shogunClan.id;
        Record(empire, string.Format(LM.Get("bakufu_established_history"), empire.GetEmpireName(), shogun.getName(),
            emperor?.getName() ?? ""));
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
        core.GetOffice().meta_object = core;
        core.GetOffice().SetActor(emperor);
        if (core.king != emperor) return;
        empire.data.empire_specific_clan = court.id;
        Record(empire, string.Format(LM.Get("bakufu_restoration_history"), empire.GetEmpireName(), shogun?.getName() ?? "",
            emperor.getName()));
    }

    // 将军人选：不属天皇一系、领地最大的成员国君主(镇守府、藩国优先)；没有就取朝中最高的武官
    private static Actor PickShogun(Empire empire, SpecificClan court)
    {
        bool Eligible(Actor actor) => actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
                                      (court == null || !SpecificClanManager.SameLineage(actor.GetSpecificClan(), court)) &&
                                      actor.CanServeOffice(empire.CoreKingdom);
        Actor lord = empire.kingdoms_list?
            .Where(kingdom => kingdom != null && !kingdom.isRekt() && kingdom != empire.CoreKingdom && Eligible(kingdom.king))
            .OrderByDescending(kingdom => kingdom.GetKingdomType() == KingdomType.LvLing_jiedushi)
            .ThenByDescending(kingdom => kingdom.cities?.Count ?? 0)
            .Select(kingdom => kingdom.king).FirstOrDefault();
        if (lord != null) return lord;
        return empire.CoreKingdom.units?
            .Where(actor => Eligible(actor) && actor.HasOfficeIdentity() && actor.GetIdentity()?.officialLevel is >= 9 and <= 12)
            .OrderBy(actor => actor.GetIdentity().officialLevel).ThenByDescending(actor => actor.data?.renown ?? 0)
            .FirstOrDefault();
    }

    // 天皇：开幕时的天皇在世就是他；否则天皇家在世族人里辈分最高、年纪最大的男子(没有男子取女子)
    public static Actor Emperor(Empire empire)
    {
        if (empire?.data == null || empire.data.court_clan_id < 0) return null;
        Actor recorded = empire.data.court_emperor_id > 0 ? World.world.units.get(empire.data.court_emperor_id) : null;
        if (recorded?.data != null && !recorded.isRekt() && recorded.isAlive()) return recorded;
        SpecificClan court = SpecificClanManager.Get(empire.data.court_clan_id);
        Actor heir = court?.AllAliveMembers?
            .Where(actor => actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.GetPersonalIdentity()?.is_main == true)
            .OrderByDescending(actor => actor.isSexMale())
            .ThenBy(actor => actor.GetPersonalIdentity()?.generation ?? int.MaxValue)
            .ThenByDescending(actor => actor.getAge()).FirstOrDefault();
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
