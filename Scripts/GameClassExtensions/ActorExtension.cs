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
using static EmpireCraft.Scripts.System.ExamSystem;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Runtime.Serialization;
using static EmpireCraft.Scripts.HelperFunc.OverallHelperFunc;
using System.Security.Principal;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using UnityEngine;

namespace EmpireCraft.Scripts.GameClassExtensions;
public class Name
{
    public string firstName;
    public string familyName;
    public string cultureName;
    public bool has_sex_post;
    public bool use_local_as_family_name;
    public bool is_invert;
    public ActorSex sex;

    public bool has_whole_name(Actor actor)
    {
        return firstName != "" && familyName != ""&&firstName!=null&&familyName!=null;
    }
    
    public bool hasFamilyName(Actor actor)
    {
        return !string.IsNullOrEmpty(familyName);
    }
    public bool hasFirstName(Actor actor)
    {
        return !string.IsNullOrEmpty(firstName);
    }
    public bool hasCulture(Actor actor)
    {
        return actor.hasCulture();
    }

    public void Initialize(bool sex_post, bool local, bool is_invert, string culture)
    {
        this.has_sex_post = sex_post;
        this.use_local_as_family_name = local;
        this.is_invert = is_invert;
        this.cultureName = culture;
    }

    public void SetName(Actor actor)
    {
        sex = actor.isSexMale() ? ActorSex.Male : ActorSex.Female;
        if (hasFamilyName(actor))
        {
            string real_family_name;
            string cityName = "";
            if (use_local_as_family_name)
            {
                if  (actor.hasCity())
                {
                    cityName = actor.city.name;
                } else
                {
                    if (actor.current_tile.hasCity())
                    {
                        cityName = actor.current_tile.zone_city.name;
                    } else
                    {
                        if (actor.getParents().Count()>0) 
                        {
                            if (actor.getParents().Any(a=>a.hasCity()))
                            {
                                cityName = actor.getParents().ToList().Find(p => p.hasCity()).city.name;
                            }
                        }
                    }
                }
            }
            if (use_local_as_family_name && cityName != "")
            {
                real_family_name = cityName;
                familyName = real_family_name;
                if (actor.hasClan())
                {
                    actor.clan.data.name = real_family_name + "\u200A" + LM.Get("Clan");
                }
                if (actor.hasFamily())
                {
                    actor.family.data.name = real_family_name + "\u200A" + LM.Get("Family");
                    actor.family.SetFamilyCityPre(false);
                }
            }
            real_family_name = (use_local_as_family_name&&cityName!="") ? cityName : familyName;
            if (has_sex_post)
            {
                string post = LM.Get($"{cultureName}_sex_post_{sex.ToString()}");
                if (!real_family_name.Contains(post))
                {
                    real_family_name += post;
                }
            }
            actor.data.name = is_invert ? firstName + "\u200A" + real_family_name : real_family_name + "\u200A" + firstName;
            if (actor.HasSpecificClan())
            {
                PersonalClanIdentity identity = actor.GetPersonalIdentity();
                identity.name = actor.name;
                SpecificClan sc = identity._specificClan;
                if (identity.id == sc.founder)
                {
                    sc.name = real_family_name;
                }
            }
        } else
        {
            if (hasFirstName(actor))
            {
                actor.data.name = firstName;
            }
        }
    }
}
public class OfficeIdentity
{
    private long officeID { get; set; } = -1L;
    public int officialLevel { get; set; } = -1;
    public int meritLevel { get; set; }
    public int honoraryOfficial { get; set; }
    public PeerageType peerageType { get; set; }
    public double OfficePerformance { get; set; } = 100;
    private bool _is_cabinet { get; set; } = false;
    public double TotalPerformance { get; set; } = 0;
    public PerformanceEvents performanceEvents { get; set; }
    public List<EmpireExamLevel> empireExamLevels { get; set; } = new List<EmpireExamLevel>();
    public long actor_id;
    public void init (Actor actor) 
    {
        actor_id = actor.data.id;
        OfficePerformance = 100;
        empireExamLevels = new List<EmpireExamLevel>();

        if (officialLevel == -1)
        {
            performanceEvents = null;
        }
        else
        {
            performanceEvents = new PerformanceEvents();
            performanceEvents.init(actor);
        }
    }
    /// <summary>
    /// 进入内阁
    /// </summary>
    public void EnterCabinet()
    {
        _is_cabinet = true;
    }
    /// <summary>
    /// 离开内阁
    /// </summary>
    public void ExitCabinet()
    {
        _is_cabinet = false;
    }

