using System;
using System.Collections.Generic;
using System.Linq;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 公司起名(用户 2026-11-25："名字可以用当地的名字，或者一些国际和国内通用的""按照文化来""后缀也是，中国是股份有限公司，日本是株式会社")。
// 按总部那座城的文化分三路：华夏一系(字号、地名、通用名头 + 股份有限公司)、日本(东家的姓、地名、通用名头 + 株式会社)、
// 其余(东家的姓、地名、通用名头 + 公司)。名头的字表、后缀、格式都在语言文件里(company_ 开头)，这里只管挑。
// 国企另有一套格式(国营某城某业总厂、某城某业公社……)。起好的名字记进存档，以后不随语言、城名变。
public static class CompanyNaming
{
    public static string Family(string culture) => culture switch
    {
        "Huaxia" or "China" or "Aosong" or "Shanhai" => "huaxia",
        "Japan" => "japan",
        _ => "west"
    };

    public static string SectorName(string sector) => LM.Get("company_sector_" + sector);

    private static string[] Table(string key)
    {
        string text = LM.Get(key);
        return string.IsNullOrEmpty(text) || text == key ? Array.Empty<string>() : text.Split(',');
    }

    private static string Pick(string[] table, long seed) => table.Length == 0 ? "" : table[(int)(Math.Abs(seed) % table.Length)].Trim();

    // "皇家""帝国"这类名头只有君主国用
    private static bool CrownOnly(string head) => Table("company_prefixes_crown").Any(item => item.Trim() == head);

    // 私企：返回(全名, 简称)。surname 为东家的姓(没有东家传空)；crowned 为所在国是不是君主国
    public static (string full, string brief) Private(string culture, string cityName, string sector, string surname, long seed,
        ICollection<string> used, bool crowned)
    {
        string family = Family(culture);
        string suffix = LM.Get("company_suffix_" + family);
        string trade = SectorName(sector);
        for (int attempt = 0; attempt < 6; attempt++)
        {
            long roll = Math.Abs(seed + attempt * 7919L);
            int style = (int)(roll % 100);
            string head;
            if (family == "huaxia")
            {
                // 字号(两个吉字)、地名、通用名头
                if (style < 50)
                {
                    string[] chars = Table("company_hao_huaxia");
                    string first = Pick(chars, roll / 100), second = Pick(chars, roll / 100 / Math.Max(1, chars.Length) + 3);
                    head = first == second ? first + Pick(chars, roll / 7 + 1) : first + second;
                }
                else head = style < 80 ? cityName : Pick(Table("company_prefixes_huaxia"), roll / 100);
            }
            else
            {
                int byName = family == "japan" ? 45 : 35, byPlace = family == "japan" ? 75 : 70;
                head = style < byName && !string.IsNullOrEmpty(surname) ? surname
                    : style < byPlace ? cityName : Pick(Table("company_prefixes_" + family), roll / 100);
            }
            if (string.IsNullOrEmpty(head)) head = cityName;
            if (!crowned && CrownOnly(head)) continue;
            string brief = string.Format(LM.Get("company_brief_format"), head, trade);
            if (used != null && used.Contains(brief)) continue;
            return (string.Format(LM.Get("company_private_format"), head, trade, suffix), brief);
        }
        // 都重了：地名加个序数
        string fallback = string.Format(LM.Get("company_brief_format"), cityName + LM.Get("company_second"), trade);
        return (string.Format(LM.Get("company_private_format"), cityName + LM.Get("company_second"), trade, suffix), fallback);
    }

    // 国企：返回(全名, 简称)
    public static (string full, string brief) State(string culture, string cityName, string sector)
    {
        string trade = SectorName(sector);
        return (string.Format(LM.Get("company_state_format_" + Family(culture)), cityName, trade),
            string.Format(LM.Get("company_state_brief"), cityName, trade));
    }
}
