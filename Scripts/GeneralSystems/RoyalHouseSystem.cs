using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 宗室的生老病死(让王朝有起有落，不再代代父死子继、一帆风顺)：
//   · 寿数(真实年龄法则开着时)：君主与储君按古代的分布定天年——平均五十二岁上下(标准差十三，二十二到九十五)，
//     工业化、电气、计算机各加六岁；明君 +3，庸弱 -3，昏聩 -5，暴虐 -4，荒淫 -10；按人定死，不会每年重掷；
//     其余的人照旧六十到八十岁(见 RealisticAgeSystem)；
//   · 老死每年过生日时检查(原版只在人物空闲时查，纯净地图、无小人模式下几乎查不到，实体活到一两百岁)；
//   · 皇嗣夭折：君主、储君的子女与幼主，一岁内每年一成二，一到四岁四分，五到十四岁一分二，工业化、电气、计算机逐级减少；
//     储君、幼帝夭折记入史书；
//   · 生育：君主与储君一年半至多添一个孩子，在世子女至多十四个；约一成二的君主不育(按人定)——所以会有绝嗣，
//     要从兄弟、孙辈、旁支里找人继位(见 EmpireCraftKingdomBehCheckHeir)；
//   · 意外：皇帝每年有千分之六的机会意外身亡(坠马、落水、暴病、遇刺、雷击)，暴君多遇刺、荒淫之君多误服丹药或纵欲暴亡，
//     御驾亲征中流矢、出巡在外另加；身后朝野震动：天命正统下降、各城稳定下降、权臣趁势坐大，
//     新君年幼、生前没有册立储君、无人可继时更重；没有册立储君的还可能诸王争位(见 PrinceStruggleSystem)；
//   · 诸王就藩：君主成年、不是储君、不在朝任职的儿子不住都城，去自己的封地(没有封地就分到本国别的大城)——
//     都城出事不会一次把宗室死光；
//   · 补姓：在宗族里却没有姓的人(偶有起名漏了姓)，每年过生日时补上宗族的姓。
public static class RoyalHouseSystem
{
    private const float BirthIntervalYears = 1.5f;
    private const int MaxChildren = 14, InfertilePercent = 12;
    private const float MishapBase = 0.006f, MishapCampaign = 0.03f, MishapTour = 0.01f, MishapVice = 0.015f;
    private const int DeathsPerFrame = 30;

    private enum Fate { Age, Infant }

    private static readonly List<(Actor actor, Fate fate)> Pending = new();
    private static double _lastMonth = -1d;
    private static int _months;
    private static object _world;

    public static bool IsRoyal(Actor actor) =>
        actor?.data != null && !actor.isRekt() && (actor.isKing() || actor.kingdom != null && actor.kingdom.GetHeir() == actor);

    private static bool Has(Actor actor, string tech)
    {
        string culture = actor?.kingdom == null ? null : TechnologySystem.GetCultureOf(actor.kingdom);
        return !string.IsNullOrEmpty(culture) && TechnologySystem.IsActiveFor(culture) && TechnologySystem.HasTech(culture, tech);
    }

    // 君主、储君的天年：按人散列出近似正态的数，按时代与资质调整
    public static int RoyalDeathAge(Actor actor)
    {
        ulong hash = (ulong)actor.id * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;
        float sum = 0f;
        for (int i = 0; i < 4; i++) sum += ((hash >> (i * 16)) & 0xFFFFUL) / 65535f;
        float z = (sum - 2f) * 1.7320508f;
        float mean = 52f;
        if (Has(actor, "industrialization")) mean += 6f;
        if (Has(actor, "electricity")) mean += 6f;
        if (Has(actor, "computing")) mean += 6f;
        if (actor.isKing())
            mean += RulerQualitySystem.Of(actor) switch
            {
                RulerQualitySystem.Grade.Wise => 3f,
                RulerQualitySystem.Grade.Weak => -3f,
                RulerQualitySystem.Grade.Fatuous => -5f,
                RulerQualitySystem.Grade.Tyrant => -4f,
                RulerQualitySystem.Grade.Dissolute => -10f,
                _ => 0f
            };
        return Mathf.Clamp(Mathf.RoundToInt(mean + 13f * z), 22, 95);
    }

