using EmpireCraft.Scripts.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using UnityEngine;

public class SaveData
{
    public List<ExtraActordata> actorsExtraData = new List<ExtraActordata>();
    public List<ExtraKingdomData> kingdomExtraData = new List<ExtraKingdomData>();
    public List<ExtraCityData> cityExtraData = new List<ExtraCityData>();
}

public class ExtraActordata
{
    public long actorId; // 使用long类型存储ID
    [JsonConverter(typeof(StringEnumConverter))]
    public PeeragesLevel peerage;
}

public class ExtraKingdomData
{
    public long kingdomId;
	[JsonConverter(typeof(StringEnumConverter))]
    public countryLevel kingdomLevel;
}
public class ExtraCityData
{
    public long cityId;
    public long provinceId;
}
