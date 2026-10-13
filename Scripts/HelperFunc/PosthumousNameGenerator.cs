using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Scripts.HelperFunc
{
    // 谥法：按先帝在位生平(ReignRecordSystem 记在 EmpireCraftHistory 里的统计)定庙号、谥号，大体依历代惯例：
    //   开国之君称"祖"(拓土开疆者高祖，否则太祖)，谥武或文；非开国而疆域倍增者称"世祖"；
    //   二世有为者称"太宗"；中兴之主称中宗/宪宗/世宗，谥宣、光；
    //   武功：武、桓(辟土服远)、威、烈、庄；治世久安：仁、文、景、康、孝、明；
    //   守成平庸：成、穆、懿、宁、顺……谥恭、惠、安、简；
    //   丧师失地、叛乱迭起：谥灵、幽、厉、炀、荒(恶谥)；
    //   失位、早夭、享国日浅：不立庙号，谥哀、愍、殇、冲、悼、怀；
    //   末代之君：不立庙号，逊位谥恭、献，殉国或亡于乱谥哀、愍，在位久者谥思。
    // 旧档没有在位统计时，只按开国、二世、享国长短、享年、末代判断，其余按守成处理。
    // 用字是本地化键(rule_miaohaoprefixes_* / rule_shihao_*，见 Locales/Cultures 下的两个 csv)，同一朝代内尽量不重复。
    public static class PosthumousNameGenerator
    {
        public const string SuffixZu = "miaohaosuffixes_zu";
        public const string SuffixZong = "miaohaosuffixes_zong";

        private static readonly Dictionary<string, string> MiaoKeys = new()
        {
            ["高"] = "gao", ["太"] = "tai", ["世"] = "shi", ["中"] = "zhong", ["文"] = "wen", ["武"] = "wu",
            ["宣"] = "xuan", ["仁"] = "ren", ["圣"] = "sheng", ["康"] = "kang", ["景"] = "jing", ["昭"] = "zhao",
            ["英"] = "ying", ["神"] = "shen", ["宪"] = "xian", ["肃"] = "su", ["成"] = "cheng", ["穆"] = "mu",
            ["懿"] = "yi", ["德"] = "de", ["宁"] = "ning", ["顺"] = "shun", ["睿"] = "rui", ["定"] = "ding",
            ["庆"] = "qing", ["嘉"] = "jia", ["孝"] = "xiao", ["明"] = "ming", ["徽"] = "hui", ["哲"] = "zhe",
            ["熙"] = "xi", ["章"] = "zhang", ["和"] = "he", ["平"] = "ping", ["显"] = "xian2", ["隆"] = "long",
            ["敬"] = "jing2", ["襄"] = "xiang", ["惠"] = "hui2", ["简"] = "jian", ["安"] = "an", ["光"] = "guang",
            ["元"] = "yuan", ["贞"] = "zhen", ["永"] = "yong", ["恭"] = "gong"
        };

        private static readonly Dictionary<string, string> ShiKeys = new()
        {
            ["武"] = "wu", ["文"] = "wen", ["桓"] = "huan", ["威"] = "wei", ["烈"] = "lie", ["庄"] = "zhuang",
            ["景"] = "jing", ["康"] = "kang", ["仁"] = "ren", ["孝"] = "xiao", ["明"] = "ming", ["宣"] = "xuan",
            ["光"] = "guang", ["昭"] = "zhao", ["恭"] = "gong", ["惠"] = "hui", ["顺"] = "shun", ["安"] = "an",
            ["穆"] = "mu", ["简"] = "jian", ["懿"] = "yi", ["成"] = "cheng", ["献"] = "xian", ["末"] = "mo",
            ["哀"] = "ai", ["思"] = "si", ["愍"] = "min", ["殇"] = "shang", ["冲"] = "chong", ["悼"] = "dao",
            ["怀"] = "huai", ["炀"] = "yang", ["幽"] = "you", ["灵"] = "ling", ["厉"] = "li", ["荒"] = "huang",
            ["戾"] = "li2", ["章"] = "zhang", ["和"] = "he", ["平"] = "ping", ["襄"] = "xiang", ["定"] = "ding",
            ["贞"] = "zhen", ["敬"] = "jing2", ["德"] = "de", ["显"] = "xian2", ["元"] = "yuan", ["肃"] = "su",
            ["宁"] = "ning", ["僖"] = "xi"
        };

        // 递补用字：某一类的候选在本朝都用过了，就从同一基调里挑本朝没用过的字(一朝之内庙号、谥号尽量不重复)
        // 庙号：除"高""太"(开国、二世专用)外都可递补
        private static readonly string[] MiaoFallback = MiaoKeysExceptFounders();
        private static readonly string[] GoodShi =
            { "武", "文", "桓", "威", "烈", "庄", "景", "康", "仁", "孝", "明", "宣", "光", "昭", "章", "和", "襄", "定", "贞", "敬", "德", "显", "元", "肃" };
        private static readonly string[] SteadyShi = { "恭", "惠", "顺", "安", "穆", "简", "懿", "成", "平", "宁", "僖" };
        private static readonly string[] BadShi = { "炀", "幽", "灵", "厉", "荒", "戾" };
        private static readonly string[] SadShi = { "哀", "思", "愍", "殇", "冲", "悼", "怀", "献", "末" };

        private static string[] MiaoKeysExceptFounders() =>
            new[] { "世", "中", "文", "武", "宣", "仁", "圣", "康", "景", "昭", "英", "神", "宪", "肃", "成", "穆", "懿", "德",
                "宁", "顺", "睿", "定", "庆", "嘉", "孝", "明", "徽", "哲", "熙", "章", "和", "平", "显", "隆", "敬", "襄", "惠",
                "简", "安", "光", "元", "贞", "永", "恭" };

        // 按首选字的基调取递补字：美谥先美后平、平谥先平后美、恶谥先恶后哀、哀谥先哀后恶
        private static string[] ShiFallback(string[] chars)
        {
            string first = chars.FirstOrDefault() ?? "";
            if (BadShi.Contains(first)) return BadShi.Concat(SadShi).ToArray();
            if (SadShi.Contains(first)) return SadShi.Concat(BadShi).ToArray();
            if (SteadyShi.Contains(first)) return SteadyShi.Concat(GoodShi).ToArray();
            return GoodShi.Concat(SteadyShi).ToArray();
        }

        private static readonly Random _rng = new();

        public sealed class Result
        {
            public string miao = "";
            public string miao_suffix = "";
            public string shi = "";
            public string reason = "";
            public bool HasTemple => !string.IsNullOrEmpty(miao);
        }

        public static Result Decide(Empire empire, EmpireCraftHistory reign)
        {
            List<EmpireCraftHistory> dynasty = (empire?.data?.history ?? new List<EmpireCraftHistory>())
                .Where(other => other != null && other != reign && !other.is_republic &&
                                other.dynasty_name == reign.dynasty_name && other.royal_surname == reign.royal_surname)
                .ToList();
            var usedMiao = new HashSet<string>(dynasty.Where(other => !string.IsNullOrEmpty(other.miaohao_name))
                .Select(other => LM.Get(other.miaohao_name)));
            var usedShi = new HashSet<string>(dynasty.Where(other => !string.IsNullOrEmpty(other.shihao_name))
                .Select(other => LM.Get(other.shihao_name)));

            bool known = reign.start_cities > 0;
            double growth = known ? Math.Max(reign.max_cities, reign.end_cities) / (double)reign.start_cities : 1d;
            double kept = known && reign.end_cities >= 0 ? reign.end_cities / (double)reign.start_cities : 1d;
            int years = reign.total_time;
            bool young = reign.age_at_end >= 0 && reign.age_at_end < 20;
            bool founder = empire != null && empire.IsFoundingEmperorHistory(reign);
            bool martial = reign.wars_won >= 2 && reign.wars_won > reign.wars_lost;
            bool conqueror = known && (growth >= 2d || growth >= 1.5d && reign.wars_won >= 2);
            bool disaster = known && (kept < 0.6d || reign.rebellions_lost >= 2 ||
                                      reign.wars_lost >= 3 && reign.wars_lost > reign.wars_won);
            bool restorer = reign.restorer || known && kept >= 1.2d && reign.start_mandate >= 0 &&
                reign.start_mandate < 40 && reign.end_mandate >= reign.start_mandate + 15;
            bool prosperous = years >= 25 && kept >= 0.9d && reign.rebellions_lost == 0 && !disaster;

            var result = new Result();
            Result Temple(string suffix, string[] miao, string[] shi, string reason)
            {
                // 开国称祖的"高""太"不递补到别的字
                string[] miaoFallback = miao.Any(c => c is "高" or "太") ? null : MiaoFallback;
                result.miao = Pick("rule_miaohaoprefixes_", MiaoKeys, miao, usedMiao, miaoFallback);
                result.miao_suffix = suffix;
                result.shi = Pick("rule_shihao_", ShiKeys, shi, usedShi, ShiFallback(shi));
                result.reason = reason;
                return result;
            }
            Result NoTemple(string[] shi, string reason)
            {
                result.shi = Pick("rule_shihao_", ShiKeys, shi, usedShi, ShiFallback(shi));
                result.reason = reason;
                return result;
            }

            if (founder && !(reign.ended_alive && reign.ended_dynasty))
                return conqueror || martial
                    ? Temple(SuffixZu, new[] { "高" }, new[] { "武" }, "posthumous_reason_founder_conquer")
                    : Temple(SuffixZu, new[] { "太" }, new[] { "文", "武" }, "posthumous_reason_founder");
            if (reign.ended_dynasty)
            {
                if (reign.ended_alive) return NoTemple(new[] { "恭", "献" }, "posthumous_reason_last_abdicate");
                if (years >= 15) return NoTemple(new[] { "思" }, "posthumous_reason_last");
                return NoTemple(disaster ? new[] { "哀", "愍" } : new[] { "献", "末", "哀" }, "posthumous_reason_last");
            }
            if (young) return NoTemple(new[] { "殇", "冲" }, "posthumous_reason_young");
            if (reign.ended_alive) return NoTemple(new[] { "哀", "愍", "怀" }, "posthumous_reason_deposed");
            if (years < 2) return NoTemple(new[] { "悼", "怀" }, "posthumous_reason_short");
            // 君主资质(见 RulerQualitySystem)：暴虐、荒淫、昏聩得恶谥；庸弱而被权臣外戚挟制的得献、哀
            switch (reign.ruler_grade)
            {
                case "Tyrant":
                    return NoTemple(new[] { "厉", "炀" }, "posthumous_reason_tyrant");
                case "Dissolute":
                    return NoTemple(new[] { "炀", "荒", "灵" }, "posthumous_reason_dissolute");
                case "Fatuous":
                    return NoTemple(new[] { "灵", "幽", "荒" }, "posthumous_reason_fatuous");
                case "Weak" when reign.was_controlled:
                    return NoTemple(new[] { "献", "哀", "愍" }, "posthumous_reason_puppet");
            }
            if (restorer)
                return Temple(SuffixZong, new[] { "中", "宪", "世", "宣", "肃" }, new[] { "宣", "光", "昭", "明" },
                    "posthumous_reason_restorer");
            if (conqueror && growth >= 2d && !usedMiao.Contains(LM.Get("rule_miaohaoprefixes_shi")))
                return Temple(SuffixZu, new[] { "世" }, new[] { "武", "桓" }, "posthumous_reason_conqueror");
            if (conqueror)
                return Temple(SuffixZong, new[] { "武", "世", "英", "神" }, new[] { "武", "桓", "威", "烈" },
                    "posthumous_reason_conqueror");
            if (IsSecondOfDynasty(empire, reign) && (martial || prosperous || growth >= 1.2d))
                return Temple(SuffixZong, new[] { "太" }, prosperous ? new[] { "文" } : new[] { "武", "文" },
                    "posthumous_reason_second");
            if (disaster)
            {
                string[] shi = reign.rebellions_lost >= 2 ? new[] { "厉", "炀", "荒" } : new[] { "灵", "幽", "哀" };
                return kept < 0.4d
                    ? NoTemple(shi, "posthumous_reason_disaster")
                    : Temple(SuffixZong, new[] { "顺", "宁", "安", "穆" }, shi,
                        reign.rebellions_lost >= 2 ? "posthumous_reason_rebellion" : "posthumous_reason_disaster");
            }
            if (prosperous)
                return Temple(SuffixZong, new[] { "仁", "圣", "康", "文", "景", "昭", "熙" },
                    new[] { "仁", "文", "景", "康", "孝", "明" }, "posthumous_reason_prosperous");
            if (martial)
                return Temple(SuffixZong, new[] { "武", "英", "宣", "昭" }, new[] { "武", "烈", "威", "庄" },
                    "posthumous_reason_martial");
            return Temple(SuffixZong,
                new[] { "成", "穆", "懿", "德", "宁", "顺", "睿", "定", "庆", "嘉", "孝", "明", "徽", "哲" },
                new[] { "孝", "恭", "惠", "顺", "安", "穆", "简", "懿", "成" }, "posthumous_reason_steady");
        }

        // 封国国君的单字谥(见 FeudalPosthumousSystem)：没有帝王那样的在位统计，只看享国年数、享年、
        // 是否死于非命和封国大小。usedText 为本封国已用过的谥字
        public static (string shi, string reason) DecideVassal(int years, int age, bool killed, int cities,
            HashSet<string> usedText)
        {
            string Choose(string[] chars) => Pick("rule_shihao_", ShiKeys, chars, usedText);
            if (age >= 0 && age < 20) return (Choose(new[] { "殇", "冲", "悼" }), "posthumous_reason_young");
            if (killed) return (Choose(new[] { "烈", "愍", "庄" }), "posthumous_reason_vassal_killed");
            if (years >= 0 && years < 2) return (Choose(new[] { "悼", "怀" }), "posthumous_reason_short");
            if (cities >= 6) return (Choose(new[] { "桓", "武", "威" }), "posthumous_reason_vassal_great");
            if (years >= 30) return (Choose(new[] { "文", "景", "康", "穆", "成" }), "posthumous_reason_prosperous");
            return (Choose(new[] { "孝", "恭", "惠", "顺", "安", "简", "懿" }), "posthumous_reason_steady");
        }

        // 二世：前一位是同一朝代的开国之君
        private static bool IsSecondOfDynasty(Empire empire, EmpireCraftHistory reign)
        {
            List<EmpireCraftHistory> history = empire?.data?.history;
            int index = history?.IndexOf(reign) ?? -1;
            if (index <= 0) return false;
            EmpireCraftHistory previous = history[index - 1];
            return previous != null && !previous.is_republic && !previous.ended_dynasty &&
                   previous.dynasty_name == reign.dynasty_name && empire.IsFoundingEmperorHistory(previous);
        }

        // 候选字按顺序越靠前越贴切：从前三个未用过的里随机；全用过了就在全部候选里随机
        #region 明式长谥

        // 本朝施行"增广尊谥"(皇帝谋划 emperor_extend_posthumous)之后，有庙号的先帝得长谥：
        //   "某天某道" + 十二字美谥(开国之君十六字) + 本谥一字 + "皇帝"，如"启天弘道高明肇运圣武神功纯仁至孝文皇帝"；
        //   明制本谥之前多为"孝"字。无庙号的(失位、末代、早夭)照旧短谥(原型明建文、崇祯)。
        //   各字是本地化键 honor_shi_<字>，只在中文下显示长谥，英文仍显示短谥。
        private static readonly string[] HonorHeads =
            { "开", "启", "敬", "宪", "法", "继", "建", "承", "钦", "契", "范", "崇", "达", "应", "绍", "体" };
        private static readonly string[] HonorWays =
            { "行", "弘", "体", "崇", "立", "凝", "明", "达", "履", "隆", "合", "契", "祥", "运", "纪", "极" };
        private static readonly string[] HonorVirtues =
        {
            "英", "明", "神", "圣", "钦", "文", "昭", "武", "宽", "仁", "纯", "诚", "敬", "睿", "哲", "肃", "敦", "简", "光", "章",
            "安", "毅", "弘", "广", "洪", "恭", "俭", "庄", "渊", "懿", "显", "达", "至", "德", "功", "中", "正", "俊", "义", "大"
        };

        public static List<string> LongName(string shiKey, bool founder)
        {
            string head = HonorHeads[_rng.Next(HonorHeads.Length)];
            string way = HonorWays.Where(c => c != head).OrderBy(_ => _rng.Next()).First();
            var keys = new List<string> { Honor(head), Honor("天"), Honor(way), Honor("道") };
            string shi = LM.Get(shiKey);
            List<string> virtues = HonorVirtues.Where(c => c != head && c != way && c != shi).OrderBy(_ => _rng.Next())
                .Take(founder ? 15 : 11).ToList();
            keys.AddRange(virtues.Select(Honor));
            keys.Add(Honor("孝"));
            keys.Add(shiKey);
            return keys;
        }

        private static string Honor(string character) => "honor_shi_" + character;

        private static bool ChineseLocale()
        {
            string language = LocalizedTextManager.instance?.language;
            return language == "cz" || language == "ch";
        }

        // 谥号的显示：帝国设置开了长谥号、有长谥且在中文下为"……孝某皇帝"，否则"国号某帝"
        public static string ShiTitle(EmpireCraftHistory history, Empire empire = null)
        {
            if (history == null || string.IsNullOrEmpty(history.shihao_name)) return "";
            if (history.shihao_long is { Count: > 0 } && ChineseLocale() && (empire ?? EmpireOf(history))?.data?.show_long_posthumous == true)
                return string.Concat(history.shihao_long.Select(LM.Get)) + LM.Get("emperor_suffix_long");
            return history.empire_name + LM.Get(history.shihao_name) + LM.Get("emperor_suffix");
        }

        // 这条纪年属于哪个帝国(史书里有它的)
        private static Empire EmpireOf(EmpireCraftHistory history)
        {
            if (ModClass.EMPIRE_MANAGER == null) return null;
            foreach (Empire empire in ModClass.EMPIRE_MANAGER)
                if (empire?.data != null && (empire.data.currentHistory == history || empire.data.history?.Contains(history) == true))
                    return empire;
            return null;
        }

        // 帝国设置打开长谥号：本朝有庙号、谥号的先帝补上长谥(之后定谥的先帝也给，见 EmpireCraftPlotsAddition)
        public static void EnsureLong(Empire empire)
        {
            if (empire?.data == null) return;
            foreach (EmpireCraftHistory history in CurrentDynasty(empire))
            {
                if (string.IsNullOrEmpty(history.shihao_name) || string.IsNullOrEmpty(history.miaohao_name) ||
                    history.shihao_long is { Count: > 0 }) continue;
                history.shihao_long = LongName(history.shihao_name, history.is_first);
            }
        }

        // 谥法由皇帝定：增广尊谥(长谥)或谥法从简(简谥)，一个帝国只定一次(EmpireData.posthumous_decided)。
        // 条件：华夏律令制的皇帝亲政、本朝已有三位以上定了谥号的先帝、正统 50 以上；
        // 荒淫、昏聩、暴虐之君好排场，只会增广尊谥；其余两样都可能
        private static bool CanDecide(Actor actor, out Empire empire)
        {
            Kingdom kingdom = actor?.kingdom;
            empire = kingdom?.GetEmpire();
            if (actor == null || !actor.isKing() || empire == null || empire.CoreKingdom != kingdom || !empire.AllowsPosthumousNames() ||
                empire.data.posthumous_decided || empire.data.long_posthumous || empire.Mandate < 50 || !actor.isAdult() ||
                kingdom.GetRegime()?.type != Regimes.RegimeType.LvLing) return false;
            return CurrentDynasty(empire).Count(history => !string.IsNullOrEmpty(history.shihao_name)) >= 3;
        }

        public static bool CanExtend(Actor actor) => CanDecide(actor, out _);

        public static bool CanSimplify(Actor actor) => CanDecide(actor, out _) &&
            GeneralSystems.RulerQualitySystem.Of(actor) is not (GeneralSystems.RulerQualitySystem.Grade.Dissolute or
                GeneralSystems.RulerQualitySystem.Grade.Fatuous or GeneralSystems.RulerQualitySystem.Grade.Tyrant);

        // 谥法从简：此后先帝只用简谥，帝国设置的长谥号随之关上(仍可在设置里打开)
        public static bool Simplify(Empire empire, Actor emperor)
        {
            if (empire?.data == null || empire.data.posthumous_decided) return false;
            empire.data.posthumous_decided = true;
            empire.data.long_posthumous = false;
            empire.data.show_long_posthumous = false;
            string text = string.Format(LM.Get("history_simplify_posthumous"), emperor?.getName() ?? "", empire.GetEmpireFullName());
            empire.RecordHistory(directContent: text, actorId: emperor?.id ?? -1L);
            TranslateHelper.LogEventMessage(text, empire.CoreKingdom);
            return true;
        }

        public static bool Extend(Empire empire, Actor emperor)
        {
            if (empire?.data == null || empire.data.long_posthumous || empire.data.posthumous_decided) return false;
            empire.data.posthumous_decided = true;
            empire.data.long_posthumous = true;
            empire.data.show_long_posthumous = true;
            foreach (EmpireCraftHistory history in CurrentDynasty(empire))
            {
                if (string.IsNullOrEmpty(history.shihao_name) || string.IsNullOrEmpty(history.miaohao_name) ||
                    history.shihao_long is { Count: > 0 }) continue;
                history.shihao_long = LongName(history.shihao_name, history.is_first);
            }
            string text = string.Format(LM.Get("history_extend_posthumous"), emperor?.getName() ?? "", empire.GetEmpireFullName());
            empire.RecordHistory(directContent: text, actorId: emperor?.id ?? -1L);
            TranslateHelper.LogEventMessage(text, empire.CoreKingdom);
            return true;
        }

        // 本朝(与最近一位君主同朝号、同姓)的历代君主
        private static List<EmpireCraftHistory> CurrentDynasty(Empire empire)
        {
            List<EmpireCraftHistory> histories = empire?.data?.history ?? new List<EmpireCraftHistory>();
            EmpireCraftHistory latest = histories.LastOrDefault(history => history != null && !history.is_republic);
            if (latest == null) return new List<EmpireCraftHistory>();
            return histories.Where(history => history != null && !history.is_republic &&
                                              history.dynasty_name == latest.dynasty_name &&
                                              history.royal_surname == latest.royal_surname).ToList();
        }

        #endregion

        // 先从这一类的候选里挑本朝没用过的；都用过了从同基调的递补字里挑没用过的；再没有才重复
        private static string Pick(string prefix, Dictionary<string, string> table, string[] chars,
            HashSet<string> usedText, string[] fallback = null)
        {
            List<string> Keys(IEnumerable<string> list) =>
                list.Where(table.ContainsKey).Select(c => prefix + table[c]).Distinct().ToList();
            List<string> keys = Keys(chars);
            List<string> free = keys.Where(key => !usedText.Contains(LM.Get(key))).Take(3).ToList();
            if (free.Count == 0 && fallback != null)
                free = Keys(fallback).Where(key => !usedText.Contains(LM.Get(key))).ToList();
            List<string> pool = free.Count > 0 ? free : keys;
            string chosen = pool[_rng.Next(pool.Count)];
            usedText.Add(LM.Get(chosen));
            return chosen;
        }
    }
}
