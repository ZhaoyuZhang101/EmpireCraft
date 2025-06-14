using EmpireCraft.Scripts.Layer;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EmpireCraft.Scripts.Data
{
    public static class ConfigData
    {
        [JsonIgnore]
        public static Empire CURRENT_SELECTED_EMPIRE;
        // Token: 0x06002120 RID: 8480 RVA: 0x00118D18 File Offset: 0x00116F18
        public static Dictionary<long, Empire> EMPIRE_DICT = new Dictionary<long, Empire>();
        public static Empire EMPIRE = null;
    }
}
