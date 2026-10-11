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
            ["熙"] = "xi"
        };

        private static readonly Dictionary<string, string> ShiKeys = new()
        {
            ["武"] = "wu", ["文"] = "wen", ["桓"] = "huan", ["威"] = "wei", ["烈"] = "lie", ["庄"] = "zhuang",
            ["景"] = "jing", ["康"] = "kang", ["仁"] = "ren", ["孝"] = "xiao", ["明"] = "ming", ["宣"] = "xuan",
            ["光"] = "guang", ["昭"] = "zhao", ["恭"] = "gong", ["惠"] = "hui", ["顺"] = "shun", ["安"] = "an",
            ["穆"] = "mu", ["简"] = "jian", ["懿"] = "yi", ["成"] = "cheng", ["献"] = "xian", ["末"] = "mo",
            ["哀"] = "ai", ["思"] = "si", ["愍"] = "min", ["殇"] = "shang", ["冲"] = "chong", ["悼"] = "dao",
            ["怀"] = "huai", ["炀"] = "yang", ["幽"] = "you", ["灵"] = "ling", ["厉"] = "li", ["荒"] = "huang",
            ["戾"] = "li2"
        };

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
                result.miao = Pick("rule_miaohaoprefixes_", MiaoKeys, miao, usedMiao);
                result.miao_suffix = suffix;
                result.shi = Pick("rule_shihao_", ShiKeys, shi, usedShi);
                result.reason = reason;
                return result;
            }
            Result NoTemple(string[] shi, string reason)
            {
                result.shi = Pick("rule_shihao_", ShiKeys, shi, usedShi);
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
        private static string Pick(string prefix, Dictionary<string, string> table, string[] chars,
            HashSet<string> usedText)
        {
            List<string> keys = chars.Where(table.ContainsKey).Select(c => prefix + table[c]).ToList();
            List<string> free = keys.Where(key => !usedText.Contains(LM.Get(key))).Take(3).ToList();
            List<string> pool = free.Count > 0 ? free : keys;
            string chosen = pool[_rng.Next(pool.Count)];
            usedText.Add(LM.Get(chosen));
            return chosen;
        }
    }
}
