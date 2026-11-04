# Runs extracted production methods against a small world; no game process needed.
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$source = Get-Content (Join-Path $root 'Scripts/GeneralSystems/WildernessSystem.cs') -Raw
function Method([string]$signature) {
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production method: $signature" }
    $open = $source.IndexOf('{', $start); $end = $open + 1; $depth = 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Substring($start, $end - $start)
}
$data = Method 'public class WildRegionData'
$methods = @('private sealed class OldRegion', 'private static int Regroup(', 'public static List<WildRegionData> Save(',
    'public static void SetNeutral(', 'public static WildRegion ToggleNeutral(', 'internal static int SetNeutralZones(') |
    ForEach-Object { Method $_ }
$methods += [regex]::Match($source, 'public static bool IsNeutral\(int id\)[\s\S]*?;').Value
$methods += [regex]::Match($source, 'public static bool IsNeutral\(TileZone zone\)[^;]+;').Value
$methods += [regex]::Match($source, 'public static bool IsPainted\([^;]+;').Value
$methods += Method 'public static void EnsureBuilt('
$methods += [regex]::Match($source, 'internal static bool Absorbs\([^;]+;').Value
$methods += [regex]::Match($source, 'private static float EdgeOf\([^;]+;').Value
$source = Get-Content (Join-Path $root 'Scripts/GeneralSystems/WorldMap/WorldMapWilderness.cs') -Raw
$mapData = (Method 'public sealed class ScenarioLandOrigin') + (Method 'public class WorldMapWildSave')
$mapMethods = @('private static void ApplyNeutral(', 'public static KingdomTitle TitleFor(',
    'private static KingdomTitle HistoricalTitle(', 'private static bool SamePolity(', 'public static void Begin(',
    'private static void ResolveOwners(', 'internal static void CaptureTerritory(', 'internal static void RememberTerritory(',
    'private static bool LandOrigin(', 'internal static Kingdom KnownOwner(', 'internal static City KnownCity(') | ForEach-Object { Method $_ }
$mapMethods += [regex]::Match($source, 'internal static KingdomTitle FixedTitle\([^;]+;').Value
$mapMethods += [regex]::Match($source, 'internal static IEnumerable<int> KnownTerritory\([^;]+;').Value
$source = Get-Content (Join-Path $root 'Scripts/GeneralSystems/SmoothBorders.Canvas.cs') -Raw
$fill = Method 'private static int FillAt('
$source = Get-Content (Join-Path $root 'Scripts/GeneralSystems/WildernessPassage.cs') -Raw
$passage = Method 'private static byte Blocking('
$source = Get-Content (Join-Path $root 'Scripts/GeneralSystems/WorldMap/ScenarioWilderness.cs') -Raw
$preview = Method 'private static void Own('
$source = Get-Content (Join-Path $root 'Scripts/UI/Components/WorldMapLoadingScreen.cs') -Raw
$loading = Method 'private bool AfterLoadReady('
$fixture = @'
using System;using System.Linq;using System.Collections.Generic;
public enum WildKind {None,Mountain,Desert,Tundra,Jungle,Swamp,Lava}
public class WorldTile {public TileZone zone;}
public class TileZone {public int id;public int tiles_with_ground=64;public City city;public List<WorldTile> tiles=new();public TileZone[] neighbours;}
public class Empire {public long id;}
public class Kingdom {public bool dead,wild,neutral;public object data=new();public Empire empire;public bool isRekt()=>dead;public bool isNeutral()=>neutral;public Empire GetEmpire()=>empire;}
public class City {public object data=new();public long id;public bool dead;public Kingdom kingdom;public KingdomTitle title;public List<TileZone> zones=new();
 public bool isRekt()=>dead;public WorldTile getTile()=>null;public KingdomTitle GetTitle()=>title;
 public object GetTileZoneOccupier(TileZone z)=>null;public void RemoveOccupiedTileZoneFromAll(TileZone z){}public void removeZone(TileZone z){z.city=null;}}
