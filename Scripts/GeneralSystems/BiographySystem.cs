using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 生平与列传(用户 2026-10-19 选"生平补全与列传")：
//   · 死因：怎么死、死在哪、死于谁手写进生平(以前一律"逝世")。模组里赐死、兵变、清君侧、遇刺这些先登记死因
//     (Kill / NoteDeath)，原版的刀兵、疾病、寿终按死亡类型与最后的攻击者写；被杀的是要人时，下手的人生平也记一笔；
//   · 大事：就任、离任、加入派系、赴考、颁年号、生子这些例行公事以外的都算大事(记录时打上 major)。
//     生平超过上限、族谱瘦身时先删例行的，大事留着——以前只留第一条和最后几条，一生的起落全被删光；
//   · 列传：帝王、有爵有官、名将名臣，或生平里大事够多的人，生平页顶上有一段自动成文的列传
//     (籍贯出身、父祖、名号、一生大事、死因、谥号、子女)。
public static class BiographySystem
{
    // 生平里至多保留的大事条数(超过时删最早的大事)；已故者、族谱瘦身时的上限
    public const int MaxMajor = 24, MaxDeceasedMajor = 8;
    private const int ListedMajor = 10;

    #region 例行与大事

    private static readonly string[] RoutineKeys =
    {
        "personal_history_office_started", "personal_history_office_reappointed", "personal_history_office_left",
        "personal_history_joined_faction", "personal_history_exam_city_attended", "personal_history_exam_province_attended",
        "personal_history_exam_empire_attended", "personal_history_new_year_name", "personal_history_child_born",
        "personal_history_joined_cabinet", "personal_history_war_commander_dismissed", "personal_history_born"
    };

    private static List<Regex> _routine;
    private static string _routineLanguage;

    // 例行公事：内容套得上上面这几条文本的格式
    public static bool IsRoutine(string content)
    {
        if (string.IsNullOrEmpty(content)) return true;
        string language = LM.Get("personal_history_office_started");
        if (_routine == null || _routineLanguage != language)
        {
            _routineLanguage = language;
            _routine = RoutineKeys.Select(key => LM.Get(key)).Where(text => !string.IsNullOrEmpty(text))
                .Select(text => new Regex("^" + Regex.Replace(Regex.Escape(text), @"\\\{\d+\}", ".*") + "$",
                    RegexOptions.Compiled)).ToList();
        }
        foreach (Regex pattern in _routine)
            if (pattern.IsMatch(content)) return true;
        return false;
    }

    // 超过 cap 条时：第一条(出生)、最近 tail 条、至多 majors 条大事留下，其余例行的、过早的大事删去
    public static int Trim(List<PersonalHistoryRecord> history, int cap, int tail, int majors)
    {
        if (history == null || history.Count <= cap) return 0;
        var keep = new bool[history.Count];
        keep[0] = true;
        int kept = 1;
        for (int i = history.Count - 1; i >= Math.Max(1, history.Count - tail); i--)
        {
            keep[i] = true;
            kept++;
        }
        int majorKept = 0;
        for (int i = history.Count - 1; i >= 1 && kept < cap && majorKept < majors; i--)
        {
            if (keep[i] || history[i]?.major != true) continue;
            keep[i] = true;
            kept++;
            majorKept++;
        }
        for (int i = history.Count - 1; i >= 1 && kept < cap; i--)
        {
            if (keep[i]) continue;
            keep[i] = true;
            kept++;
        }
        int before = history.Count;
        var result = new List<PersonalHistoryRecord>(kept);
        for (int i = 0; i < history.Count; i++)
            if (keep[i]) result.Add(history[i]);
        history.Clear();
        history.AddRange(result);
        return before - history.Count;
    }

    #endregion

    #region 死因

    private sealed class Pending
    {
        public string Text;
        public long Killer = -1L;
        public bool Executed;
    }

    private static readonly Dictionary<long, Pending> PendingDeaths = new();

    public static void ResetWorldState() => PendingDeaths.Clear();

