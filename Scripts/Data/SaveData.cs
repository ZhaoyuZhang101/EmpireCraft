using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
using static EmpireCraft.Scripts.GameClassExtensions.ActorExtension;
using static EmpireCraft.Scripts.GameClassExtensions.KingdomExtension;
using static EmpireCraft.Scripts.GameClassExtensions.CityExtension;
using static EmpireCraft.Scripts.GameClassExtensions.ClanExtension;
using static EmpireCraft.Scripts.GameClassExtensions.WarExtension;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.HelperFunc;

public class SaveData
{
    public List<ActorExtraData> actorsExtraData = new List<ActorExtraData>();
    public List<KingdomExtraData> kingdomExtraData = new List<KingdomExtraData>();
    public List<CityExtraData> cityExtraData = new List<CityExtraData>();
    public List<ClanExtraData> clanExtraData = new List<ClanExtraData>();
    public List<WarExtraData> warExtraData = new List<WarExtraData>();
    public List<ModObjectData> pModObjectDatas = new List<ModObjectData>();
}