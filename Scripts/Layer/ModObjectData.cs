using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.Layer;
public class ModObjectData : MetaObjectData
{
    public int banner_background_id { get; set; }
    public int banner_icon_id { get; set; }
    public string founder_actor_name { get; set; }
    [DefaultValue(-1L)]
    public long founder_actor_id { get; set; } = -1L;

    public long core_city { get; set; } = -1L;
    [DefaultValue(-1L)]
    public string original_actor_asset;
    public List<long> cities;
    public List<string> history_officers;

    public double timestamp_established_time;
}