using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using static EmpireCraft.Scripts.GameClassExtensions.ReligionExtension;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows
{
    // 宗教的显示用字(宗教窗口、天下宗教名册、宗教图层提示框、原版宗教提示框共用)：类型、天下、地位、教阶、配色与统计
    public static class ReligionDisplay
    {
        public const string Purple = "#C9A7E8";
        private const double SecondsPerYear = 60d;
        private const int HereticWindowYears = 30;

        public static bool Alive(Religion religion) => religion?.data != null && !religion.isRekt();

        public static string FaithName(Religion religion) =>
            LM.Get("faith_type_" + ReligionFaith.Of(religion).ToString().ToLowerInvariant());

        public static string FaithName(FaithType type) => LM.Get("faith_type_" + type.ToString().ToLowerInvariant());

        // 天下：语言里有"religion_sphere_<标识>"就用，没有就取同天下的一个文化名
        public static string SphereName(Religion religion)
        {
            string id = ReligionSphere.SphereOf(religion);
            if (string.IsNullOrEmpty(id)) return "—";
            string key = "religion_sphere_" + id;
            string text = LM.Get(key);
            if (!string.IsNullOrEmpty(text) && text != key) return text;
            string culture = OnomasticsRule.ALL_CULTURE_RULE.Keys.FirstOrDefault(name => name.GetTianxiaId() == id);
            return culture != null ? culture.GetCultureTranslate() : id;
        }

        public static Religion Parent(Religion religion)
        {
            long id = religion?.data == null ? -1L : religion.GetOrCreate().ParentReligionId;
            Religion parent = id >= 0 ? World.world.religions.get(id) : null;
            return Alive(parent) && parent != religion ? parent : null;
        }

        public static List<Religion> Children(Religion religion) =>
            !Alive(religion) ? new List<Religion>()
                : World.world.religions.Where(other => Alive(other) && other != religion &&
                                                        other.GetOrCreate().ParentReligionId == religion.data.id).ToList();

        // 地位：分出的教会(分自某教)、正教、地方教会、本土
        public static (string text, string hex) Standing(Religion religion)
        {
            Religion parent = Parent(religion);
            if (religion.GetOrCreate().ParentReligionId >= 0)
                return (string.Format(LM.Get("religion_status_split"), parent?.name ?? LM.Get("label_none")), Purple);
            if (ReligionSphere.IsLocalChurch(religion)) return (LM.Get("religion_status_local"), WideKit.Orange);
            if (ReligionSphere.OrthodoxOf(ReligionSphere.SphereOf(religion)) == religion)
                return (LM.Get("religion_status_orthodox"), WideKit.Green);
            return (LM.Get("religion_status_native"), WideKit.Soft);
        }

        public static string HierarchyName(Religion religion) => ReligionFaith.IsPapal(religion)
            ? LM.Get("religion_hierarchy_papacy")
            : ReligionFaith.IsCaliphal(religion) ? LM.Get("religion_hierarchy_caliphate") : LM.Get("religion_hierarchy_none");

        // 教首的称呼：皇帝兼管的教会称皇帝，普世教会称教宗，伊斯兰称哈里发
        public static string HeadTitle(Religion religion) => religion?.data != null && religion.GetOrCreate().Caesaropapist
            ? LM.Get("religion_head_emperor")
            : ReligionFaith.IsPapal(religion) ? LM.Get("papacy_pope")
            : ReligionFaith.IsCaliphal(religion) ? LM.Get("religion_head_caliph") : LM.Get("label_none");

        public static string HolyWarName(Religion religion) => ReligionFaith.Of(religion) switch
        {
            FaithType.Church => LM.Get("religion_holywar_crusade"),
            FaithType.Islam => LM.Get("religion_holywar_jihad"),
            _ => LM.Get("label_none")
        };

        public static string MissionName(Religion religion)
        {
            float value = ReligionFaith.Mission(religion);
            return LM.Get(value >= 0.9f ? "religion_level_strong" : value >= 0.5f ? "religion_level_medium" : "religion_level_weak");
        }

        public static string InertiaName(Religion religion)
        {
            float value = ReligionFaith.Inertia(religion);
            return LM.Get(value >= 1.5f ? "religion_level_hard" : value >= 0.9f ? "religion_level_medium" : "religion_level_easy");
        }

        public static string ExclusiveName(Religion religion) =>
            LM.Get(ReligionFaith.IsExclusive(religion) ? "religion_exclusive_yes" : "religion_exclusive_no");

        public static Color MainColor(Religion religion) => religion?.getColor()?.getColorMain() ?? Color.gray;

        public static string TextHex(Religion religion)
        {
            string hex = religion?.getColor()?.color_text;
            return string.IsNullOrEmpty(hex) ? WideKit.Value : hex.StartsWith("#") ? hex : "#" + hex;
        }

        // 分出的缘由：对立一线分立、国教会(见 PapacySystem.Antipope、Reformation)；皇帝兼管 → 东西大分裂；伊斯兰 → 争位分出；其余 → 宗教改革
        public static string SplitCause(Religion religion)
        {
            int origin = PapacySystem.SplitOrigin(religion)?.kind ?? 0;
            if (origin == PapacySystem.OriginRivalLine) return LM.Get("religion_split_antipope");
            if (origin == PapacySystem.OriginNational) return LM.Get("religion_split_national");
            if (religion.GetOrCreate().Caesaropapist) return LM.Get("religion_split_great_schism");
            // 哈里发立教时记了本天下别教的正教作母宗教(见 CaliphateFaith)，不是争位分出来的
            if (ReligionFaith.IsCaliphal(religion) && Parent(religion) is Religion parent && !ReligionFaith.IsCaliphal(parent))
                return LM.Get("religion_split_caliphate");
            return LM.Get(ReligionFaith.IsCaliphal(religion) ? "religion_split_succession" : "religion_split_reformation");
        }

        // 母教还能讨伐这支(分出 30 年内、不在"教随国定"期间)
        public static bool Heretic(Religion child, out string until)
        {
            until = "";
            Religion parent = Parent(child);
            var data = child.GetOrCreate();
            if (parent == null || data.SplitAt < 0d) return false;
            double end = data.SplitAt + HereticWindowYears * SecondsPerYear;
            double now = World.world.getCurWorldTime();
            until = HistoryDateFormatter.GetYear(end);
            return now < end && now >= parent.GetOrCreate().ReligiousPeaceUntil;
        }

        // 分出的教会的发端：类别 · 发起人 · 发端之城(旧档、剧本带的没有)
        public static string OriginText(Religion religion)
        {
            SplitOriginData origin = PapacySystem.SplitOrigin(religion);
            return origin == null ? "" : string.Format(LM.Get("religion_origin_value"), LM.Get("religion_origin_" + origin.kind),
                origin.founder_name, origin.city_name);
        }

        // 宗教改革分出的一支：发端、年份、城数、奉为国教的国数
        public static string ReformationDetail(Religion church)
        {
            int cities = church.cities?.Count ?? 0;
            int states = World.world.kingdoms.list.Count(kingdom => kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild &&
                                                                    kingdom.religion == church);
            SplitOriginData origin = PapacySystem.SplitOrigin(church);
            return origin == null
                ? string.Format(LM.Get("religion_reformation_legacy"), cities, states)
                : string.Format(LM.Get("religion_reformation_detail"), origin.founder_name, origin.city_name,
                    HistoryDateFormatter.GetYear(origin.at), cities, states);
        }

        // 大分裂的起因、收场
        public static string SchismCause(PapalSchismData schism) => LM.Get("antipope_cause_" + schism.cause);

        public static string SchismOutcome(PapalSchismData schism) =>
            string.Format(LM.Get("religion_schism_end_" + schism.outcome), schism.outcome_place);

        // 一国(帝国按中央)奉哪位教宗：大分裂中才有，否则空串
        public static string ObedienceTag(Kingdom kingdom)
        {
            int obedience = PapacySystem.PapalObedience(kingdom);
            return obedience == PapacySystem.PapalObedienceNone ? "" : LM.Get("religion_obedience_tag_" + obedience);
        }

        // 国名列表：至多 count 个，多的记"等 N 国"
        public static string RealmNames(List<Kingdom> kingdoms, int count = 8) =>
            kingdoms.Count == 0 ? LM.Get("label_none")
                : string.Join("、", kingdoms.Take(count).Select(kingdom => kingdom.GetKingdomName())) +
                  (kingdoms.Count > count ? string.Format(LM.Get("religion_realms_more"), kingdoms.Count) : "");

        public static string KindName(int kind) => LM.Get("religion_event_kind_" + kind);

        public static string KindHex(int kind) => kind switch
        {
            ReligionChronicle.KindPapacy => WideKit.Gold,
            ReligionChronicle.KindHolyWar => WideKit.Red,
            ReligionChronicle.KindSchism => Purple,
            ReligionChronicle.KindConversion => WideKit.Cyan,
            ReligionChronicle.KindSyncretism => WideKit.Orange,
            _ => WideKit.Green
        };

        #region 教义(见 ReligionTenets、SyncretismSystem，用户 2026-11-06)

        public static string TenetName(string id, string sphere) => SyncretismSystem.TenetName(id, sphere);

        // 一条教义的效果(k：本有 1，融来的按六成)；没有数值效果的(教理、本有时只作显示的经典教阶)写说明
        public static string TenetDetail(string id, string sphere, float k, bool innate)
        {
            ReligionTenetDef def = ReligionTenets.Def(id);
            if (def == null) return "";
            var parts = new List<string>();
            if (!innate || def.innate_effects)
            {
                void Fx(float value, string key)
                {
                    if (Mathf.Abs(value) > 0.0001f) parts.Add(string.Format(LM.Get(key), value * k));
                }

                Fx(def.mission, "tenet_fx_mission");
                Fx(def.inertia, "tenet_fx_inertia");
                Fx(def.home_inertia, "tenet_fx_home_inertia");
                if (def.saint_inertia > 0f)
                    parts.Add(string.Format(LM.Get("tenet_fx_saint"), def.saint_inertia * k, SyncretismSystem.SphereLabel(sphere)));
                if (def.resist > 0f) parts.Add(string.Format(LM.Get("tenet_fx_resist"), 1f - Mathf.Clamp01(def.resist * k)));
                Fx(def.defense, "tenet_fx_defense");
                Fx(def.temple_research, "tenet_fx_temple");
                Fx(def.holy_city, "tenet_fx_holy");
                Fx(def.concord, "tenet_fx_concord");
                if (innate)
                {
                    Fx(def.openness, "tenet_fx_openness");
                    if (def.slots != 0) parts.Add(string.Format(LM.Get("tenet_fx_slots"), def.slots));
                    if (def.capture_immune) parts.Add(LM.Get("tenet_fx_capture"));
                }
                if (def.legitimacy != 0)
                {
                    int value = Mathf.RoundToInt(def.legitimacy * k);
                    parts.Add(def.legitimacy_share > 0f
                        ? string.Format(LM.Get("tenet_fx_legitimacy_share"), value, def.legitimacy_share)
                        : string.Format(LM.Get("tenet_fx_legitimacy"), value));
                }
                if (def.organizes && !innate) parts.Add(LM.Get("tenet_fx_organizes"));
            }
            return parts.Count > 0 ? string.Join(" · ", parts) : LM.Get("tenet_" + id + "_desc");
        }

        // 融来的一条：效果 · 融自某教(某年)
        public static string AbsorbedDetail(SyncreticTenetData entry)
        {
            string effect = TenetDetail(entry.tenet, entry.sphere, ReligionTenets.Syncretism.absorbed_strength, false);
            return string.Format(LM.Get("religion_tenet_from"), effect, entry.from_name, HistoryDateFormatter.GetYear(entry.at));
        }

        // 本土加成一览：本天下、本土惰性倍数、外天下传教的折扣、升格
        public static string HomeSummary(Religion religion)
        {
            string sphere = ReligionSphere.SphereOf(religion);
            string text = string.Format(LM.Get("religion_home_summary"), SphereName(religion), ReligionTenets.HomeFactor(religion, sphere),
                ReligionFaith.IsUniversal(religion) ? ReligionSphere.CrossUniversalFactor : ReligionSphere.CrossEthnicFactor);
            if (ReligionTenets.IsOrganized(religion)) text += " · " + LM.Get("religion_tenet_organized");
            Religion revealedOf = NativeFaithSystem.RevealedOf(sphere);
            if (revealedOf == religion) text += " · " + LM.Get("religion_tenet_revealed");
            return text;
        }

        #endregion

        public static string HeadCaption(Religion religion, Actor head)
        {
            if (head == null) return HierarchyName(religion);
            Kingdom see = PapacySystem.Papacy(religion);
            double since = see?.data?.timestamp_king_rule ?? -1d;
            int reign = since >= 0d ? Date.getYearsSince(since) + 1 : 1;
            return string.Format(LM.Get("religion_head_caption"), HeadTitle(religion), reign);
        }

        // 一个宗教的统计：奉它的城、信众(城人口)、奉为国教的国家
        public readonly struct Stat
        {
            public readonly int Cities, States;
            public readonly float People;

            public Stat(int cities, float people, int states)
            {
                Cities = cities;
                People = people;
                States = states;
            }
        }

        public static Dictionary<Religion, Stat> Stats(out float worldPeople)
        {
            var cities = new Dictionary<Religion, int>();
            var people = new Dictionary<Religion, float>();
            var states = new Dictionary<Religion, int>();
            worldPeople = 0f;
            foreach (City city in World.world.cities.list)
            {
                if (city?.data == null || city.isRekt()) continue;
                float count = city.CountLivingPopulation();
                worldPeople += count;
                if (!city.hasReligion()) continue;
                cities.TryGetValue(city.religion, out int c);
                cities[city.religion] = c + 1;
                people.TryGetValue(city.religion, out float p);
                people[city.religion] = p + count;
            }
            foreach (Kingdom kingdom in World.world.kingdoms.list)
            {
                if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild || !kingdom.hasReligion()) continue;
                states.TryGetValue(kingdom.religion, out int s);
                states[kingdom.religion] = s + 1;
            }
            var result = new Dictionary<Religion, Stat>();
            foreach (Religion religion in World.world.religions)
            {
                if (!Alive(religion)) continue;
                cities.TryGetValue(religion, out int c);
                people.TryGetValue(religion, out float p);
                states.TryGetValue(religion, out int s);
                if (c == 0 && s == 0) continue;
                result[religion] = new Stat(c, p, s);
            }
            return result;
        }
    }
}
