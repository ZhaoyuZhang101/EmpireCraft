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

namespace EmpireCraft.Scripts.GeneralSystems;

// 武家执政(用户 2026-10-19 选"改用权臣机制")：幕府的征夷大将军、阿拉伯施行"苏丹官僚"后的苏丹都不坐君位——
//   · 天皇、哈里发照旧在位，成为傀儡(见 Empire 的权臣：is_been_controlled)；将军、苏丹以权臣掌实权，朝议由他批
//     (见 CourtSystem.Decider)，不走篡位那一套(权臣谋位只给律令制)；
//   · 人选：不属皇族一系、领地最大的成员国君主(苏丹国、镇守府优先)，没有就取朝中最高的武官；
//   · 承袭：死了由他家族里辈分最高的成年男子接任；家族绝了，另择一家；
//   · 收场：离开幕府政体(明治维新大政奉还)、离开阿拉伯政体时不再有将军、苏丹。
// 以前将军、苏丹直接坐上君位、执政宗族换成他家，天皇、哈里发只记个名字——旧档读进来按新做法改回(见 Migrate)。
public static class MilitaryRegency
{
    public const int None = 0, Shogun = 1, Sultan = 2;
    // 各帝国上次检查的时间(不存档)
    private static readonly Dictionary<long, double> LastCheck = new();
    private static object _world;

    public static bool Active(Empire empire) => empire?.data != null && empire.data.military_regency != None;

    public static int KindOf(Empire empire) => empire?.data?.military_regency ?? None;

    public static Actor Regent(Empire empire) => Active(empire) ? empire.GetPowerfulMinister() : null;

    // 该不该有将军/苏丹：幕府政体，或施行了苏丹官僚的阿拉伯政体
    private static int Wanted(Empire empire)
    {
        RegimeType? regime = empire?.CoreKingdom?.GetRegime()?.type;
        if (regime == RegimeType.Bakufu) return Shogun;
        if (regime == RegimeType.Arabic && InstitutionSystem.HasFeature(empire, InstitutionFeatures.Sultanate)) return Sultan;
        return None;
    }

    public static string TitleKey(Empire empire) => KindOf(empire) == Shogun ? "bakufu_shogun_title" : "sultanate_regent_title";

    // 立即检查一次(开幕、施行苏丹官僚时)
    public static void Refresh(Empire empire)
    {
        if (empire?.data == null) return;
        LastCheck.Remove(empire.id);
        Maintain(empire);
    }

    // 帝国每次更新调用(幕府、阿拉伯政体)；每月才真正检查一次
    public static void Maintain(Empire empire)
    {
        if (empire?.data == null || empire.CoreKingdom == null || World.world == null) return;
        if (!ReferenceEquals(_world, World.world))
        {
            _world = World.world;
            LastCheck.Clear();
        }
        if (Active(empire)) Assert(empire);
        double now = World.world.getCurWorldTime();
        if (LastCheck.TryGetValue(empire.id, out double last) && now >= last && Date.getMonthsSince(last) < 1) return;
        LastCheck[empire.id] = now;
        int wanted = Wanted(empire);
        if (wanted == None)
        {
            if (Active(empire)) Dissolve(empire, null);
            else if (empire.data.powerful_minister_id > 0) empire.ResetPowerfulMinister();
            return;
        }
        if (empire.data.court_clan_id >= 0) Migrate(empire);
        if (!Active(empire))
        {
            Establish(empire, wanted);
            return;
        }
        if (Regent(empire) == null) Succeed(empire);
    }

    // 将军、苏丹在任：保持挟制(别处的清算、换代会把挟制解开)
    private static void Assert(Empire empire)
    {
        Actor regent = empire.GetPowerfulMinister();
        if (regent == null) return;
        var data = empire.data;
        data.is_been_controlled = true;
        data.powerful_minister_progress = 100;
        if (data.powerful_minister_stage < Empire.PowerfulMinisterStageDominant)
            data.powerful_minister_stage = Empire.PowerfulMinisterStageDominant;
        data.powerful_minister_emperor_id = empire.Emperor?.id ?? -1L;
    }

    private static void Install(Empire empire, Actor regent, int kind)
    {
        empire.ResetPowerfulMinister();
        var data = empire.data;
        double now = World.world.getCurWorldTime();
        data.military_regency = kind;
        data.regent_clan_id = regent.GetSpecificClan()?.id ?? -1L;
        data.powerful_minister_id = regent.getID();
        data.powerful_minister_stage_timestamp = now;
        data.last_powerful_minister_timestamp = now;
        Assert(empire);
    }

    private static void Establish(Empire empire, int kind)
    {
        Actor emperor = empire.Emperor;
        Actor regent = Pick(empire, kind);
        if (emperor?.data == null || regent == null) return;
        Install(empire, regent, kind);
        string culture = CultureService.GetRealmCulture(empire.CoreKingdom);
        string key = kind == Shogun ? "bakufu_established_history" : "sultanate_established_history";
        Record(empire, string.Format(LM.Get(DynastyNaming.CultureVariant(key, culture) ?? key), empire.GetEmpireName(),
            regent.getName(), emperor.getName()));
    }

