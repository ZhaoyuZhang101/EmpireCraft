using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.System;

namespace EmpireCraft.Scripts.GeneralSystems;

// Naming events only; never scan the population or infer paternity from a surname.
public static class SurnameInheritanceSystem
{
    public static Actor SelectParent(IEnumerable<Actor> parents) => parents?
        .Where(parent => parent?.data != null && !string.IsNullOrWhiteSpace(FamilyNameOf(parent)))
        .Distinct().OrderByDescending(ParentPriority).ThenBy(parent => parent.getID()).FirstOrDefault();

    // 父母的姓：人物名字上的姓；名字上没有(旧档的族谱只记了全名)取族谱的姓。以前名字上没姓的父亲不算，
    // 孩子随了母亲的姓(聂郁民之子成了步鲸)
    public static string FamilyNameOf(Actor actor)
    {
        string family = actor?.GetModName()?.familyName;
        return !string.IsNullOrWhiteSpace(family) ? family : SurnameOf(actor?.GetPersonalIdentity());
    }

    // 在位的君主、储君(女主也一样)：配偶嫁入或入赘皇族，子女随君主姓、入皇族宗族，帝系不断
    public static bool Reigns(Actor actor)
    {
        if (actor?.data == null || actor.isRekt()) return false;
        if (actor.isKing()) return true;
        Kingdom kingdom = actor.kingdom;
        return kingdom?.data != null && !kingdom.isRekt() && kingdom.GetOrCreate().HeirID == actor.getID();
    }

    private static int ParentPriority(Actor parent)
    {
        // 君主、储君的子女随君主姓(女帝与皇夫所生的也是皇族，以前跟了皇夫的姓，帝系断开、姓氏跳来跳去)
        if (Reigns(parent)) return 1000 + (parent.isSexMale() ? 10 : 0);
        var identity = parent.GetPersonalIdentity();
        // 华夏一系宗法父系：随父姓(入赘的除外)。以前"婚姻有记录的一方"优先，生母有婚姻记录、生父没有时，
        // 皇子随了生母的姓(聂志之子成了欧阳朗，此后一路姓欧阳)
        if (parent.isSexMale() && identity?._specificClan?.Patrilineal() == true && identity.is_main) return 500;
        // 华夏母系氏族阶段：随母姓、入母亲的氏族(见 PrehistoricClanSystem)
        if (!parent.isSexMale() && identity?._specificClan?.Matrilineal() == true && identity.is_main) return 500;
        // Respect the existing marriage/concubinage and maternal/paternal clan rules.
        int priority = identity == null ? 0 : !identity.is_main ? -100
            : identity.hasLover() || identity.is_concubine ? 100 : 0;
        if (identity?._specificClan != null && identity.IsHeirPriority()) priority += 20;
        if (parent.isSexMale()) priority += 10;
        return priority;
    }

    // 读档修正一次(见 VirtualGenealogySystem.EnsureIndex)：华夏一系宗族里与父亲同宗、却没随父姓的族人改回父姓
    // (以前随了生母的姓)。按编号(大致是出生先后)从上往下改，父亲改过的儿子跟着改；活着的人名字一起改，史书里的旧名不动
    public static int RepairPatrilineal(IEnumerable<PersonalClanIdentity> people)
    {
        int repaired = 0;
        foreach (PersonalClanIdentity person in people.Where(person => person != null).OrderBy(person => person.id))
        {
            if (!person.is_main || person.father <= 0 || person.matrilineal_birth) continue;
            SpecificClan clan = person._specificClan;
            if (clan == null || !clan.Patrilineal()) continue;
            PersonalClanIdentity father = SpecificClanManager.getPerson(person.father);
            if (father == null || father.specific_clan_id != person.specific_clan_id || string.IsNullOrWhiteSpace(father.surname) ||
                string.IsNullOrWhiteSpace(person.surname) || person.surname == father.surname || string.IsNullOrWhiteSpace(person.given_name))
                continue;
            ApplySurname(person, father.surname);
            repaired++;
        }
        return repaired;
    }

