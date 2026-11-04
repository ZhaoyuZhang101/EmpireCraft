# Executes production city assignment and growth rules against small terrain grids.
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
function Member([string]$path, [string]$signature) {
    $source = Get-Content (Join-Path $root $path) -Raw
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $open = $source.IndexOf('{', $start)
    $arrow = $source.IndexOf('=>', $start)
    if ($arrow -ge 0 -and $arrow -lt $open) { return $source.Substring($start,$source.IndexOf(';',$arrow)+1-$start) }
    $end = $open+1; $depth=1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Substring($start,$end-$start)
}
$zonesPath='Scripts/GeneralSystems/WorldMap/WorldMapZones.cs'
$graph=@('public static int[] NearestOf(', 'public static int[] Partition(', 'public static int[] RootedPartition(', 'private static float Grain(', 'private static float Step(', 'private static bool Diagonal(',
    'private static IEnumerable<int> Neighbours(', 'private static long PairKey(', 'internal sealed class MinHeap') |
    ForEach-Object { Member $zonesPath $_ }
$scenarioPath='Scripts/GeneralSystems/WorldMap/WorldMapScenario.cs'
$assign=@('private static PlannedCity NearestOf(', 'private static Vector2Int Seat(', 'private static List<List<City>> DivisionParts(') | ForEach-Object { Member $scenarioPath $_ }
$growthPath='Scripts/GamePatches/CityExpansionPatch.cs'
$growth=@('public static void AfterGetZoneToClaim(', 'private static float ExpansionScore(', 'public static bool Bordering(',
    'public static bool Reachable(', 'public static void AfterIsZoneToClaimStillGood(', 'public static void AfterCanGrowZones(') |
    ForEach-Object { Member $growthPath $_ }
$source=Get-Content (Join-Path $root $growthPath) -Raw
$growth += [regex]::Match($source,'public static bool AtLimit\([^;]+;').Value
$base=Member 'Scripts/GeneralSystems/TerrainResistance.cs' 'public static float Base('
$fixture=@'
using System;using System.Linq;using System.Collections.Generic;
public enum FrontTerrain {Plains,Forest,Jungle,Hills,Mountain,River,Swamp,Desert,Snow,Urban}
public struct Vector2Int {public int x,y;public Vector2Int(int a,int b){x=a;y=b;}public int sqrMagnitude=>x*x+y*y;
 public static Vector2Int operator -(Vector2Int a,Vector2Int b)=>new(a.x-b.x,a.y-b.y);}
public class WorldTile {public int x,y,island=1;public object region=new();public TileZone zone;public bool isSameIsland(WorldTile t)=>island==t.island;}
public class TileZone {public int id,x,y,tiles_with_ground=64;public City city;public bool closed,mountain,wild,allowed=true,biome=true,settled=true,road;
 public FrontTerrain terrain=FrontTerrain.Plains;public WorldTile centerTile;public WorldTile[] tiles;public TileZone[] neighbours=Array.Empty<TileZone>();
 public bool canBeClaimedByCity(City c)=>allowed&&city==null;public bool checkCanSettleInThisBiomes(object s)=>biome;}
public class City {public long id;public object data=new();public bool dead,foreign,grow=true;public List<TileZone> zones=new();public WorldTile tile;
 public bool isRekt()=>dead;public WorldTile getTile()=>tile;public bool canGrowZones()=>grow;}