public class Cities:List<City>{public City get(long id)=>this.FirstOrDefault(c=>c.id==id);}
public class World {public static World world=new();public Cities cities=new();public Calculator zone_calculator=new();}
public class Calculator {public void setDrawnZonesDirty(){}}
public class KingdomTitle {public long id;public bool dead;public Kingdom holder;public Kingdom FindRealmTitleHolder()=>holder;public bool isRekt()=>dead;public List<City> getCities()=>World.world.cities.Where(c=>c.title==this&&!c.dead).ToList();}
public class TitleManager:Dictionary<long,KingdomTitle>{public KingdomTitle get(long id)=>TryGetValue(id,out var t)?t:null;}
public static class ModClass {public static TitleManager KINGDOM_TITLE_MANAGER=new();}
public class WildRegion {public int Id;public WildKind Kind;public string Name,Culture;public bool Neutral;public List<int> Zones=new();}
public class ScenarioAdministrativeOrigin {}
public class WorldMapFrame {public string id="earth";}
public struct Vector2Int {public int x,y;}
public class WorldMapEra {public string id="test";public int year;public Dictionary<string,string> wilderness=new(),wilderness_regions=new();}
public class WorldMapCity {public string id,region;public int rank=2;}
public class WorldMapPolity {public string id,capital;public bool empire;}
public class WorldMapRegion {public string capital;}
public static class ScenarioCells {public static float LandStep=.2f;public static int Key(float lon,float lat,float step)=>(int)Math.Round(lon*5);}
public static class ScenarioEditing {
 public static WorldMapEra Era=new();public static List<WorldMapCity> Cities=new();public static Dictionary<string,WorldMapPolity> owners=new();
 public static Dictionary<int,string> LandCells=new();public static WorldMapPolity OwnerOf(WorldMapCity c)=>owners[c.id];
 public static WorldMapRegion Region(string id)=>new();public static int RankOf(WorldMapCity c)=>c.rank;
 public static WorldMapPolity Polity(string id)=>owners.Values.FirstOrDefault(p=>p.id==id);
}
public class WorldMapWildUnit {public string id;public float Area;public bool covers=true;public List<string> kinds=new(){"desert"};}
public static class LogService {public static void LogInfo(string text){}}
public static class AncientWarfareCompatibility {public static bool OwnsObject(City city)=>false;}
public static class RuralSettlementSystem {public static IEnumerable<int> SettledZones()=>Array.Empty<int>();}
public static class FactorySystem {public static IEnumerable<int> CoveredZones()=>Array.Empty<int>();}
public static class WildernessDevelopmentSystem {public static IEnumerable<int> DevelopedZones()=>Array.Empty<int>();public static bool open;public static bool Passable(TileZone z)=>open;}
public static class RoadNetwork {public static HashSet<int> roads=new();public static bool IsRoad(TileZone z)=>roads.Contains(z.id);public static bool IsTrail(TileZone z)=>false;}
public static class TerrainSystem {public static bool Hilly(TileZone z)=>false;}
public static class CityDistricts {public static void Reset(){}}
public static class PureMapPatch {public static void Redraw(City c){}}
public static class WildernessSystem {public static List<WildRegion> Regions=>Actual._regions;public static WildKind kind=WildKind.Desert;
 public static bool Enabled=true;
 public static int requests;public static void RequestReattach(){requests++;}
 public static bool IsWild(TileZone z)=>Actual.RegionOf(z)!=null;
 public static int SetNeutralZones(IEnumerable<int> ids,bool value)=>Actual.SetNeutralZones(ids,value);public static WildKind KindOf(TileZone z)=>kind;}
public static partial class PassageActual {private const int CivilShift=3;private const byte DesertBlocks=1,TundraBlocks=3,AllBlocks=7;public static byte Mask(TileZone z)=>(byte)(Blocking(z)&AllBlocks);}
namespace WorldMap {public static class WorldMapWilderness {
 public static IEnumerable<int> KeptZones=>Array.Empty<int>();
 public static void RememberTerritory(TileZone z,KingdomTitle t,City c,bool neutral,bool pinned=false)=>MapActual.RememberTerritory(z,t,c,neutral,pinned);
 public static KingdomTitle PaintedTitle(TileZone z)=>MapActual.PaintedTitle(z);
}}
public static class WildernessShape {public static int slot;public static int SlotAt(int t)=>slot;}
public static partial class FillActual {public static int[] _zoneFill=new int[3];public static int sample;
 private static int WildFillAt(int x,int y,int slot,int[] source)=>sample;public static int Pick(int zone)=>FillAt(0,0,0,zone);}
