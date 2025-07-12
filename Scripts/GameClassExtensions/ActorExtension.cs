using EmpireCraft.Scripts.Enums;
using NeoModLoader.services;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using static EmpireCraft.Scripts.GameClassExtensions.ActorExtension;
using EpPathFinding.cs;
using static EmpireCraft.Scripts.GameClassExtensions.ClanExtension;
using NeoModLoader.General;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.GodPowers;
using EmpireCraft.Scripts.Layer;
using static EmpireCraft.Scripts.GameClassExtensions.CityExtension;
using EmpireCraft.Scripts.Data;
using System.Configuration;
using static EmpireCraft.Scripts.HelperFunc.ExamSystem;
using System.Numerics;

namespace EmpireCraft.Scripts.GameClassExtensions;
public class OfficeIdentity
{
    public OfficialLevel officialLevel { get; set; }
    public int meritLevel { get; set; }
    public int honoraryOfficial { get; set; }
    public PeerageType peerageType { get; set; }
    public double OfficePerformance { get; set; } = 100;
    public PerformanceEvents performanceEvents { get; set; }
    public List<EmpireExamLevel> empireExamLevels { get; set; } = new List<EmpireExamLevel>();
    public long empire_id;
    public long actor_id;
    public void init (Empire empire, Actor actor) 
    {
        empire_id = empire.data.id;
        actor_id = actor.data.id;
        OfficePerformance = 100;
        empireExamLevels = new List<EmpireExamLevel>();
    }
    public void ChangeOfficialLevel(OfficialLevel level)
    {
        Empire empire = ModClass.EMPIRE_MANAGER.get(empire_id);
        Actor actor = World.world.units.get(actor_id);
        officialLevel = level;
        if (level == OfficialLevel.officiallevel_10)
        {
            performanceEvents = null;
        } else
        {
            performanceEvents = new PerformanceEvents();
            performanceEvents.init(empire, actor);
        }
    }
}

public enum PerformanceEventType
{
    Irrigation_Project_Completed,
    Local_Rebellion_Suppressed,
    Virtuous_Talent_Recommended,
    Disaster_Relief_Conducted,
    Schools_Restored,
    Factionalism_Exposed,
    Negligence_in_Local_Affairs,
    Excessive_Taxation_Imposed,
    Corrupted_Performance_Reports,
    Refused_Imperial_Summons,
    Administrative_Reform,
    Legal_Code_Revision,
    Policy_Championed_at_Court,
    Talents_Recommended,
    Corruption_Exposed,
    Factional_Infighting,
    Examination_Manipulation,
    Neglect_of_Duty,
    Unpopular_Reforms,
    Misleading_the_Emperor,
    None
}
public static class ActorExtension
{
    public class ActorExtraData
    {
        public long id;
        // 爵位  
        [JsonConverter(typeof(StringEnumConverter))]
        public PeeragesLevel peeragesLevel;
        public PeerageType peerageType;
        public string title = "";
        public List<long> titles = new List<long>();
        public List<long> want_acuired_title = new List<long>();
        public List<long> owned_title = new List<long>();
        public OfficeIdentity officeIdentity { get; set; } = null;
        public long empire_id { get; set; } = -1L;
        public long provinceId { get; set; } = -1L;
    }
    public static void SetTitle(this Actor a, string value)
    {
        if (a == null || value == null) return;
        if (a.kingdom == null) return;
        if (a.kingdom.GetEmpire() == null) return;
        GetOrCreate(a).title = value + " " + TranslateHelper.GetPeerageTranslate(a.GetPeeragesLevel());
    }

    public static void SetEmpire(this Actor a, Empire empire)
    {
        GetOrCreate(a).empire_id = empire.data.id;
    }

    public static void RemoveEmpire(this Actor a)
    {
        GetOrCreate(a).empire_id = -1L;
    }

    public static Empire GetEmpire(this Actor a)
    {
        if (GetOrCreate(a).empire_id==-1L)
        {
            return null;
        } else
        {
            return ModClass.EMPIRE_MANAGER.get(GetOrCreate(a).empire_id);
        }
    }

