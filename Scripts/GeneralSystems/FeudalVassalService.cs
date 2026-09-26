using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;

namespace EmpireCraft.Scripts.GeneralSystems;

// Direct fealty is separate from both imperial membership and a personal-union alliance.
public static class FeudalVassalService
{
    public const int AnnualControlCost = 300;
    public const int ProgressPerLevel = 5;

    private static bool IsFeudal(Kingdom kingdom) => kingdom != null && !kingdom.isRekt() &&
        kingdom.GetRegime()?.type == RegimeType.Feudalism;

    public static Kingdom GetOverlord(Kingdom subject)
    {
        if (subject == null || subject.isRekt() || World.world?.kingdoms == null) return null;
        long id = subject.GetOrCreate().feudal_overlord_kingdom_id;
        Kingdom lord = id < 0 ? null : World.world.kingdoms.get(id);
        return lord != subject && lord != null && !lord.isRekt() ? lord : null;
    }

    public static IEnumerable<Kingdom> GetDirectVassals(Kingdom lord)
    {
        if (lord == null || World.world?.kingdoms == null) return Enumerable.Empty<Kingdom>();
        return World.world.kingdoms.Where(candidate => candidate != null && !candidate.isRekt() &&
            candidate.GetOrCreate().feudal_overlord_kingdom_id == lord.id);
    }

    public static bool IsInChain(Kingdom kingdom, Kingdom possibleAncestor)
    {
        var visited = new HashSet<long>();
        for (Kingdom current = kingdom; current != null && visited.Add(current.id); current = GetOverlord(current))
            if (current == possibleAncestor) return true;
        return false;
    }

    private static Kingdom GetFeudalRoot(Kingdom kingdom)
    {
        var visited = new HashSet<long>();
        while (kingdom != null && visited.Add(kingdom.id) && GetOverlord(kingdom) is Kingdom lord)
            kingdom = lord;
        return kingdom;
    }

    public static bool CanBind(Kingdom lord, Kingdom subject)
    {
        if (!IsFeudal(lord) || subject == null || subject.isRekt() || lord == subject ||
            AncientWarfareCompatibility.Owns(lord) || AncientWarfareCompatibility.Owns(subject) ||
            subject.IsEmpire() ||
            subject.king != null && subject.king == lord.king ||
            lord.GetOrCreate().feudal_vassal_level >= 3 && GetOverlord(lord) != null ||
            IsInChain(lord, subject)) return false;
        string culture = CultureService.GetRealmCulture(lord);
        return InstitutionSystem.IsEnacted(culture, "western_feudalization");
    }

    public static bool Bind(Kingdom lord, Kingdom subject)
    {
        if (!CanBind(lord, subject) || !subject.hasKing()) return false;
        if (GetOverlord(subject) == lord) return true;
        if (GetOverlord(subject) != null) Break(subject);
        Empire formerEmpire = subject.GetEmpire();
        if (formerEmpire != null) formerEmpire.leave(subject);
        var data = subject.GetOrCreate();
        data.feudal_overlord_kingdom_id = lord.id;
        data.feudal_vassal_level = 1;
        data.feudal_vassal_progress = 0;
        data.feudal_last_control_timestamp = -1d;
        if (subject.hasAlliance() && lord.hasAlliance() && subject.getAlliance() == lord.getAlliance())
            subject.getAlliance().leave(subject);
        subject.updateColor(lord.getColor());
        SyncColors(subject);
        return true;
    }

    public static void Break(Kingdom subject)
    {
        if (subject?.data == null || subject.isRekt()) return;
        var data = subject.GetOrCreate();
        if (data.feudal_overlord_kingdom_id < 0) return;
        data.feudal_overlord_kingdom_id = -1L;
        data.feudal_vassal_level = 0;
        data.feudal_vassal_progress = 0;
        data.feudal_last_control_timestamp = -1d;
        subject.generateColor();
        SyncColors(subject);
    }

    public static void Sync(Kingdom subject)
    {
        if (subject?.data == null || subject.isRekt()) return;
        if (subject.GetOrCreate().feudal_overlord_kingdom_id < 0) return;
        Kingdom lord = GetOverlord(subject);
        if (!IsFeudal(lord) || subject.IsEmpire() ||
            subject.king != null && subject.king == lord.king ||
            AncientWarfareCompatibility.Owns(lord) || AncientWarfareCompatibility.Owns(subject) ||
            IsInChain(lord, subject))
        {
            Break(subject);
            if (subject.king != null && subject.king == lord?.king)
                PersonalUnionService.OnKingCrowned(subject, subject.king);
            return;
        }
        if (subject.getColor() != lord.getColor()) subject.updateColor(lord.getColor());
        if (subject.GetOrCreate().feudal_vassal_level >= 3 && subject.hasAlliance())
            subject.getAlliance().leave(subject);
    }