public static class WorldMapScenario {public static int AfterLoadRemaining;public static bool AfterLoadPending=true;}
public partial class LoadingActual {private int _remainingAfterLoad=-1;private float _doneAt;private const float AfterLoadSeconds=30f;
 public bool Ready(float now)=>AfterLoadReady(now);}
public static partial class Actual {
 public static TileZone[] _zones;public static int[] _regionOf;public static byte[] _kind;
 public static KingdomTitle[] _titles;public static City[] _parents;public static bool[] _painted,_neutralZones;
 public static List<WildRegion> _regions=new();public static List<WildRegionData> _saved;
 public static int Version,_nextId=1;public static bool _built=true,_enabled=true;
 public static int builds;public static void BuildNow(){builds++;_built=true;}
 private static Dictionary<int,WildRegion> IdIndex=new();private static bool _attachPending;private static long _titleSignature;
 private const int MinZones=4,MinLand=16;private const float MountainEdgeShare=.6f,EdgeShare=.5f,LavaEdgeShare=.15f,Hysteresis=.1f;
 private class Counts {public int Land,Rock,Hills,Sand,Frozen,Jungle,Swamp,Lava;}
 private static Counts Count(TileZone z)=>new();private static City OriginOf(TileZone z)=>_parents[z.id];
 private static void Attach(bool full){}private static long TitleSignature()=>0;private static bool RefreshOwners()=>false;
 private static void Shape(List<WildRegion> r,int[] ids){}private static void NameAll(List<WildRegion> r,HashSet<string> used){}
 public static WildRegion RegionOf(TileZone z)=>z!=null&&z.id>=0&&z.id<_regionOf.Length&&_regionOf[z.id]>=0?_regions[_regionOf[z.id]]:null;
 public static void Restore(List<WildRegionData> data) {
  World.world=new();_zones=Enumerable.Range(0,9).Select(i=>new TileZone{id=i}).ToArray();
  for(int i=0;i<9;i++)_zones[i].neighbours=_zones.Where(z=>Math.Abs(z.id-i)==1).ToArray();
  _kind=Enumerable.Repeat((byte)WildKind.Desert,9).ToArray();_regionOf=Enumerable.Repeat(-1,9).ToArray();
  _neutralZones=new bool[0];_titles=new KingdomTitle[9];_parents=new City[9];_painted=new bool[9];
  _regions=new();_saved=data;Regroup(true);
 }
 public static void Rebuild()=>Regroup(false);
}
public static partial class MapActual {
 public static WorldMapWildSave _state;public static WorldMapFrame _frame;public static Dictionary<string,City> _capitals;
 public static Dictionary<string,WorldMapWildUnit> _unitById=new();public static HashSet<int> _neutral;
 private static object _plan;private static bool Active=>_state!=null&&_frame!=null;
 private static bool EnsureUnits()=>true;private static void Bind(){_frame=new();}
 private static string KindKey(WildKind k)=>"desert";private static bool Covers(WorldMapWildUnit u,float x,float y)=>u.covers;
 private static void LonLat(TileZone z,out float x,out float y){x=y=0;}private static void Jitter(TileZone z,ref float x,ref float y){}
 public static KingdomTitle paint;public static KingdomTitle PaintedTitle(TileZone z)=>paint;
 public static void NeutralPaint(int id){_neutral=new(){id};ApplyNeutral();}
}
public static partial class PreviewActual {
 public enum Source:byte {None,Paint,History,Near,Surround,Neutral}
 public class Piece {public List<int> Zones=new();public HashSet<int> Around=new();}
 public class Layout {public int W=3,H=1;public List<Piece> Pieces=new();public List<string> Seats=new();public Dictionary<string,int> SeatOf=new();public int[] Label;public string[] History,HistoryRegion;}
 public static Layout _layout;public static string[] _owner,_title,_surround;public static byte[] _source;public static bool[] _neutral;
 private const int Filler=-2;
 private static void Center(Layout l,int z,out float lon,out float lat){lon=z*.2f;lat=0;}
 public static void Refresh()=>Own();
}
public static class Checks {
 static int n;static void Check(bool ok,string message){n++;if(!ok)throw new Exception(message);}
 public static int Run(){
  var old=new List<WildRegionData>{new(){id=1,kind=2,neutral=true,zones=new(){0,1,2,3}},new(){id=2,kind=2,zones=new(){5,6,7,8}}};
  Actual.Restore(old);Check(Actual._regions.Count==1,"Fixture did not merge neighboring deserts");
  Check(Enumerable.Range(0,9).Where(Actual.IsNeutral).SequenceEqual(new[]{0,1,2,3}),"Legacy neutrality spreads into newly merged land");
  Check(!Actual._regions[0].Neutral,"Partially neutral desert marked entirely neutral");
  var saved=Actual.Save();Check(saved[0].neutralZones.SequenceEqual(new[]{0,1,2,3})&&!saved[0].neutral,"Partial neutrality missing from save");
  Check(saved[0].ShouldSerializeneutralZones(),"Partial neutral zone list omitted by serializer");
  Actual.Restore(saved);Check(Enumerable.Range(0,9).Where(Actual.IsNeutral).SequenceEqual(new[]{0,1,2,3}),"Save/load changes partial neutral extent");
  Actual._kind[4]=0;Actual.Rebuild();Check(Actual._regions.Count==2&&Actual._regions[0].Neutral&&!Actual._regions[1].Neutral,"Region split loses or spreads neutrality");
  Actual._kind[4]=2;Actual.Rebuild();Check(Enumerable.Range(0,9).Where(Actual.IsNeutral).SequenceEqual(new[]{0,1,2,3}),"Region remerge spreads neutrality");
  MapActual.NeutralPaint(6);Check(Actual.IsNeutral(6)&&!Actual.IsNeutral(5)&&!Actual.IsNeutral(7),"Scenario neutral brush affects unpainted neighbors");
  Actual.ToggleNeutral(Actual._zones[6]);Check(!Enumerable.Range(0,9).Any(Actual.IsNeutral),"Whole-region cancel leaves partial neutral flags");
  Actual.ToggleNeutral(Actual._zones[6]);Check(Enumerable.Range(0,9).All(Actual.IsNeutral)&&Actual._regions[0].Neutral,"Whole-region god power no longer works");
  saved=Actual.Save();Check(saved[0].neutral&&!saved[0].ShouldSerializeneutralZones(),"Full-region saves lose legacy neutral flag");
  Actual.SetNeutral(Actual._regions[0],false);Check(!Enumerable.Range(0,9).Any(Actual.IsNeutral),"Clearing neutral region fails");
  Check(Actual.SetNeutralZones(new[]{-1,99},true)==0&&!Actual.IsNeutral(-1)&&!Actual.IsNeutral(99),"Invalid IDs mutate neutral extent");
  var a=new Kingdom();var b=new Kingdom();var national=new KingdomTitle{id=10};var regional=new KingdomTitle{id=20};
  var capital=new City{id=1,kingdom=a,title=national};var local=new City{id=2,kingdom=a,title=regional};
  World.world.cities.AddRange(new[]{capital,local});ModClass.KINGDOM_TITLE_MANAGER=new(){{10,national},{20,regional}};
  var era=new WorldMapEra{wilderness=new(){{"desert","a"}},wilderness_regions=new(){{"desert","local"}}};
  var capitals=new Dictionary<string,City>{{"a",capital}};var titles=new Dictionary<string,KingdomTitle>{{"local",regional}};
  MapActual._unitById=new(){{"desert",new(){id="desert",Area=100}}};
  MapActual.Begin(new(),new(){x=10,y=10},era,capitals,titles);
  Check(WildernessSystem.requests>0,"Late scenario defaults do not request reattachment");
  var z=Actual._zones[0];Check(MapActual.FixedTitle(z)==regional&&MapActual.TitleFor(z)==regional,"Explicit local title replaced by national capital title");
  Check(MapActual._state.regional["desert"]==20,"Explicit local default not saved independently");
  national.dead=true;Check(MapActual.FixedTitle(z)==regional,"Surviving local title depends on destroyed capital title");national.dead=false;
  var manual=new KingdomTitle{id=30};MapActual.paint=manual;Check(MapActual.FixedTitle(z)==manual,"Local defaults overwrite editor paint");MapActual.paint=null;
  regional.dead=true;Check(MapActual.FixedTitle(z)==null&&MapActual.TitleFor(z)==national,"Destroyed local title never falls back");regional.dead=false;
  local.kingdom=b;MapActual.Begin(new(),new(),era,capitals,titles);Check(MapActual._state.regional==null,"Custom scenario changed ownership but stale default grabs foreign title");
  local.kingdom=a;MapActual.Begin(new(),new(),era,capitals,titles);
  MapActual._unitById["small"]=new(){id="small",Area=1};MapActual._state.owners["small"]=10;
  Check(MapActual.FixedTitle(z)==null&&MapActual.TitleFor(z)==national,"Large-unit local default overrides a more specific national declaration");
  MapActual._state=new(){owners=new(){{"desert",10}}};Check(MapActual.FixedTitle(z)==null,"Loading old save invents new local defaults");
  WildernessSystem.kind=WildKind.Mountain;Check(PassageActual.Mask(z)==7,"Mountain ceases to block ordinary land movement");
  Actual.SetNeutralZones(new[]{z.id},true);Check(PassageActual.Mask(z)==7,"Neutral painting changes terrain passability");
  RoadNetwork.roads.Add(z.id);Check(PassageActual.Mask(z)==0,"Road through neutral mountains no longer opens a corridor");
  Check(PassageActual.Mask(Actual._zones[1])==7,"Road corridor opens unconnected neighboring mountain zones");RoadNetwork.roads.Clear();
  WildernessSystem.kind=WildKind.Desert;Check(PassageActual.Mask(z)==1,"Desert adaptation rules changed");
  WildernessSystem.kind=WildKind.Tundra;Check(PassageActual.Mask(z)==3,"Nomadic tundra adaptation rules changed");
  WildernessSystem.kind=WildKind.Jungle;WildernessDevelopmentSystem.open=true;Check(PassageActual.Mask(z)==0,"Developed jungle corridor blocked");
  WildernessDevelopmentSystem.open=false;WildernessSystem.kind=WildKind.None;Check(PassageActual.Mask(z)==0,"Settled valley blocked by wilderness rule");
  var pa=new WorldMapPolity{id="a",capital="capital"};var pb=new WorldMapPolity{id="b",capital="local"};
  ScenarioEditing.Cities=new(){new(){id="capital",region="core"},new(){id="local",region="local_region"}};
  ScenarioEditing.owners=new(){{"capital",pa},{"local",pa}};ScenarioEditing.LandCells=new(){{1,"n"}};
  PreviewActual._layout=new(){Pieces=new(){new(){Zones=new(){0,1,2},Around=new(){0}}},Seats=new(){"capital","local"},
   SeatOf=new(){{"capital",0},{"local",1}},Label=new[]{0,0,0},History=new[]{"a","a","a"},HistoryRegion=new[]{"local_region","local_region","local_region"}};
  PreviewActual.Refresh();Check(!PreviewActual._neutral[0]&&PreviewActual._neutral[1]&&!PreviewActual._neutral[2],"Editor preview neutral brush affects an entire piece");
  Check(PreviewActual._owner[0]=="p:a"&&PreviewActual._owner[1]==null&&PreviewActual._owner[2]=="p:a","Editor preview hides unpainted neighboring ownership");
  Check(PreviewActual._title[2]=="local_region","Editor preview still shows national capital title despite local defaults");
  ScenarioEditing.LandCells[0]="r:core";PreviewActual.Refresh();Check(PreviewActual._title[0]=="core","Editor preview local defaults override explicit regional paint");
  ScenarioEditing.LandCells.Clear();PreviewActual.Refresh();Check(PreviewActual._neutral.All(v=>!v),"Erasing editor neutral paint leaves stale flags");
  ScenarioEditing.owners["local"]=pb;PreviewActual.Refresh();Check(PreviewActual._title[2]=="core","Editor preview applies stale local defaults to foreign city");
  FillActual._zoneFill[0]=10;WildernessShape.slot=0;Check(FillActual.Pick(0)==10,"Shape-free valley loses known political coverage");
  WildernessShape.slot=1;FillActual.sample=0;Check(FillActual.Pick(0)==10,"Empty wilderness edge candidate punches a hole in known territory");
  FillActual.sample=20;Check(FillActual.Pick(0)==20,"Known-territory fallback overrides a valid original smoothing result");
  WildernessShape.slot=0;Check(FillActual.Pick(1)==0,"Unclaimed land is annexed by coverage fallback");
  WildernessShape.slot=1;FillActual.sample=0;Check(FillActual.Pick(1)==0,"Neutral wilderness is annexed by coverage fallback");
  Actual.Restore(new(){new(){id=1,kind=2,zones=new(){0,1,2,3,4,5,6,7,8}}});
  a=new();regional=new(){id=20,holder=a};local=new(){id=2,kingdom=a,title=regional};World.world.cities.Add(local);
  ModClass.KINGDOM_TITLE_MANAGER=new(){{20,regional}};MapActual._state=new();MapActual._frame=new();
  for(int i=0;i<9;i++){Actual._titles[i]=regional;Actual._parents[i]=local;}
  Actual._kind[4]=0;Actual.Rebuild();var valley=Actual._zones[4];
  Check(!WildernessSystem.IsWild(valley)&&valley.city==null&&MapActual.KnownOwner(valley)==a,"Reclassified former wilderness loses all political coverage");
  Check(MapActual.KnownCity(valley)==local&&MapActual.KnownTerritory().Contains(4),"Reclassification loses administrative origin");
  Check(World.world.cities.Count==1,"Coverage restoration creates fake cities");
  valley.tiles_with_ground=0;Check(MapActual.KnownOwner(valley)==null,"Flooded former territory remains owned");valley.tiles_with_ground=64;
  valley.city=local;Check(MapActual.KnownOwner(valley)==null,"Coverage metadata overrides real city");
  local.zones.Add(valley);MapActual.CaptureTerritory(new[]{local});valley.city=null;
  Check(MapActual.KnownOwner(valley)==a,"Pre-carving city coverage not captured");
  regional.holder=b;Check(MapActual.KnownOwner(valley)==b&&MapActual.KnownCity(valley)==null,"Changed legal holder ignored by fallback");regional.holder=a;
  var revised=new KingdomTitle{id=30,holder=b};ModClass.KINGDOM_TITLE_MANAGER[30]=revised;local.title=revised;local.kingdom=b;
  Check(MapActual.KnownOwner(valley)==b&&MapActual.KnownCity(valley)==local&&MapActual._state.territory[4].title==30,"Former wilderness retains obsolete administrative title after reassignment");
  MapActual.RememberTerritory(valley,regional,local,false,true);
  Check(MapActual.KnownOwner(valley)==a&&MapActual.KnownCity(valley)==null&&MapActual._state.territory[4].title==20,"Former wilderness ignores pinned manual ownership");
  MapActual.RememberTerritory(valley,revised,local,false);local.title=null;
  Check(MapActual.KnownOwner(valley)==b&&MapActual._state.territory[4].title==-1,"Former wilderness resurrects removed city title");
  MapActual.RememberTerritory(valley,regional,local,true);Check(MapActual.KnownOwner(valley)==null,"Explicit neutral land regains obsolete coverage");
  Actual._built=false;Actual.builds=0;Actual.EnsureBuilt();Actual.EnsureBuilt();Check(Actual._built&&Actual.builds==1,"Scenario finalization skips initial wilderness or rebuilds it twice");
  var screen=new LoadingActual();WorldMapScenario.AfterLoadRemaining=10;Check(!screen.Ready(0),"Loading screen opens before pending work");
  WorldMapScenario.AfterLoadRemaining=9;Check(!screen.Ready(25)&&!screen.Ready(40),"Loading screen opens at fixed 30 seconds despite progress");
  WorldMapScenario.AfterLoadRemaining=8;Check(!screen.Ready(50),"Loading screen fails to renew progress watchdog");
  Check(screen.Ready(81),"Stalled loading screen never times out");
  WorldMapScenario.AfterLoadPending=false;Check(screen.Ready(82),"Completed initialization keeps screen closed");
  return n;
 }
}
'@
Add-Type -TypeDefinition ($fixture + $data + $mapData + 'public static partial class Actual {' + ($methods -join "`n") + '}' +
    'public static partial class MapActual {' + ($mapMethods -join "`n") + '}' +
    'public static partial class PassageActual {' + $passage + '}' +
    'public static partial class PreviewActual {' + $preview + '}' +
    'public static partial class FillActual {' + $fill + '}' +
    'public partial class LoadingActual {' + $loading + '}') -IgnoreWarnings -WarningAction SilentlyContinue