    public static void AddOfficeExamLevel(this Actor actor, EmpireExamLevel level)
    {
        if (GetOrCreate(actor).officeIdentity!=null)
        {
            GetOrCreate(actor).officeIdentity.empireExamLevels.Add(level);
            if (GetOrCreate(actor).officeIdentity.empireExamLevels.Count==4)
            {
                JudgeOfficeLevel(actor);
            }
            if (GetOrCreate(actor).officeIdentity.empireExamLevels.Count>4)
            {
                GetOrCreate(actor).officeIdentity.empireExamLevels.RemoveAt(0);
            }
        }
    }

    public static void JudgeOfficeLevel(Actor actor)
    {
        List<EmpireExamLevel> empireExamLevels = actor.GetEmpireExamLevels();
        if (empireExamLevels.All(a => a.Equals(EmpireExamLevel.HD)))
        {
            actor.UpgradeOfficial();
            actor.UpgradeOfficial();
        } else if (empireExamLevels.All(a => a.Equals(EmpireExamLevel.HD)|| a.Equals(EmpireExamLevel.CR)))
        {
            actor.UpgradeOfficial();
        } else if (empireExamLevels.All(a => a.Equals(EmpireExamLevel.F)|| a.Equals(EmpireExamLevel.P))&& empireExamLevels.Any(a=>a.Equals(EmpireExamLevel.F)))
        {
            actor.DegradeOfficial();
        } else if (empireExamLevels.All(a => a.Equals(EmpireExamLevel.F)))
        {
            actor.DegradeOfficial();
            actor.DegradeOfficial();
        }
    }

    public static List<EmpireExamLevel> GetEmpireExamLevels(this Actor actor)
    {
        List < EmpireExamLevel > levels= new List<EmpireExamLevel >();
        if (GetOrCreate(actor).officeIdentity != null)
        {
            levels = GetOrCreate(actor).officeIdentity.empireExamLevels;
        }
        return levels;
    }

    public static string GetEmpireExamLevelsString(this Actor actor)
    {
        List<EmpireExamLevel> levels = actor.GetEmpireExamLevels();
        List<string> result = new List<string>();
        if (levels == null) return "";
        if (levels.Count<=0) return "";
        return String.Join(", ", levels.Select(e=>e.ToString()).ToList());
    }

    public static void ResetPerformance(this Actor a)
    {
        OfficeIdentity identity = GetOrCreate(a).officeIdentity;
        if (identity != null) 
        {
            identity.OfficePerformance = 50;
        }
    }

    public static PeerageType GetPeerageType(this Actor a)
    {
        return GetOrCreate(a).peerageType;
    }

    public static void SetPeerageType(this Actor a, PeerageType type = PeerageType.Civil)
    {
        GetOrCreate(a).peerageType = type;
    }

    public static long GetProvinceID(this Actor a)
    {
        if (a ==null)
        {
            return -1L;
        }
        return GetOrCreate(a).provinceId;
    }
    public static void SetProvinceID( this Actor a, long id)
    {
        if (a == null) return;
        GetOrCreate(a).provinceId = id;
    }
    public static void SetProvince( this Actor a, Province province)
    {
        if (a == null) return;
        GetOrCreate(a).provinceId = province.getID();
        if (GetOrCreate(a).officeIdentity == null)
        {
            GetOrCreate(a).officeIdentity = new OfficeIdentity();
            GetOrCreate(a).officeIdentity.init(province.empire, a);
            GetOrCreate(a).officeIdentity.honoraryOfficial = 8;
            GetOrCreate(a).officeIdentity.meritLevel = 10;
            GetOrCreate(a).officeIdentity.officialLevel = OfficialLevel.officiallevel_8;
            GetOrCreate(a).peerageType = PeerageType.Civil;
        } else
        {
            GetOrCreate(a).officeIdentity.officialLevel = OfficialLevel.officiallevel_8;
        }
    }

