using EmpireCraft.Scripts.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.HelperFunc
{
    public static class OverallHelperFunc
    {

        public static class IdGenerator
        {
            private static long _lastId = DateTime.UtcNow.Ticks;

            public static long NextId()
            {
                return Interlocked.Increment(ref _lastId);
            }
        }
        public static string GetCultureFromSpecies(string species)
        {
            if (ConfigData.speciesCulturePair.TryGetValue(species, out var insertCulture))
            {
                return insertCulture;
            }
            else
            {
                return "Western";
            }
        }
        public static EmpireAddition CalcPower(this Actor officer, OfficerPowerType type, Empire empire)
        {
            EmpireAddition additions = new();
            switch (type)
            {
                case OfficerPowerType.审核:
                    if (!officer.isRekt())
                    {
                        if (officer.IsSameFactionWithEmpire(empire))
                        {
                            additions.addition[OfficerPowerType.审核] = officer.intelligence*2;
                        }
                        else
                        {
                            if (officer.GetFaction() == null)
                            {
                                additions.addition[OfficerPowerType.审核] = officer.intelligence;
                            }
                            else
                            {
                                additions.addition[OfficerPowerType.审核] = -officer.intelligence;
                            }
                        }
                    }
                    else
                    {
                        additions.addition[OfficerPowerType.审核] = 0;
                    }
                    break;
                case OfficerPowerType.军事:
                    if (officer != null)
                    {
                        additions.addition[OfficerPowerType.军事] = officer.warfare;
                    }
                    else
                    {
                        additions.addition[OfficerPowerType.军事] = 0;
                    }
                    break;
                case OfficerPowerType.建设:
                    if (officer != null)
                    {
                        additions.addition[OfficerPowerType.建设] = officer.stewardship;
                    }
                    else
                    {
                        additions.addition[OfficerPowerType.建设] = 0;
                    }
                    break;
                case OfficerPowerType.教育:
                    if (officer != null)
                    {
                        additions.addition[OfficerPowerType.教育] = officer.intelligence;
                    }
                    else
                    {
                        additions.addition[OfficerPowerType.教育] = 0;
                    }
                    break;
                case OfficerPowerType.天子护理:
                    if (officer != null)
                    {
                        additions.addition[OfficerPowerType.天子护理] = officer.level*5;
                    }
                    else
                    {
                        additions.addition[OfficerPowerType.天子护理] = 0;
                    }
                    break;
                case OfficerPowerType.天子政教:
                    if (officer != null)
                    {
                        additions.addition[OfficerPowerType.天子政教] = officer.level*5;
                    }
                    else
                    {
                        additions.addition[OfficerPowerType.天子政教] = 0;
                    }
                    break;
                case OfficerPowerType.天子智教:
                    if (officer != null)
                    {
                        additions.addition[OfficerPowerType.天子智教] = officer.intelligence;
                    }
                    else
                    {
                        additions.addition[OfficerPowerType.天子智教] = 0;
                    }
                    break;
                case OfficerPowerType.宗教:
                    if (officer != null)
                    {
                        additions.addition[OfficerPowerType.宗教] = officer.intelligence;
                    }
                    else
                    {
                        additions.addition[OfficerPowerType.宗教] = 0;
                    }
                    break;
                case OfficerPowerType.礼仪:
                    if (officer != null)
                    {
                        additions.addition[OfficerPowerType.礼仪] = officer.stewardship;
                    }
                    else
                    {
                        additions.addition[OfficerPowerType.礼仪] = 0;
                    }
                    break;
                case OfficerPowerType.财政:
                    if (officer != null)
                    {
                        additions.addition[OfficerPowerType.财政] = officer.stewardship;
                    }
                    else
                    {
                        additions.addition[OfficerPowerType.财政] = 0;
                    }
                    break;
                }
            return additions;
        }
        public static bool IsSameFactionWithEmpire(this Actor pActor, Empire empire)
        {
            var dominate = empire.CoreKingdom.GetRegime().GetDominateFaction();
            if (pActor != null)
            {
                if (pActor.GetFaction() == dominate)
                {
                    return true;
                }
            }
            return false;
        }
        public static void SetFamilyCityPre(this Family family, bool has_pre = true)
        {
            if (family.data.custom_data_bool == null)
            {
                family.data.custom_data_bool = new CustomDataContainer<bool>();
            }
            family.data.custom_data_bool.dict["has_city_pre"] = has_pre;
        }
        public static bool HasBeenSetBefored(this Family family)
        {
            if (family.data.custom_data_bool == null)
            {
                family.data.custom_data_bool = new CustomDataContainer<bool>();
            }
            return family.data.custom_data_bool.dict.ContainsKey("has_city_pre");
        }
        public static  List<Actor> SearchUnitHelper(string content, List<Actor> actors)
        {
            List<Actor> actorsPool = new List<Actor>();
            if (content == "")
            {
                return actorsPool;
            }
            foreach (Actor actor in actors)
            {
                if (actor.isUnitFitToRule())
                {
                    string culture = ConfigData.speciesCulturePair.TryGetValue(actor.asset.id, out string culturePair)? culturePair:"Western";
                    string merit = "";
                    string honoraryOfficial = "";
                    string PeeragesLevel = "";
                    string officialLevel = "";
                    string kingdomName = actor.kingdom.name;
                    string cityName = actor.city.name;
                    string officer = actor.isOfficer() ? "officer" + LM.Get("actor_officer") : "";
                    string name = "";
                    int age = -1;
                    string educationLevel;
                    OfficeIdentity identity = actor.GetIdentity();
                    if (identity!=null)
                    {
                        merit = string.Join("_", culture, "meritlevel", identity.peerageType.ToString(), identity.meritLevel);
                        merit += LM.Get(merit);
                        honoraryOfficial = string.Join("_", culture, "honoraryofficial", identity.peerageType.ToString(), identity.honoraryOfficial);
                        honoraryOfficial += LM.Get(honoraryOfficial);
                        officialLevel = string.Join("_", culture, identity.officialLevel.ToString());
                        officialLevel += LM.Get(officialLevel);
                    }
                    educationLevel = (actor.hasTrait("jingshi") ? "trait_jingshi" : "") +"/" +(actor.hasTrait("gongshi") ? "trait_gongshi" : "") +"/"+ (actor.hasTrait("juren")?"trait_juren":"");
                    educationLevel += string.Join("/", educationLevel.Split('/').Select(c=>LM.Get(c)));
                    PeeragesLevel = string.Join("_", culture, actor.GetPeeragesLevel().ToString());
                    PeeragesLevel += LM.Get(PeeragesLevel);
                    name = actor.getName();
                    age = actor.getAge();
                    List<string> searchContent = new List<string>()
                    {
                        merit, honoraryOfficial, officialLevel, PeeragesLevel, name, age.ToString(), educationLevel, kingdomName, cityName, officer
                    };
                    bool isSatisfied = searchContent.ToList().Any(t =>t.Contains(content))||(int.TryParse(content, out int num) && num>=age);
                    if (isSatisfied) actorsPool.Add(actor);
                }
            }
            return actorsPool;
        }
    public static List<(ClanRelation, PersonalClanIdentity)> SearchPersonalClanIdentityHelper(string content, List<(ClanRelation, PersonalClanIdentity)> cIdentities)
        {
            List<(ClanRelation, PersonalClanIdentity)> identityPool = new List<(ClanRelation, PersonalClanIdentity)>();
            if (content == "")
            {
                return cIdentities;
            }
            foreach (var cIdentity in cIdentities)
            {
                string culture = ConfigData.speciesCulturePair.TryGetValue(cIdentity.Item2.species, out string culturePair)? culturePair:"Western";
                string merit = "";
                string honoraryOfficial = "";
                string PeeragesLevel = "";
                string officialLevel = "";
                string kingdomName = "";
                string cityName = "";
                string provinceName = "";
                string officer = "";
                string officeName = "";
                string name = cIdentity.Item2.name;
                string educationLevel = "";
                if (cIdentity.Item2.is_alive)
                {
                    Actor actor = cIdentity.Item2._actor;
                    OfficeIdentity identity = actor.GetIdentity();
                    kingdomName = actor.kingdom.name;
                    cityName = actor.city.name;
                    officer = actor.isOfficer() ? "officer" + LM.Get("actor_officer") : "";
                    if (identity!=null)
                    {
                        merit = string.Join("_", culture, "meritlevel", identity.peerageType.ToString(), identity.meritLevel);
                        merit += LM.Get(merit);
                        honoraryOfficial = string.Join("_", culture, "honoraryofficial", identity.peerageType.ToString(), identity.honoraryOfficial);
                        honoraryOfficial += LM.Get(honoraryOfficial);
                        officialLevel = string.Join("_", culture, identity.officialLevel.ToString());
                        officialLevel += LM.Get(officialLevel);
                    }
                    educationLevel = (actor.hasTrait("jingshi") ? "trait_jingshi" : "") +"/" +(actor.hasTrait("gongshi") ? "trait_gongshi" : "") +"/"+ (actor.hasTrait("juren")?"trait_juren":"");
                    educationLevel += string.Join("/", educationLevel.Split('/').Select(c=>LM.Get(c)));
                    PeeragesLevel = string.Join("_", culture, actor.GetPeeragesLevel().ToString());
                    officeName = actor.GetOffice()?.GetOfficeName()??"";
                    PeeragesLevel += LM.Get(PeeragesLevel);
                }
                else
                {
                    merit = cIdentity.Item2.merit + LM.Get(cIdentity.Item2.merit);
                    honoraryOfficial = cIdentity.Item2.honoraryOfficial + LM.Get(cIdentity.Item2.honoraryOfficial);
                    PeeragesLevel = cIdentity.Item2.PeeragesLevel + LM.Get(cIdentity.Item2.PeeragesLevel);
                    officialLevel = cIdentity.Item2.officialLevel + LM.Get(cIdentity.Item2.officialLevel);
                    kingdomName = cIdentity.Item2.kingdomName;
                    cityName = cIdentity.Item2.cityName;
                    officeName = cIdentity.Item2.officeName;
                    educationLevel = cIdentity.Item2.educationLevel + string.Join("/", cIdentity.Item2.educationLevel.Split('/').Select(c=>LM.Get(c)));;
                }
                List<string> searchContent = new List<string>()
                {
                    merit, honoraryOfficial, officialLevel, PeeragesLevel, name, educationLevel, kingdomName, cityName, provinceName, officer, officeName
                };
                bool isSatisfied = searchContent.ToList().Any(t =>t.Contains(content));
                if (isSatisfied) identityPool.Add(cIdentity);
            }
            return identityPool;
        }
    }
    
}