    private static void SyncColors(Kingdom lord)
    {
        SyncColors(lord, new HashSet<long>());
    }

    private static void SyncColors(Kingdom lord, HashSet<long> visited)
    {
        if (lord == null || !visited.Add(lord.id)) return;
        foreach (Kingdom child in GetDirectVassals(lord).ToList())
        {
            child.updateColor(lord.getColor());
            SyncColors(child, visited);
        }
    }

    public static Kingdom FindControlTarget(Kingdom lord) => GetDirectVassals(lord)
        .Where(subject => CanIncreaseControl(lord, subject))
        .OrderByDescending(subject => subject.GetNationalPower()).FirstOrDefault();

    public static Kingdom FindAnnexTarget(Kingdom lord) => GetDirectVassals(lord)
        .FirstOrDefault(subject => CanAnnex(lord, subject));

    public static Kingdom FindPeacefulSubmissionTarget(Kingdom lord)
    {
        if (lord == null || World.world?.kingdoms == null) return null;
        double lordPower = GetWarPower(lord, false);
        return World.world.kingdoms.Where(subject => subject != null && subject.hasKing() &&
                GetOverlord(subject) == null && CanBind(lord, subject) &&
                lord.IsNeighbourWith(subject) &&
                (World.world.diplomacy?.getOpinion(subject, lord)?.total ?? 0) >= 50 &&
                lordPower > GetWarPower(subject, true))
            .OrderByDescending(subject => World.world.diplomacy.getOpinion(subject, lord).total)
            .FirstOrDefault();
    }

    public static bool CanJoinAlliance(Kingdom subject, Alliance alliance)
    {
        Kingdom lord = GetOverlord(subject);
        if (lord == null) return true;
        return subject.GetOrCreate().feudal_vassal_level == 1 &&
               (alliance == null || !alliance.kingdoms_hashset.Contains(lord));
    }

    public static bool CanIncreaseControl(Kingdom lord, Kingdom subject)
    {
        if (lord?.king?.data == null || lord.king.data.renown < AnnualControlCost ||
            GetOverlord(subject) != lord || World.world == null) return false;
        var data = subject.GetOrCreate();
        return data.feudal_vassal_level is >= 1 and <= 3 &&
               (data.feudal_vassal_level < 3 || data.feudal_vassal_progress < ProgressPerLevel) &&
               (data.feudal_last_control_timestamp < 0 ||
                Date.getYearsSince(data.feudal_last_control_timestamp) >= 1);
    }

    public static bool IncreaseControl(Kingdom lord, Kingdom subject)
    {
        if (!CanIncreaseControl(lord, subject)) return false;
        lord.king.editRenown(-AnnualControlCost);
        var data = subject.GetOrCreate();
        data.feudal_last_control_timestamp = World.world.getCurWorldTime();
        data.feudal_vassal_progress++;
        if (data.feudal_vassal_progress >= ProgressPerLevel && data.feudal_vassal_level < 3)
        {
            data.feudal_vassal_level++;
            data.feudal_vassal_progress = 0;
            if (data.feudal_vassal_level == 3)
            {
                foreach (Kingdom child in GetDirectVassals(subject).ToList()) Break(child);
                if (subject.hasAlliance()) subject.getAlliance().leave(subject);
            }
        }
        return true;
    }

    public static bool CanAnnex(Kingdom lord, Kingdom subject) =>
        lord?.king != null && lord.king.isAlive() && GetOverlord(subject) == lord &&
        !lord.getWars().Any(war => war != null && !war.hasEnded()) &&
        !subject.getWars().Any(war => war != null && !war.hasEnded()) &&
        subject.GetOrCreate().feudal_vassal_level == 3 &&
        subject.GetOrCreate().feudal_vassal_progress >= ProgressPerLevel;

    public static bool Annex(Kingdom lord, Kingdom subject)
    {
        if (!CanAnnex(lord, subject)) return false;
        PersonalUnionService.CrownInUnion(subject, lord.king);
        if (subject.king != lord.king) return false;
        Break(subject);
        subject.updateColor(lord.getColor());
        if (lord.IsInEmpire()) lord.GetEmpire()?.join(subject, pForce: true);
        else PersonalUnionService.JoinAlliance(lord, subject);
        return true;
    }

    private static void CollectPower(Kingdom kingdom, HashSet<long> visited, Kingdom excluded)
    {
        if (kingdom == null || kingdom.isRekt() ||
            excluded != null && IsInChain(kingdom, excluded) || !visited.Add(kingdom.id)) return;
        Empire empire = kingdom.GetEmpire();
        if (empire != null && !empire.IsArchived())
            foreach (Kingdom member in empire.kingdoms_list ?? new List<Kingdom>())
                if (member != kingdom) CollectPower(member, visited, excluded);
        if (kingdom.king != null)
            foreach (Kingdom realm in PersonalUnionService.GetRealms(kingdom.king))
                if (realm != kingdom) CollectPower(realm, visited, excluded);
        foreach (Kingdom child in GetDirectVassals(kingdom)) CollectPower(child, visited, excluded);
    }

