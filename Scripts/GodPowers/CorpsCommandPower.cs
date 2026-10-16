using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GodPowers;

// 军令神力(见 CorpsCommandSystem)：先点一座城选中它的国家，再点目标城——本国的城集中固守，交战敌国的城集中攻打；
// 再点同一座城撤销；点到没有国家的地方取消选择。
public static class CorpsCommandPower
{
    public const string Id = "corps_command";
    private static long _selected = -1L;

    public static void init()
    {
        AssetManager.powers.add(new GodPower { id = Id, name = Id, click_action = Click });
    }

    private static void Tip(string text) => WorldTip.showNow(text, false, "top", 3f);

    private static bool Click(WorldTile tile, string power)
    {
        City city = tile?.zone?.city;
        Kingdom kingdom = city?.kingdom;
        if (city == null || kingdom == null || kingdom.wild || kingdom.isRekt())
        {
            _selected = -1L;
            Tip(LM.Get("force_vassal_no_kingdom"));
            return false;
        }
        Kingdom commander = _selected > 0 ? World.world.kingdoms.get(_selected) : null;
        if (commander == null || commander.isRekt())
        {
            _selected = kingdom.id;
            Tip(string.Format(LM.Get("corps_command_pick"), kingdom.GetKingdomName()));
            return true;
        }
        _selected = -1L;
        Tip(CorpsCommandSystem.Command(commander, city));
        return true;
    }
}
