using EmpireCraft.Scripts.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.HelperFunc
{
    public static class OverallHelperFunc
    {
        public static bool IsEmpireLayerOn()
        {
            return PlayerConfig.dict["map_empire_layer"].boolVal;
        }

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
                    string provinceName = actor.city.hasProvince() ? actor.city.GetProvince()?.name : "";
                    string officer = actor.isOfficer() ? "officer" + LM.Get("actor_officer") : "";
                    string name = "";
                    int age = -1;
                    string educationLevel;
                    OfficeIdentity identity = actor.GetIdentity(ConfigData.CURRENT_SELECTED_EMPIRE);
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
                        merit, honoraryOfficial, officialLevel, PeeragesLevel, name, age.ToString(), educationLevel, kingdomName, cityName, provinceName, officer
                    };
                    bool isSatisfied = searchContent.ToList().Any(t =>t.Contains(content))||(int.TryParse(content, out int num)?num>=age:false);
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
                string name = cIdentity.Item2.name;
                string educationLevel = "";
                if (cIdentity.Item2.is_alive)
                {
                    Actor actor = cIdentity.Item2._actor;
                    OfficeIdentity identity = actor.GetIdentity(actor.city.GetEmpire());
                    kingdomName = actor.kingdom.name;
                    cityName = actor.city.name;
                    provinceName = actor.city.hasProvince() ? actor.city.GetProvince()?.name : "";
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
                    provinceName = cIdentity.Item2.provinceName;
                    educationLevel = cIdentity.Item2.educationLevel + string.Join("/", cIdentity.Item2.educationLevel.Split('/').Select(c=>LM.Get(c)));;
                }
                List<string> searchContent = new List<string>()
                {
                    merit, honoraryOfficial, officialLevel, PeeragesLevel, name, educationLevel, kingdomName, cityName, provinceName, officer
                };
                bool isSatisfied = searchContent.ToList().Any(t =>t.Contains(content));
                if (isSatisfied) identityPool.Add(cIdentity);
            }
            return identityPool;
        }
    }
    
}
