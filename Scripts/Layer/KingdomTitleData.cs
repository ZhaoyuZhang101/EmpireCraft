using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.Layer;
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

    public double title_controlled_rate = 0.9;

    public long owner = -1L;
    public string province_name { get; set; }

    public List<long> cities;
    public List<string> history_emperrors;
    public long main_kingdom { get; set; } = -1L;

    public double timestamp_been_controlled;

    public double timestamp_established_time;
}