    // 登记死因(一个短语，如"为某所杀"；写进生平时补上享年)，下一次这个人死时写进生平；死的是要人时 killer 的生平也记一笔
    public static void NoteDeath(Actor victim, string causeText, Actor killer = null, bool executed = false)
    {
        if (victim?.data == null || string.IsNullOrEmpty(causeText)) return;
        PendingDeaths[victim.id] = new Pending { Text = causeText, Killer = killer?.data != null ? killer.id : -1L, Executed = executed };
        if (PendingDeaths.Count > 256) PendingDeaths.Remove(PendingDeaths.Keys.First());
    }

    // 登记死因后让他死
    public static void Kill(Actor victim, string causeText, Actor killer = null, bool executed = false, bool destroy = false,
        bool countDeath = true)
    {
        if (victim?.data == null || victim.isRekt() || !victim.isAlive()) return;
        NoteDeath(victim, causeText, killer, executed);
        victim.die(destroy, AttackType.Other, countDeath, countDeath);
    }

    public static bool WasExecuted(Actor actor) =>
        actor?.data != null && PendingDeaths.TryGetValue(actor.id, out Pending pending) && pending.Executed;

    // 写进生平的死因(ActorPatch.Die)：先看登记的，没有就按原版死因与最后的攻击者
    public static string DeathText(Actor actor, AttackType type, out Actor killer)
    {
        killer = null;
        int age = actor?.data == null ? 0 : Math.Max(0, (int)Math.Round((double)actor.getAge()));
        if (actor?.data != null && PendingDeaths.TryGetValue(actor.id, out Pending pending))
        {
            PendingDeaths.Remove(actor.id);
            killer = pending.Killer >= 0 ? World.world?.units?.get(pending.Killer) : null;
            return string.Format(LM.Get("death_custom"), pending.Text, age);
        }
        string place = Place(actor);
        if (actor?.attackedBy is BaseSimObject by && !by.isRekt() && by.isActor() && by.a != actor &&
            type is AttackType.Weapon or AttackType.Explosion or AttackType.Fire or AttackType.Poison)
        {
            killer = by.a;
            return string.Format(LM.Get("death_killed"), place, Who(killer), age);
        }
        string key = type switch
        {
            AttackType.Age => "death_age",
            AttackType.Weapon or AttackType.Explosion => "death_battle",
            AttackType.Plague or AttackType.Infection or AttackType.Tumor or AttackType.AshFever => "death_illness",
            AttackType.Starvation => "death_starved",
            AttackType.Poison => "death_poison",
            AttackType.Fire or AttackType.Acid => "death_fire",
            AttackType.Drowning or AttackType.Water => "death_drowned",
            AttackType.Eaten => "death_eaten",
            _ => "death_other"
        };
        return string.Format(LM.Get(key), place, age);
    }

    private static string Place(Actor actor)
    {
        City city = actor?.current_tile?.zone_city ?? actor?.city;
        string name = city?.data != null && !city.isRekt() ? city.GetCityName() : "";
        return string.IsNullOrWhiteSpace(name) ? LM.Get("death_place_wild") : name;
    }

    private static string Who(Actor actor)
    {
        string name = actor?.getName() ?? "";
        string realm = actor?.kingdom?.data != null && actor.kingdom.isCiv() ? actor.kingdom.GetKingdomName() : "";
        return string.IsNullOrEmpty(realm) ? name : string.Format(LM.Get("death_killer_of"), realm, name);
    }

    // 要人：在位的君主、有官职、有爵位封号、名将名臣
    public static bool Notable(Actor actor) =>
        actor?.data != null && (actor.isKing() || actor.IsOnOffice() || (actor.GetOwnedTitle()?.Count ?? 0) > 0 ||
                                FamousTalentSystem.IsFamous(actor) || actor.isCityLeader());

