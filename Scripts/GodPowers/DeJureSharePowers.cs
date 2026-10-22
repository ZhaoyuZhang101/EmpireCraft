using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GodPowers;

// 兼属(见 DeJureShareSystem)，三层各一个，都是先点下级、再点上级里的任一座城；已兼属的再点一次撤掉，
// 第二下点回本来的上级(同一王国法理、帝国法理、天下的城)撤销它的全部兼属。冻结法理时照样能用，手动挂的不过期。
//   · 王国法理兼属：先点城，再点另一王国法理的城 → 这座城兼属那个王国法理；
//   · 帝国法理兼属：先点城(取它的王国法理)，再点另一帝国法理的城 → 这个王国法理兼属那个帝国法理；
//   · 天下兼属：先点城(取它的帝国法理)，再点另一天下的城 → 这个帝国法理兼属那个天下。
// 另有「帝国法理图层」：打开法理图层的帝国法理显示(法理图层第一档)，与王国法理图层是同一个图层
public static class DeJureSharePowers
{
    public const string TitleShare = "title_share";
    public const string CoreShare = "core_share";
    public const string TianxiaShare = "tianxia_share";
    public const string CoreLayer = "empire_core_layer";
    private static City _titlePending, _corePending, _tianxiaPending;

    public static void init()
    {
        AssetManager.powers.add(new GodPower { id = TitleShare, name = TitleShare, click_action = ClickTitle });
        AssetManager.powers.add(new GodPower { id = CoreShare, name = CoreShare, click_action = ClickCore });
        AssetManager.powers.add(new GodPower { id = TianxiaShare, name = TianxiaShare, click_action = ClickTianxia });
        AssetManager.powers.add(new GodPower { id = CoreLayer, name = CoreLayer, select_button_action = ShowCoreLayer });
    }

    private static City CityAt(WorldTile tile)
    {
        City city = tile?.zone?.city;
        if (city?.data != null && !city.isRekt()) return city;
        ActionLibrary.showWhisperTip("dejure_share_invalid_target");
        return null;
    }

    private static bool Pending(City pending) => pending?.data != null && !pending.isRekt();

    private static bool ClickTitle(WorldTile pTile, string pPower)
    {
        City city = CityAt(pTile);
        if (city == null) return false;
        if (!Pending(_titlePending))
        {
            _titlePending = city;
            Tip(string.Format(LM.Get("title_share_selected"), city.GetCityName()));
            return false;
        }
        City target = _titlePending;
        _titlePending = null;
        KingdomTitle title = city.GetTitle();
        if (city == target || title == null || title.isRekt() || title.id == target.GetTitleID())
        {
            if (DeJureShareSystem.ClearTitles(target)) Tip(string.Format(LM.Get("title_share_cleared"), target.GetCityName()));
            else ActionLibrary.showWhisperTip("city_cancelled");
            return true;
        }
        if (DeJureShareSystem.SharedTitles(target).Contains(title))
        {
            DeJureShareSystem.UnshareTitle(target, title);
            Tip(string.Format(LM.Get("title_share_removed"), target.GetCityName(), title.data.name));
            return true;
        }
        DeJureShareSystem.ShareTitle(target, title, manual: true);
        Tip(string.Format(LM.Get("title_share_added"), target.GetCityName(), title.data.name));
        return true;
    }

    private static bool ClickCore(WorldTile pTile, string pPower)
    {
        City city = CityAt(pTile);
        if (city == null) return false;
        if (!Pending(_corePending))
        {
            KingdomTitle own = city.GetTitle();
            if (own == null || own.isRekt())
            {
                ActionLibrary.showWhisperTip("core_share_need_title");
                return false;
            }
            _corePending = city;
            Tip(string.Format(LM.Get("core_share_selected"), own.data.name));
            return false;
        }
        KingdomTitle title = _corePending.GetTitle();
        _corePending = null;
        if (title == null || title.isRekt()) return false;
        EmpireCore core = city.GetEmpireCore() ?? EmpireCoreManager.LawfulCoreOf(city.GetTitle());
        if (core == null || EmpireCoreManager.ContainsTitle(core, title))
        {
            if (DeJureShareSystem.ClearCores(title)) Tip(string.Format(LM.Get("core_share_cleared"), title.data.name));
            else ActionLibrary.showWhisperTip("city_cancelled");
            return true;
        }
        string name = EmpireCoreManager.GetStatusDisplayName(core);
        if (DeJureShareSystem.SharedCores(title).Contains(core))
        {
            DeJureShareSystem.UnshareCore(title, core);
            Tip(string.Format(LM.Get("core_share_removed"), title.data.name, name));
            return true;
        }
        DeJureShareSystem.ShareCore(title, core, manual: true);
        Tip(string.Format(LM.Get("core_share_added"), title.data.name, name));
        return true;
    }

    private static bool ClickTianxia(WorldTile pTile, string pPower)
    {
        City city = CityAt(pTile);
        if (city == null) return false;
        if (!Pending(_tianxiaPending))
        {
            EmpireCore own = city.GetEmpireCore();
            if (own == null)
            {
                ActionLibrary.showWhisperTip("tianxia_share_need_core");
                return false;
            }
            _tianxiaPending = city;
            Tip(string.Format(LM.Get("tianxia_share_selected"), EmpireCoreManager.GetStatusDisplayName(own)));
            return false;
        }
        EmpireCore core = _tianxiaPending.GetEmpireCore();
        _tianxiaPending = null;
        if (core == null) return false;
        string coreName = EmpireCoreManager.GetStatusDisplayName(core);
        EmpireCore lead = TianxiaSystem.LeadOf(city);
        if (lead == null || lead == TianxiaSystem.Lead(core))
        {
            if (DeJureShareSystem.ClearTianxia(core)) Tip(string.Format(LM.Get("tianxia_share_cleared"), coreName));
            else ActionLibrary.showWhisperTip("city_cancelled");
            return true;
        }
        string name = TianxiaSystem.Name(lead);
        if (DeJureShareSystem.SharedLeads(core).Contains(lead))
        {
            DeJureShareSystem.UnshareTianxia(core, lead);
            Tip(string.Format(LM.Get("tianxia_share_removed"), coreName, name));
            return true;
        }
        DeJureShareSystem.ShareTianxia(core, lead, manual: true);
        Tip(string.Format(LM.Get("tianxia_share_added"), coreName, name));
        return true;
    }

    // 帝国法理图层：法理图层开到第一档(帝国法理显示)；已经开着这一档就关掉
    private static bool ShowCoreLayer(string pPower)
    {
        MetaTypeAsset layer = EmpireCraftMetaTypeLibrary.kingdomTitle;
        PlayerOptionData data = layer?.option_asset?.data;
        GodPower layerPower = layer == null ? null : AssetManager.powers.get(layer.power_option_zone_id);
        if (data == null || layerPower == null) return false;
        if (data.boolVal && data.intVal == 0)
        {
            data.boolVal = false;
            WorldTip.instance.startHide();
        }
        else
        {
            data.boolVal = true;
            data.intVal = 0;
            PowerLibrary.disableAllOtherMapModes(layerPower.id);
            WorldTip.instance.showToolbarText(LM.Get(CoreLayer), LM.Get(CoreLayer + "_description"));
        }
        PlayerConfig.saveData();
        return false;
    }

    private static void Tip(string text) => WorldTip.showNow(text, false, "top", 3f);
}