    // 改姓：族谱记录和(有实体的)人物名字一起改，史书里的旧名不动
    public static void ApplySurname(PersonalClanIdentity person, string surname)
    {
        if (person == null || string.IsNullOrWhiteSpace(surname) || person.surname == surname) return;
        person.surname = surname;
        Actor actor = person.is_alive && !person.is_virtual ? person._actor : null;
        if (actor?.data != null && actor.GetModName() != null)
        {
            actor.SetFamilyName(surname);
            actor.GetModName().SetName(actor);
            person.name = actor.getName();
        }
        else if (!string.IsNullOrWhiteSpace(person.given_name))
        {
            bool invert = OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(person.culture ?? "", out var rule) && rule?.Unit?.is_invert == true;
            person.name = invert ? OverallHelperFunc.JoinNameParts(person.given_name, person.surname)
                : OverallHelperFunc.JoinNameParts(person.surname, person.given_name);
        }
    }

    // 孩子入哪一族、随谁的姓：在位君主、储君一方；华夏一系宗法父系随父(入赘的除外)；
    // 其余是婚姻里为主、又是本宗继承性别的一方。都不是返回 null(按原版的氏族)
    public static PersonalClanIdentity ClanParent(IEnumerable<PersonalClanIdentity> parents)
    {
        List<PersonalClanIdentity> list = parents?.Where(parent => parent?._specificClan != null).ToList() ??
                                          new List<PersonalClanIdentity>();
        PersonalClanIdentity reigning = list.FirstOrDefault(parent => parent.is_alive && !parent.is_virtual && Reigns(parent._actor));
        if (reigning != null) return reigning;
        PersonalClanIdentity father = list.FirstOrDefault(parent => parent.sex == ActorSex.Male && parent.is_main &&
                                                                     parent._specificClan.Patrilineal());
        if (father != null) return father;
        PersonalClanIdentity mother = list.FirstOrDefault(parent => parent.sex != ActorSex.Male && parent.is_main &&
                                                                     parent._specificClan.Matrilineal());
        if (mother != null) return mother;
        return list.FirstOrDefault(parent => parent.is_main && parent.IsHeirPriority());
    }

    // 读档修正一次：华夏一系里父亲是本宗主位、本人却不在父亲那一族(以前入谱时跟了原版分给他的母亲的氏族)的，
    // 改回父亲的宗族、随父姓。父亲先改，儿子跟着改；分出去的诸王各支(同一世系)不动
    // moved：挪过族的人 → 原来的宗族 id(在位君主挪了族，国家、帝国记的皇族要跟着改)
    public static int RepairMaternalClan(IEnumerable<PersonalClanIdentity> people, Dictionary<long, long> moved = null)
    {
        int repaired = 0;
        foreach (PersonalClanIdentity person in people.Where(person => person != null).OrderBy(person => person.id).ToList())
        {
            if (!person.is_alive || !person.is_main || person.is_concubine || person.father <= 0 ||
                person.matrilineal_birth) continue;
            PersonalClanIdentity father = SpecificClanManager.getPerson(person.father);
            SpecificClan fatherClan = father?._specificClan;
            if (fatherClan == null || !father.is_main || !fatherClan.Patrilineal() || person.specific_clan_id == fatherClan.id) continue;
            if (SpecificClanManager.SameLineage(person._specificClan, fatherClan)) continue;
            // 女主(女帝、女王)所生、入了她那一族(皇族)的：配偶是嫁入、入赘皇族的，不往配偶那一族挪，否则帝系改姓
            PersonalClanIdentity mother = person.mother > 0 ? SpecificClanManager.getPerson(person.mother) : null;
            if (mother != null && mother.is_main && mother.specific_clan_id == person.specific_clan_id && Dynastic(person._specificClan))
                continue;
            moved?.Add(person.id, person.specific_clan_id);
            SpecificClanManager.MoveToClan(person, fatherClan);
            ApplySurname(person, !string.IsNullOrWhiteSpace(father.surname) ? father.surname : fatherClan.name);
            repaired++;
        }
        return repaired;
    }