    #region 每年过生日(ActorPatch.UpdateAge)

    public static void OnBirthday(Actor actor)
    {
        if (actor?.data == null || actor.isRekt() || !actor.isAlive() || !actor.isSapient()) return;
        RepairSurname(actor);
        if (RealisticAgeSystem.Enabled && RealisticAgeSystem.OldAgeEnabled && !actor.hasTrait("immortal") &&
            actor.getAge() >= RealisticAgeSystem.DeathAge(actor))
        {
            Pending.Add((actor, Fate.Age));
            return;
        }
        float infant = InfantMortality(actor);
        if (infant > 0f && UnityEngine.Random.value < infant) Pending.Add((actor, Fate.Infant));
    }

    private static float InfantMortality(Actor actor)
    {
        int age = actor.getAge();
        if (age >= 15) return 0f;
        bool royal = IsRoyal(actor) || (actor.getParents()?.Any(IsRoyal) ?? false);
        if (!royal) return 0f;
        float chance = age < 1 ? 0.12f : age < 5 ? 0.04f : 0.012f;
        if (Has(actor, "industrialization")) chance *= 0.6f;
        if (Has(actor, "electricity")) chance *= 0.6f;
        if (Has(actor, "computing")) chance *= 0.5f;
        return chance;
    }

    private static void RepairSurname(Actor actor)
    {
        string clan = actor.GetPersonalIdentity()?._specificClan?.name;
        if (string.IsNullOrWhiteSpace(clan)) return;
        Name name = actor.GetModName();
        if (name == null || name.hasFamilyName(actor) || !name.hasFirstName(actor)) return;
        actor.SetFamilyName(clan);
        name.SetName(actor);
    }

    #endregion

    #region 生育

    // 君主、储君还能不能添孩子：不育的不能，一年半内添过的不能，在世子女满了不能
    public static bool CanBirth(Actor parent)
    {
        if (!IsRoyal(parent)) return true;
        if (((ulong)parent.id * 0xD6E8FEB86659FD93UL >> 40) % 100UL < InfertilePercent) return false;
        double last = parent.GetOrCreate().last_royal_birth;
        if (last >= 0d && World.world.getCurWorldTime() >= last && Date.getYearsSince(last) < BirthIntervalYears) return false;
        int living = parent.getChildren(false)?.Count(child => child?.data != null && !child.isRekt() && child.isAlive()) ?? 0;
        return living < MaxChildren;
    }

    // 添了孩子(BabyHelperPatch.MakeBaby)：记下君主、储君添孩子的时间
    public static void NoteBirth(Actor parent1, Actor parent2)
    {
        double now = World.world.getCurWorldTime();
        if (IsRoyal(parent1)) parent1.GetOrCreate().last_royal_birth = now;
        if (IsRoyal(parent2)) parent2.GetOrCreate().last_royal_birth = now;
    }

    #endregion

    #region 每帧调度

    public static void Tick()
    {
        if (World.world == null) return;
        if (!ReferenceEquals(_world, World.world))
        {
            _world = World.world;
            Pending.Clear();
            _lastMonth = -1d;
            _months = 0;
        }
        Flush();
        double now = World.world.getCurWorldTime();
        if (_lastMonth >= 0d && now >= _lastMonth && Date.getMonthsSince(_lastMonth) < 1) return;
        bool first = _lastMonth < 0d;
        _lastMonth = now;
        if (first) return;
        try
        {
            PrinceStruggleSystem.Monthly();
            SuccessionCouncilSystem.Monthly();
            EmpirePartitionSystem.Monthly();
            CityStateService.EnforceOneCity();
            if (++_months % 12 != 0) return;
            if (ModClass.EMPIRE_MANAGER != null)
                foreach (Empire empire in ModClass.EMPIRE_MANAGER.ToList())
                {
                    if (empire?.data == null || RepublicSystem.IsRepublic(empire)) continue;
                    Mishap(empire);
                    PrinceStruggleSystem.Yearly(empire);
                    EmpirePartitionSystem.Yearly(empire);
                }
            foreach (Kingdom kingdom in World.world.kingdoms.list.ToList()) Enfeoff(kingdom);
        }
        catch (Exception exception)
        {
            NeoModLoader.services.LogService.LogWarning($"[EmpireCraft][宗室] 结算失败: {exception.Message}");
        }
    }

