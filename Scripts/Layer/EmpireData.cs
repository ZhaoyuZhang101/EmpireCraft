using System;
using System.Collections.Generic;
using System.ComponentModel;

// Token: 0x0200023D RID: 573
public class EmpireData : MetaObjectData
{
    public string motto { get; set; }
    public int banner_background_id { get; set; }
    public int banner_icon_id { get; set; }
    public string founder_actor_name { get; set; }
    [DefaultValue(-1L)]
    public long founder_actor_id { get; set; } = -1L;
    public string founder_kingdom_name { get; set; }

    [DefaultValue(-1L)]
    public long founder_kingdom_id { get; set; } = -1L;

    public List<long> kingdoms;

    public long empire;

    public double timestamp_member_joined;

}