    public bool IsCabinet()
    {
        return _is_cabinet;
    }
    public void SetOfficeId(long oid)
    {
        this.officeID = oid;
    }

    public bool HasOffice()
    {
        return officeID != -1L;
    }

    public void RemoveOffice()
    {
        if (OfficeManager.Offices.TryGetValue(this.GetOfficeId(), out var value))
        {
            value.RemoveActor();
        }

        officialLevel = -1;
        officeID = -1L;
    }

    public long GetOfficeId()
    {
        return this.officeID;
    }
    
    public void ChangeOfficialLevel(int level)
    {
        Actor actor = World.world.units.get(actor_id);
        officialLevel = level;
        if (level == -1)
        {
            performanceEvents = null;
        } else
        {
            performanceEvents = new PerformanceEvents();
            performanceEvents.init(actor);
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
    public class ActorExtraData:ExtraDataBase
    {
        // 爵位  
        [JsonConverter(typeof(StringEnumConverter))]
        public PeeragesLevel peeragesLevel;
        public PeerageType peerageType;
        public List<long> want_acuired_title = new List<long>();
        public List<long> owned_title = new List<long>();
        public FactionType factionType = FactionType.无;
        public long faction_empire = -1L;
        public Name name;
        public bool has_become_cleric = false;
        public OfficeIdentity officeIdentity { get; set; } = null;
        public double last_tax_timestamp = -1L;
        public long empire_id { get; set; } = -1L;
        public long OfficeId { get; set; } = -1L;
        public bool is_on_office = false;
        public long personal_identity { get; set; } = -1L;
        public float death_rate = 0.0f;
    }

    public static void StartOffice(this Actor a, OfficeObject o)
    {
        a.GetOrCreate().OfficeId = o.OfficeID;
        a.GetOrCreate().is_on_office = true;
    }

    public static bool HasChooseToBecomeCleric(this Actor a)
    {
        return a.GetOrCreate().has_become_cleric;
    }

    public static bool FinishChooseToBecomeCleric(this Actor a)
    {
        return a.GetOrCreate().has_become_cleric = true;
    }
    public static FixedFaction GetFaction(this Actor a)
    {
        Empire empire = ModClass.EMPIRE_MANAGER.get(a.GetOrCreate().faction_empire);
        List<FixedFaction> factions = empire?.CoreKingdom?.GetRegime().Factions??new List<FixedFaction>();
        foreach (FixedFaction faction in factions)
        {
            if (faction.Type == a.GetOrCreate().factionType)
            {
                return faction;
            }
        }

        return null;
    }

    public static void SetFaction(this Actor a, FixedFaction faction)
    {
        var lastFaction = a.GetFaction();
        if (lastFaction != null)
        {
            lastFaction.RemoveMember(a);
        } 
        faction.AddMember(a);
        a.GetOrCreate().factionType = faction.Type;
        a.GetOrCreate().faction_empire = faction.EmpireId;
    }

    public static void RemoveFaction(this Actor a)
    {
        a.GetOrCreate().factionType = FactionType.无;
        a.GetOrCreate().faction_empire = -1L;
    }

    public static void EndOffice(this Actor a)
    {
        a.GetOrCreate().OfficeId = -1L;
        a.GetOrCreate().is_on_office = false;
    }

    public static OfficeObject GetOffice(this Actor a)
    {
        return OfficeManager.Offices.TryGetValue(a.GetOrCreate().OfficeId,  out var value) ? value : null;
    }

    public static bool IsOnOffice(this Actor a)
    {
        return a.GetOrCreate().is_on_office;
    }
    public static bool NeedDead(this Actor a)
    {
        if (a == null) return false;

        var data = a.GetOrCreate();
        float rate = Mathf.Clamp01(data.death_rate);

        // 边界：0 一定不死；1 一定会死
        if (rate <= 0f) return false;
        if (rate >= 1f) return true;

        // 概率判定（Unity）
        return UnityEngine.Random.value < rate;
    }
    public static double CalcCorruptionValue(this Actor actor)
    {
        double result = 0f;
        double value = 0.5f-PerformanceEvents.GetPersonalPerformance(actor);
        if (value > 0)
        {
            result = value / 0.5f;
        }
        return result;
    }
    public static double GetLastTaxTime(this Actor k)
    {
        return k.GetOrCreate().last_tax_timestamp;
    }
    public static void RecordTaxTime(this Actor k)
    {
        k.GetOrCreate().last_tax_timestamp = World.world.getCurWorldTime();
    }

    public static bool IsNeedToSubmitTax(this Actor k)
    {
        if (!k.hasKingdom()) return false;
        return Date.getYearsSince(k.GetLastTaxTime()) >= 1;
    }

    public static void ChangeDeathRate(this Actor a, float value)
    {
        a.GetOrCreate().death_rate += value;
        if (a.GetOrCreate().death_rate <= 0.0f)
        {
            a.GetOrCreate().death_rate = 0.0f;
        } else if (a.GetOrCreate().death_rate >= 1.0f)
        {
            a.GetOrCreate().death_rate = 1.0f;
        }
    }
    public static SpecificClan GetSpecificClan(this Actor a)
    {
        if (a == null) return null;
        var identity = a.GetPersonalIdentity();
        return identity?._specificClan;
    }
    public static PersonalClanIdentity GetPersonalIdentity(this Actor a)
    {
        if (a == null) return null;
        var ed = a.GetOrCreate();
        return SpecificClanManager.getPerson(ed.personal_identity);
    }
    public static void RemoveSpecificClan(this Actor a)
    {
        if (a == null) return;
        var ed = a.GetOrCreate();
        ed.personal_identity = -1L;
    }
    public static void SetPersonalIdentity(this Actor a, PersonalClanIdentity pci)
    {
        if (a == null) return;
        var ed = a.GetOrCreate();
        ed.personal_identity = pci.id;
    }
    public static void RemovePersonalIdentity(this Actor a)
    {
        if (a == null) return;
        var ed = GetOrCreate(a);
        ed.personal_identity = -1L;
    }

    public static PersonalClanIdentity InitialPersonalIdentity(this Actor a, SpecificClan clan)
    {
        if (a == null) return null;
        var ed = a.GetOrCreate();
        PersonalClanIdentity pci = new PersonalClanIdentity();
        pci.newPersonalClanIdentity(clan, a);
        clan._cache.Add(pci.id, pci);
        a.SetPersonalIdentity (pci);
        if (a.hasLover())
        {
            pci.setLover(a.lover);
        }
        return pci;
    }

    public static Culture GetCulture(this Actor a)
    {
        if (a == null) return null;
        if (a.hasCulture())
        {
            return a.culture;
        } else
        {
            if (a.getParents().Any())
            {
                foreach(Actor parent in a.getParents())
                {
                    if (parent.hasCulture())
                    {
                        return parent.culture;
                    }
                }
            }
        }
        return null;
    }

    public static bool HasSpecificClan(this Actor a)
    {
        if (a == null) return false;
        var ed = a.GetOrCreate();
        return ed.personal_identity != -1L;
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
    public static void editRenown(this Actor a, int value)
    {
        a.data.renown += value;
        if (value <= 0)
        {
            a.data.renown = 0;
        }
    }
    public static void AddOfficeExamLevel(this Actor actor, EmpireExamLevel level)
    {
        if (GetOrCreate(actor).officeIdentity!=null)
        {
            GetOrCreate(actor).officeIdentity.empireExamLevels.Add(level);
            if (GetOrCreate(actor).officeIdentity.empireExamLevels.Count>4)
            {
                GetOrCreate(actor).officeIdentity.empireExamLevels.RemoveAt(0);
            }
            JudgeOfficeLevel(actor);
        }
    }

    public static void JudgeOfficeLevel(Actor actor)
    {
        var p = actor.GetIdentity().TotalPerformance;
        if (actor.kingdom.GetRegime().type!=RegimeType.LvLing) return;
        if (p > 1600)
        {
            actor.UpgradeOfficial(direct:0);
        }
        else if (p >1300)
        {
            actor.UpgradeOfficial(direct:1);
        }
        else if (p > 1000)
        {
            actor.UpgradeOfficial(direct:2);
        }
        else
        {
            var dir = 5 - (int)Math.Floor(5 * p / 1000.0f) + 3;
            actor.UpgradeOfficial(direct:dir);
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
    
    public static void initializeActorName(this Actor a)
    {
        string culture_name = GetCultureFromSpecies(a.getActorAsset().id);
        if (OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture_name, out Setting setting))
        {
            a.GetModName().Initialize(setting.Clan.has_sex_post, setting.Clan.use_local_as_lastname, setting.Unit.is_invert, culture_name);
        }
    }

    public static void SetFirstName(this Actor a, string name)
    {
        if (a == null) return;
        if (name == null) return;
        if (name == "") return;
        a.GetOrCreate().name.firstName = name;
    }

    public static void SetFamilyName(this Actor a, string name)
    {
        if (a == null) return;
        if (name == null) return;
        if (name == "") return;
        a.GetOrCreate().name.familyName = name;
    }

    public static bool HasOfficeIdentity(this Actor a)
    {
        return a?.GetOrCreate().officeIdentity != null;
    }
    public static OfficeIdentity GetIdentity( this Actor a)
    {
        if (!a.HasOfficeIdentity()) return null;
        if (GetOrCreate(a).officeIdentity.GetOfficeId()!=-1L)
        {
            if (GetOrCreate(a).officeIdentity.performanceEvents == null)
            {
                GetOrCreate(a).officeIdentity.performanceEvents = new PerformanceEvents();
                GetOrCreate(a).officeIdentity.performanceEvents.init(a);

            } else
            {
                GetOrCreate(a).officeIdentity.performanceEvents.init(a);
            }

        } 
        return GetOrCreate(a).officeIdentity;
    }

    public static void ChangeOfficialLevel(this Actor a, int level)
    {
        if (a == null) return;
        GetOrCreate(a).officeIdentity.ChangeOfficialLevel(level);
    }

    public static void SetIdentityType(this Actor a, PeerageType type=PeerageType.Civil)
    {
        if (a == null) return;
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

    public static void InitialIdentity(this Actor a)
    {
        OfficeIdentity identity = new OfficeIdentity
        {
            actor_id = a.getID()
        };
        a.SetIdentity(identity, true);
    }

    public static void RemoveIdentity(this Actor a)
    {
        if (a != null) 
        {
            GetOrCreate(a).officeIdentity = null;
        }
    }
    public static bool isOfficer(this Actor a)
    {
        if (a == null) return false;
        OfficeIdentity identity = GetOrCreate(a).officeIdentity;
        if (identity == null) return false;
        if (identity.officialLevel == -1) return false;
        return true;
    }

    public static bool CanGrabAlliance(this Actor a)
    {

        return false;
    }


    public static bool CanAcquireTitle(this Actor a)
    {
        if (a.isKing())
        {
            Kingdom k = a.kingdom;
            if (!(k.GetRegime()?.IsAllowDiplomacy()??false)) return false;
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
                    if (title.data != null && !a.GetOwnedTitle().Contains(title.data.id))
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

    public static void UpgradeOfficial(this Actor a, bool merit = false, int direct = -1)
    {
        if (GetOrCreate(a).officeIdentity==null)
        {
            return;
        } else
        {
            OfficeIdentity identity = GetOrCreate(a).officeIdentity;
            if (merit)
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
                if (direct != -1)
                {
                    var original = identity.honoraryOfficial;
                    if (original != direct)
                    {
                        //LogService.LogInfo("升官");
                        if (direct < 5)
                        {
                            TranslateHelper.LogOfficeMove(a, identity.peerageType, direct);
                        }
                    }
                    identity.honoraryOfficial = direct;
                }
                else
                {
                    if (identity.honoraryOfficial <= 0)
                    {
                        identity.honoraryOfficial = 0;
                    }
                    else
                    {
                        identity.honoraryOfficial -= 1;
                    }
                }
                a.data.renown += 5;
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
            // LogService.LogInfo("贬官");
            if(a.data.renown>=5)
            {
                a.data.renown -= 5;
            } else
            {
                a.data.renown = 0;
            }
            GetOrCreate(a).officeIdentity = identity;
        }
    }

    public static string GetActorName(this Actor a)
    {
        if (a == null) return null;
        if (string.IsNullOrEmpty(a.name)) return null;
        string[] nameParts = a.name.Split('\u200A');

        if (ConfigData.speciesCulturePair.TryGetValue(a.asset.id, out var culture))
        {
            if (OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting))
            {
                if (nameParts.Length - 1 >= setting.Unit.name_pos)
                {
                    if (setting.Unit.is_invert)
                    {
                        return nameParts[setting.Unit.name_pos].Split(' ').Last();
                    } else
                    {
                        return nameParts[setting.Unit.name_pos].Split(' ').First();
                    }
                }
            }
        }
        return nameParts[0].Split(' ').Last();
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
    public static bool CanTakeCity(this Actor pActor)
    {
        Kingdom kingdom = pActor.kingdom;
        if (kingdom.isRekt()) return false;
        Empire empire = kingdom.GetEmpire();
        if (empire.isRekt()) return false;
        foreach (City city in kingdom.cities)
        {
            if (city.isRekt()) continue;
            if (city.neighbours_cities.Count > 0)
            {
                foreach (City city2 in city.neighbours_cities)
                {
                    if (city2.kingdom.IsInEmpire() && city2.kingdom != kingdom && city2.kingdom.IsEmpire() && !city2.isCapitalCity())
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }
    public static string GetTitle(this Actor a)
    {
        if (!a.HasTitle()) return "";
        if (!a.isKing()) return "";
        if (a.kingdom == null) return "";
        var ownedTitles = a.GetOwnedTitle();
        if (ownedTitles == null) return "";
        KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.get(ownedTitles.First());
        return title?.data?.name??"";
    }
    public static KingdomTitle GetMainTitle(this Actor a)
    {
        var ownedTitles = a.GetOwnedTitle();
        if (ownedTitles == null) return null;
        if (ownedTitles.Count <= 0) return null;
        KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.get(ownedTitles.First());
        return title;
    }
    public static bool HasTitle(this Actor a)
    {
        if(a == null) return false;
        if(GetOrCreate(a).owned_title==null) return false;
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
        if (a.kingdom.GetEmpire().Emperor.GetOwnedTitle()==null) return false;
        if (a.kingdom.capital.GetTitle()==null) return false;
        if (a.kingdom.IsInEmpire())
        {
            if (a.kingdom.capital.hasTitle())
            {
                return a.kingdom.GetEmpire().Emperor.GetOwnedTitle().Contains(a.kingdom.capital.GetTitle().data.id);
            }
        }
        return false;
    }


    public static bool canTakeTitle(this Actor a)
    {
        if (!a.isKing()) return false;
        Kingdom kingdom = a.kingdom;
        if (kingdom == null) return false;
        List<long> controlledTitles = kingdom.GetControlledTitle().FindAll(t=>!t.owner.IsEmperor()).Select(t=>t.data.id).ToList();
        var commonTitles = controlledTitles.Intersect(a.GetOwnedTitle());
        return commonTitles.Count() < controlledTitles.Count();
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
                if (Date.getYearsSince(kt.data.timestamp_been_controlled) >= ModClass.TITLE_BEEN_DESTROY_TIME && kt != a.kingdom.GetMainTitle())
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
        List<KingdomTitle> titles = kingdom.GetControlledTitle();
        foreach(KingdomTitle t in titles)
        {
            if (t.main_kingdom!=null)
            {
                t.main_kingdom.RemoveMainTitle();
                t.main_kingdom = null;
            }
            if(t.HasOwner()&&t.owner.IsEmperor())
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
                        t.owner?.removeTitle(t);
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
        {
            ed.owned_title.Add(title.data.id);
            title.owner = a;
        }
    }

    public static void removeTitle(this Actor a, KingdomTitle title)
    {
        if (title == null) return;
        if (a == null) return;
        if (title.data == null) return;
        if (a.kingdom == null) return;
        var ed = GetOrCreate(a);
        if (a.GetOwnedTitle().Contains(title.data.id))
        {
            if (!a.IsEmperor()&&a.isKing())
            {
                if (a.kingdom.GetKingdomName()==title.data.name)
                {
                    a.kingdom.data.name = a.kingdom.capital.name;
                    a.kingdom.EmpireLeave();
                }
                if (a.kingdom.GetMainTitle() == title)
                {
                    a.kingdom.RemoveMainTitle();
                }
            }
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
    public static Name GetModName(this Actor a)
    {
        var ed = GetOrCreate(a);
        if (ed == null) return null;
        if (ed.name == null)
        {
            ed.name = new Name();
        }
        return ed.name;
    }

    public static void ClearTitle(this Actor a)
    {
        var ed = GetOrCreate(a);
        ed.owned_title.Select(t=>ModClass.KINGDOM_TITLE_MANAGER.get(t)!.owner=null);
        ed.owned_title.Clear();
        ed.want_acuired_title.Clear();
    }

    public static bool IsEmperor(this Actor a)
    {
        if (a==null) return false;
        return GetOrCreate(a).empire_id!=-1L;
    }

    public static void SetEmpire(this Actor a, Empire empire)
    {
        a.GetOrCreate().empire_id = empire.data.id;
    }

    public static ActorExtraData GetOrCreate(this Actor a, bool isSave=false)
    {
        return a.GetOrCreate<Actor, ActorExtraData>(isSave); ; 
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