    // 过生日时排下的生死，分帧执行(不在原版逐人更新年龄的循环里杀人)
    private static void Flush()
    {
        int done = 0;
        while (Pending.Count > 0 && done < DeathsPerFrame)
        {
            (Actor actor, Fate fate) = Pending[Pending.Count - 1];
            Pending.RemoveAt(Pending.Count - 1);
            if (actor?.data == null || actor.isRekt() || !actor.isAlive()) continue;
            done++;
            Empire empire = actor.kingdom?.GetEmpire();
            bool emperor = empire?.data != null && empire.Emperor == actor;
            Kingdom core = emperor ? empire.CoreKingdom : null;
            Actor heir = core != null && core.HasHeir() ? core.GetHeir() : null;
            bool designated = core != null && core.GetOrCreate().designated_heir_id >= 0;
            if (fate == Fate.Age)
            {
                actor.getHitFullHealth(AttackType.Age);
                // 寿终而没有册立储君：诸子也可能相争
                if (emperor && !designated && !actor.isAlive()) PrinceStruggleSystem.QueueContest(empire, 0.6f);
                continue;
            }
            // 夭折：储君、幼帝记入史书；幼帝夭折朝局动荡
            Empire realm = actor.kingdom?.GetEmpire();
            bool crownPrince = actor.kingdom != null && actor.kingdom.GetHeir() == actor;
            if ((emperor || crownPrince) && realm?.data != null)
            {
                string text = string.Format(LM.Get(emperor ? "royal_child_emperor_died" : "royal_heir_died"), actor.getName());
                realm.RecordHistory(directContent: text, actorId: actor.id);
                TranslateHelper.LogEventMessage(text, realm.CoreKingdom);
            }
            actor.die(false, AttackType.Other, true, true);
            if (emperor) Turmoil(empire, heir, designated);
        }
    }

    #endregion

    #region 意外与动荡

    private static void Mishap(Empire empire)
    {
        Actor emperor = empire.Emperor;
        if (emperor?.data == null || emperor.isRekt() || !emperor.isAlive()) return;
        RulerQualitySystem.Grade grade = RulerQualitySystem.Of(emperor);
        string tour = empire.data.tour_kind;
        float chance = MishapBase;
        if (grade is RulerQualitySystem.Grade.Tyrant or RulerQualitySystem.Grade.Dissolute) chance += MishapVice;
        if (tour == EmperorTourSystem.Campaign) chance += MishapCampaign;
        else if (!string.IsNullOrEmpty(tour)) chance += MishapTour;
        if (UnityEngine.Random.value >= chance) return;
        var causes = new List<(string key, float weight)> { ("fall", 1f), ("drown", 0.6f), ("illness", 1.2f), ("assassin", 0.5f), ("lightning", 0.15f) };
        if (grade == RulerQualitySystem.Grade.Tyrant) causes.Add(("assassin", 3f));
        if (grade == RulerQualitySystem.Grade.Dissolute) causes.AddRange(new[] { ("elixir", 2f), ("excess", 2f) });
        if (tour == EmperorTourSystem.Campaign) causes.Add(("arrow", 4f));
        float roll = UnityEngine.Random.value * causes.Sum(cause => cause.weight);
        string key = causes[causes.Count - 1].key;
        foreach ((string cause, float weight) in causes)
        {
            if (roll < weight) { key = cause; break; }
            roll -= weight;
        }
        Kingdom core = empire.CoreKingdom;
        Actor heir = core.HasHeir() ? core.GetHeir() : null;
        bool designated = core.GetOrCreate().designated_heir_id >= 0;
        string text = string.Format(LM.Get("emperor_mishap_" + key), emperor.getName());
        empire.RecordHistory(directContent: text, actorId: emperor.id);
        TranslateHelper.LogEventMessage(text, core);
        emperor.die(false, AttackType.Other, true, true);
        Turmoil(empire, heir, designated);
    }

