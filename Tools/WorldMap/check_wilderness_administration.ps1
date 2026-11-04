$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$source=Get-Content (Join-Path $root 'Scripts/GeneralSystems/WildernessSystem.cs') -Raw
function Method([string]$signature){
 $start=$source.IndexOf($signature,[StringComparison]::Ordinal);if($start -lt 0){throw "Missing: $signature"}
 $open=$source.IndexOf('{',$start);$end=$open+1;$depth=1
 while($depth -gt 0){if($source[$end] -eq '{'){$depth++};if($source[$end] -eq '}'){$depth--};$end++}
 $source.Substring($start,$end-$start)
}
$methods=@('public static City OriginOf(', 'private static void Attach(', 'private static void Connect(', 'private static bool[] FollowSpread(', 'private static int Absorb(', 'private static KingdomTitle AnchorTitle(', 'private static KingdomTitle TitleOfCity(', 'private static City NearestCity(', 'public static List<WildRegionData> Save(', 'public static void Load(')|ForEach-Object{Method $_}
$methods += [regex]::Match($source,'private static bool PinnedTitle\([^;]+;').Value
$methods += Method 'internal static int SetNeutralZones('
$methods += [regex]::Match($source,'public static bool IsNeutral\(int id\)[\s\S]*?;').Value
$methods += [regex]::Match($source,'public static bool IsPainted\([^;]+;').Value
$methods += [regex]::Match($source,'private static bool UntitledOrigin\([^;]+;').Value
$methods += Method 'private static long TitleSignature('
$restoreStart=$source.IndexOf('        _titles = titles;')
$restoreEnd=$source.IndexOf('        return carved;',$restoreStart)
if($restoreStart -lt 0 -or $restoreEnd -lt 0){throw 'Missing production regroup restoration finalization'}
$restore=$source.Substring($restoreStart,$restoreEnd-$restoreStart)
$methods += 'public static void RestoreSaved(bool pending){bool fromSave=true,missing=false;_attachPending=pending;var titles=_titles;var oldParents=_parents;var painted=_painted;'+$restore+'}'
$source=Get-Content (Join-Path $root 'Scripts/GeneralSystems/WorldMap/WorldMapWilderness.cs') -Raw
$guardMethods=@('internal static void CaptureAdministration(', 'internal static bool PreservesAdministration(')|ForEach-Object{Method $_}
$guardMethods += @('internal static KingdomTitle PaintedTitle(', 'internal static void PreparePaint(', 'private static void ApplyNeutral(')|ForEach-Object{Method $_}
$guardMethods += [regex]::Match($source,'internal static bool HasPreservedAdministration\([^;]+;').Value
$guardMethods += [regex]::Match($source,'private static string AdministrativeRegime\([^;]+;').Value
$guardData=(Method 'public sealed class ScenarioAdministrativeOrigin')+(Method 'public sealed class ScenarioLandOrigin')+(Method 'public class WorldMapWildSave')
$fixture=@'
using System;using System.Collections.Generic;using System.Linq;
public enum WildKind {None,Mountain,Desert,Tundra,Jungle,Swamp,Lava}
public class Regime {public string type="LvLing";}
public class Empire {public long id;public Kingdom CoreKingdom;public List<Kingdom> kingdoms_list=new();}
public class Kingdom {public object data=new();public long id;public Empire empire;public Regime regime=new();public List<City> cities=new();public bool isRekt()=>false;public Empire GetEmpire()=>empire;public Regime GetRegime()=>regime;}
public class WorldTile {public int x,y;}
public class TileZone {public int id;public City city;public WorldTile centerTile=new();public List<TileZone> neighbours_all=new();}
public class City {public long id;public object data=new();public bool dead;public KingdomTitle title;public Kingdom kingdom=new();public WorldTile tile=new();public KingdomTitle GetTitle()=>title;public bool isRekt()=>dead;public WorldTile getTile()=>tile;}
public class Cities:List<City>{public List<City> list=>this;public City get(long id)=>this.FirstOrDefault(c=>c.id==id);}
public class World {public static World world=new();public Cities cities=new();}
public class KingdomTitle {public long id;public bool dead;public Kingdom FindRealmTitleHolder()=>null;public bool isRekt()=>dead;public List<City> getCities()=>World.world.cities.Where(c=>!c.dead&&c.title==this).ToList();}
public class WildRegion {public int Id;public WildKind Kind;public string Name,Culture;public bool Neutral;public List<int> Zones=new();}
public class WildRegionData {public int id,kind;public string name,culture;public bool neutral;public List<int> zones=new(),painted,neutralZones;public List<long> titles=new(),origins;}
public static class LogService {public static void LogInfo(string text){}}
public class TitleManager{public KingdomTitle get(long id)=>WorldMap.WorldMapWilderness.painted.Values.FirstOrDefault(t=>t.id==id);}
public static class ModClass{public static TitleManager KINGDOM_TITLE_MANAGER=new();}
public static class WildernessSystem{public static List<WildRegion> Regions=>Actual._regions;public static int SetNeutralZones(IEnumerable<int> zones,bool value)=>Actual.SetNeutralZones(zones,value);}
namespace WorldMap {
 public static class FrontierWalls {public static bool Behind(City c,TileZone z)=>false;}
 public static partial class WorldMapWilderness {
  public static bool OnEarth=true;public static Dictionary<int,City> anchors=new();
  public static Dictionary<int,KingdomTitle> painted=new(),regional=new();
  public static Dictionary<int,long> _painted;public static HashSet<int> _neutral;private static void ResolvePaint(){_painted=painted.ToDictionary(p=>p.Key,p=>p.Value.id);}
  public static WorldMapWildSave _state=new();public static bool Active=>OnEarth;
  public static City AnchorCity(TileZone z)=>anchors.TryGetValue(z.id,out City c)&&!c.dead?c:null;
  public static bool NeedsBoundaryRepair=>false;public static void MarkBoundaryRepaired(){}
  public static bool BoundedAnchor(TileZone z)=>false;public static KingdomTitle TitleFor(TileZone z)=>null;
  public static void FollowAnchor(TileZone z,City c){if(anchors.ContainsKey(z.id))anchors[z.id]=c;}
  internal static KingdomTitle FixedTitle(TileZone z)=>PaintedTitle(z)??(regional.TryGetValue(z.id,out var t)?t:null);
  public static void RememberTerritory(TileZone z,KingdomTitle t,City c,bool neutral,bool pinned=false){}
 }
}
public static partial class Actual {
 private static Kingdom RealmOf(Kingdom k)=>k;
 private static bool OnWall(int id)=>_walled[id];
 public static KingdomTitle[] _titles;public static City[] _parents;public static TileZone[] _zones;
 public static List<WildRegion> _regions;public static int[] _regionOf;public static bool[] _painted;
 public static bool[] _neutralZones;public static int Version;
 private static bool[] _walled;private static bool _built=true,_enabled=true;private static int _nextId=1;
 public static List<WildRegionData> _saved;public static int spreads;private static bool _attachPending;private static long _titleSignature;private static bool RefreshOwners()=>false;
 public static long Signature()=>TitleSignature();
 private const int MaxAttachSteps=64;
 private static int Index(TileZone zone)=>zone==null?-1:_regionOf[zone.id];
 private static KingdomTitle TitleOf(TileZone zone)=>zone==null?null:_titles[zone.id];
 private static bool[] WallZones()=>new bool[_titles.Length];
 private static bool NomadKind(WildKind kind)=>false;
 private static bool Nomad(City city,Dictionary<Kingdom,bool> cache)=>false;
 private static KingdomTitle HistoricalTitle(TileZone z,Dictionary<City,KingdomTitle> titles,KingdomTitle[] from,City[] fromCity,Dictionary<Kingdom,KingdomTitle[]> realms)=>null;
 private static KingdomTitle[] Spread(Dictionary<City,KingdomTitle> titles,Func<City,bool> filter,float max,out bool[] reached,out City[] fromCity){
  spreads++;fromCity=Enumerable.Repeat(World.world.cities.Last(),_titles.Length).ToArray();
  reached=Enumerable.Repeat(true,_titles.Length).ToArray();return fromCity.Select(c=>c.title).ToArray();
 }
 public static void Setup(){
  World.world=new();WorldMap.WorldMapWilderness.OnEarth=true;WorldMap.WorldMapWilderness.anchors.Clear();WorldMap.WorldMapWilderness.painted.Clear();WorldMap.WorldMapWilderness.regional.Clear();WorldMap.WorldMapWilderness._state=new();WorldMap.WorldMapWilderness._neutral=null;spreads=0;
  _zones=Enumerable.Range(0,3).Select(i=>new TileZone{id=i,centerTile=new(){x=i*8,y=0}}).ToArray();
  _titles=new KingdomTitle[3];_parents=new City[3];_painted=new bool[3];_walled=new bool[3];_regionOf=new[]{0,0,0};
  _neutralZones=new bool[3];
  _regions=new(){new(){Id=1,Kind=WildKind.Mountain,Zones=new(){0,1,2}}};
  _built=true;_enabled=true;_nextId=2;
 }
 public static void Link()=>Connect(new(),new City[3],new KingdomTitle[3]);
 public static void Reattach()=>Attach(true);
}
public static class Checks {
 static int n;static void Check(bool ok,string msg){n++;if(!ok)throw new Exception(msg);}
 public static int Run(){
  Actual.Setup();var title=new KingdomTitle{id=11};var other=new KingdomTitle{id=22};
  var a=new City{id=1,title=title,tile=new(){x=0,y=0}};var b=new City{id=2,title=title,tile=new(){x=20,y=0}};
  World.world.cities.AddRange(new[]{a,b});
  for(int i=0;i<3;i++){Actual._titles[i]=title;Actual._parents[i]=b;WorldMap.WorldMapWilderness.anchors[i]=a;}
  Check(Actual.OriginOf(Actual._zones[1])==a,"Cached neighboring city overrides historical administrative anchor");
  Actual.Link();Check(Actual._parents.All(c=>c==a)&&Actual._titles.All(t=>t==title),"Connectivity check discards lawful anchored territory");
  World.world.cities.Add(new(){id=3,title=other,tile=new(){x=8,y=0}});Actual.Reattach();
  Check(Actual.spreads==0&&Actual._titles.All(t=>t==title)&&Actual._parents.All(c=>c==a),"Unrelated new city redraws valid scenario territory");
  var save=Actual.Save();Check(save[0].origins.SequenceEqual(new long[]{1,1,1})&&save[0].titles.All(id=>id==11),"Save loses administrative origin");
  Actual.Load(save,2);Check(Actual._saved[0].origins.SequenceEqual(save[0].origins),"Load loses administrative origins");
  a.dead=true;Actual.Reattach();Check(Actual._titles.All(t=>t==title)&&Actual._parents.All(c=>c==b),"Lost administrative seat transfers territory to another law");
  var stable=Actual.Save();Check(stable[0].origins.All(id=>id==2),"Replacement origin not persisted");
  title.dead=true;Actual.Reattach();Check(Actual.spreads>0&&Actual._titles.All(t=>t==other),"Destroyed legal title never releases territory");
  Actual.Setup();title=new(){id=11};a=new(){id=1,title=title};World.world.cities.Add(a);
  for(int i=0;i<3;i++){Actual._titles[i]=title;WorldMap.WorldMapWilderness.anchors[i]=a;}
  Actual.Reattach();Check(Actual._parents.All(c=>c==a),"Legacy save without origin fields fails to migrate from anchors");
  var realm=new Kingdom{id=10,empire=new(){id=100}};a.kingdom=realm;
  WorldMap.WorldMapWilderness.CaptureAdministration(new[]{a});
  Check(WorldMap.WorldMapWilderness.PreservesAdministration(a),"Scripted administrative membership not preserved");
  realm.cities.Add(a);realm.empire.kingdoms_list.Add(realm);
  Check(WorldMap.WorldMapWilderness.HasPreservedAdministration(realm.empire),"Automatic reform misses preserved historical structure");
  realm.regime.type="Modern";Check(!WorldMap.WorldMapWilderness.PreservesAdministration(a),"Scenario guard freezes real regime transition");realm.regime.type="LvLing";
  a.kingdom=new(){id=20,empire=realm.empire};Check(!WorldMap.WorldMapWilderness.PreservesAdministration(a),"Guard undoes actual conquest or reassignment");
  a.kingdom=realm;a.title=other;Check(!WorldMap.WorldMapWilderness.PreservesAdministration(a),"Guard blocks legal-title reform");
  a.title=title;realm.empire=new(){id=200};Check(!WorldMap.WorldMapWilderness.PreservesAdministration(a),"Guard blocks new imperial structure");
  WorldMap.WorldMapWilderness._state=new(){anchors=new(){{0,a.id}}};
  Check(WorldMap.WorldMapWilderness.PreservesAdministration(a),"Legacy administrative snapshot does not migrate current membership");
  Actual.Setup();title=new(){id=11};other=new(){id=22};a=new(){id=1,title=title};World.world.cities.Add(a);
  for(int i=0;i<3;i++){WorldMap.WorldMapWilderness.anchors[i]=a;WorldMap.WorldMapWilderness.painted[i]=other;}
  Actual.Reattach();Check(Actual._titles.All(t=>t==other),"Automatic administrative anchors override explicit editor paint");
  WorldMap.WorldMapWilderness.OnEarth=false;Actual.Link();Check(Actual._titles.All(t=>t==other),"Connectivity repair overwrites explicit editor paint on imported map");
  Actual.Setup();title=new(){id=11};other=new(){id=22};a=new(){id=1,title=title};b=new(){id=2,title=other};World.world.cities.AddRange(new[]{a,b});
  for(int i=0;i<3;i++){Actual._titles[i]=title;Actual._parents[i]=a;}
  WorldMap.WorldMapWilderness.painted[1]=other;Actual.Reattach();Check(Actual._titles[1]==other&&Actual._titles[0]==title&&Actual.spreads==0,"Late editor paint skipped by preservation fast path");
  Actual._painted[1]=true;Actual._titles[1]=title;Actual.Reattach();Check(Actual._titles[1]==title,"Scenario defaults overwrite runtime god-power painting");
  WorldMap.WorldMapWilderness.painted.Clear();Actual._painted[1]=false;Actual._titles[2]=null;Actual._parents[0]=Actual._parents[1]=Actual._parents[2]=null;Actual.Reattach();Check(Actual._titles[0]==title&&Actual._titles[1]==title&&Actual._titles[2]==other,"Repairing missing title redraws valid legacy claims without saved origins");
  WorldMap.WorldMapWilderness._neutral=new(){0};WorldMap.WorldMapWilderness.PaintedTitle(Actual._zones[0]);Check(!Actual._regions[0].Neutral&&WorldMap.WorldMapWilderness._neutral!=null,"Title lookup mutates neutral regions or consumes pending paint");
  var regions=Actual._regions;Actual._regions=new();WorldMap.WorldMapWilderness.PreparePaint();Check(WorldMap.WorldMapWilderness._neutral!=null,"Pending neutral paint consumed before regions exist");
  Actual._regions=regions;Actual.Reattach();Check(Actual.IsNeutral(0)&&!Actual.IsNeutral(1)&&!Actual._regions[0].Neutral&&WorldMap.WorldMapWilderness._neutral==null,"Neutral paint skipped or expands to an entire connected region");
  Actual.Setup();title=new(){id=11};other=new(){id=22};a=new(){id=1,title=title};World.world.cities.Add(a);for(int i=0;i<3;i++)Actual._titles[i]=title;
  WorldMap.WorldMapWilderness.painted[0]=other;Actual.RestoreSaved(true);Check(Actual._titles[0]==other,"Saved valid titles skip queued scenario paint during regroup");
  Actual.Setup();title=new(){id=11};other=new(){id=22};a=new(){id=1,title=title};b=new(){id=2,title=title};World.world.cities.AddRange(new[]{a,b});
  for(int i=0;i<3;i++){Actual._titles[i]=title;Actual._parents[i]=a;WorldMap.WorldMapWilderness.regional[i]=title;WorldMap.WorldMapWilderness.anchors[i]=a;}
  a.title=other;int version=Actual.Version;
  Check(Actual.OriginOf(Actual._zones[0])==a&&Actual._titles[0]==other,"Reassigned city loses wilderness to another city in its former title");
  Check(Actual.Version>version,"Administrative change fails to invalidate map and tooltip caches");
  Actual.Reattach();Check(Actual._titles.All(t=>t==other)&&Actual._parents.All(c=>c==a),"Opening regional defaults restore obsolete administrative enclaves");
  Actual.Reattach();Check(Actual._titles.All(t=>t==other)&&Actual.spreads==0,"Repeated refresh reverts reassigned territory or redraws the world");
  a.title=title;save=Actual.Save();Check(save[0].titles.All(id=>id==11)&&save[0].origins.All(id=>id==1),"Saving before refresh serializes stale titles with updated origins");
  Actual._painted[1]=true;WorldMap.WorldMapWilderness.painted[2]=title;a.title=other;Actual.Reattach();
  Check(Actual._titles[0]==other&&Actual._titles[1]==title&&Actual._titles[2]==title,"Following administration overwrites manual god or editor paint");
  Actual._painted[1]=false;WorldMap.WorldMapWilderness.painted.Clear();for(int i=0;i<3;i++)Actual._parents[i]=a;a.title=null;Actual.Reattach();
  Check(Actual._titles.All(t=>t==null)&&Actual._parents.All(c=>c==a),"Removing a city title resurrects its obsolete regional default");
  a.title=other;Actual.Reattach();Check(Actual._titles.All(t=>t==other),"Untitled origin fails to follow a later title assignment");
  long signature=Actual.Signature();a.kingdom=new(){id=333};Check(signature!=Actual.Signature(),"Same-title provincial reassignment does not refresh ownership");
  signature=Actual.Signature();a.kingdom.empire=new(){id=444};Check(signature!=Actual.Signature(),"Imperial transfer does not refresh ownership");
  b.title=title;signature=Actual.Signature();a.title=title;b.title=other;Check(signature!=Actual.Signature(),"Two cities swapping titles cancel each other in change detection");
  signature=Actual.Signature();World.world.cities.Reverse();Check(signature==Actual.Signature(),"City iteration order alone triggers administrative rebuilding");
  return n;
 }
}
'@
Add-Type -TypeDefinition ($fixture+'public static partial class Actual {'+($methods -join "`n")+'}namespace WorldMap {'+$guardData+'public static partial class WorldMapWilderness {'+($guardMethods -join "`n")+'}}')
Write-Output "$([Checks]::Run()) production wilderness origin, save/load, new city, dead seat and destroyed title checks passed."