    // 华夏一系宗法父系：本宗主位族人(生在这一族的，不含嫁入的、妾)一律姓这一族的姓。以前有几处随了母亲、
    // 落成时随手起的、旧档里记错的姓(女帝之子崔辉、聂郁民之子步鲸都在聂氏一族却不姓聂)，世系上看着是改了姓
    public static string ClanSurname(SpecificClan clan)
    {
        string name = clan?.name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 4 || name.Any(char.IsWhiteSpace) || name.Contains('\u200a')) return null;
        return clan.Patrilineal() ? name : null;
    }

    // 这个人该姓宗族的姓却没姓(族谱记的姓、全名、人物名字上的姓有一处不对)：改过来(活着的；史书里的旧名不动)。返回是否改了
    public static bool EnforceClanSurname(PersonalClanIdentity person)
    {
        if (person == null || !person.is_alive || !person.is_main || person.is_concubine) return false;
        string surname = ClanSurname(person._specificClan);
        if (surname == null) return false;
        Actor actor = person.is_virtual ? null : person._actor;
        bool hasActor = actor?.data != null && !actor.isRekt() && actor.GetModName() != null;
        string named = NameSurname(person);
        bool actorWrong = hasActor && actor.GetModName().familyName != surname;
        if (person.surname == surname && (named == null || named == surname) && !actorWrong) return false;
        if (person.surname != surname)
        {
            ApplySurname(person, surname);
            return true;
        }
        // 族谱的姓本来就对、只是全名或人物名字上不对的：跟着改
        if (hasActor)
        {
            actor.SetFamilyName(surname);
            actor.GetModName().SetName(actor);
            person.name = actor.getName();
        }
        else if (!string.IsNullOrWhiteSpace(person.given_name))
        {
            bool invert = OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(person.culture ?? "", out var rule) && rule?.Unit?.is_invert == true;
            person.name = invert ? OverallHelperFunc.JoinNameParts(person.given_name, surname)
                : OverallHelperFunc.JoinNameParts(surname, person.given_name);
        }
        return true;
    }

    // 读档修正：华夏宗族里在世的主位族人姓不是本族姓的，改回本族姓
    public static int RepairClanSurnames(IEnumerable<PersonalClanIdentity> people)
    {
        int repaired = 0;
        foreach (PersonalClanIdentity person in people.Where(person => person != null).ToList())
            if (EnforceClanSurname(person)) repaired++;
        return repaired;
    }

    // 做过一朝皇族的宗族
    private static bool Dynastic(SpecificClan clan) =>
        clan != null && (clan.HasHistoryEmpire() || !string.IsNullOrEmpty(clan.empire_name));

    // 一个人的姓：族谱记的姓；没记(旧档里很多)就取族谱名字里的姓，再没有取宗族名。
    // 不取有实体时的原版名字——落成时随手起了一个姓，子女跟着它就改了姓(崔周彬之子成了怀山)
    public static string SurnameOf(PersonalClanIdentity person)
    {
        if (person == null) return null;
        if (!string.IsNullOrWhiteSpace(person.surname)) return person.surname;
        return NameSurname(person) ?? person._specificClan?.name;
    }

    // 族谱全名里的姓(拆不出来为 null)
    private static string NameSurname(PersonalClanIdentity person)
    {
        string[] parts = (person?.name ?? "").SplitNameParts();
        if (parts.Length < 2) return null;
        bool invert = OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(person.culture ?? "", out var rule) && rule?.Unit?.is_invert == true;
        string part = invert ? parts[parts.Length - 1] : parts[0];
        return string.IsNullOrWhiteSpace(part) ? null : part;
    }

    public static bool TryInheritSurname(Actor child, bool overwrite = false)
    {
        if (child?.data == null) return false;
        Name name = child.GetModName();
        if (name == null || !overwrite && name.hasFamilyName(child)) return false;
        Actor parent = SelectParent(child.getParents());
        if (parent == null) return false;
        child.SetFamilyName(FamilyNameOf(parent));
        return true;
    }
}
