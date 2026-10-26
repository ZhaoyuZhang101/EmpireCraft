using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 教宗推举(用户 2026-10-27 选"改成推举、不再传子"：以前教宗就是教宗国的国君，照教宗国的继承法传给子孙)：
//   · 教宗国(普世教会的教廷所在、类型为教宗国)君位出缺时不走继承(见 EmpireCraftKingdomBehCheckKing)：由枢机推举——
//     人选出自教宗国都城与本教各主教领的都城，有教士特质的优先，再按威望；选上的入住圣城、加教士特质、改奉本教；
//   · 世俗强立：教宗国是某帝国的成员或附庸、或教廷之囚落在帝国手里(RealmNamingSystem.ControlsPapacy)时，那位皇帝有
//     ImposeChance 机会从帝都派自己的人选(同族优先)；
//   · 记下选立方式与时间(教宗正统的一项，见 PopeLegitimacy)，写进宗教大事记、世界日志与本人生平。
//   圣座帝国(教宗国自己做了帝国中央)不在此列，照帝国继承。
public static class PapalElection
{
    public const int ByConclave = 1, ByImposition = 2;
    private const float ImposeChance = 0.6f;

    public static bool TryHandleVacancy(Kingdom kingdom)
    {
        try
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.hasKing() || kingdom.capital == null || kingdom.IsEmpire() ||
                kingdom.GetKingdomType() != KingdomType.Feudalism_papal_state || !kingdom.hasReligion()) return false;
            Religion religion = kingdom.religion;
            if (!ReligionFaith.IsPapal(religion) || PapacySystem.Papacy(religion) != kingdom) return false;
            Empire patron = Patron(kingdom);
            bool imposed = patron?.CoreKingdom?.capital != null && UnityEngine.Random.value < ImposeChance;
            Actor pick = imposed
                ? Pick(new[] { patron.CoreKingdom.capital }, patron.Emperor)
                : Pick(Conclave(kingdom, religion), null);
            if (pick == null)
            {
                imposed = false;
                pick = CityPopulationSystem.SpawnOfficial(kingdom.capital, kingdom);
            }
            if (pick == null) return false;
            if (pick.city != kingdom.capital) pick.joinCity(kingdom.capital);
            GraceEdictService.Crown(kingdom, pick);
            if (kingdom.king != pick) return false;
            if (!pick.hasTrait("cleric")) pick.addTrait("cleric");
            if (pick.religion != religion) pick.setReligion(religion);
            ReligionExtension.ReligionExtraData data = religion.GetOrCreate();
            data.LeaderID = pick.id;
            data.PopeChosenBy = imposed ? ByImposition : ByConclave;
            data.PopeChosenAt = World.world.getCurWorldTime();
            string text = imposed
                ? string.Format(LM.Get("pope_imposed_history"), patron.Emperor?.getName() ?? patron.GetEmpireName(), pick.getName(),
                    religion.name)
                : string.Format(LM.Get("pope_elected_history"), kingdom.capital.GetCityName(), pick.getName(), religion.name);
            ReligionChronicle.Record(religion, ReligionChronicle.KindPapacy, text);
            TranslateHelper.LogEventMessage(text, kingdom);
            pick.RecordPersonalHistory(text);
            LogService.LogInfo($"[EmpireCraft][教廷] {religion.name} 新教宗 {pick.getName()}({(imposed ? "世俗强立" : "枢机推举")})");
            return true;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][教廷] 推举教宗失败: {exception.Message}");
            return false;
        }
    }

    // 控制教廷的帝国：教宗国是其成员、附庸，或教廷之囚落在它手里
    private static Empire Patron(Kingdom papacy)
    {
        foreach (Empire empire in ModClass.EMPIRE_MANAGER?.ToList() ?? new List<Empire>())
            if (empire?.data != null && !empire.isRekt() && !empire.IsArchived() && empire.CoreKingdom != papacy &&
                empire.Emperor != null && RealmNamingSystem.ControlsPapacy(empire, papacy)) return empire;
        return null;
    }

    // 枢机所在：教宗国都城与本教各主教领的都城
    private static IEnumerable<City> Conclave(Kingdom papacy, Religion religion)
    {
        yield return papacy.capital;
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
            if (kingdom?.data != null && !kingdom.isRekt() && kingdom != papacy && kingdom.religion == religion &&
                kingdom.capital != null && HolyRomanSystem.IsChurchEstate(kingdom)) yield return kingdom.capital;
    }

    private static Actor Pick(IEnumerable<City> cities, Actor patron) =>
        cities.SelectMany(Candidates).Distinct()
            .OrderByDescending(actor => actor.hasTrait("cleric"))
            .ThenByDescending(actor => patron != null && SameHouse(actor, patron))
            .ThenByDescending(actor => actor.data.renown).FirstOrDefault();

    private static IEnumerable<Actor> Candidates(City city) =>
        (city?.units ?? new List<Actor>()).ToList().Where(actor => actor?.data != null && !actor.isRekt() && actor.isAlive() &&
                                                                    actor.isAdult() && !actor.isKing() && !actor.isCityLeader() &&
                                                                    actor.isUnitFitToRule());

    private static bool SameHouse(Actor a, Actor b) =>
        a?.data != null && b?.data != null && SpecificClanManager.SameLineage(a.GetSpecificClan(), b.GetSpecificClan());
}
