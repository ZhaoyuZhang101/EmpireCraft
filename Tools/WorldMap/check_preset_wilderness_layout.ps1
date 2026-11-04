$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$source=Get-Content (Join-Path $root 'Scripts/GeneralSystems/WildernessSystem.cs') -Raw
function Member([string]$signature){
 $start=$source.IndexOf($signature,[StringComparison]::Ordinal);if($start -lt 0){throw "Missing $signature"}
 $open=$source.IndexOf('{',$start);$arrow=$source.IndexOf('=>',$start)
 if($arrow -ge 0 -and $arrow -lt $open){return $source.Substring($start,$source.IndexOf(';',$arrow)+1-$start)}
 $end=$open+1;$depth=1;while($depth -gt 0){if($source[$end] -eq '{'){$depth++};if($source[$end] -eq '}'){$depth--};$end++}
 $source.Substring($start,$end-$start)
}
$members=@('private static void Build(', 'private static int Regroup(', 'private sealed class OldRegion',
 'private struct Counts', 'private static float EdgeOf(', 'internal static bool Absorbs(',
 'private static List<(List<int> zones, WildKind kind)> PresetGroups(', 'private static bool KeepsPresetKind(')|ForEach-Object{Member $_}
$data=(Member 'public sealed class WildRegion')+(Member 'public class WildRegionData')
$source=Get-Content (Join-Path $root 'Scripts/GeneralSystems/AdministrationReorganizationSystem.cs') -Raw
$proposal=Member 'public static bool ShouldPropose('
$requested=Member 'private static bool Requested('
$source=Get-Content (Join-Path $root 'Scripts/GeneralSystems/CoreCapitalRestorationSystem.cs') -Raw
$capital=Member 'private static void AlignAdministration('
$source=Get-Content (Join-Path $root 'Scripts/GeneralSystems/WorldMap/WorldMapScenario.cs') -Raw
$start=$source.IndexOf('        List<City> administrativeCities =')
$end=$source.IndexOf('CaptureAdministration(administrativeCities)));',$start)+'CaptureAdministration(administrativeCities)));'.Length
if($start -lt 0 -or $end -lt $start){throw 'Missing delayed administrative capture block'}
$snapshot=$source.Substring($start,$end-$start)
$fixture=@'
using System;using System.Linq;using System.Collections.Generic;using WorldMap;
public enum WildKind {None,Mountain,Desert,Tundra,Jungle,Swamp,Lava}
public struct Color32 {}
public enum RegimeType {Modern,Ancient}
public class Regime {public RegimeType type=RegimeType.Ancient;}
public class Empire {public Data data=new();public class Data {public double last_administrative_reorganization_timestamp=-1;public bool administrative_reorganization_pending,administrative_reorganization_explicit;}}
public class Kingdom {public Empire empire=new();public KingdomTitle title;public Regime regime=new();public Empire GetEmpire()=>empire;public Regime GetRegime()=>regime;public KingdomTitle GetMainTitle()=>title;public void SetMainTitle(KingdomTitle t){title=t;}}
public class Actor {public Kingdom kingdom=new();}
public static class Date {public static int getYearsSince(double t)=>10;}
public static class AdministrationReorganizationSystem {public static int resumed;public static void Resume(Empire e){resumed++;}}
public class KingdomTitle {public long id;public bool isRekt()=>false;}
public class WorldTile {public TileZone zone;}
public class TileZone {public int id;public City city;public object[] tiles=new object[64];public int total=64,water,rock=40,hills,frozen,sand,jungle,swamp,lava;public byte fresh=1;public TileZone[] neighbours,neighbours_all;}
public class City {public object data=new();public KingdomTitle title;public List<TileZone> zones=new();public WorldTile tile;public bool isRekt()=>false;public WorldTile getTile()=>tile;public KingdomTitle GetTitle()=>title;
 public object GetTileZoneOccupier(TileZone z)=>null;public void RemoveOccupiedTileZoneFromAll(TileZone z){}public void removeZone(TileZone z){zones.Remove(z);z.city=null;}}
