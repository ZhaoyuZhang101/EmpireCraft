using EmpireCraft.Scripts.AI.KingdomAI;
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
using NeoModLoader.services;
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
        public static string GetLocal(this string key)
        {
            return LM.Get(key);
        }
        public static string AppendWithNarrowSpace(this string textA, string textB)
        {
            return JoinNameParts(textA, textB);
        }

        public static string NamePartSeparator
        {
            get
            {
                // English uses a paired narrow-space separator for compatibility with
                // the mod's existing name parsers; CJK names remain compact.
                return IsEnglishLanguage()
                    ? ModClass.NARROW_SPACE + ModClass.NARROW_SPACE
                    : ModClass.NARROW_SPACE;
            }
        }

        public static bool IsEnglishLanguage()
        {
            string language = PlayerConfig.dict["language"].stringVal;
            return !string.IsNullOrWhiteSpace(language) &&
                   language == "en";
        }

        public static string JoinNameParts(params string[] parts)
        {
            if (parts == null || parts.Length == 0) return "";
            return string.Join(NamePartSeparator, parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        public static string[] SplitNameParts(this string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
            return text.Split(new[] { '\u200A' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => part.Length > 0)
                .ToArray();
        }

        public static string ReduceNarrowSpaces(this string text)
        {
            return text.UseLocalizedNameSeparator();
        }

        public static string UseLocalizedNameSeparator(this string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string[] parts = text.SplitNameParts();
            return parts.Length <= 1 ? text.Trim() : JoinNameParts(parts);
        }

        public static bool UsesEnglishCountryTypePrefix(string cultureName)
        {
            if (!IsEnglishLanguage()) return false;
            if (!string.IsNullOrWhiteSpace(cultureName) &&
                OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(cultureName, out Setting setting) &&
                setting?.Kingdom != null)
            {
                return setting.Kingdom.english_type_prefix;
            }

            // New cultures and species should follow English grammar unless their
            // culture configuration explicitly opts into suffix-style names.
            return true;
        }

        public static string FormatCountryTypeName(string coreName, string typeName, string cultureName)
        {
            coreName = coreName.UseLocalizedNameSeparator();
            if (string.IsNullOrWhiteSpace(typeName)) return coreName;
            return UsesEnglishCountryTypePrefix(cultureName)
                ? JoinNameParts("The", typeName, "of", coreName)
                : JoinNameParts(coreName, typeName);
        }

        public static bool TryExtractEnglishPrefixedCountryName(string fullName, out string coreName)
        {
            coreName = "";
            if (string.IsNullOrWhiteSpace(fullName)) return false;
            Match match = Regex.Match(fullName.Trim(), @"^The\s+(.+?)\s+of\s+(.+)$", RegexOptions.IgnoreCase);
            if (!match.Success) return false;
            coreName = match.Groups[2].Value.Trim();
            return !string.IsNullOrWhiteSpace(coreName);
        }

        public static string FormatKingdomFullName(string coreName, string typeName, string cultureName)
        {
            if (string.IsNullOrWhiteSpace(coreName)) return "";
            if (string.IsNullOrWhiteSpace(typeName)) return coreName;
            string result;
            if (UsesEnglishCountryTypePrefix(cultureName))
            {
                result = JoinNameParts("The", typeName, "of", coreName);
            }
            else
            {
                result = JoinNameParts(coreName, typeName);
            }
            return result.ReduceNarrowSpaces();
        }

        public static string FormatEmpireFullName(string coreName, string typeName, string localizedPrefix,
            string cultureName)
        {
            if (string.IsNullOrWhiteSpace(coreName)) return "";
            if (UsesEnglishCountryTypePrefix(cultureName) && !string.IsNullOrWhiteSpace(typeName))
            {
                return JoinNameParts("The", localizedPrefix, typeName, "of", coreName);
            }
            return JoinNameParts(localizedPrefix, coreName, typeName);
        }

        public static string FormatCityFullName(string coreName, string typeName)
        {
            string result = coreName ?? "";
            if (!string.IsNullOrWhiteSpace(typeName))
            {
                result = JoinNameParts(result, typeName);
            }
            return result.ReduceNarrowSpaces();
        }

        public static string StripLocalizedTypeSuffix(string fullName, string typeName)
        {
            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(typeName)) return fullName?.Trim() ?? "";
            string value = fullName.Trim();
            string[] separators = { ModClass.NARROW_SPACE + ModClass.NARROW_SPACE, ModClass.NARROW_SPACE, " " };
            foreach (string separator in separators)
            {
                string suffix = separator + typeName;
                if (value.EndsWith(suffix, StringComparison.Ordinal))
                    return value.Substring(0, value.Length - suffix.Length).Trim();
            }
            return string.Equals(value, typeName, StringComparison.Ordinal) ? "" : value;
        }

        public static string ResolveEmpireTypeKey(Regime regime, Kingdom coreKingdom)
        {
            string key = null;
            if (regime != null && regime.centre_empire_separate)
            {
                key = $"{regime.type}_empire";
            }
            else if (coreKingdom != null && !coreKingdom.isRekt())
            {
                key = EmpireCraftKingdomBehCheckKingdomType.CalcKingdomType(coreKingdom).ToString();
            }

            if (string.IsNullOrWhiteSpace(key)) return "EmpireText";
            string translated = LM.Get(key);
            if (string.IsNullOrWhiteSpace(translated) || string.Equals(translated, key, StringComparison.Ordinal))
                return "EmpireText";
            return key;
        }

        private static readonly Dictionary<string, string> DirectPrefixKeyMap = new()
        {
            { "神圣", "empire_prefix_holy" },
            { "神聖", "empire_prefix_holy" },
            { "Holy", "empire_prefix_holy" },
            { "大", "great" },
            { "Great", "great" },
            { "西", "Western" },
            { "West", "Western" },
            { "东", "Eastern" },
            { "東", "Eastern" },
            { "East", "Eastern" },
            { "南", "Southern" },
            { "South", "Southern" },
            { "北", "Northern" },
            { "North", "Northern" },
            { "后", "Later" },
            { "後", "Later" },
            { "Later", "Later" },
        };

        public static string LocalizeDirectPrefix(EmpireData data)
        {
            if (data == null || string.IsNullOrWhiteSpace(data.directPre)) return "";
            string raw = data.directPre.Trim();
            string key = DirectPrefixKeyMap.TryGetValue(raw, out string mapped) ? mapped : raw;
            string translated = LM.Get(key);
            if (!string.IsNullOrWhiteSpace(translated) && !string.Equals(translated, key, StringComparison.Ordinal))
            {
                return translated;
            }
            return IsEnglishLanguage() ? "" : raw;
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
                case OfficerPowerType.人事:
                    if (officer != null)
                    {
                        additions.addition[OfficerPowerType.人事] = officer.stewardship;
                    }
                    else
                    {
                        additions.addition[OfficerPowerType.人事] = 0;
                    }
                    break;
                case OfficerPowerType.生育:
                    if (officer != null)
                    {
                        additions.addition[OfficerPowerType.生育] = officer.getChildren(false).ToList().FindAll(a=>a.getParents().Any(p=>p.IsEmperor())).Count;
                    }
                    else
                    {
                        additions.addition[OfficerPowerType.生育] = 0;
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

        public static bool HasChangeToGiveBirth(Actor pMotherTarget, Actor pFatherTarget)
        {
            int mNum = (int)pMotherTarget.stats["birth_rate"];
            int fNum = (int)pFatherTarget.stats["birth_rate"];
            float num2 = 0.5f;
            for (int i = 0; i < (mNum+fNum)/2; i++)
            {
                if (!Randy.randomChance(num2))
                {
                    return true;
                };
                num2 *= 0.85f;
            }
            return false;
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