Write-Output "$([Checks]::Run()) production neutral brush, regroup, legacy save and local-title policy checks passed."
$partial = [WildRegionData]::new()
$partial.neutralZones = [System.Collections.Generic.List[int]]::new([int[]]@(1,3))
$json = [Newtonsoft.Json.JsonConvert]::SerializeObject($partial)
$loaded = [Newtonsoft.Json.JsonConvert]::DeserializeObject($json, [WildRegionData])
if ($loaded.neutral -or ($loaded.neutralZones -join ',') -ne '1,3') { throw 'Partial neutral JSON roundtrip loses zone flags' }
$partial.neutral = $true
if ([Newtonsoft.Json.JsonConvert]::SerializeObject($partial).Contains('neutralZones')) { throw 'Full neutral save redundantly writes partial flags' }
$legacy = [Newtonsoft.Json.JsonConvert]::DeserializeObject('{"id":1,"kind":2,"neutral":true,"zones":[0,1,2,3,4,5,6,7,8]}', [WildRegionData])
[Actual]::Restore([System.Collections.Generic.List[WildRegionData]]::new([WildRegionData[]]@($legacy)))
if (@(0..8 | Where-Object { [Actual]::IsNeutral([int]$_) }).Count -ne 9) { throw 'Legacy neutral JSON no longer restores the whole region' }
$state = [WorldMapWildSave]::new()
$state.regional = [System.Collections.Generic.Dictionary[string,long]]::new()
$state.regional.Add('desert',20)
$state.territory = [System.Collections.Generic.Dictionary[int,ScenarioLandOrigin]]::new()
$land = [ScenarioLandOrigin]::new(); $land.city=2; $land.title=20; $land.pinned=$true
$state.territory.Add(4,$land)
$state.bounded_anchors = [System.Collections.Generic.HashSet[int]]::new()
[void]$state.bounded_anchors.Add(4)
$state.boundary_revision=1
$savedState = [Newtonsoft.Json.JsonConvert]::DeserializeObject([Newtonsoft.Json.JsonConvert]::SerializeObject($state), [WorldMapWildSave])
if ($savedState.regional['desert'] -ne 20) { throw 'Local title defaults lost in JSON roundtrip' }
if ($savedState.territory[4].city -ne 2 -or $savedState.territory[4].title -ne 20) { throw 'Former wilderness coverage lost in JSON roundtrip' }
if (!$savedState.territory[4].pinned) { throw 'Manually pinned territorial coverage lost in JSON roundtrip' }
if (!$savedState.bounded_anchors.Contains(4) -or $savedState.boundary_revision -ne 1) { throw 'Explicit administrative bounds or repair revision lost in JSON roundtrip' }
$legacyState = [Newtonsoft.Json.JsonConvert]::DeserializeObject('{}', [WorldMapWildSave])
if ($null -ne $legacyState.regional -or $legacyState.ShouldSerializeregional()) { throw 'Old save invents local defaults' }
if ($null -ne $legacyState.territory -or $legacyState.ShouldSerializeterritory()) { throw 'Old save invents territorial coverage' }
if ($legacyState.boundary_revision -ne 0 -or $legacyState.ShouldSerializebounded_anchors()) { throw 'Legacy save skips boundary migration or invents fixed administrative boundaries' }
Write-Output '10 Newtonsoft.Json neutral, legacy save, regional default and territorial coverage persistence checks passed.'