    public static bool isInOffice(this Actor a )
    {
        if(a == null) return false;
        if (!a.hasTrait("officer")) return false;
        if (GetOrCreate(a).officeIdentity==null) return false;
        if (GetOrCreate(a).officeIdentity.officialLevel == OfficialLevel.officiallevel_10) return false;
        return true;
    }
    public static OfficeIdentity GetIdentity( this Actor a, Empire empire)
    {
        if (a == null) return null;
        if (a.city == null) return null;
        if (a.city.kingdom == null) return null;
        if (GetOrCreate(a).officeIdentity == null)
        {
            GetOrCreate(a).officeIdentity = new OfficeIdentity();
            GetOrCreate(a).officeIdentity.init(empire, a);
            GetOrCreate(a).officeIdentity.honoraryOfficial = 8;
            GetOrCreate(a).officeIdentity.meritLevel = 10;
        }
        return GetOrCreate(a).officeIdentity;
    }

    public static void ChangeOfficialLevel(this Actor a, OfficialLevel level)
    {
        if (a == null) return;
        if (a.city == null) return;
        if (a.city.kingdom == null) return;
        Empire empire = a.city.kingdom.GetEmpire();
        if (empire==null) return;
        if (GetOrCreate(a).officeIdentity==null) 
        {
            GetOrCreate(a).officeIdentity = new OfficeIdentity();
            GetOrCreate(a).officeIdentity.init(empire, a);
            GetOrCreate(a).officeIdentity.honoraryOfficial = 8;
            GetOrCreate(a).officeIdentity.meritLevel = 10;
        }
        GetOrCreate(a).officeIdentity.ChangeOfficialLevel(level);
    }

    public static void SetIdentityType(this Actor a, PeerageType type=PeerageType.Civil)
    {
        if (a == null) return;
        if (a.city == null) return;
        if (a.city.kingdom == null) return;
        Empire empire = a.city.kingdom.GetEmpire();
        if (empire == null) return;
        if (GetOrCreate(a).officeIdentity == null)
        {
            GetOrCreate(a).officeIdentity = new OfficeIdentity();
            GetOrCreate(a).officeIdentity.init(empire, a);
            GetOrCreate(a).officeIdentity.honoraryOfficial = 8;
            GetOrCreate(a).officeIdentity.meritLevel = 10;
        }
        GetOrCreate(a).officeIdentity.peerageType = type;
    }
    public static void SetIdentity(this Actor a, OfficeIdentity identity, bool isInitial=false)
    {
        if (a == null) return;
        if (isInitial)
        {
            identity.honoraryOfficial = 8;
            identity.meritLevel = 10;
            identity.peerageType = PeerageType.Civil;
        }
        GetOrCreate(a).officeIdentity = identity;
    }

    public static void RemoveIdentity(this Actor a)
    {
        if (a != null) 
        {
            GetOrCreate(a).officeIdentity = null;
        }
    }
    public static void RemoveProvinceID(this Actor a)
    {
        if (a == null) return;
        GetOrCreate(a).provinceId = -1L;
    }
    public static bool isOfficer(this Actor a)
    {
        if (a == null) return false;
        OfficeIdentity indentity = GetOrCreate(a).officeIdentity;
        if (indentity == null) return false;
        if (indentity.officialLevel == OfficialLevel.officiallevel_10) return false;
        return true;
    }

    public static void RemoveExtraData(this Actor a)
    {
        if (a == null) return;
        ExtensionManager<Actor, ActorExtraData>.Remove(a);
    }

    public static void Clear()
    {
        ExtensionManager<Actor, ActorExtraData>.Clear();
    }

