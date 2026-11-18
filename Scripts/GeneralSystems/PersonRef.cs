using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 按族谱认人：存着的单位编号会失效——人被并回人口转成虚拟族人后实体没了，再落成时又换了新编号——
// 权臣、名将名臣这类记着"某个人"的数据就把在世的人当成死了(用户 2026-11-18 选"改存族谱 id")。
// 这里按族谱身份找回这个人：在地图上的直接取；转成了虚拟族人的，要用时落成一次并改记新编号。
// 另外这些要角留在地图上不并回人口(Keeps，见 CityPopulationSystem.IsNotable)：并回人口会丢名望、政绩，落成回来只是个白身
public static class PersonRef
{
    public static bool Alive(Actor actor) => actor?.data != null && !actor.isRekt() && actor.isAlive();

    // 存着的人：单位编号(转成虚拟族人时族谱仍记着这个编号)优先，其次族谱身份编号
    public static PersonalClanIdentity Person(long actorId, long identityId)
    {
        PersonalClanIdentity person = actorId > 0 ? SpecificClanManager.PersonByActorId(actorId) : null;
        return person ?? (identityId > 0 ? SpecificClanManager.getPerson(identityId) : null);
    }

    // 找回存着的人。actorId、identityId 就地改成最新的(落成换了编号、旧档没记族谱身份的都补上)。
    // realize 为假时只认地图上的(虚拟族人返回空，人在不在世看 Person(..).is_alive)
    public static Actor Resolve(ref long actorId, ref long identityId, bool realize = true)
    {
        if (actorId <= 0 && identityId <= 0 || World.world == null) return null;
        Actor actor = actorId > 0 ? World.world.units?.get(actorId) : null;
        if (Alive(actor))
        {
            identityId = actor.GetPersonalIdentity()?.id ?? identityId;
            return actor;
        }
        PersonalClanIdentity person = Person(actorId, identityId);
        if (person == null || !person.is_alive) return null;
        identityId = person.id;
        if (!KinRoster.OnMap(person) && !realize) return null;
        actor = Present(person);
        if (actor == null) return null;
        actorId = actor.id;
        return actor;
    }

    // 族谱里在世的人：在地图上的直接取，虚拟族人落成
    public static Actor Present(PersonalClanIdentity person)
    {
        if (person == null || !person.is_alive) return null;
        if (KinRoster.OnMap(person)) return person._actor;
        if (!person.is_virtual) return null;
        Actor actor = person.Realize();
        return Alive(actor) ? actor : null;
    }

    // 记着族谱身份的要角留在地图上：名将名臣、权臣、君主(储君)的生父母与配偶妃嫔、先君遗孀(太后)
    public static bool Keeps(Actor actor)
    {
        if (actor?.data == null || actor.isRekt()) return false;
        if (FamousTalentSystem.IsFamous(actor)) return true;
        Kingdom kingdom = actor.kingdom;
        Empire empire = kingdom?.GetEmpire();
        if (empire?.data != null && empire.data.powerful_minister_id == actor.id) return true;
        if (ConsortKinSystem.IsChief(actor)) return true;
        // 幕府下的天皇(见 BakufuSystem.Emperor)
        if (empire?.data != null && empire.data.court_emperor_id == actor.id) return true;
        PersonalClanIdentity me = actor.GetPersonalIdentity();
        if (me == null) return false;
        foreach (long childId in me.children)
            if (Ruling(SpecificClanManager.getPerson(childId))) return true;
        PersonalClanIdentity spouse = me.lover.identity > 0 ? SpecificClanManager.getPerson(me.lover.identity) : null;
        if (spouse != null)
        {
            if (Ruling(spouse)) return true;
            if (!spouse.is_alive && kingdom != null && kingdom.GetOrCreate().last_ruler_identity_id == spouse.id) return true;
            if (!spouse.is_alive && empire != null && PreviousEmperor(empire) == spouse.id) return true;
        }
        if (me.is_concubine && kingdom != null)
            foreach (Actor lord in new[] { kingdom.king, kingdom.HasHeir() ? kingdom.GetHeir() : null })
                if (lord?.GetPersonalIdentity()?.concubines?.Exists(pair => pair.Item2 == me.id) == true) return true;
        return false;
    }

    // 在位的君主或储君(在地图上)
    private static bool Ruling(PersonalClanIdentity person)
    {
        if (!KinRoster.OnMap(person)) return false;
        Actor actor = person._actor;
        return actor.isKing() || actor.kingdom != null && actor.kingdom.HasHeir() && actor.kingdom.GetHeir() == actor;
    }

    // 帝国上一位皇帝的族谱身份(见 Empire.GetPreviousEmperorWife)
    private static long PreviousEmperor(Empire empire)
    {
        var history = empire.data.history;
        if (history == null) return -1L;
        for (int i = history.Count - 1; i >= 0; i--)
            if (history[i] != null && history[i].id > 0)
                return SpecificClanManager.PersonByActorId(history[i].id)?.id ?? -1L;
        return -1L;
    }
}
