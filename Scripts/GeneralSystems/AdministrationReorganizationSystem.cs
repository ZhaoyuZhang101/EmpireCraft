using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.AI.KingdomAI;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 改划仅操作本帝国城市。每帧最多处理一个城市，所有易主操作留在主线程。
public static class AdministrationReorganizationSystem
{
    private sealed class Region
    {
        public KingdomTitle Title;
        public List<City> Cities;
        public Kingdom Target;
        public int Cursor;
    }
    private sealed class Work
    {
        public Empire Empire;
        public Queue<Region> Regions;
        public HashSet<long> Protected;
        public Kingdom Core;
        public City Capital;
        public KingdomTitle MainTitle, CapitalTitle;
    }
    private static readonly Queue<Work> Pending = new();
    private static readonly HashSet<long> Active = new();
    public static bool IsRunning(Empire empire) => empire != null && Active.Contains(empire.id);
    public static void ResetWorldState() { Pending.Clear(); Active.Clear(); }

    public static bool CanPropose(Actor actor) => actor != null && !actor.isRekt() && actor.isKing() &&
        actor.kingdom?.GetEmpire() is Empire empire && empire.Emperor == actor && actor.kingdom == empire.CoreKingdom &&
        CanContinue(empire) && !empire.data.administrative_reorganization_pending;

    public static bool ShouldPropose(Actor actor) => CanPropose(actor) &&
        (actor.kingdom.GetEmpire().data.last_administrative_reorganization_timestamp < 0 ||
         Date.getYearsSince(actor.kingdom.GetEmpire().data.last_administrative_reorganization_timestamp) >= 5);

    public static bool CanContinue(Empire empire) => empire?.data != null && !empire.isRekt() &&
        !empire.IsArchived() && empire.CoreKingdom != null && !empire.CoreKingdom.isRekt() &&
        empire.Legitimacy > 80 && !empire.HasTerritorialWar() &&
        !AncientWarfareCompatibility.Owns(empire.CoreKingdom);

    public static bool Begin(Actor actor)
    {
        if (!CanPropose(actor)) return false;
        Empire empire = actor.kingdom.GetEmpire();
        empire.data.administrative_reorganization_pending = true;
        Resume(empire);
        if (!Active.Contains(empire.id)) { empire.data.administrative_reorganization_pending = false; return false; }
        empire.data.last_administrative_reorganization_timestamp = World.world.getCurWorldTime();
        return true;
    }

    public static void Resume(Empire empire)
    {
        if (empire?.data?.administrative_reorganization_pending != true || Active.Contains(empire.id) ||
            Pending.Count >= 8 || !CanContinue(empire)) return;
        var groups = empire.kingdoms_list.Where(kingdom => Eligible(empire, kingdom))
            .SelectMany(kingdom => kingdom.cities ?? new List<City>())
            .Where(city => city != null && !city.isRekt() && city.hasTitle() && city.GetTitle()?.isRekt() == false)
            .Distinct().GroupBy(city => city.GetTitle()).OrderBy(group => group.Key.id)
            .Select(group => new Region { Title = group.Key,
                Cities = group.OrderBy(city => city == group.Key.title_capital ? 0 : 1).ThenBy(city => city.id).ToList() });
        var work = new Work { Empire = empire, Regions = new Queue<Region>(groups) };
        RefreshProtected(work);
        Pending.Enqueue(work);
        Active.Add(empire.id);
    }

    private static bool Eligible(Empire empire, Kingdom kingdom) => kingdom != null && !kingdom.isRekt() &&
        kingdom.GetEmpire() == empire && kingdom.cities?.Count > 0 && !AncientWarfareCompatibility.Owns(kingdom);

    private static void RefreshProtected(Work work)
    {
        Kingdom core = work.Empire.CoreKingdom;
        City capital = core.capital;
        KingdomTitle mainTitle = core.GetMainTitle(), capitalTitle = capital?.GetTitle();
        if (work.Protected != null && work.Core == core && work.Capital == capital &&
            work.MainTitle == mainTitle && work.CapitalTitle == capitalTitle) return;
        work.Core = core; work.Capital = capital;
        work.MainTitle = mainTitle; work.CapitalTitle = capitalTitle;
        work.Protected = work.Empire.GetProtectedDirectCityIds();
    }

    public static void Tick()
    {
        if (Pending.Count == 0 || !SimulationFrameBudget.HasTime) return;
        Work work = Pending.Dequeue();
        Empire empire = work.Empire;
        if (!CanContinue(empire))
        {
            // 战争和正统不足时暂停；存档中的标记在条件恢复后重新规划。
            Active.Remove(empire.id);
            return;
        }
        RefreshProtected(work);
        if (work.Regions.Count == 0)
        {
            empire.data.administrative_reorganization_pending = false;
            Active.Remove(empire.id);
            empire.RecordHistory(directContent: LM.Get("administration_reorganization_completed"));
            return;
        }
        Region region = work.Regions.Peek();
        if (region.Title == null || region.Title.isRekt() || region.Cursor >= region.Cities.Count)
        {
            if (Eligible(empire, region.Target))
            {
                if (region.Target.IsAdministrativeKingdomType()) region.Target.SetAdministrativeTitle(region.Title);
                empire.SynchronizeLandedLegalTitles(region.Target);
                EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(region.Target);
            }
            region.Title?.RefreshAdministrativeDivisionNames();
            work.Regions.Dequeue();
            Pending.Enqueue(work);
            return;
        }
        City city = region.Cities[region.Cursor++];
        if (city == null || city.isRekt() || city.GetTitle() != region.Title || !Eligible(empire, city.kingdom))
        { Pending.Enqueue(work); return; }

        // 王畿、开国法理和现首都法理归核心直辖，核心首都始终不会被转出。
        // Revalidate capital/title changes while the work is spread over frames.
        // Keep saved title-list protections even if old city/title pointers disagree.
        City capital = empire.CoreKingdom.capital;
        bool protectedCity = work.Protected.Contains(city.id) || city == capital || region.Title == empire.CoreKingdom.GetMainTitle() ||
                             region.Title == capital?.GetTitle();
        if (protectedCity) region.Target = empire.CoreKingdom;
        else
        {
            if (region.Target == empire.CoreKingdom) region.Target = null;
            if (!Eligible(empire, region.Target))
            {
                region.Target = empire.kingdoms_list.Where(kingdom => Eligible(empire, kingdom) &&
                    kingdom != empire.CoreKingdom && kingdom.capital?.kingdom == kingdom &&
                    kingdom.capital.GetTitle() == region.Title)
                    .OrderBy(kingdom => kingdom.GetMainTitle() == region.Title ? 0 : 1).ThenBy(kingdom => kingdom.id)
                    .FirstOrDefault();
                if (region.Target == null)
                {
                    if (city.kingdom != empire.CoreKingdom) city.joinAnotherKingdom(empire.CoreKingdom);
                    region.Target = empire.EstablishTerritorialDivision(new List<City> { city }, region.Title);
                    // 尚无合格长官时留下待办，下一轮战略检查可重新尝试。
                    if (region.Target == null) { Active.Remove(empire.id); return; }
                }
            }
        }
        if (city.kingdom != region.Target && city != empire.CoreKingdom.capital && !WartimeCustodySystem.InCustody(city))
            city.joinAnotherKingdom(region.Target);
        Pending.Enqueue(work);
    }
}
