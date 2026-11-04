$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content (Join-Path $root 'Scripts/GeneralSystems/GentrySystem.Kin.cs') -Raw
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
$methods = @('public static City MemberCity(', 'public static List<(City city, int recorded, int unrecorded)> PopulationPlaces(',
    'private static bool Revive(') | ForEach-Object { Method $_ }
$methods += [regex]::Match($source, 'public static int Members\(SpecificClan clan, int named\)[^;]+;').Value
$methods += [regex]::Match($source, 'public static int UnrecordedMembers\([^;]+;').Value
$fixture = @'
using System;using System.Linq;using System.Collections.Generic;
public static class Mathf {public static int Max(int a,int b)=>Math.Max(a,b);public static int RoundToInt(float v)=>(int)Math.Round(v);}
public class City {public long id;public object data=new();public bool dead;public string name;public bool isRekt()=>dead;}
public class Cities:List<City> {public City get(long id)=>this.FirstOrDefault(c=>c.id==id);}
public class World {public static World world=new();public Cities cities=new();}
public class Actor {public City city;}
public class PersonalClanIdentity {public bool is_alive=true,is_virtual;public long virtual_city_id;public Actor _actor;public string name="Recorded kinsman";}
public class SpecificClan {public float gentry_kin;public City seat;public double extinct_since=1;public List<PersonalClanIdentity> living=new();public int Count=>living.Count;}
public static class CityPopulationSystem {public static bool AbstractPopulationEnabled=true;}
public static class VirtualGenealogySystem {
 public static bool fail;
 public static PersonalClanIdentity RecordKinsman(SpecificClan c,City city){if(fail)return null;var p=new PersonalClanIdentity{is_virtual=true,virtual_city_id=city.id};c.living.Add(p);return p;}
}
public static class LM {public static string Get(string key)=>"{0} {1}";}
public static class LogService {public static void LogInfo(string text){}}
public static partial class GentrySystem {
 private const int KinRevive=20;
 public static City Seat(SpecificClan c)=>c?.seat?.data!=null&&!c.seat.dead?c.seat:null;
 private static string JunWang(SpecificClan c)=>"House";
 private static void NoteClan(SpecificClan c,string kind,string text){}
 public static bool TryRevive(SpecificClan c)=>Revive(c);
}
public static class Checks {
 static int n;
 static void Check(bool value,string message){n++;if(!value)throw new Exception(message);}
 static int Total(List<(City city,int recorded,int unrecorded)> places)=>places.Sum(p=>p.recorded+p.unrecorded);
 public static int Run(){
  var seat=new City{id=1,name="Seat"};var north=new City{id=2,name="North"};var south=new City{id=3,name="South"};
  World.world=new();World.world.cities.AddRange(new[]{seat,north,south});
  var clan=new SpecificClan{gentry_kin=427,seat=seat};
  for(int i=0;i<42;i++)clan.living.Add(new(){is_virtual=true,virtual_city_id=i<38?1:i<41?2:3});
  Check(GentrySystem.Members(clan,42)==427,"Total kin double-counts named people");
  Check(GentrySystem.UnrecordedMembers(clan,42)==385,"Named people not subtracted from inclusive kin total");
  var places=GentrySystem.PopulationPlaces(clan,clan.living);
  Check(Total(places)==427,"Screenshot case loses background kin in city distribution");
  Check(places[0].city==seat&&places[0].recorded==38&&places[0].unrecorded==385,"Background kin not counted at the modeled seat");
  Check(places.Single(p=>p.city==north).recorded==3&&places.Single(p=>p.city==south).recorded==1,"Recorded people moved to seat");
  Check(clan.gentry_kin==427&&clan.Count==42,"Opening population view mutates population");
  var unknown=new PersonalClanIdentity();clan.living.Add(unknown);
  places=GentrySystem.PopulationPlaces(clan,clan.living);
  Check(Total(places)==427&&places.Single(p=>p.city==null).recorded==1,"Unknown whereabouts silently removed from totals");
  Check(places.Sum(p=>p.unrecorded)==384,"Newly recorded kin counted twice");
  unknown._actor=new(){city=north};Check(GentrySystem.MemberCity(unknown)==north,"Real actor city not used");
  var namesake=new City{id=4,name="North"};World.world.cities.Add(namesake);unknown._actor.city=namesake;
  places=GentrySystem.PopulationPlaces(clan,clan.living);
  Check(places.Count(p=>p.city?.name=="North")==2,"Distinct cities merged by localized name");
  unknown._actor.city=null;Check(GentrySystem.MemberCity(unknown)==null,"Traveling actor assigned fictitious city");
  unknown.is_virtual=true;unknown.virtual_city_id=999;
  Check(GentrySystem.MemberCity(unknown)==null,"Missing virtual city throws or gets unrelated city");
  unknown.virtual_city_id=2;north.dead=true;
  Check(GentrySystem.MemberCity(unknown)==null,"Destroyed city still used as residence");north.dead=false;
  clan.living.Add(new(){is_alive=false,is_virtual=true,virtual_city_id=1});
  Check(GentrySystem.PopulationPlaces(clan,clan.living).Sum(p=>p.recorded)==43,"Dead person counted as living");
  clan.gentry_kin=10;places=GentrySystem.PopulationPlaces(clan,clan.living);
  Check(Total(places)==43&&places.Sum(p=>p.unrecorded)==0,"Kin count drops below recorded living people");
  clan.gentry_kin=0;Check(Total(GentrySystem.PopulationPlaces(clan,clan.living))==43,"Ordinary clan gains fictional background kin");
  clan.gentry_kin=427;seat.dead=true;places=GentrySystem.PopulationPlaces(clan,clan.living);
  Check(Total(places)==427&&places.Single(p=>p.city==null).unrecorded==384,"Lost seat removes unrecorded kin");seat.dead=false;
  Check(Total(GentrySystem.PopulationPlaces(clan,null))==427,"Empty genealogy erases surviving kin");
  Check(GentrySystem.PopulationPlaces(new(),null).Count==0,"Empty clan fabricates population");
  Check(GentrySystem.UnrecordedMembers(null,0)==0,"Null clan produces population");
  clan.living.Clear();clan.gentry_kin=427;
  Check(GentrySystem.TryRevive(clan)&&clan.Count==1,"Existing background kin cannot be recorded as new head");
  Check(clan.gentry_kin==427&&GentrySystem.UnrecordedMembers(clan,1)==426,"Recording an existing kinsman destroys or duplicates population");
  Check(clan.extinct_since==-1,"Revived clan remains extinct");
  VirtualGenealogySystem.fail=true;clan.living.Clear();
  Check(!GentrySystem.TryRevive(clan)&&clan.gentry_kin==427&&clan.Count==0,"Failed recording mutates kin");
  VirtualGenealogySystem.fail=false;clan.gentry_kin=19;
  Check(!GentrySystem.TryRevive(clan),"Revival threshold changed");
  clan.gentry_kin=427;CityPopulationSystem.AbstractPopulationEnabled=false;
  Check(!GentrySystem.TryRevive(clan),"Population view enables background genealogy in actor mode");
  return n;
 }
}
'@
Add-Type -TypeDefinition ($fixture+'public static partial class GentrySystem {'+($methods -join "`n")+'}')
Write-Output "$([Checks]::Run()) production clan population, distribution and recording-conservation checks passed."
foreach ($language in 'cz','ch','en') {
    $locale = Get-Content (Join-Path $root "Locales/$language.json") -Raw | ConvertFrom-Json -AsHashtable
    foreach ($key in 'clan_population_hint','clan_population_places_hint','clan_population_unknown','clan_population_other_cities') {
        if (!$locale.ContainsKey($key)) { throw "Missing $language localization: $key" }
    }
    $hint = [string]::Format($locale['clan_population_hint'],427,42,385)
    if (!$hint.Contains('427') -or !$hint.Contains('42') -or !$hint.Contains('385')) { throw "Inconsistent population explanation: $language" }
}
Write-Output '3 population localization and format checks passed.'