    public static double GetWarPower(Kingdom kingdom, bool defending, Kingdom excluded = null)
    {
        if (kingdom == null || kingdom.isRekt()) return 0d;
        Kingdom root = kingdom;
        if (defending)
        {
            var seen = new HashSet<long>();
            while (GetOverlord(root) is Kingdom lord && seen.Add(root.id)) root = lord;
        }
        var visited = new HashSet<long>();
        CollectPower(root, visited, excluded);
        return visited.Sum(id => World.world.kingdoms.get(id)?.GetNationalPower() ?? 0d);
    }

    public static bool CanDeclareExternalWar(Kingdom attacker, Kingdom target)
    {
        if (attacker == null || target == null || attacker == target || !target.isAlive() ||
            attacker.IsInSameEmpire(target) ||
            IsInChain(attacker, target) || IsInChain(target, attacker) ||
            attacker.king != null && attacker.king == target.king ||
            attacker.hasAlliance() && attacker.getAlliance() == target.getAlliance()) return false;
        Kingdom attackerRoot = GetFeudalRoot(attacker);
        Kingdom targetRoot = GetFeudalRoot(target);
        if (attackerRoot == targetRoot || attackerRoot.IsInSameEmpire(targetRoot)) return false;
        Kingdom lord = GetOverlord(attacker);
        if (lord != null)
        {
            int level = attacker.GetOrCreate().feudal_vassal_level;
            if (level >= 3) return false;
            if (level == 2 && (World.world?.diplomacy?.getOpinion(lord, target)?.total ?? 0) >= 0)
                return false;
        }
        return (World.world?.diplomacy?.getOpinion(attacker, target)?.total ?? 0) < 0 &&
               GetWarPower(attacker, false) > GetWarPower(target, true);
    }

    public static bool CanSeekIndependence(Kingdom subject)
    {
        Kingdom lord = GetOverlord(subject);
        return lord != null && subject.king != null && subject.king.isAlive() &&
               !subject.getWars().Any(war => war != null && !war.hasEnded()) &&
               GetWarPower(subject, false) > GetWarPower(lord, false, subject);
    }

    public static bool StartIndependenceWar(Kingdom subject)
    {
        if (!CanSeekIndependence(subject)) return false;
        Kingdom lord = GetOverlord(subject);
        War war = World.world.diplomacy.startWar(subject, lord, WarTypeLibrary.normal);
        if (war == null) return false;
        war.SetEmpireWarType(EmpireWarType.附庸独立);
        return true;
    }

    public static void ResolveIndependenceWar(War war, WarWinner winner)
    {
        if (war?.GetEmpireWarType() != EmpireWarType.附庸独立 || winner != WarWinner.Attackers) return;
        Kingdom subject = war.getMainAttacker();
        if (GetOverlord(subject) == war.getMainDefender()) Break(subject);
    }

    public static void ExpandWar(War war)
    {
        if (war == null || war.hasEnded()) return;
        ExpandSide(war, true);
        ExpandSide(war, false);
    }

    private static void ExpandSide(War war, bool attackers)
    {
        var side = attackers ? war._list_attackers : war._list_defenders;
        var opposite = attackers ? war._list_defenders : war._list_attackers;
        var visited = new HashSet<long>();
        var queue = new Queue<Kingdom>(side.Where(kingdom => kingdom != null));
        while (queue.Count > 0)
        {
            Kingdom current = queue.Dequeue();
            if (current == null || current.isRekt() || !visited.Add(current.id)) continue;
            var followers = GetDirectVassals(current).ToList();
            Empire empire = current.GetEmpire();
            if (empire != null && war.getMainAttacker() != null && war.getMainDefender() != null &&
                !war.getMainAttacker().IsInSameEmpire(war.getMainDefender()))
                followers.AddRange(empire.kingdoms_list.Where(member => member != null && member != current));
            bool directIndependence = GetOverlord(war.getMainAttacker()) == war.getMainDefender();
            if (!attackers && !directIndependence && GetOverlord(current) is Kingdom protector)
                followers.Add(protector);
            if (current.king != null) followers.AddRange(PersonalUnionService.GetRealms(current.king));
            foreach (Kingdom follower in followers)
            {
                if (follower == null || follower.isRekt() || opposite.Contains(follower)) continue;
                if (!side.Contains(follower))
                {
                    if (attackers) war.joinAttackers(follower);
                    else war.joinDefenders(follower);
                }
                queue.Enqueue(follower);
            }
        }
    }
}