    // 承袭：家族里辈分最高的成年男子；家族绝了另择一家
    private static void Succeed(Empire empire)
    {
        int kind = KindOf(empire);
        SpecificClan clan = SpecificClanManager.Get(empire.data.regent_clan_id);
        Actor heir = clan?.AllAliveMembers?
            .Where(actor => Eligible(empire, actor))
            .OrderByDescending(actor => actor.isSexMale())
            .ThenBy(actor => actor.GetPersonalIdentity()?.generation ?? int.MaxValue)
            .ThenByDescending(actor => actor.getAge()).FirstOrDefault() ?? Pick(empire, kind);
        if (heir == null)
        {
            empire.ResetPowerfulMinister();
            return;
        }
        Install(empire, heir, kind);
        Record(empire, string.Format(LM.Get("military_regency_succession"), heir.getName(), LM.Get(TitleKey(empire)),
            empire.GetEmpireName()));
        // 苏丹更替：积弊深重或正统低落时世袭的埃米尔可能举兵自立(见 NomadCycleSystem 的哈里发更替之乱)
        if (kind == Sultan) NomadCycleSystem.OnSultanSucceeded(empire, heir);
    }

    // 收场：大政奉还(幕府)、不再有苏丹(阿拉伯)
    public static void Dissolve(Empire empire, string historyKey)
    {
        if (!Active(empire)) return;
        Actor regent = Regent(empire);
        empire.data.military_regency = None;
        empire.data.regent_clan_id = -1L;
        empire.ResetPowerfulMinister();
        if (historyKey != null && regent != null && empire.Emperor != null)
            Record(empire, string.Format(LM.Get(historyKey), empire.GetEmpireName(), regent.getName(), empire.Emperor.getName()));
    }

    private static bool Eligible(Empire empire, Actor actor) =>
        actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() && actor != empire.Emperor &&
        actor.kingdom?.GetEmpire() == empire &&
        !SpecificClanManager.SameLineage(actor.GetSpecificClan(), empire.EmpireSpecificClan);

    // 人选：不属皇族一系、领地最大的成员国君主(苏丹国、镇守府优先)；没有就取朝中最高的武官
    private static Actor Pick(Empire empire, int kind)
    {
        Actor lord = empire.kingdoms_list?
            .Where(kingdom => kingdom != null && !kingdom.isRekt() && kingdom != empire.CoreKingdom && Eligible(empire, kingdom.king))
            .OrderByDescending(kingdom => kingdom.GetKingdomType() ==
                                          (kind == Sultan ? KingdomType.Arabic_sultanate : KingdomType.LvLing_jiedushi))
            .ThenByDescending(kingdom => kingdom.cities?.Count ?? 0)
            .Select(kingdom => kingdom.king).FirstOrDefault();
        if (lord != null) return lord;
        return empire.CoreKingdom.units?
            .Where(actor => Eligible(empire, actor) && actor.HasOfficeIdentity() &&
                            actor.GetIdentity()?.officialLevel is >= 9 and <= 12)
            .OrderBy(actor => actor.GetIdentity().officialLevel).ThenByDescending(actor => actor.data?.renown ?? 0)
            .FirstOrDefault();
    }

    // 旧档：将军坐在君位上、天皇家记在 court_clan_id——请天皇家复位，坐着君位的将军改为权臣
    private static void Migrate(Empire empire)
    {
        var data = empire.data;
        Actor shogun = empire.CoreKingdom.king;
        Actor emperor = BakufuSystem.Emperor(empire);
        SpecificClan court = SpecificClanManager.Get(data.court_clan_id);
        data.court_clan_id = -1L;
        data.court_emperor_id = -1L;
        if (court == null || emperor?.data == null || shogun?.data == null || emperor == shogun) return;
        Kingdom core = empire.CoreKingdom;
        core.GetOffice().meta_object = core;
        core.GetOffice().SetActor(emperor);
        if (core.king != emperor) return;
        data.empire_specific_clan = court.id;
        if (Eligible(empire, shogun)) Install(empire, shogun, Wanted(empire) == None ? Shogun : Wanted(empire));
        LogService.LogInfo($"[EmpireCraft][武家执政] {empire.GetEmpireName()} 旧档迁移：{emperor.getName()} 复位，{shogun.getName()} 为权臣");
    }

    private static void Record(Empire empire, string content)
    {
        LogService.LogInfo($"[EmpireCraft][武家执政] {content}");
        empire.RecordHistory(directContent: content, kingdomId: empire.CoreKingdom.id);
        TranslateHelper.LogEventMessage(content, empire.CoreKingdom);
    }
}
