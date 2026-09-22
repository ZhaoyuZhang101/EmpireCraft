using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.Layer;
public class KingdomTitleJurisdictionRecord
{
    public long kingdom_id = -1L;
    public string kingdom_name = "";
    public string relation = "";
    public double start_time = -1L;
    public double end_time = -1L;
}

public class KingdomTitleData: MetaObjectData
{
    public int banner_background_id { get; set; }
    public int banner_icon_id { get; set; }
    public string founder_actor_name { get; set; }
    [DefaultValue(-1L)]
    public long founder_actor_id { get; set; } = -1L;
    public string founder_kingdom_name { get; set; }

    [DefaultValue(-1L)]
    public long founder_kingdom_id { get; set; } = -1L;
    public string original_actor_asset;
    public long title_capital = -1L;
    public bool is_allow_to_inherit = false;

    public double title_controlled_rate = 0.9;

    public long owner = -1L;
    public string province_name { get; set; }

    public List<long> cities;
    public List<string> history_emperrors;
    public List<KingdomTitleJurisdictionRecord> jurisdiction_history = new List<KingdomTitleJurisdictionRecord>();
    public long main_kingdom { get; set; } = -1L;

    public double timestamp_been_controlled;

    public double timestamp_established_time;

    // EmpireCraft culture key (for example Huaxia), not a vanilla species or Culture id.
    public string culture = "";
    public bool culture_explicit = false;
    public bool culture_locked = false;
    public double last_culture_check_timestamp = -1L;

    // 每种文化在这个法理头衔/省份上出现过的名字：key 是文化 key。一旦某个文化记录过名字就
    // 永久保留，只有玩家在法理界面手动删除才会清除，不会被文化转变逻辑自动覆盖或清空
    // （见 CultureService.ResolveCulturalTitleName）。
    public Dictionary<string, string> title_name_history = new Dictionary<string, string>();
    public Dictionary<string, string> province_name_history = new Dictionary<string, string>();
}
