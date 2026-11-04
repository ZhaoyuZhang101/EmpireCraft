$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
function Member([string]$file,[string]$signature){
 $source=Get-Content (Join-Path $root $file) -Raw
 $start=$source.IndexOf($signature,[StringComparison]::Ordinal);if($start -lt 0){throw "Missing $signature"}
 $open=$source.IndexOf('{',$start);$arrow=$source.IndexOf('=>',$start)
 if($arrow -ge 0 -and $arrow -lt $open){return $source.Substring($start,$source.IndexOf(';',$arrow)+1-$start)}
 $end=$open+1;$depth=1;while($depth -gt 0){if($source[$end] -eq '{'){$depth++};if($source[$end] -eq '}'){$depth--};$end++}
 $source.Substring($start,$end-$start)
}
$file='Scripts/GeneralSystems/WildernessSystem.cs'
$methods=@('private static void Connect(', 'private static bool[] FollowSpread(', 'private static int Absorb(',
 'private static bool PinnedTitle(', 'private static KingdomTitle TitleOfCity(', 'private static KingdomTitle AnchorTitle(',
 'private static Kingdom RealmOf(', 'private static City NearestCity(', 'public static City OriginOf(')|ForEach-Object{Member $file $_}
$file='Scripts/GeneralSystems/WorldMap/WorldMapWilderness.cs'
$map=@('internal static bool BoundedAnchor(', 'internal static void FollowAnchor(', 'internal static City AnchorCity(',
 'internal static void MarkBoundaryRepaired(')|ForEach-Object{Member $file $_}
$fixture=@'
using System;using System.Linq;using System.Collections.Generic;
public sealed class Empire {public Kingdom CoreKingdom;}
public sealed class Kingdom {public long id;public object data=new();public Empire empire;public bool isRekt()=>false;public Empire GetEmpire()=>empire;}
public sealed class WorldTile {public int x,y;}
public sealed class TileZone {public int id;public City city;public WorldTile centerTile=new();public TileZone[] neighbours_all=Array.Empty<TileZone>();}
public sealed class City {public long id;public object data=new();public bool dead;public Kingdom kingdom;public KingdomTitle title;public WorldTile tile=new();
 public bool isRekt()=>dead;public bool isAlive()=>!dead;public WorldTile getTile()=>tile;public KingdomTitle GetTitle()=>title;}
public sealed class KingdomTitle {public long id;public Kingdom holder;public bool isRekt()=>false;public Kingdom FindRealmTitleHolder()=>holder;
 public List<City> getCities()=>World.world.cities.list.Where(c=>c.title==this&&!c.dead).ToList();}
