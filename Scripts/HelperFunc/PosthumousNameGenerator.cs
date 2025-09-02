using EmpireCraft.Scripts.Layer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.HelperFunc
{
    public static class PosthumousNameGenerator
    {
        // 庙号前半部分
        private static List<string> MiaoPrefixes => OnomasticsHelper.getKeysFromPath(Path.Combine(ModClass._declare.FolderPath, "Locales", "Cultures", "MiaoHaoPrefixes.csv")).ToList();

        // 庙号后半部分
        private static List<string> MiaoSuffixes => OnomasticsHelper.getKeysFromPath(Path.Combine(ModClass._declare.FolderPath, "Locales", "Cultures", "MiaoHaoSuffixes.csv")).ToList();

        // 谥号常用词库（按褒义度排，前面的更常见/权重高）
        private static List<string> ShiWords => OnomasticsHelper.getKeysFromPath(Path.Combine(ModClass._declare.FolderPath, "Locales", "Cultures", "ShiHao.csv")).ToList();

        private static List<string> used_miaos;
        private static List<string> used_shis;

        private static readonly Random _rng = new Random();

        /// <summary>
        /// 生成一个随机的庙号，如“昭宗”“高祖”。
        /// </summary>
        public static (string pre, string suf) GenerateMiaohao(bool isFirst=false)
        {
            string p;
            string s;
            // 如果是第一个庙号，直接返回“祖”
            if (isFirst)
            {
                p = MiaoPrefixes.Take(2).ToArray()[_rng.Next(2)];
                s = MiaoSuffixes.First();
            }else
            {
                var p_list = MiaoPrefixes.Except(used_miaos).ToArray();
                if (p_list.Length == 0)
                {
                    p_list = MiaoPrefixes.Skip(2).ToArray();
                }
                p = p_list[_rng.Next(p_list.Length)];
                s = MiaoSuffixes.Last();
            }
            return (p, s);
        }

        /// <summary>
        /// 生成一个谥号，长度在2~4字之间，可根据功绩浓淡调整 count 个字。
        /// </summary>
        /// <param name="count">谥号字数，推荐2~3</param>
        public static string GenerateShihao(int count = 2, bool isLast=false, bool isGood=true)
        {
            if (count < 1) count = 2;
            // 从前几个高权重词里随机取
            List<string> goodShiWords = ShiWords.FindAll(w => w.Split('_')[0]=="good").ToList();
            List<string> badShiWords = ShiWords.FindAll(w => w.Split('_')[0]=="bad").ToList();
            List<string> lastShiWords = ShiWords.FindAll(w => w.Split('_')[0]=="last").ToList();
            List<string> pool = goodShiWords;
            if (!isGood)
            {
                pool = badShiWords;
            }else
            {
                pool = goodShiWords;
            }
            if (isLast)
            {
                pool = lastShiWords;
            }
            var chosen = new HashSet<string>();
            while (chosen.Count < count)
            {
                var w = pool[_rng.Next(pool.Count())];
                chosen.Add(w);
            }
            return string.Concat(chosen);
        }
        public static void syncHistoryUsedPosthumous(Empire empire)
        {
            used_miaos = new List<string>();
            used_shis = new List<string>();
            foreach (EmpireCraftHistory history in empire.data.history)
            {
                if (history.miaohao_name != null && !string.IsNullOrEmpty(history.miaohao_name) && !used_miaos.Contains(history.miaohao_name))
                {
                    used_miaos.Add(history.miaohao_name);
                }
                if (history.shihao_name != null && !string.IsNullOrEmpty(history.shihao_name) && !used_shis.Contains(history.shihao_name))
                {
                    used_shis.Add(history.shihao_name);
                }
            }
        }

        /// <summary>
        /// 一次性生成完整的“庙谥”对：Tuple.Item1=庙号，.Item2=谥号
        /// </summary>
        public static ((string pre, string suf) miao, string shi) GenerateBoth(this Empire empire,int shiCount = 1, bool isFirst=false, bool isLast=false, bool isGood=true)
        {
            syncHistoryUsedPosthumous(empire);
            return (GenerateMiaohao(isFirst), GenerateShihao(shiCount,isLast,isGood));
        }
    }
}