    // 骤然驾崩后的动荡：新君年幼、没有册立储君、无人可继时更重
    public static void Turmoil(Empire empire, Actor heir, bool designated)
    {
        if (empire?.data == null) return;
        float severity = 1f;
        var notes = new List<string>();
        if (heir == null)
        {
            severity *= 1.3f;
            notes.Add("emperor_turmoil_no_heir");
        }
        else if (!heir.isAdult())
        {
            severity *= 1.5f;
            notes.Add("emperor_turmoil_child");
        }
        if (!designated)
        {
            severity *= 1.4f;
            notes.Add("emperor_turmoil_undesignated");
        }
        empire.AddMandate(-Mathf.RoundToInt(4f * severity));
        Kingdom core = empire.CoreKingdom;
        foreach (City city in core?.cities?.ToList() ?? new List<City>())
        {
            CityStabilityData state = city?.data == null ? null : CityStabilitySystem.State(city);
            if (state != null) state.stability = Mathf.Max(0f, state.stability - 8f * severity);
        }
        empire.data.powerful_minister_progress += Mathf.RoundToInt(3f * severity);
        string text = LM.Get("emperor_turmoil_history") + string.Concat(notes.Select(LM.Get));
        empire.RecordHistory(directContent: text);
        if (!designated) PrinceStruggleSystem.QueueContest(empire, severity);
    }

    #endregion

    #region 诸王就藩

    private static void Enfeoff(Kingdom kingdom)
    {
        Actor king = kingdom?.king;
        City capital = kingdom?.capital;
        if (king?.data == null || capital?.data == null || kingdom.cities == null || kingdom.cities.Count < 2) return;
        Actor heir = kingdom.HasHeir() ? kingdom.GetHeir() : null;
        var princes = (king.getChildren(false) ?? Enumerable.Empty<Actor>())
            .Where(son => son?.data != null && !son.isRekt() && son.isAlive() && son.isAdult() && son != heir &&
                          !son.isKing() && !son.IsOnOffice() && son.kingdom == kingdom && son.city == capital).ToList();
        foreach (Actor prince in princes)
        {
            City fief = Fief(prince, kingdom) ?? kingdom.cities
                .Where(city => city?.data != null && !city.isRekt() && city != capital && city.getTile() != null)
                .OrderBy(city => city.units?.Count(unit => unit != null && unit.isAlive() && unit.getParents().Contains(king)) ?? 0)
                .ThenByDescending(CityPopulationSystem.GetBackgroundTotal).FirstOrDefault();
            if (fief == null) continue;
            prince.joinCity(fief);
            if (PureMapMode.Enabled) prince.setCurrentTilePosition(fief.getTile());
            else prince.goTo(fief.getTile());
        }
    }

    // 封地：有实封或虚封的爵位，取那块封地的治所(还在本国的)
    private static City Fief(Actor prince, Kingdom kingdom)
    {
        var data = prince.GetOrCreate();
        IEnumerable<long> titles = (prince.GetOwnedTitle() ?? new List<long>()).ToList();
        if (data.virtual_enfeoff) titles = titles.Append(data.virtual_enfeoff_title_id);
        foreach (long id in titles)
        {
            City seat = ModClass.KINGDOM_TITLE_MANAGER?.get(id)?.title_capital;
            if (seat?.data != null && !seat.isRekt() && seat.kingdom == kingdom && seat != kingdom.capital) return seat;
        }
        return null;
    }

    #endregion
}