public class Actor {public object subspecies=new();}
public class ZoneConnection {public TileZone zone;}
public class CityZoneGrowth {public List<ZoneConnection> _zones_checked=new();public TileZone fallback;public TileZone getRandomZone(City c)=>fallback;}
public static class ModClass {public static int CITY_MAX_ZONES=50;}
public static class Config {public const int CITY_ZONE_SIZE=8;}
public static class AncientWarfareCompatibility {public static bool OwnsObject(City c)=>c?.foreign==true;}
public static class RoadNetwork {public static bool IsRoad(TileZone z)=>z.road;}
namespace GeneralSystems {
 public static class TerrainSystem {
  public static bool LastSeatOpen;
  public static FrontTerrain Of(TileZone z)=>z.terrain;public static bool Mountainous(TileZone z)=>z.mountain;
  public static bool Crossable(TileZone a,TileZone b,bool seatOpen=true){LastSeatOpen=seatOpen;return seatOpen&&a.city?.getTile()?.zone==a||!a.closed&&!b.closed;}
 }
}
public static partial class TerrainResistance {public const float RiverCrossing=2f;}
public static class BarrenLandSystem {public const int None=0;}
public class WorldMapDivision {public string name="山西";public List<int> boundary;}
public static class WorldMapAdministrativeBoundaries {public static bool defined;public static List<int> For(object era,string polity,string division)=>defined?new(){1}:new();}
public static class FrontierWalls {public static List<List<City>> Split(List<City> cities)=>cities.Select(c=>new List<City>{c}).ToList();}
public static partial class WorldMapZones {
 public static int Width,Height,calls;public static TileZone[] _zones;public static float[] _travel,_noise,_grain;public static bool[] _claimable,_river;public static int[] _land,_barren;
 private const int MinLand=6;private const float Mountain=6f,RiverCost=1.5f;private static HashSet<long> _blocked=new();
 private static bool Blocked(int a,int b)=>_blocked.Contains(PairKey(a,b));
 private static bool Open(int a,int b){
  if(Blocked(a,b))return false;
  if(!Diagonal(a,b))return true;
  int first=a/Width*Width+b%Width,second=b/Width*Width+a%Width;
  return _claimable[first]&&!Blocked(a,first)&&!Blocked(first,b)||_claimable[second]&&!Blocked(a,second)&&!Blocked(second,b);
 }
 public static HashSet<int> behind=new();public static bool Behind(Vector2Int seat,int index)=>behind.Contains(IndexOf(seat));
 public static int IndexOf(Vector2Int t)=>t.y/8*Width+t.x/8;
 public static int I(int x,int y)=>y*Width+x;
 public static void Setup(int w,int h){Width=w;Height=h;int n=w*h;_blocked.Clear();behind.Clear();
  _travel=Enumerable.Repeat(1f,n).ToArray();_noise=Enumerable.Repeat(1f,n).ToArray();_grain=Enumerable.Repeat(1f,n).ToArray();_claimable=Enumerable.Repeat(true,n).ToArray();
  _river=new bool[n];_land=Enumerable.Repeat(64,n).ToArray();_barren=new int[n];
  _zones=Enumerable.Range(0,n).Select(i=>NewZone(i,i%w,i/w)).ToArray();ScenarioActual.Clear();
 }
 private static TileZone NewZone(int id,int x,int y){var z=new TileZone{id=id,x=x,y=y};z.centerTile=new(){x=x*8+4,y=y*8+4,zone=z};z.tiles=new[]{z.centerTile};return z;}
 public static void Rock(int x,int y){int i=I(x,y);_claimable[i]=false;_travel[i]=6;}
 public static void Water(int x,int y){int i=I(x,y);_claimable[i]=false;_land[i]=0;_travel[i]=6;}
}
public static partial class ScenarioActual {
 private static object _era=new();
 public static List<List<City>> Parts(WorldMapDivision data,List<City> cities)=>DivisionParts("ming",data,cities);
 public class PlannedCity {public City city;public Vector2Int tile;}
 private static readonly Dictionary<string,int[]> _cityTerrainReach=new();
 public static void Clear()=>_cityTerrainReach.Clear();public static int Cached=>_cityTerrainReach.Count;
 public static PlannedCity CityAt(int x,int y,int id){var z=WorldMapZones._zones[WorldMapZones.I(x,y)];var c=new City{id=id,tile=z.centerTile};c.zones.Add(z);z.city=c;return new(){city=c,tile=new(z.centerTile.x,z.centerTile.y)};}
 public static PlannedCity Pick(List<PlannedCity> cities,int x,int y,bool terrain=true){int i=WorldMapZones.I(x,y);return NearestOf(cities,WorldMapZones._zones[i],i,terrain);}
}
public static partial class GrowthActual {
 public static bool CanClaimZone(City c,TileZone z)=>c!=null&&z!=null&&z.city==null&&!z.wild&&!AtLimit(c);
 public static bool Settled(City c,TileZone z)=>z.settled;
 public static TileZone Pick(City c,TileZone native,params TileZone[] others){var g=new CityZoneGrowth();g._zones_checked=others.Select(z=>new ZoneConnection{zone=z}).ToList();
  AfterGetZoneToClaim(g,new(),c,false,ref native);return native;}
}
public static class Checks {
 static int n;static void Check(bool ok,string m){n++;if(!ok)throw new Exception(m);}
 static TileZone Candidate(City c,FrontTerrain t,int x=12,int y=4){var z=new TileZone{terrain=t,x=x/8,y=y/8};z.centerTile=new(){x=x,y=y,zone=z};z.tiles=new[]{z.centerTile};z.neighbours=new[]{c.tile.zone};return z;}
 public static int Run(){
  WorldMapZones.Setup(9,7);for(int y=0;y<7;y++)WorldMapZones.Rock(3,y);
  var labels=WorldMapZones.Partition(new(){(WorldMapZones.I(2,1),0,60),(WorldMapZones.I(6,5),1,60)},true);
  Check(labels[WorldMapZones.I(4,1)]==1,"Wild administrative partition ignores the ridge and bypasses the valley approach");
  WorldMapZones.Setup(11,3);var seeds=new List<(int index,int label,float budget)>{(WorldMapZones.I(0,1),0,60),(WorldMapZones.I(5,1),1,60),(WorldMapZones.I(10,1),0,60)};
  labels=WorldMapZones.Partition(seeds,true);Check(labels[WorldMapZones.I(10,1)]==0,"Fixture fails to reproduce independent seat island");
  var roots=new List<(int index,int label)>{(WorldMapZones.I(0,1),0),(WorldMapZones.I(5,1),1)};
  labels=WorldMapZones.RootedPartition(seeds,roots,true);
  Check(labels[WorldMapZones.I(10,1)]==1,"Remote auxiliary seat creates a province enclave across another province");
  Check(labels[WorldMapZones.I(0,1)]==0&&labels[WorldMapZones.I(5,1)]==1,"Connectivity repair moves actual city roots");
  roots.Add((WorldMapZones.I(10,1),0));labels=WorldMapZones.RootedPartition(seeds,roots,true);
  Check(labels[WorldMapZones.I(10,1)]==0,"A real independent city is deleted as if it were a stray seat");
  WorldMapZones.Setup(7,3);for(int y=0;y<3;y++)WorldMapZones.Water(3,y);
  seeds=new(){(WorldMapZones.I(0,1),0,60),(WorldMapZones.I(6,1),0,60)};roots=new(){(WorldMapZones.I(0,1),0)};
  labels=WorldMapZones.RootedPartition(seeds,roots,true);
  Check(labels[WorldMapZones.I(6,1)]==-1&&labels[WorldMapZones.I(3,1)]==-1,"An auxiliary seat claims an island across the sea");
  var divided=new List<City>{new(){id=1},new(){id=2}};var definition=new WorldMapDivision();WorldMapAdministrativeBoundaries.defined=true;
  Check(ScenarioActual.Parts(definition,divided).Count==1,"Historical Shanxi is split into a separate Datong province by the wall");
  WorldMapAdministrativeBoundaries.defined=false;definition.boundary=new(){1};Check(ScenarioActual.Parts(definition,divided).Count==1,"Custom historical boundary is ignored by provincial creation");
  definition.boundary=null;Check(ScenarioActual.Parts(definition,divided).Count==2,"Unspecified territorial inference loses wall-side protection");
  WorldMapZones.Setup(9,7);for(int y=0;y<6;y++)WorldMapZones.Rock(4,y);
  var near=ScenarioActual.CityAt(3,2,1);var valley=ScenarioActual.CityAt(7,6,2);var pool=new List<ScenarioActual.PlannedCity>{near,valley};
  Check(ScenarioActual.Pick(pool,5,2,false)==near,"Fixture fails to reproduce straight-distance cross-ridge assignment");
  Check(ScenarioActual.Pick(pool,5,2)==valley,"City assignment still prefers near city across ridge to reachable valley city");
  Check(ScenarioActual.Pick(pool,5,1)==valley&&ScenarioActual.Cached==1,"City assignment reruns terrain search for every target");
  WorldMapZones.behind.Add(WorldMapZones.I(3,2));Check(ScenarioActual.Pick(pool,2,2)==valley,"Unavailable wall-side city prevents choosing another reachable city");
  WorldMapZones.behind.Add(WorldMapZones.I(7,6));Check(ScenarioActual.Pick(pool,5,2)==null,"City assignment ignores wall-side exclusion");WorldMapZones.behind.Clear();
  for(int y=0;y<7;y++)WorldMapZones.Rock(4,y);ScenarioActual.Clear();
  Check(ScenarioActual.Pick(new(){near},5,2)==null,"A disconnected administrative region borrows a city across a sealed ridge");
  Check(ScenarioActual.Pick(new(){near},5,2,false)==near,"Virtual administrative/wilderness fallback changed its original distance semantics");
  WorldMapZones.Setup(5,5);for(int x=0;x<5;x++)WorldMapZones.Water(x,2);near=ScenarioActual.CityAt(1,1,1);
  Check(ScenarioActual.Pick(new(){near},1,3)==null,"Settled city assignment crosses sea");
  int[] general=WorldMapZones.NearestOf(new[]{WorldMapZones.I(1,1)});Check(general[WorldMapZones.I(1,3)]==0,"General cross-sea ownership lookup was changed");
  WorldMapZones.Setup(4,4);near=ScenarioActual.CityAt(0,0,1);WorldMapZones.Rock(1,0);WorldMapZones.Rock(0,1);
  Check(ScenarioActual.Pick(new(){near},1,1)==null,"Settled assignment slips diagonally through blocked terrain corner");
  WorldMapZones.Setup(3,3);var plan=ScenarioActual.CityAt(1,1,1);var c=plan.city;var seat=c.tile.zone;
  var hill=Candidate(c,FrontTerrain.Hills);var plain=Candidate(c,FrontTerrain.Plains,20,4);
  Check(GrowthActual.Pick(c,hill,plain)==plain,"Native random hill candidate beats reachable plain");
  Check(GrowthActual.Pick(c,plain,hill)==plain,"Checked hill replaces already preferred plain");
  seat.neighbours=new[]{plain};Check(GrowthActual.Pick(c,hill)==plain,"Native early result hides a better candidate on the existing city frontier");seat.neighbours=Array.Empty<TileZone>();
  var own=new TileZone{city=c,terrain=FrontTerrain.Plains};c.zones.Add(own);var pocket=Candidate(c,FrontTerrain.Plains,24,4);pocket.neighbours=new[]{seat,own};
  Check(GrowthActual.Pick(c,plain,pocket)==pocket,"Compact connected pocket loses to a one-edge protrusion");c.zones.Remove(own);
  plain.centerTile.x=52;Check(GrowthActual.Pick(c,hill,plain)==plain,"Straight distance overwhelms terrain resistance");plain.centerTile.x=20;
  var river=Candidate(c,FrontTerrain.River);var forest=Candidate(c,FrontTerrain.Forest);
  Check(GrowthActual.Pick(c,river,forest)==forest,"Unbridged river crossing has no expansion cost");
  river.road=true;Check(GrowthActual.Pick(c,river,forest)==river,"Existing road does not reduce river crossing cost");
  river.road=false;own=new(){city=c,terrain=FrontTerrain.River};river.neighbours=new[]{own};
  Check(GrowthActual.Pick(c,river,forest)==river,"Following an existing river corridor is treated as crossing the river");river.neighbours=new[]{seat};
  var blocked=Candidate(c,FrontTerrain.Plains);blocked.closed=true;
  Check(GrowthActual.Pick(c,blocked,hill)==hill,"Seat exemption claims through a blocked boundary");
  blocked.road=true;Check(GrowthActual.Pick(c,blocked,hill)==hill,"A road flag overrides a physically closed ridge");
  Check(!GeneralSystems.TerrainSystem.LastSeatOpen,"Settlement boundary still uses military seat exception");
  plain.wild=true;Check(GrowthActual.Pick(c,plain,hill)==hill,"Terrain preference overrides wilderness exclusion");plain.wild=false;
  plain.biome=false;Check(GrowthActual.Pick(c,plain,hill)==hill,"Terrain preference overrides species biome restriction");plain.biome=true;
  plain.city=new();Check(GrowthActual.Pick(c,plain,hill)==hill,"Terrain preference steals occupied city land");plain.city=null;
  plain.centerTile.island=2;Check(GrowthActual.Pick(c,plain,hill)==hill,"Terrain preference crosses an island barrier");plain.centerTile.island=1;
  c.grow=false;Check(GrowthActual.Pick(c,plain,hill)==null,"Terrain preference overrides native growth gate");c.grow=true;
  ModClass.CITY_MAX_ZONES=1;Check(GrowthActual.Pick(c,plain,hill)==null,"Terrain preference exceeds city limit");ModClass.CITY_MAX_ZONES=50;
  bool valid=true;GrowthActual.AfterIsZoneToClaimStillGood(c,blocked,ref valid);Check(!valid,"Queued expansion ignores newly blocked boundary");
  blocked.closed=false;valid=true;GrowthActual.AfterIsZoneToClaimStillGood(c,blocked,ref valid);Check(valid,"Reopened terrain stays permanently unclaimable");
  TileZone result=blocked;c.foreign=true;GrowthActual.AfterGetZoneToClaim(new(),new(),c,false,ref result);Check(result==blocked,"Foreign compatibility city modified");c.foreign=false;
  result=blocked;GrowthActual.AfterGetZoneToClaim(new(),new(),c,true,ref result);Check(result==blocked,"Debug candidate modified");
  return n;
 }
}
'@
Add-Type -TypeDefinition ($fixture+'public static partial class WorldMapZones {'+($graph -join "`n")+'}'+
    'public static partial class ScenarioActual {'+($assign -join "`n")+'}'+
    'public static partial class TerrainResistance {'+$base+'}'+
    'public static partial class GrowthActual {'+($growth -join "`n")+'}') -IgnoreWarnings -WarningAction SilentlyContinue
Write-Output "$([Checks]::Run()) production terrain-based city assignment, growth preference, barrier and capacity checks passed."
