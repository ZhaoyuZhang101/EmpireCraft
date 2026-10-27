using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 世家防绝嗣(用户 2026-10-27："世家几乎没什么存在感而且经常绝嗣"，问答选"防绝嗣：多子与过继")：
//   · 多子：世家族人生育按宗室与平民之间(见 VirtualGenealogySystem.TryBirth 的 Gentry* 常量)；
//   · 过继：每年结算门第时，世家在世族人里没有能承嗣的男丁(承嗣一方、AdoptAge 岁以下)，族里还有成年人，
//     就从同一世系的支脉过继一位年轻男丁为嗣(本支至少留一个)，没有支脉可过继就从族中远房记一位族子入谱
//     (无小人模式；有实体小人时照原版生育)；
//   · 支脉承嗣：本宗凋尽，同一世系里在世族人最多、还不是世家的一支承其门第(打九折)、郡望与列为世家的时间，
//     史书记"某氏本宗凋尽，某支承其门第郡望"；没有支脉才是"世家遂绝"。
public static partial class GentrySystem
{
    private const int AdoptAge = 50, AdopterMinAge = 25;
    private const float BranchPedigreeShare = 0.9f;

    // 同一世系里别的支(不是皇族)，族人多的在前
    private static IEnumerable<SpecificClan> Kin(SpecificClan clan) =>
        SpecificClanManager.LineageClans(clan).Where(other => other != null && other != clan && !Ruling.Contains(other))
            .OrderByDescending(other => other.LivingPeople.Count);

    private static void TryAdopt(SpecificClan clan, List<PersonalClanIdentity> living)
    {
        if (!CityPopulationSystem.AbstractPopulationEnabled ||
            living.Any(person => person.is_main && person.IsHeirPriority() && person.age < AdoptAge)) return;
        PersonalClanIdentity adopter = living.Where(person => person.age >= AdopterMinAge)
            .OrderByDescending(person => person.IsHeirPriority()).ThenByDescending(person => person.age).FirstOrDefault();
        if (adopter == null) return;
        string from = null;
        PersonalClanIdentity heir = VirtualGenealogySystem.AdoptHeir(clan, adopter, Kin(clan));
        if (heir == null) return;
        if (heir.specific_clan_id == clan.id && heir.father > 0 && SpecificClanManager.getPerson(heir.father) is PersonalClanIdentity father &&
            father.specific_clan_id != clan.id)
            from = father._specificClan?.branch_label;
        string text = string.Format(LM.Get(string.IsNullOrEmpty(from) ? "gentry_adopt_history" : "gentry_adopt_branch_history"),
            JunWang(clan), adopter.name, heir.name, from ?? "");
        NoteClan(clan, "clan_history_gentry_adopt", text);
        LogService.LogInfo("[EmpireCraft][世家] " + text);
    }

    // 本宗凋尽：支脉承嗣；返回是否有支承了
    private static bool BranchSucceeds(SpecificClan clan)
    {
        SpecificClan heir = Kin(clan).FirstOrDefault(other => other.extinct_since < 0d && !other.gentry && other.LivingPeople.Count > 0);
        if (heir == null) return false;
        string from = JunWang(clan);
        heir.gentry = true;
        heir.gentry_assessed = true;
        heir.gentry_pedigree = Mathf.Max(heir.gentry_pedigree, clan.gentry_pedigree * BranchPedigreeShare);
        heir.gentry_seat_city_id = clan.gentry_seat_city_id;
        heir.gentry_since = clan.gentry_since;
        heir.gentry_patron_empire_id = clan.gentry_patron_empire_id;
        clan.gentry = false;
        string branch = string.IsNullOrWhiteSpace(heir.branch_label) ? LM.Get("gentry_branch_default") : heir.branch_label;
        string text = string.Format(LM.Get("gentry_branch_succession_history"), from, branch);
        Record(Seat(heir), text, force: true);
        NoteClan(heir, "clan_history_gentry_branch", text);
        NoteClan(clan, "clan_history_gentry_branch", text);
        return true;
    }
}
