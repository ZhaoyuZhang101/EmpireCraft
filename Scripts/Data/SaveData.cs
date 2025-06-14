using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.Layer;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;

public class SaveData
{
    public List<ExtraActorData> actorsExtraData = new List<ExtraActorData>();
    public List<ExtraKingdomData> kingdomExtraData = new List<ExtraKingdomData>();
    public List<ExtraCityData> cityExtraData = new List<ExtraCityData>();
    public List<EmpireData> empireDatas = new List<EmpireData>();
}

public class ExtraActorData
{
    public long actorId; // 使用long类型存储ID
    [DefaultValue(PeeragesLevel.peerages_6)]
    [JsonConverter(typeof(StringEnumConverter))]
    public PeeragesLevel peerage;
}

public class ExtraKingdomData
{
    public long kingdomId;
    [DefaultValue(countryLevel.countrylevel_3)]
    [JsonConverter(typeof(StringEnumConverter))]
    public countryLevel kingdomLevel;
    [DefaultValue(-1L)]
    public long vassaled_kingdom_id;
    public long empire_id;
    public double timestamp_empire;
}
public class ExtraCityData
{
    public long cityId;
    public string kingdomNames;
}