    public static bool canAcquireTitle(this Actor a)
    {
        if (a.isKing())
        {
            Kingdom k = a.kingdom;
            if (k.isInEmpire())
            {
                if (k.GetEmpire().getEmpirePeriod()!=EmpirePeriod.逐鹿群雄 && k.GetEmpire().getEmpirePeriod() != EmpirePeriod.天命丧失) return false;
            }
            foreach (City city in k.cities)
            {
                if (city.hasTitle())
                {
                    KingdomTitle title = city.GetTitle();
                    if (title == null) continue;
                    if (title.data == null)
                    {
                        ModClass.KINGDOM_TITLE_MANAGER.update(-1L);
                    }
                    if (!a.GetOwnedTitle().Contains(title.data.id))
                    {
                        foreach(City tCity in title.city_list)
                        {
                            if (tCity.kingdom != k && tCity.kingdom.countTotalWarriors()<k.countTotalWarriors())
                            {
                                return true;
                            }
                        }
                    }
                }
            }
        }
        return false;
    }

    public static void UpgradeOfficial(this Actor a, bool need_merit = false, int direct = -1)
    {
        if (GetOrCreate(a).officeIdentity==null)
        {
            return;
        } else
        {
            OfficeIdentity identity = GetOrCreate(a).officeIdentity;
            if (need_merit)
            {
                if (identity.meritLevel <= 0)
                {
                    identity.meritLevel = 0;
                }
                else
                {
                    identity.meritLevel -= 1;
                }
                if (direct!=-1||direct>identity.meritLevel)
                {
                    identity.meritLevel = direct;
                }
            } else
            {
                if (identity.honoraryOfficial <= 0)
                {
                    identity.honoraryOfficial = 0;
                }
                else
                {
                    identity.honoraryOfficial -= 1;
                }
                if (direct != -1)
                {
                    identity.honoraryOfficial = direct;
                }
                LogService.LogInfo("升官");
            }
            GetOrCreate(a).officeIdentity = identity;
        }
    }

    public static void DegradeOfficial(this Actor a)
    {
        if (GetOrCreate(a).officeIdentity==null)
        {
            return;
        } else
        {
            OfficeIdentity identity = GetOrCreate(a).officeIdentity;

            if (identity.meritLevel >= 10)
            {
                identity.meritLevel = 10;
            }
            else
            {
                identity.meritLevel += 1;
            }
            if (identity.honoraryOfficial>=8)
            {
                identity.honoraryOfficial = 8;
            } else
            {
                identity.honoraryOfficial += 1;
            }
            LogService.LogInfo("贬官");
            GetOrCreate(a).officeIdentity = identity;
        }
    }

