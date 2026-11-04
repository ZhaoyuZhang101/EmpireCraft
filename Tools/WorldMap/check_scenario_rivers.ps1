$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$source = Get-Content (Join-Path $root 'Scripts/GeneralSystems/WorldMap/EarthTerrain.cs') -Raw
function Member([string]$signature) {
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
$members = @('private static bool EnsureRivers(', 'public static void RiverTiles(',
    'public static void DrawRivers(', 'private static void Wet(', 'internal static int RepairRiverSurfaces(',
    'public static void Fill(') | ForEach-Object { Member $_ }
$frame = Member 'public sealed class WorldMapFrame'
$ford = [regex]::Match($source, 'private const int FordEvery[^;]+;').Value
$fixture = @'
using System;using System.IO;using System.Linq;using System.Text;using System.Collections.Generic;
public struct Vector2 {public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}}
public struct Vector2Int {public int x,y;public Vector2Int(int x,int y){this.x=x;this.y=y;}}
public static class Mathf {public static int FloorToInt(float x)=>(int)Math.Floor(x);public static int Abs(int x)=>Math.Abs(x);}
public class TileTypeBase {public string id;public bool liquid,road,wall,farm_field;public int height_min;}
public class TileType:TileTypeBase {}
public class TopTileType:TileTypeBase {}
public sealed class WorldTile {
 public TileType main_type;public TopTileType top_type;public int Height;public bool frozen;
 public TileTypeBase Type=>(TileTypeBase)top_type??main_type;
 public void setTileType(string id){main_type=new(){id=id,liquid=id=="shallow_waters"||id=="deep_ocean",height_min=-1};}
 public void setTileTypes(string id,TopTileType top){top_type=top;setTileType(id);}
 public void setTileTypes(TileType main,TopTileType top){main_type=main;top_type=top;}
}
public static class MapBox {public static int width,height;}
public sealed class World {
 public static World world=new();public Dictionary<(int,int),WorldTile> tiles=new();
 public WorldTile GetTileSimple(int x,int y)=>tiles.TryGetValue((x,y),out var t)?t:null;
}
public static class WorldMapData {public static string Folder;}
public static class LogService {public static void LogInfo(string s){}public static void LogWarning(string s){throw new Exception(s);}}
public static class AssetManager {public static Tops top_tiles=new();public class Tops {public TopTileType get(string id)=>new(){id=id};}}
public static partial class Actual {
 private static List<(int rank,Vector2[] points)> _rivers;
 public static string TypeAt(float lon,float lat)=>lon<1?"deep_ocean":"soil_low:grass_low";
 public static void Rivers(params (int rank,Vector2[] points)[] lines)=>_rivers=lines.ToList();
 public static void Reload()=>_rivers=null;
 public static void Paint(int x,int y)=>Wet(x,y,MapBox.width,MapBox.height);
}
public static class Checks {
 static int n;static void Check(bool ok,string text){n++;if(!ok)throw new Exception(text);}
 static WorldTile Put(int x,int y,string main="soil_low",string top="grass_low"){
  var tile=new WorldTile();tile.setTileTypes(main,top==null?null:new(){id=top});World.world.tiles[(x,y)]=tile;return tile;
 }
 public static int Run(string folder){
  WorldMapData.Folder=folder;MapBox.width=32;MapBox.height=16;World.world=new();
  var tile=Put(5,5);Actual.Paint(5,5);
  Check(tile.main_type.id=="shallow_waters"&&tile.top_type==null&&tile.Type.liquid,"Drawing a river leaves the previous grass surface on top of water");
  Check(tile.Height==tile.Type.height_min,"River elevation does not follow its water tile type");
  var border=Put(0,5);Actual.Paint(0,5);Check(border.main_type.id=="soil_low","River drawing touches the protected map edge");
  var sea=Put(6,5,"deep_ocean",null);Actual.Paint(6,5);Check(sea.main_type.id=="deep_ocean","River drawing shallows the open sea");
  var frame=new WorldMapFrame{west=0,east=64,top=64};var size=new Vector2Int(1,1);
  Actual.Rivers((1,new Vector2[]{new(1,5),new(30,5)}));
  var points=new HashSet<(int,int)>();Actual.RiverTiles(frame,size,(x,y)=>points.Add((x,y)));
  Check(!points.Contains((1,5))&&!points.Contains((2,5))&&points.Contains((3,5)),"River rasterization removes the first ford");
  Check(!points.Contains((15,5))&&!points.Contains((16,5))&&points.Contains((17,5)),"River rasterization removes periodic land crossings");
  Check(points.Contains((3,6)),"Major river loses its two-tile width");
  Actual.Rivers((5,new Vector2[]{new(1,5),new(30,5)}));points.Clear();Actual.RiverTiles(frame,size,(x,y)=>points.Add((x,y)));
  Check(points.Contains((3,5))&&!points.Contains((3,6)),"Minor river is widened to major-river width");
  Actual.Rivers((1,new Vector2[]{new(1,5),new(30,5)}));World.world=new();
  var hidden=Put(3,5,"shallow_waters");var snow=Put(4,5,"shallow_waters","snow_hills");snow.frozen=true;
  var filled=Put(5,5);var ford=Put(1,5,"shallow_waters");var away=Put(3,10,"shallow_waters");
  var farm=Put(6,5,"shallow_waters");farm.top_type.farm_field=true;
  var road=Put(7,5,"shallow_waters");road.top_type.road=true;
  var wall=Put(8,5,"shallow_waters");wall.top_type.wall=true;
  Check(Actual.RepairRiverSurfaces(frame,size)==2,"Old-save migration misses a hidden river or touches unrelated terrain");
  Check(hidden.top_type==null&&hidden.Type.liquid,"Migration leaves a river rendered as land");
  Check(snow.top_type==null&&snow.frozen,"Migration destroys the game's temporary frozen state");
  Check(filled.main_type.id=="soil_low"&&filled.top_type!=null,"Migration re-digs a river filled by the player");
  Check(ford.top_type!=null&&away.top_type!=null,"Migration touches a ford or terrain outside the river footprint");
  Check(farm.top_type!=null&&road.top_type!=null&&wall.top_type!=null,"Migration overwrites a later farm, road or wall");
  Check(Actual.RepairRiverSurfaces(frame,size)==0,"River surface migration is not idempotent");
  MapBox.width=2;MapBox.height=1;World.world=new();var stale=Put(0,0,"soil_low");Put(1,0,"deep_ocean",null);
  Actual.Fill(frame,size);
  Check(stale.main_type.id=="deep_ocean"&&stale.top_type==null,"Base terrain fill keeps an old land surface on a lake or sea tile");
  Check(World.world.GetTileSimple(1,0).top_type?.id=="grass_low","Base terrain fill drops a valid land biome");
  Actual.Reload();var eastasia=new WorldMapFrame{west=62,east=152,top=62};var medium=new Vector2Int(28,20);
  eastasia.Bounds(medium,out float bottom,out float degrees);points.Clear();
  Actual.RiverTiles(eastasia,medium,(x,y)=>{if(x>=0&&y>=0&&x<1792&&y<1280)points.Add((x,y));});
  bool Near(float lon,float lat)=>points.Any(p=>Math.Abs(62+(p.Item1+.5f)*degrees-lon)<.2f&&Math.Abs(bottom+(p.Item2+.5f)*degrees-lat)<.2f);
  Check(points.Count>10000,"Bundled river data produces almost no river cells in the East Asia preset");
  Check(Near(114.3f,30.59f)&&Near(119f,32.15f),"Bundled river data omits the Yangtze middle or lower course");
  Check(Near(113.65f,34.9f)&&Near(119.0f,37.7f),"Bundled river data omits the Yellow River middle or lower course");
  return n;
 }
}
'@
Add-Type -TypeDefinition ($fixture + $frame + 'public static partial class Actual {' + $ford + ($members -join "`n") + '}')
Write-Output "$([Checks]::Run((Join-Path $root 'WorldMaps'))) production river drawing, crossings, old-save surface repair and bundled river data checks passed."
