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
using static EmpireCraft.Scripts.GameClassExtensions.ReligionExtension;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;

public class SaveData
{
    public List<ActorExtraData> actorsExtraData = new List<ActorExtraData>();
    public List<KingdomExtraData> kingdomExtraData = new List<KingdomExtraData>();
    public List<CityExtraData> cityExtraData = new List<CityExtraData>();
    public List<ClanExtraData> clanExtraData = new List<ClanExtraData>();
    // 时代名著挂在原版书上的扩展(见 BookExtension)
    public List<BookExtension.BookExtraData> bookExtraData = new List<BookExtension.BookExtraData>();
    public List<WarExtraData> warExtraData = new List<WarExtraData>();
    public List<ReligionExtraData> religionExtraData = new List<ReligionExtraData>();
    public List<EmpireData> empireDatas = new List<EmpireData>();
    public List<EmpireCore> empireCoreDatas = new List<EmpireCore>();
    public List<KingdomTitleData> kingdomTitleDatas = new List<KingdomTitleData>();
    public Dictionary<long, string> cultureBindings = new Dictionary<long, string>();
    public Dictionary<string, CultureInstitutionState> cultureInstitutionStates = new Dictionary<string, CultureInstitutionState>();
    public Dictionary<string, CultureTechState> cultureTechStates = new Dictionary<string, CultureTechState>();
    // 名将名臣名册(见 FamousTalentSystem)
    public TalentSaveData famousTalents = new TalentSaveData();
    public List<string> yearNameSubspecies = new List<string>();
    public Dictionary<long, List<EmpireCraftHistory>> all_history;
    // 已亡帝国名录(见 FallenEmpireRecords)
    public List<FallenEmpireRecord> fallen_empires = new List<FallenEmpireRecord>();
    public Dictionary<string, double> culture_no_empire_since = new Dictionary<string, double>();
    public Dictionary<string, List<long>> feudal_empire_lineage = new Dictionary<string, List<long>>();
    public List<long> mod_alliance_ids = new List<long>();
    // 图层城墙(纯净地图把原版城墙转成的数据，格编号 → 城墙种类，见 MapWallSystem)
    public Dictionary<int, string> map_walls = new Dictionary<int, string>();
    // 神力画的路(格编号 → 等级、路况，见 RoadPaintSystem)
    public Dictionary<int, EmpireCraft.Scripts.GeneralSystems.PaintedRoad> painted_roads =
        new Dictionary<int, EmpireCraft.Scripts.GeneralSystems.PaintedRoad>();
    // 城郊的镇村(钉在地图上，见 RuralSettlementSystem)
    public List<RuralSettlement> rural_settlements = new List<RuralSettlement>();
    // 工厂(钉在地图上，见 FactorySystem)
    public List<FactorySite> factories = new List<FactorySite>();
    // 无人区(连片的山脉、沙漠、冻土雪原、丛林、沼泽，见 WildernessSystem)：编号、类型、名字、起名的文化、地块与各块挂的法理；
    // 下一片的编号。旧档没有，读档后按地形划
    public List<EmpireCraft.Scripts.GeneralSystems.WildRegionData> wild_regions =
        new List<EmpireCraft.Scripts.GeneralSystems.WildRegionData>();
    public int wild_next_id = 1;
    // 雨林、大泽无人区的开发度(按地块)与朝廷的开发诏令(见 WildernessDevelopmentSystem)。旧档没有，从零开发
    public EmpireCraft.Scripts.GeneralSystems.WildDevelopmentData wild_development =
        new EmpireCraft.Scripts.GeneralSystems.WildDevelopmentData();
    // 通路之约：两国交好的年数、有过友好时期的、议定的(见 RoadTreatySystem)
    public RoadTreatyData road_treaties = new RoadTreatyData();
    public bool switch_real_num = false;
    public bool switch_simple_nameplate = false;
    public bool switch_empire_show_alliance = false;
    public List<SpecificClan> specificClans = new List<SpecificClan>();
    public Dictionary<long, OfficeObject>  officeObjects = new Dictionary<long, OfficeObject>();
    public int mod_version = 0;
    // 世界地图一块区块合多少公里(见 WorldMapScale；不是世界地图为 0)
    public float world_map_zone_km = 0f;
    // 世界地图的范围、大小、时代、开局年份与无人区的史实归属(见 WorldMapWilderness；不是世界地图为 null)
    public EmpireCraft.Scripts.GeneralSystems.WorldMap.WorldMapWildSave world_map_wild;
}
