namespace EmpireCraft.Scripts.GameLibrary;

public static class EmpireCraftJobLibrary
{
    public static void init()
    {
        AssetManager.job_kingdom.t.addTask("do_mod_kingdom_beh");
        AssetManager.job_city.t.addTask("do_mod_city_beh");
        AssetManager.job_actor.t.addTask("do_mod_actor_beh");
    }
}