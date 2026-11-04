$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
function Member([string]$path, [string]$signature) {
    $source = Get-Content (Join-Path $root $path) -Raw
    $start = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $open = $source.IndexOf('{', $start); $end = $open + 1; $depth = 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Substring($start, $end - $start)
}
$territory = 'Scripts/UI/Components/WildernessTerritory.cs'
$helpers = @('internal static Kingdom KingdomAt(', 'internal static City AdministrativeCityAt(',
    'internal static IMetaObject ProvinceShade(', 'private static bool SameRealm(', 'private static void DrawClaimed(',
    'private static void Refresh(', 'private static void Follow(', 'private static IMetaObject MetaAt(', 'private static IMetaObject MetaOf(') |
    ForEach-Object { Member $territory $_ }
$metaPath = 'Scripts/GameLibrary/EmpireCraftMetaTypeLibrary.cs'
$tip = Member $metaPath 'private static void ShowEmpireCursorTooltip('
$callback = Member $metaPath 'pAsset13.check_cursor_tooltip = (pZone, pAsset, pZoneOption) =>'
$callback = $callback.Substring($callback.IndexOf('{'))
$smooth = @('private static Kingdom HoveredProvince(', 'private static IMetaObject ShadeOf(') |
    ForEach-Object { Member 'Scripts/GeneralSystems/SmoothBorders.cs' $_ }
$smooth += @('private static void RefreshZone(', 'private static void MarkKingdom(') |
    ForEach-Object { Member 'Scripts/GeneralSystems/SmoothBorders.Canvas.cs' $_ }
$name = Member 'Scripts/GameLibrary/EmpireCraftNamePlateLibrary.cs' 'private static Kingdom GetHoveredKingdom('
$fixture = @'
using System;using System.Linq;using System.Collections.Generic;using EmpireCraft.Scripts.Layer;
public interface IMetaObject {bool isAlive();}
public class NanoObject:IMetaObject {public long id;public bool dead;public object data=new();public bool isRekt()=>dead;public bool isAlive()=>!dead;}
namespace EmpireCraft.Scripts.Layer {public class Empire:NanoObject {public Kingdom CoreKingdom;public bool archived;public bool IsArchived()=>archived;}}
public class Kingdom:NanoObject {public bool neutral;public City capital;public EmpireCraft.Scripts.Layer.Empire empire,mappedEmpire;public List<City> cities=new();
 public bool isNeutral()=>neutral;public EmpireCraft.Scripts.Layer.Empire GetEmpire()=>empire;}
public class City:NanoObject {public Kingdom kingdom;public List<TileZone> zones=new();public WorldTile getTile()=>zones.Count>0?new(){zone=zones[0]}:null;}
public class TileZone {public int id;public City city;public TileZone[] neighbours;public void resetRenderHelpers(){}}
public class WorldTile {public TileZone zone;}
public class World {public static World world=new MapBox();public WorldTile mouse;public ZoneCalculator zone_calculator=new();public WorldTile getMouseTilePosCachedFrame()=>mouse;}
public class MapBox:World {}
public enum MetaType {None,Empire,Kingdom}
public static class MetaTypeExtension {public const MetaType Empire=MetaType.Empire;}
public delegate IMetaObject MetaZoneGetMetaSimple(TileZone z);
public delegate IMetaObject MetaZoneGetMeta(TileZone z,int option);
public class MetaTypeAsset {public string id="empire";public MetaType map_mode;public int option;public MetaZoneGetMetaSimple tile_get_metaobject_0;public MetaZoneGetMeta tile_get_metaobject;
 public Action<NanoObject> cursor_tooltip_action=_=>{};public int getZoneOptionState()=>option;public bool isMetaZoneOptionSelectedFluid()=>false;}
public class ZoneCalculator {public Dictionary<int,TileZone> zones=new();public Dictionary<int,IMetaObject> drawn=new();
 public TileZone getZoneByID(int id)=>zones.TryGetValue(id,out var z)?z:null;public void drawZoneClear(TileZone z){}
 public TileZone getZone(int x,int y)=>zones.TryGetValue(y*3+x,out var z)?z:null;
 public void drawBegin(){}public void drawEnd(TileZone z){}public void drawZoneMeta(TileZone z,MetaTypeAsset a,MetaZoneGetMetaSimple getter){drawn[z.id]=getter(z);}}