    // 死于人手：要人死了，下手的人也记一笔
    public static void RecordKiller(Actor victim, Actor killer)
    {
        if (killer?.data == null || killer.isRekt() || killer == victim || !Notable(victim) || killer.GetPersonalIdentity() == null) return;
        killer.RecordPersonalHistory(string.Format(LM.Get("death_killer_record"), victim.getName()), relatedActorId: victim.id,
            major: true);
        // 死者的子与兄弟与凶手结下杀亲之仇(见 PersonalTieSystem)
        PersonalTieSystem.OnKinKilled(victim, killer);
    }

    // 一句生平(大事)，人不在、没族谱身份的跳过
    public static void Note(Actor actor, string text, Actor related = null)
    {
        if (actor?.data == null || actor.isRekt() || string.IsNullOrEmpty(text) || actor.GetPersonalIdentity() == null) return;
        actor.RecordPersonalHistory(text, relatedActorId: related?.data != null ? related.id : -1L, major: true);
    }

    public static string Format(string key, params object[] args) => string.Format(LM.Get(key), args);

    #endregion

    #region 列传

    // 一段自动成文的列传；不够立传的返回空
    public static string Compose(PersonalClanIdentity person)
    {
        if (person == null) return "";
        List<PersonalHistoryRecord> history = person.personal_history ?? new List<PersonalHistoryRecord>();
        List<PersonalHistoryRecord> majors = history.Where(record => record != null && record.major).OrderBy(record => record.timestamp).ToList();
        TalentRecord talent = FamousTalentSystem.OfActorId(person.actor_id);
        bool titled = person.ownedTitleNames?.Count > 0 || !string.IsNullOrWhiteSpace(person.PeeragesLevel) ||
                      !string.IsNullOrWhiteSpace(person.posthumous_name) || !string.IsNullOrWhiteSpace(person.prince_title);
        if (!titled && talent == null && majors.Count(record => !IsDeath(record)) < 3) return "";

        var text = new StringBuilder();
        string name = string.IsNullOrWhiteSpace(person.name) ? "?" : person.name;
        text.Append(string.IsNullOrWhiteSpace(person.birthplace)
            ? string.Format(LM.Get("biography_opening"), name)
            : string.Format(LM.Get("biography_opening_place"), name, person.birthplace));
        PersonalClanIdentity father = person.father > 0 ? SpecificClanManager.getPerson(person.father) : null;
        if (!string.IsNullOrWhiteSpace(father?.name)) text.Append(string.Format(LM.Get("biography_father"), father.name));
        text.Append(LM.Get("biography_period"));
        if (talent != null)
        {
            string title = FamousTalentSystem.TitleText(talent);
            text.Append(string.Format(LM.Get("biography_talent"), FamousTalentSystem.RankName(talent.rank),
                FamousTalentSystem.KindName(talent.kind), string.IsNullOrEmpty(title) ? "" : string.Format(LM.Get("biography_title"), title)));
        }
        List<PersonalHistoryRecord> deeds = majors.Where(record => !IsDeath(record)).ToList();
        foreach (PersonalHistoryRecord record in deeds.Skip(Math.Max(0, deeds.Count - ListedMajor)))
            text.Append(record.content.Trim());
        text.Append(PersonalTieSystem.Describe(person));
        PersonalHistoryRecord death = majors.LastOrDefault(IsDeath) ?? history.LastOrDefault(IsDeath);
        if (death != null) text.Append(death.content.Trim());
        if (!string.IsNullOrWhiteSpace(person.posthumous_name))
            text.Append(string.Format(LM.Get("biography_posthumous"), person.posthumous_name));
        int sons = 0, daughters = 0;
        foreach (long id in person.children ?? new List<long>())
        {
            PersonalClanIdentity child = SpecificClanManager.getPerson(id);
            if (child == null) continue;
            if (child.sex == ActorSex.Male) sons++;
            else daughters++;
        }
        if (sons + daughters > 0) text.Append(string.Format(LM.Get("biography_children"), sons, daughters));
        return text.ToString();
    }

    private static bool IsDeath(PersonalHistoryRecord record) => record?.event_key == DeathKey;

    public const string DeathKey = "death";

    #endregion
}
