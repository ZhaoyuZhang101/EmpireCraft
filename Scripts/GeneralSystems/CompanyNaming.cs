using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GeneralSystems.WorldMap;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 公司起名(用户 2026-11-25："名字可以用当地的名字，或者一些国际和国内通用的""按照文化来""后缀也是，中国是股份有限公司，日本是株式会社"；
// 2026-11-28 "可是古代的公司不是什么股份有限公司什么的""能不能模拟历史，做出更符合的显示")。
// 名号分两截：字号(head，两个吉字、东家的姓、总部的城名、通用名头、屋号吉字，起好以后一直留着)与形态的格式(随本国时代改组：
// 华夏 恒裕窑 → 恒裕陶瓷公司 → 恒裕陶瓷股份有限公司 → 恒裕集团；日本 大阪屋 → 田中组 → 田中纺织株式会社；西方 某某父子商行 → 某某有限公司)。
// 官办的另一套(某城官窑、宝某局 → 某城机器制造局 → 国营某城某业总厂)。格式、字号取法、字表都在 CompanyRules.json(政体编辑器可改)。
// 起好的名字记进存档，以后不随语言、城名变；改过名的(Rename)不随改组换名。
public static class CompanyNaming
{
    public static string Family(string culture) => CompanyRules.Family(culture);

    public static string SectorName(string sector)
    {
        string name = LM.Get("company_sector_" + sector);
        return string.IsNullOrEmpty(name) || name == "company_sector_" + sector ? FactorySystem.TypeName(sector) : name;
    }

    private static string Pick(string[] table, long seed) => table.Length == 0 ? "" : table[(int)(Math.Abs(seed) % table.Length)];

    // "皇家""帝国"这类名头只有君主国用
    private static bool CrownOnly(string head) => CompanyRules.Table("prefixes_crown").Contains(head);

    private static string FirstElement(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        global::System.Globalization.TextElementEnumerator elements = global::System.Globalization.StringInfo.GetTextElementEnumerator(text);
        return elements.MoveNext() ? elements.GetTextElement() : "";
    }

    private static string Fill(string format, string head, string trade, string shop, string city)
    {
        string text = WorldMapData.PickParts(format);
        return text.Replace("{head}", head ?? "").Replace("{trade}", trade ?? "").Replace("{shop}", shop ?? "")
            .Replace("{city1}", FirstElement(city)).Replace("{city}", city ?? "").Trim();
    }

    private static string Shop(CompanyFormNames names, string sector)
    {
        if (names.shops == null || names.shops.Count == 0) return "";
        return names.shops.TryGetValue(sector ?? "", out string word) || names.shops.TryGetValue("default", out word)
            ? WorldMapData.PickParts(word) : "";
    }

    // 字号：按这一档格式给的权重挑取法(两个吉字、东家的姓、城名、通用名头、屋号吉字)
    private static string RollHead(CompanyFormNames names, string family, string cityName, string surname, long roll, bool crowned)
    {
        Dictionary<string, int> heads = names.heads is { Count: > 0 } ? names.heads : new Dictionary<string, int> { ["city"] = 1 };
        int total = heads.Values.Where(weight => weight > 0).Sum();
        if (total <= 0) return cityName;
        int at = (int)(Math.Abs(roll) % total);
        string kind = heads.First().Key;
        foreach (KeyValuePair<string, int> pair in heads)
        {
            if (pair.Value <= 0) continue;
            if (at < pair.Value)
            {
                kind = pair.Key;
                break;
            }
            at -= pair.Value;
        }
        string head = kind switch
        {
            "hao" => Hao(family, roll / 100),
            "surname" => surname,
            "prefix" => Pick(CompanyRules.Table("prefixes_" + family), roll / 100),
            "yago" => Pick(CompanyRules.Table("yago_" + family), roll / 100),
            _ => cityName
        };
        if (string.IsNullOrEmpty(head) || !crowned && CrownOnly(head)) head = cityName;
        return head;
    }

    // 两个吉字(恒裕、同泰……)
    private static string Hao(string family, long roll)
    {
        string[] chars = CompanyRules.Table("hao_" + family);
        if (chars.Length == 0) chars = CompanyRules.Table("hao_huaxia");
        if (chars.Length == 0) return "";
        string first = Pick(chars, roll), second = Pick(chars, roll / Math.Max(1, chars.Length) + 3);
        return first == second ? first + Pick(chars, roll / 7 + 1) : first + second;
    }

    // 按公司眼下的形态起名(新立的、改组的)：字号没有就先起一个；同国已有的简称避开。crowned 为所在国是不是君主国
    public static void Name(CompanyData company, string cityName, bool crowned, ICollection<string> used, long seed)
    {
        if (company == null) return;
        CompanyForm form = CompanyRules.FormOf(company);
        string family = Family(company.culture);
        CompanyFormNames names = CompanyRules.Names(family, form.id);
        string trade = SectorName(company.sector), shop = Shop(names, company.sector);
        cityName ??= "";
        if (company.state)
        {
            string format = names.state_sectors != null && names.state_sectors.TryGetValue(company.sector ?? "", out string bySector)
                ? bySector : !crowned && !string.IsNullOrEmpty(names.state_republic) ? names.state_republic : names.state;
            if (string.IsNullOrEmpty(format)) format = "{city}{trade}";
            company.head = cityName;
            company.name = Fill(format, cityName, trade, shop, cityName);
            company.short_name = Clip(string.IsNullOrEmpty(names.state_brief) ? company.name : Fill(names.state_brief, cityName, trade, shop, cityName));
            return;
        }
        for (int attempt = 0; attempt < 6; attempt++)
        {
            long roll = Math.Abs(seed + attempt * 7919L);
            // 字号起过的(改组)照用；头一回、或撞了名的另起
            string head = attempt == 0 && !string.IsNullOrEmpty(company.head) ? company.head
                : RollHead(names, family, cityName, company.owner, roll, crowned);
            (string full, string brief) = Compose(names, head, company.owner, trade, shop, cityName);
            if (used != null && used.Contains(brief))
            {
                // 改组时字号撞了(两家同号的公司都成了"某某集团")：字号后面带上行业
                if (attempt == 0 && !string.IsNullOrEmpty(company.head))
                {
                    (full, brief) = Compose(names, head + trade, company.owner, trade, shop, cityName);
                    if (!used.Contains(brief)) head += trade;
                    else continue;
                }
                else continue;
            }
            company.head = head;
            company.name = full;
            company.short_name = brief;
            return;
        }
        // 都重了：城名加个序数
        string fallback = cityName + LM.Get("company_second");
        company.head = fallback;
        (company.name, company.short_name) = Compose(names, fallback, company.owner, trade, shop, cityName);
    }

    private static (string full, string brief) Compose(CompanyFormNames names, string head, string surname, string trade, string shop, string city)
    {
        string format = !string.IsNullOrEmpty(surname) && head == surname && !string.IsNullOrEmpty(names.private_surname)
            ? names.private_surname : names.@private;
        if (string.IsNullOrEmpty(format)) format = "{head}{trade}";
        string full = Fill(format, head, trade, shop, city);
        string brief = string.IsNullOrEmpty(names.brief) ? full : Fill(names.brief, head, trade, shop, city);
        return (full, Clip(brief));
    }

    private static string Clip(string text) => text.Length > 8 && WorldMapData.ChineseUi ? text.Substring(0, 8) : text.Length > 24 ? text.Substring(0, 24) : text;

    // 外国公司来设的分公司：返回(全名, 简称)。照母公司的文化叫(分公司、支社)
    public static (string full, string brief) Branch(string culture, string parentBrief, string cityName) =>
        (string.Format(LM.Get("company_branch_format_" + Family(culture)), parentBrief, cityName),
            string.Format(LM.Get("company_branch_brief"), parentBrief, cityName));

    // 改名以后地图上标的简称：去掉常见的后缀，再长就截短
    private static readonly string[] Suffixes =
        { "股份有限公司", "有限公司", "株式会社", "株式會社", "公司", "会社", "會社", "集团", "集團", " Co., Ltd.", " Ltd.", " K.K.", " Group", " Company", " & Co." };

    public static string Brief(string name)
    {
        string value = (name ?? "").Trim();
        foreach (string suffix in Suffixes)
        {
            if (value.Length <= suffix.Length || !value.EndsWith(suffix, StringComparison.Ordinal)) continue;
            value = value.Substring(0, value.Length - suffix.Length).Trim();
            break;
        }
        return Clip(value);
    }

    // 旧档的公司：从简称里拆出字号(简称 = 字号 + 行业)
    public static string GuessHead(CompanyData company)
    {
        string brief = company?.short_name ?? "";
        string trade = SectorName(company?.sector);
        return !string.IsNullOrEmpty(trade) && brief.Length > trade.Length && brief.EndsWith(trade, StringComparison.Ordinal)
            ? brief.Substring(0, brief.Length - trade.Length) : brief;
    }
}