public class Cities:List<City> {public City get(long id)=>null;}
public class Calculator {public List<TileZone> zones=new();public void setDrawnZonesDirty(){}}
public class World {public static World world=new();public Cities cities=new();public Calculator zone_calculator=new();public double getCurWorldTime()=>10;}
public class TitleManager {public KingdomTitle get(long id)=>new(){id=id};}
public static class ModClass {public static TitleManager KINGDOM_TITLE_MANAGER=new();}
public static class AncientWarfareCompatibility {public static bool OwnsObject(City c)=>false;}
public static class RuralSettlementSystem {public static int Revision;public static IEnumerable<int> SettledZones()=>Array.Empty<int>();}
public static class FactorySystem {public static int Revision;public static IEnumerable<int> CoveredZones()=>Array.Empty<int>();}
public static class WildernessDevelopmentSystem {public static HashSet<int> done=new();public static IEnumerable<int> DevelopedZones()=>done;}
public static class CityDistricts {public static void Reset(){}}
public static class PureMapPatch {public static void Redraw(City c){}}
public static class LogService {public static void LogInfo(string s){}}
namespace WorldMap {public static class WorldMapWilderness {
 public static bool FixedLayout=true;public static IEnumerable<int> KeptZones=>Array.Empty<int>();public static KingdomTitle PaintedTitle(TileZone z)=>null;
 public static bool HasPreservedAdministration(Empire e)=>false;public static List<City> captured=new();public static void CaptureAdministration(IEnumerable<City> cities){captured=cities.ToList();}
 public static void RememberTerritory(TileZone z,KingdomTitle t,City c,bool n,bool p){}
}}
public static partial class Reform {public static bool CanPropose(Actor a)=>true;public static bool ResumeAllowed(Empire e)=>Requested(e);}
public static partial class Capital {public static void Move(Empire e,Kingdom k,City c)=>AlignAdministration(e,k,c);}
public static partial class Snapshot {
 public class Planned {public City city;public Owner owner=new();}public class Owner {public Data data=new();}public class Data {public List<int> divisions=new(){1};}
 static List<Planned> _cities=new();static List<(string,Action)> _afterLoad=new();
 public static void Run(){_cities=new(){new(){city=new()},new(){city=new()}};_afterLoad.Clear();Queue();_cities=new();foreach(var step in _afterLoad)step.Item2();}
}
public static partial class Actual {
 const int MinLand=16,MinZones=4;const float MaxWater=.25f,Hysteresis=.1f,MountainEdgeShare=.6f,EdgeShare=.5f,LavaEdgeShare=.15f,LavaCore=.3f,LavaFlank=.15f,LavaRugged=.6f;
 static TileZone[] _zones;static byte[] _kind;static int[] _regionOf;static KingdomTitle[] _titles;static City[] _parents;
 static bool[] _painted,_neutralZones;static List<WildRegion> _regions=new();static Dictionary<int,WildRegion> IdIndex=new();
 static List<WildRegionData> _saved;static int _nextId=100,_settlementRevision,_factoryRevision;static bool _built,_regroup,_pending,_attachPending;
 static double _builtAt;static long _titleSignature;public static int Builds,Version;static HashSet<int> Dirty=new();
 static Counts Count(TileZone z)=>new(){Total=z.total,Water=z.water,Rock=z.rock,Hills=z.hills,Frozen=z.frozen,Sand=z.sand,Jungle=z.jungle,Swamp=z.swamp,Lava=z.lava};
 static bool Reclassify(TileZone z){byte was=_kind[z.id];_kind[z.id]=z.fresh;return was!=z.fresh;}
 static bool IsNeutral(int id)=>false;static bool IsPainted(TileZone z)=>false;static City OriginOf(TileZone z)=>_parents[z.id];
 static void Shape(List<WildRegion> r,int[] of){}static void NameAll(List<WildRegion> r,HashSet<string> used){foreach(var x in r)if(x.Name==null)x.Name="new-"+x.Id;}
 static long TitleSignature()=>1;static void Attach(bool full){}static void RefreshOwners(){}
 public static void Setup(){World.world=new();WildernessDevelopmentSystem.done.Clear();WorldMap.WorldMapWilderness.FixedLayout=true;Dirty.Clear();_regions=new();IdIndex.Clear();
  _zones=Enumerable.Range(0,12).Select(i=>new TileZone{id=i}).ToArray();
  for(int i=0;i<12;i++){_zones[i].neighbours=_zones.Where(z=>Math.Abs(z.id-i)==1).ToArray();_zones[i].neighbours_all=_zones[i].neighbours;}
  World.world.zone_calculator.zones=_zones.ToList();_kind=new byte[12];_regionOf=Enumerable.Repeat(-1,12).ToArray();_titles=new KingdomTitle[12];_parents=new City[12];_painted=new bool[12];_neutralZones=new bool[12];
  _saved=new(){new(){id=10,kind=1,name="western",culture="test",zones=new(){0,1,2,3},titles=new(){1,1,1,1}},
   new(){id=20,kind=1,name="eastern",culture="test",zones=new(){4,5,6,7},titles=new(){1,1,1,1}}};
  _built=false;_pending=_regroup=_attachPending=false;_nextId=100;
 }
 public static void Load()=>Build(true,true);public static void Refresh()=>Build(false,true);
 public static void Terrain(int id,bool water){_zones[id].water=water?64:0;_zones[id].fresh=(byte)(water?0:1);Dirty.Add(id);Build(false,false);}
 public static void Release(int id){WildernessDevelopmentSystem.done.Add(id);Build(false,true);}
 public static void Town(int id){var c=new City{tile=new(){zone=_zones[id]}};c.zones.Add(_zones[id]);_zones[id].city=c;World.world.cities.Add(c);Build(false,true);}
 public static string Layout()=>string.Join(";",_regions.Select(r=>$"{r.Id}:{r.Name}:{r.Culture}:"+string.Join(",",r.Zones)));
 public static WildRegion Region(int id)=>_regions.FirstOrDefault(r=>r.Id==id);public static int RegionCount=>_regions.Count;
 public static void NewTerrain(){for(int i=8;i<12;i++)Dirty.Add(i);Build(false,false);}
 public static void Erupt(){for(int i=0;i<4;i++){_zones[i].rock=0;_zones[i].lava=64;_zones[i].fresh=6;Dirty.Add(i);}Build(false,false);}
 public static void RoundTrip(){_saved=_regions.Select(r=>new WildRegionData{id=r.Id,kind=(int)r.Kind,name=r.Name,culture=r.Culture,zones=r.Zones.ToList(),titles=r.Zones.Select(i=>1L).ToList()}).ToList();_built=false;Build(true,true);}
}
public static class Checks {
 static int n;static void Check(bool ok,string text){n++;if(!ok)throw new Exception(text);}
 public static int Run(){
  Actual.Setup();Actual.Load();Check(Actual.RegionCount==2,"Loading merges saved neighboring regions of the same terrain type");
  Check(Actual.Region(10).Zones.SequenceEqual(new[]{0,1,2,3})&&Actual.Region(20).Zones.SequenceEqual(new[]{4,5,6,7}),"Loading resamples and moves saved preset boundaries");
  Check(Actual.Region(10).Name=="western"&&Actual.Region(20).Culture=="test","Loading loses geographic identity or naming culture");
  string original=Actual.Layout();for(int i=0;i<50;i++)Actual.Refresh();Check(Actual.Layout()==original,"Ordinary refresh grows, splits or renames preset regions");
  for(int i=0;i<5;i++)Actual.RoundTrip();Check(Actual.Layout()==original,"Repeated save/load gradually changes preset layout");
  Actual.Town(1);Check(Actual.Region(10).Zones.SequenceEqual(new[]{0,2,3}),"A new town changes more than its occupied zone");
  Check(Actual.Region(10).Id==10&&Actual.Region(10).Name=="western","A town splitting a mountain changes its region number or name");
  Check(Actual.Region(20).Zones.SequenceEqual(new[]{4,5,6,7}),"Town placement redraws an unrelated saved region");
  Actual.Release(2);Check(Actual.Region(10).Zones.SequenceEqual(new[]{0,3}),"Development drops an entire small residual geographic region");
  Check(Actual.RegionCount==2,"A small residual geographic region is silently removed at MinZones cutoff");
  Actual.Terrain(4,true);Check(Actual.Region(20).Zones.SequenceEqual(new[]{5,6,7}),"Explicit flooding fails to remove the changed zone");
  Check(Actual.Region(10).Zones.SequenceEqual(new[]{0,3}),"Local terrain edit changes a distant geographic region");
  string stable=Actual.Layout();Actual.Refresh();Actual.RoundTrip();Check(Actual.Layout()==stable,"Post-development layout changes again on load");
  Actual.Setup();Actual.Load();Actual.NewTerrain();Check(Actual.RegionCount==3,"New explicit terrain cannot form a new geographic region");
  Check(Actual.Region(10).Zones.Count==4&&Actual.Region(20).Zones.Count==4,"Explicit new mountain merges existing geographic regions");
  Actual.Setup();Actual.Load();Actual.Erupt();Check(Actual.Region(10)==null&&Actual.Region(100).Kind==WildKind.Lava&&Actual.Region(20).Zones.Count==4,"Fixed geography hides a real eruption or redraws its neighboring region");
  Actual.Setup();WorldMap.WorldMapWilderness.FixedLayout=false;Actual.Load();Check(Actual.RegionCount==1,"Random/custom world loses its dynamic terrain grouping behavior");
  WorldMap.WorldMapWilderness.FixedLayout=true;Snapshot.Run();Check(WorldMap.WorldMapWilderness.captured.Count==2,"Deferred administrative capture reads the cleared scenario city list");
  var actor=new Actor();Check(!Reform.ShouldPropose(actor),"Preset AI proposes generic redistricting when its legacy administrative snapshot is missing");
  Check(!Reform.ResumeAllowed(actor.kingdom.empire),"Legacy automatic pending redistricting resumes in a fixed preset");
  actor.kingdom.empire.data.administrative_reorganization_explicit=true;Check(Reform.ResumeAllowed(actor.kingdom.empire),"Fixed preset blocks an explicit player-requested reorganization");
  WorldMap.WorldMapWilderness.FixedLayout=false;Check(Reform.ShouldPropose(actor),"Ordinary dynamic world loses automatic administrative reform");
  actor.kingdom.empire.data.administrative_reorganization_explicit=false;Check(Reform.ResumeAllowed(actor.kingdom.empire),"Dynamic world cannot resume automatic pending work");
  WorldMap.WorldMapWilderness.FixedLayout=true;var home=new KingdomTitle{id=8};var capital=new City{title=home};var core=new Kingdom();var empire=core.empire;AdministrationReorganizationSystem.resumed=0;
  Capital.Move(empire,core,capital);Check(core.title==home&&!empire.data.administrative_reorganization_pending&&AdministrationReorganizationSystem.resumed==0,"Restoring a preset capital automatically queues whole-empire redistricting");
  WorldMap.WorldMapWilderness.FixedLayout=false;Capital.Move(empire,core,capital);Check(empire.data.administrative_reorganization_pending&&AdministrationReorganizationSystem.resumed==1,"Ordinary-world capital restoration no longer aligns its administration");
  return n;
 }
}
'@
Add-Type -TypeDefinition ($fixture+$data+'public static partial class Actual {'+($members -join "`n")+'}'+
 'public static partial class Reform {'+$proposal+$requested+'}public static partial class Capital {'+$capital+'}'+
 'public static partial class Snapshot {static void Queue(){'+$snapshot+'}}') -IgnoreWarnings -WarningAction SilentlyContinue
Write-Output "$([Checks]::Run()) production preset layout, repeated save/load, local terraforming, development and dynamic-world checks passed."