public sealed class Cities {public List<City> list=new();public City get(long id)=>list.FirstOrDefault(c=>c.id==id);}
public sealed class World {public static World world=new();public Cities cities=new();}
public sealed class WildRegion {public List<int> Zones=new();}
public static class LogService {public static void LogInfo(string s){}}
namespace WorldMap {
 public static class FrontierWalls {public static bool Behind(City c,TileZone z)=>false;}
 public static partial class WorldMapWilderness {
  public sealed class State {public Dictionary<int,long> anchors=new();public HashSet<int> bounded_anchors=new();public int boundary_revision;}
  public static State _state=new();public static bool OnEarth=true,FixedLayout=false;public static bool NeedsBoundaryRepair=>_state.boundary_revision<1;
  public static Dictionary<int,KingdomTitle> painted=new(),history=new();
  public static KingdomTitle PaintedTitle(TileZone z)=>painted.TryGetValue(z.id,out var t)?t:null;
  public static KingdomTitle FixedTitle(TileZone z)=>PaintedTitle(z);
  public static KingdomTitle TitleFor(TileZone z)=>history.TryGetValue(z.id,out var t)?t:null;
 }
}
public static partial class Actual {
 public static KingdomTitle[] _titles;public static City[] _parents;public static TileZone[] _zones;public static bool[] _painted,_walled;
 public static int[] _regionOf;public static List<WildRegion> _regions;public static int Version;
 private static int Index(TileZone z)=>z?.id??-1;public static KingdomTitle TitleOf(TileZone z)=>_titles[z.id];
 public static bool IsPainted(TileZone z)=>_painted[z.id];private static bool OnWall(int id)=>_walled[id];
 private static KingdomTitle[] Spread(Dictionary<City,KingdomTitle> d,Func<City,bool> starts,float limit,out bool[] reached,out City[] cities){
  cities=_zones.Select(z=>World.world.cities.list.OrderBy(c=>Math.Abs(c.tile.x-z.centerTile.x)).First()).ToArray();
  reached=Enumerable.Repeat(true,_zones.Length).ToArray();return cities.Select(c=>c.title).ToArray();}
 public static void Setup(){World.world=new();WorldMap.WorldMapWilderness._state=new();WorldMap.WorldMapWilderness.painted.Clear();WorldMap.WorldMapWilderness.history.Clear();
  _zones=Enumerable.Range(0,9).Select(i=>new TileZone{id=i,centerTile=new(){x=i}}).ToArray();
  for(int i=0;i<9;i++)_zones[i].neighbours_all=_zones.Where(z=>Math.Abs(z.id-i)==1).ToArray();
  _titles=new KingdomTitle[9];_parents=new City[9];_painted=new bool[9];_walled=new bool[9];_regionOf=Enumerable.Repeat(0,9).ToArray();
  _regions=new(){new(){Zones=Enumerable.Range(1,7).ToList()}};}
 public static City Put(int zone,long id,Kingdom k,KingdomTitle t){var c=new City{id=id,kingdom=k,title=t,tile=new(){x=zone}};_zones[zone].city=c;World.world.cities.list.Add(c);return c;}
 public static void Link()=>Connect(new(),new City[9],new KingdomTitle[9]);public static KingdomTitle Anchor(int id)=>AnchorTitle(_zones[id]);
}
public static class Checks {
 static int n;static void Check(bool v,string s){n++;if(!v)throw new Exception(s);}
 public static int Run(){
  Actual.Setup();var ka=new Kingdom{id=1};var kb=new Kingdom{id=2};var shared=new KingdomTitle{id=10,holder=ka};
  var a=Actual.Put(0,1,ka,shared);var b=Actual.Put(8,2,kb,shared);
  for(int i=1;i<8;i++){Actual._titles[i]=shared;var c=i<=5?a:b;Actual._parents[i]=c;WorldMap.WorldMapWilderness._state.anchors[i]=c.id;}
  WorldMap.WorldMapWilderness._state.anchors[3]=b.id;Actual.Link();
  Check(Actual._parents[3]==a,"Same lawful title lets a remote province keep an unconnected administrative island");
  Check(WorldMap.WorldMapWilderness.AnchorCity(Actual._zones[3])==a,"Connectivity repair leaves a stale anchor that restores the enclave");
  Check(Actual.OriginOf(Actual._zones[3])==a,"Origin lookup restores the obsolete province after repair");
  Check(Actual._parents[6]==b&&Actual._parents[7]==b,"Repair eats the other province's connected territory");
  var old=Actual._parents.ToArray();Actual.Link();Check(old.SequenceEqual(Actual._parents),"Second connectivity pass oscillates administrative ownership");
  WorldMap.WorldMapWilderness._state.bounded_anchors.Add(3);WorldMap.WorldMapWilderness._state.anchors[3]=b.id;Actual._parents[3]=b;Actual.Link();
  Check(WorldMap.WorldMapWilderness.AnchorCity(Actual._zones[3])==b,"A deliberate historical boundary is overwritten as a random seat island");
  WorldMap.WorldMapWilderness._state.bounded_anchors.Clear();var foreign=new KingdomTitle{id=20,holder=kb};
  WorldMap.WorldMapWilderness.history[3]=foreign;WorldMap.WorldMapWilderness._state.anchors[3]=a.id;
  Check(Actual.Anchor(3)==null,"Inferred province anchor overrides another realm's historical territory");
  WorldMap.WorldMapWilderness._state.bounded_anchors.Add(3);Check(Actual.Anchor(3)==shared,"Explicit core administrative bounds lose to broad geographic-unit defaults");
  WorldMap.WorldMapWilderness.painted[3]=foreign;Check(Actual.Anchor(3)==foreign,"Manual editor paint loses priority");
  WorldMap.WorldMapWilderness.MarkBoundaryRepaired();Check(WorldMap.WorldMapWilderness._state.boundary_revision==1,"Legacy boundary repair is not persisted");
  WorldMap.WorldMapWilderness.painted.Clear();WorldMap.WorldMapWilderness._state.bounded_anchors.Clear();WorldMap.WorldMapWilderness.FixedLayout=true;
  Actual._titles[3]=shared;Actual._parents[3]=b;WorldMap.WorldMapWilderness._state.anchors[3]=b.id;Actual.Link();
  Check(Actual._parents[3]==b&&WorldMap.WorldMapWilderness.AnchorCity(Actual._zones[3])==b,"Repairing another missing claim redraws an established preset administrative boundary");
  return n;
 }
}
'@
Add-Type -TypeDefinition ($fixture+'public static partial class Actual {'+($methods -join "`n")+'}namespace WorldMap {public static partial class WorldMapWilderness {'+($map -join "`n")+'}}')
Write-Output "$([Checks]::Run()) production provincial connectivity, stale-anchor, historical boundary and migration checks passed."