    public static string GetActorName(this Actor a)
    {
        if (a == null) return null;
        if (a.name == null || a.name == "") return null;
        string[] nameParts = a.name.Split(' ');

        if (ConfigData.speciesCulturePair.TryGetValue(a.asset.id, out var culture))
        {
            if (OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting))
            {
                if (nameParts.Length - 1 >= setting.Unit.name_pos)
                {
                    return nameParts[setting.Unit.name_pos];
                }
            }
        }
        return nameParts[0];
    }

    public static List<KingdomTitle> getAcquireTitle(this Actor a)
    {
        List<KingdomTitle> titles = new();
        if (a.isKing())
        {
            Kingdom k = a.kingdom;
            foreach (City city in k.cities)
            {
                if (city.hasTitle())
                {
                    KingdomTitle title = city.GetTitle();
                    if (!titles.Contains(title)&&!a.GetOwnedTitle().Contains(title.data.id))
                    {
                        titles.Add(title);
                    }
                }
            }
        }
        return titles;
    }

    public static string GetTitle(this Actor a)
    {
        if (!a.HasTitle()) return "";
        if (!a.isKing()) return "";
        if (a.kingdom == null) return "";
        Kingdom kingdom = a.kingdom;
        var ownedTitles = a.GetOwnedTitle();
        if (ownedTitles == null) return "";
        try
        {
            var hasCapitalTitle = ownedTitles.Exists(t => ModClass.KINGDOM_TITLE_MANAGER.checkTitleExist(t) ? ModClass.KINGDOM_TITLE_MANAGER.get(t).title_capital == kingdom.capital : false);
            if (hasCapitalTitle)
            {
                kingdom.SetMainTitle(kingdom.capital.GetTitle());
                return kingdom.capital.GetTitle().data.name;
            }
            else
            {
                KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.get(ownedTitles.FirstOrDefault());
                kingdom.SetMainTitle(title);
                return title.data.name;
            }
        }
        catch {
            return "";
        }
    }
    public static bool HasTitle(this Actor a)
    {
        return GetOrCreate(a).owned_title.Count>0;
    }

    public static bool HasCapitalTitle(this Actor a)
    {
        if (a.kingdom.capital.hasTitle())
        {
            return a.GetOwnedTitle().Contains(a.kingdom.capital.GetTitle().data.id);
        }
        return false;
    }

    public static bool IsCapitalTitleBelongsToEmperor(this Actor a)
    {
        if (!a.hasKingdom()) return false;
        if (a.kingdom.GetEmpire() == null) return false;
        if (a.kingdom.GetEmpire().emperor.GetOwnedTitle()==null) return false;
        if (a.kingdom.capital.GetTitle()==null) return false;
        if (a.kingdom.isInEmpire())
        {
            if (a.kingdom.capital.hasTitle())
            {
                return a.kingdom.GetEmpire().emperor.GetOwnedTitle().Contains(a.kingdom.capital.GetTitle().data.id);
            }
        }
        return false;
    }

    public static string GetTitleName(this Actor a)
    {
        string title = a.GetTitle();
        string[] titleParts = title.Split(' ');
        if (titleParts.Length <= 0) return null;
        if (titleParts.Length <= 1) return title;
        if (titleParts.Length <= 2) return titleParts[0];
        return titleParts[titleParts.Length - 2];
    }
    public static bool syncData(this Actor a, ActorExtraData actorExtraData)
    {
        var ed = GetOrCreate(a);
        ed.id = actorExtraData.id;
        ed.peeragesLevel = actorExtraData.peeragesLevel;
        ed.title = actorExtraData.title;
        ed.owned_title = actorExtraData.owned_title;
        ed.want_acuired_title = actorExtraData.want_acuired_title;
        ed.officeIdentity = actorExtraData.officeIdentity;
        ed.provinceId = actorExtraData.provinceId;
        ed.peerageType = actorExtraData.peerageType;
        return true;
    }

    public static ActorExtraData getExtraData(this Actor a, bool isSave=false)
    {
        if (GetOrCreate(a, isSave) == null) return null;
        ActorExtraData data = new ActorExtraData();
        data.id = a.getID();
        data.peeragesLevel = a.GetPeeragesLevel();
        data.title = a.GetTitle();
        data.want_acuired_title = a.GetAcquireTitle();
        data.owned_title = a.GetOwnedTitle();
        data.officeIdentity = GetOrCreate(a).officeIdentity;
        data.provinceId = GetOrCreate(a).provinceId;
        data.peerageType = GetOrCreate(a).peerageType;
        return data;
    }


    public static bool isCityPass(this Actor a)
    {
        foreach(ActorTrait trait in a.traits)
        {
            if (trait.group_id== "EmpireExam")
            {
                return true;
            }
        }
        return false;
    }

    public static bool canTakeTitle(this Actor a)
    {
        if (!a.isKing()) return false;
        Kingdom kingdom = a.kingdom;
        if (kingdom == null) return false;
        List<long> controledTitles = kingdom.getControledTitle().FindAll(t=>!t.owner.isEmperor()).Select(t=>t.data.id).ToList();
        var commonTitles = controledTitles.Intersect(a.GetOwnedTitle());
        return commonTitles.Count() < controledTitles.Count();
    }

    public static List<KingdomTitle> titleCanBeDestroy(this Actor a)
    {
        List<KingdomTitle> titles = new List<KingdomTitle>();
        foreach(long id in a.GetOwnedTitle())
        {
            if (ModClass.KINGDOM_TITLE_MANAGER.checkTitleExist(id))
            {
                KingdomTitle kt = ModClass.KINGDOM_TITLE_MANAGER.get(id);
                if (kt == null) continue;
                if (Date.getYearsSince(kt.data.timestamp_been_controled) >= ModClass.TITLE_BEEN_DESTROY_TIME && kt != a.kingdom.GetMainTitle())
                {
                    titles.Add(kt);
                }
            }
        }
        return titles;
    }

    public static List<KingdomTitle> takeTitle(this Actor a)
    {
        if (!a.isKing()) return null;
        List<KingdomTitle> takedTitles = new List<KingdomTitle>();
        Kingdom kingdom = a.kingdom;
        List<KingdomTitle> titles = kingdom.getControledTitle();
        foreach(KingdomTitle t in titles)
        {
            if (t.main_kingdom!=null)
            {
                t.main_kingdom.RemoveMainTitle();
                t.main_kingdom = null;
            }
            if(t.HasOwner()&&t.owner.isEmperor())
            {
                if (!a.GetAcquireTitle().Contains(t.id)&&t.owner.getID()!=a.getID()) 
                {
                    a.AddAcquireTitle(t);
                }
            }
            else
            {
                if (!a.GetOwnedTitle().Contains(t.id)) 
                {
                    takedTitles.Add(t);
                    if (t.HasOwner()) 
                    {
                        t.owner.removeTitle(t);
                    }
                    a.AddOwnedTitle(t);
                }
            }
        }
        return takedTitles;
    }

    public static void AddAcquireTitle(this Actor a, KingdomTitle title)
    {
        var ed = GetOrCreate(a);
        ed.want_acuired_title.Add(title.data.id);
    }

    public static void AddOwnedTitle(this Actor a, KingdomTitle title)
    {
        var ed = GetOrCreate(a);
        if (ed == null) return;
        if (title == null) return;
        if (ed.owned_title==null) ed.owned_title = new List<long> { 0 };
        if (!ed.owned_title.Contains(title.data.id))
            ed.owned_title.Add(title.data.id);
            title.owner = a;
    }

    public static void removeTitle(this Actor a, KingdomTitle title)
    {
        if (title == null) return;
        if (a == null) return;
        var ed = GetOrCreate(a);
        if (a.GetOwnedTitle().Contains(title.data.id))
        {
            if (!a.isEmperor()&&a.isKing())
            {
                if (a.kingdom.GetKingdomName()==title.data.name)
                {
                    a.kingdom.data.name = a.kingdom.capital.name;
                    a.kingdom.empireLeave(true);
                }
                if (a.kingdom.GetMainTitle() == title)
                {
                    a.kingdom.RemoveMainTitle();
                }
            }
            a.kingdom.GetOwnedTitle().Remove(title.data.id);
            ed.owned_title.Remove(title.data.id);
            title.owner = null;
        }
    }

    public static List<long> GetAcquireTitle(this Actor a)
    {
        var ed = GetOrCreate(a);
        return ed.want_acuired_title;
    }

    public static List<long> GetOwnedTitle(this Actor a)
    {
        if (a == null) return null;
        var ed = GetOrCreate(a);
        return ed.owned_title;
    }

    public static void ClearTitle(this Actor a)
    {
        var ed = GetOrCreate(a);
        ed.owned_title.Select(t=>ModClass.KINGDOM_TITLE_MANAGER.get(t).owner=null);
        ed.owned_title.Clear();
        ed.want_acuired_title.Clear();
    }

    public static bool isEmperor(this Actor a)
    {
        if (a==null) return false;
        return GetOrCreate(a).empire_id!=-1L;
    }

    public static ActorExtraData GetOrCreate(this Actor a, bool isSave=false)
    {
        var ed = ExtensionManager < Actor, ActorExtraData>.GetOrCreate(a, isSave);
        return ed; 
    }
    public static PeeragesLevel GetPeeragesLevel(this Actor a)
        => GetOrCreate(a).peeragesLevel;
    public static void SetPeeragesLevel(this Actor a, PeeragesLevel lvl)
    {
        
        var data = GetOrCreate(a);
        data.id = a.getID();
        data.peeragesLevel = lvl;
    }
}