public class TooltipData {public Kingdom kingdom;public string tip_description;public float tooltip_scale;public bool is_sim_tooltip;}
public static class Tooltip {public static object owner;public static TooltipData last;public static void hideTooltip(object o,bool b,string s){}
 public static void show(object o,string s,TooltipData d){owner=o;last=d;}}
public static class WildernessSystem {
 public static bool Enabled=true;public static List<WildRegion> Regions=new();public static HashSet<int> wildIds=new(){1,2};public static bool IsWild(TileZone z)=>z!=null&&wildIds.Contains(z.id);
 public static Dictionary<int,Kingdom> owners=new();public static Dictionary<int,City> origins=new();
 public static Kingdom OwnerOf(TileZone z)=>z!=null&&owners.TryGetValue(z.id,out var k)?k:null;
 public static City OriginOf(TileZone z)=>z!=null&&origins.TryGetValue(z.id,out var c)?c:null;
}
public class WildRegion {public List<int> Zones=new();}
public static class WorldMapWilderness {
 public static Dictionary<int,Kingdom> owners=new();public static Dictionary<int,City> origins=new();
 public static IEnumerable<int> KnownTerritory()=>owners.Keys;
 public static Kingdom KnownOwner(TileZone z)=>z!=null&&owners.TryGetValue(z.id,out var k)?k:null;
 public static City KnownCity(TileZone z)=>z!=null&&origins.TryGetValue(z.id,out var c)?c:null;
}
public static class Time {public static float unscaledTime=0;}
public static class LogService {public static void LogInfo(string s){}}
public static class SmoothBorders {public static bool CanvasActive=true;public static void ClaimChanged(int id){}}
public static class WildernessOverlay {public static void MarkAround(TileZone z){}}
public static class EmpireMembershipService {public static EmpireCraft.Scripts.Layer.Empire MapEmpire(Kingdom k)=>k?.mappedEmpire??k?.empire;}
public static partial class WildernessTerritory {
 private class Layer {public bool titles;public MetaTypeAsset asset;public MetaZoneGetMeta full;public MetaZoneGetMetaSimple simple;
  public Dictionary<TileZone,IMetaObject> fill=new();public Dictionary<TileZone,Kingdom> owners=new();
  public Dictionary<City,List<TileZone>> byOrigin=new();public Dictionary<Kingdom,List<TileZone>> byOwner=new();public int logged;public float logAt;}
 private static Layer current=new();private static Dictionary<Kingdom,IMetaObject> Metas=new();private static Dictionary<City,IMetaObject> CityMetas=new();public static int Revision;
 public static void Update(MetaTypeAsset asset){current.asset=asset;current.full=asset.tile_get_metaobject;current.simple=asset.tile_get_metaobject_0;Refresh(current,(MapBox)World.world);}
 public static bool CachedOwner(Kingdom k,TileZone z)=>current.byOwner.TryGetValue(k,out var l)&&l.Contains(z);
 public static bool Filled(TileZone z)=>current.fill.ContainsKey(z);
 public static IMetaObject FilledMeta(TileZone z)=>current.fill.TryGetValue(z,out var m)?m:null;
 public static Dictionary<Kingdom,List<TileZone>> following=new();public static List<TileZone> ZonesOf(Kingdom k)=>following.TryGetValue(k,out var l)?l:new();
 public static void Draw(TileZone z,MetaTypeAsset a){var layer=new Layer();layer.fill[z]=a.tile_get_metaobject(z,0);DrawClaimed(layer,a);}
}
public static partial class MetaActual {
 public static bool HoverTip(TileZone pZone,MetaTypeAsset pAsset,int pZoneOption)
/*CALLBACK*/
}
public static partial class NameActual {public static Kingdom Pick()=>GetHoveredKingdom();}
public static partial class SmoothActual {
 public static Kingdom _hover;private static IMetaObject _hoverRealm;
 public static HashSet<int> CanvasDirty=new();private static bool _canvasCityKeyed=true,_canvasReady=true;
 private const int _zonesX=3;private static bool[] _cityContested=new bool[3],_zoneSeen=new bool[3],_fillListed=new bool[3];
 private static int[] _cityOwner=new int[3],_cityOwnerKey=new int[3],_cityShade=new int[3],_zoneShade=new int[3],_zoneCity=new int[3],
  _zonePush=new int[3],_zoneOwner=new int[3],_zoneOwnerKey=new int[3],_zoneFill=new int[3],_zoneFillKey=new int[3];
 private static List<int> FillZones=new();public static IMetaObject real;
 private static int CityOf(City c)=>c==null?0:1;private static int IndexOf(IMetaObject m)=>m is NanoObject n?(int)n.id:0;
 private static int CanvasOwnerKey(MetaTypeAsset a,IMetaObject m)=>IndexOf(m);private static void RefreshCity(MetaTypeAsset a,int o,int c,TileZone z){}
 private static IMetaObject RealMetaOf(MetaTypeAsset a,int o,TileZone z)=>real;private static IMetaObject PushMetaOf(MetaTypeAsset a,TileZone z)=>null;
 private static IMetaObject LayerMeta(MetaTypeAsset a,int o,TileZone z)=>real;private static bool ControlLayer(MetaTypeAsset a)=>a.map_mode==MetaType.Empire;
 private static void ZoneMetas(MetaTypeAsset a,int o,TileZone z,out IMetaObject owner,out IMetaObject shade){owner=real;shade=ShadeOf(z,real);}
 public static Kingdom Pick(MetaTypeAsset a,int o)=>HoveredProvince(a,o);
 public static int Fill(MetaTypeAsset a,int id,out bool political){RefreshZone(World.world.zone_calculator,a,0,id,out _,out _,out political);return _zoneFill[id];}
 public static void Mark(Kingdom k)=>MarkKingdom(k);
}
public static class Checks {
 static int n;static void Check(bool ok,string m){n++;if(!ok)throw new Exception(m);}
 public static int Run(){
  var empire=new EmpireCraft.Scripts.Layer.Empire{id=10};var otherEmpire=new EmpireCraft.Scripts.Layer.Empire{id=20};
  var province=new Kingdom{id=100,empire=empire};var sibling=new Kingdom{id=101,empire=empire};var foreign=new Kingdom{id=200,empire=otherEmpire};
  empire.CoreKingdom=sibling;var seat=new City{id=1,kingdom=province};var foreignSeat=new City{id=2,kingdom=foreign};
  var cityZone=new TileZone{id=0,city=seat};var wild=new TileZone{id=1};var otherWild=new TileZone{id=2};seat.zones.Add(cityZone);province.cities.Add(seat);
  World.world.zone_calculator.zones=new(){{0,cityZone},{1,wild},{2,otherWild}};
  WildernessSystem.owners=new(){{1,province},{2,sibling}};WildernessSystem.origins=new(){{1,seat}};
  Check(WildernessTerritory.KingdomAt(cityZone)==province,"Physical city hover changed");
  Check(WildernessTerritory.KingdomAt(wild)==province&&WildernessTerritory.AdministrativeCityAt(wild)==seat,"Owned wilderness loses administrative context");
  Check(WildernessTerritory.ProvinceShade(wild,empire,province)==province,"Hovered wilderness remains imperial color");
  Check(WildernessTerritory.ProvinceShade(otherWild,empire,province)==empire,"Sibling province incorrectly highlighted");
  Check(WildernessTerritory.ProvinceShade(wild,otherEmpire,province)==otherEmpire,"Hover colors a different empire");
  World.world.mouse=new(){zone=wild};Check(NameActual.Pick()==province,"Administrative nameplate hover misses wilderness");
  var layer=new MetaTypeAsset{map_mode=MetaType.Empire,tile_get_metaobject=(z,o)=>empire,tile_get_metaobject_0=z=>empire};
  Check(SmoothActual.Pick(layer,0)==province&&SmoothActual.Pick(layer,1)==null,"Smooth hover misses wilderness or leaks into another view");
  Check(MetaActual.HoverTip(wild,layer,0)&&Tooltip.last.kingdom==province&&Tooltip.last.tip_description=="10"&&Tooltip.owner==seat,"Empire tooltip falls back to capital on wilderness");
  WildernessTerritory.Draw(wild,layer);Check(World.world.zone_calculator.drawn[1]==province&&layer.tile_get_metaobject(wild,0)==empire,"Regular rendering either misses province shade or changes logical ownership");
  SmoothActual._hover=province;SmoothActual.real=empire;Check(SmoothActual.Fill(layer,1,out bool flip)==100&&!flip,"Smooth canvas fill ignores hovered province or starts a political transition");
  WildernessTerritory.following[province]=new(){wild};SmoothActual.Mark(province);Check(SmoothActual.CanvasDirty.SetEquals(new[]{0,1}),"Hover repaint leaves distant wilderness stale");
  SmoothActual._hover=null;Check(SmoothActual.Fill(layer,1,out flip)==10&&!flip,"Leaving wilderness leaves province shading or political transition behind");
  WildernessSystem.origins[1]=foreignSeat;Check(WildernessTerritory.AdministrativeCityAt(wild)==null&&WildernessTerritory.KingdomAt(wild)==province,"Foreign origin overrides current owner");
  WildernessSystem.origins[1]=seat;WildernessSystem.owners.Remove(1);Check(WildernessTerritory.KingdomAt(wild)==null&&WildernessTerritory.AdministrativeCityAt(wild)==null,"Neutral wilderness borrows a nearby administration");
  World.world.mouse=new(){zone=wild};Check(NameActual.Pick()==null&&SmoothActual.Pick(layer,0)==null,"Neutral wilderness expands an empire");
  WildernessSystem.owners[1]=province;seat.dead=true;Check(WildernessTerritory.AdministrativeCityAt(wild)==null,"Destroyed city enters tooltip context");seat.dead=false;
  province.dead=true;Check(WildernessTerritory.KingdomAt(wild)==null,"Destroyed realm remains hoverable");province.dead=false;
  WildernessSystem.owners[0]=foreign;Check(WildernessTerritory.KingdomAt(cityZone)==province,"Wild cache overrides a physical city");
  province.empire=null;province.mappedEmpire=empire;Check(WildernessTerritory.ProvinceShade(wild,empire,province)==province,"Mapped feudal vassal hover lost");
  World.world.mouse=null;Check(NameActual.Pick()==null,"Mouse outside map retains administrative labels");
  Check(WildernessTerritory.KingdomAt(null)==null&&WildernessTerritory.AdministrativeCityAt(null)==null,"Null zone retains context");
  WildernessSystem.wildIds.Clear();WildernessSystem.Regions.Clear();WildernessSystem.owners.Clear();WildernessSystem.origins.Clear();
  province.empire=empire;province.mappedEmpire=null;WorldMapWilderness.owners[1]=province;WorldMapWilderness.origins[1]=seat;
  WildernessTerritory.Update(layer);Check(WildernessTerritory.Filled(wild)&&WildernessTerritory.CachedOwner(province,wild),"Retired wilderness does not rejoin political map rendering");
  Check(WildernessTerritory.KingdomAt(wild)==province&&WildernessTerritory.AdministrativeCityAt(wild)==seat,"Retired wilderness loses hover context");
  var siblingSeat=new City{id=3,kingdom=sibling};siblingSeat.zones.Add(new(){id=3,city=siblingSeat});sibling.cities.Add(siblingSeat);
  Check(WildernessTerritory.FilledMeta(wild)==empire,"First cached territory is not rendered as the shared empire");
  WorldMapWilderness.owners[1]=sibling;WorldMapWilderness.origins[1]=null;
  WildernessTerritory.Update(layer);Check(WildernessTerritory.CachedOwner(sibling,wild)&&!WildernessTerritory.CachedOwner(province,wild),"Same-empire reassignment leaves administrative cache on former province");
  Check(WildernessTerritory.FilledMeta(wild)==empire,"Same-empire cache test changed displayed empire instead of only province");
  WorldMapWilderness.owners.Clear();WorldMapWilderness.origins.Clear();WildernessTerritory.Update(layer);
  Check(!WildernessTerritory.Filled(wild)&&!WildernessTerritory.CachedOwner(sibling,wild),"Cleared coverage remains painted");
  return n;
 }
}
'@
$fixture = $fixture.Replace('/*CALLBACK*/', $callback)
Add-Type -TypeDefinition ($fixture + 'public static partial class WildernessTerritory {' + ($helpers -join "`n") + '}' +
    'public static partial class MetaActual {' + $tip.Replace('Empire empireMeta','EmpireCraft.Scripts.Layer.Empire empireMeta') + '}' +
    'public static partial class NameActual {' + $name + '}' +
    'public static partial class SmoothActual {' + ($smooth -join "`n") + '}') -IgnoreWarnings -WarningAction SilentlyContinue
Write-Output "$([Checks]::Run()) production wilderness hover, labels, tooltip and regular/smooth province display checks passed."
