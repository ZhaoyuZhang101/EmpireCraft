using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 国号、法理名要合本国文化(用户 2026-10-27 截图罗马城的教宗国叫"远江国"："远江国不是罗马的命名吧")：
// 原版新立的国按立国者本人的原版文化起国号，模组随后才按立国之城的法理文化定国家文化——外来的官员、教宗在罗马自立，
// 国号却是日本令制国名；立了法理，法理名也就叫"远江国"，此后的主人一并沿用。
//   · 各文化的国名词库(文化配置 Kingdom 的各词库与 ancient_group)建一张"名字 → 出自哪些文化"的表；
//   · 国号(没手动改名、没在起事)、法理名只出自不同族文化的词库、不在本文化词库里的，按本国(法理按法理文化)文化重起；
//   · 新立之国定下文化后当场查(KingdomPatch.NewCivKingdom)，各国与各法理每年查一遍(年度扫描)。
public static class NameCultureRepair
{
    private static Dictionary<string, HashSet<string>> _owners;
    private static double _lastYear = -1d;
    private static object _world;
    private const int Tries = 6;

    // 名字(连同去掉"国"字的)出自哪些文化的国名词库
    private static Dictionary<string, HashSet<string>> Owners()
    {
        if (_owners != null) return _owners;
        var owners = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        try
        {
            string root = Path.Combine(ModClass._declare.FolderPath, "Locales", "Cultures");
            foreach (KeyValuePair<string, Setting> pair in OnomasticsRule.ALL_CULTURE_RULE)
            {
                KingdomSetting kingdom = pair.Value?.Kingdom;
                if (kingdom == null) continue;
                IEnumerable<string> groups = (kingdom.groups?.Values ?? Enumerable.Empty<string>())
                    .Append(kingdom.ancient_group).Where(group => !string.IsNullOrWhiteSpace(group));
                foreach (string group in groups)
                {
                    // 写作"别的文化:词库名"的借用别家词库
                    string source = pair.Key, name = group.Trim();
                    int colon = name.IndexOf(':');
                    if (colon > 0)
                    {
                        source = name.Substring(0, colon).Trim();
                        name = name.Substring(colon + 1).Trim();
                    }
                    string path = Path.Combine(root, $"Culture_{source}", $"{source}{name}.csv");
                    if (!File.Exists(path)) continue;
                    foreach (string key in OnomasticsHelper.getKeysFromPath(path))
                    {
                        string text = LM.Get(key);
                        if (string.IsNullOrWhiteSpace(text) || text == key) continue;
                        if (!owners.TryGetValue(text.Trim(), out HashSet<string> set))
                            owners[text.Trim()] = set = new HashSet<string>(StringComparer.Ordinal);
                        set.Add(pair.Key);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][国号文化] 国名词库读取失败: {exception.Message}");
        }
        _owners = owners;
        return owners;
    }

    // 这个名字出自别的(不同族的)文化的国名词库，本文化词库里没有
    public static bool Foreign(string name, string culture)
    {
        if (string.IsNullOrWhiteSpace(name) || !CultureService.IsValidCulture(culture)) return false;
        Dictionary<string, HashSet<string>> owners = Owners();
        string trimmed = name.Trim();
        var cultures = new HashSet<string>(StringComparer.Ordinal);
        foreach (string candidate in new[] { trimmed, trimmed.TrimEnd('国', '國') })
            if (candidate.Length > 0 && owners.TryGetValue(candidate, out HashSet<string> set)) cultures.UnionWith(set);
        // 同族(同一天下标识)或同一条文明线(制度线)的都算本家：罗马一系里法兰克、日耳曼、维京互相借名不改，同化过的老国名不动
        string line = culture.GetInstitutionLine();
        return cultures.Count > 0 && !cultures.Any(owner => CultureService.SameKin(owner, culture) ||
                                                             line.Length > 0 && owner.GetInstitutionLine() == line);
    }

    // 这个名字出自某条文明线(制度线)的国名词库(连同去掉"国"字的)
    public static bool FromLine(string name, string line)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(line)) return false;
        Dictionary<string, HashSet<string>> owners = Owners();
        string trimmed = name.Trim();
        foreach (string candidate in new[] { trimmed, trimmed.TrimEnd('国', '國') })
            if (candidate.Length > 0 && owners.TryGetValue(candidate, out HashSet<string> set) &&
                set.Any(owner => owner.GetInstitutionLine() == line)) return true;
        return false;
    }

    // 按这个文化的国号规则起一个合本文化的名字；起不出返回 null
    private static string Fresh(string culture)
    {
        Culture native = CultureService.GetNativeCultureObject(culture);
        if (native?.data == null) return null;
        for (int i = 0; i < Tries; i++)
        {
            string name;
            try { name = native.getOnomasticData(MetaType.Kingdom)?.generateName()?.UseLocalizedNameSeparator(); }
            catch { return null; }
            if (!string.IsNullOrWhiteSpace(name) && !Foreign(name, culture)) return name.Trim();
        }
        return null;
    }

    // 国号不合本国文化：按本国文化重起
    public static bool RepairKingdom(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild || kingdom.HasCustomCountryNaming() ||
            kingdom.IsFactionRebelling() || kingdom.IsLocalRebelling() ||
            Compatibility.AncientWarfareCompatibility.Owns(kingdom)) return false;
        string culture = CultureService.GetRealmCulture(kingdom);
        string before = kingdom.EnsureKingdomCoreName();
        if (!Foreign(before, culture)) return false;
        string fresh = Fresh(culture);
        if (fresh == null) return false;
        kingdom.data.name = fresh;
        kingdom.RememberInitialRandomKingdomName(fresh, overwrite: true);
        var data = kingdom.GetOrCreate();
        data.core_name = "";
        data.core_name_source = "";
        kingdom.EnsureKingdomCoreName();
        LogService.LogInfo($"[EmpireCraft][国号文化] {before}({culture}文化)国号出自别的文化，改称 {kingdom.GetKingdomName()}");
        return true;
    }

    // 法理名不合法理文化：按法理文化重起(原来带"国"字的照样带)
    private static bool RepairTitle(KingdomTitle title)
    {
        string name = title?.data?.name;
        if (string.IsNullOrWhiteSpace(name) || title.isRekt()) return false;
        string culture = CultureService.GetEffectiveTitleCulture(title);
        if (!Foreign(name, culture)) return false;
        string fresh = Fresh(culture);
        if (fresh == null) return false;
        if ((name.EndsWith("国", StringComparison.Ordinal) || name.EndsWith("國", StringComparison.Ordinal)) &&
            !fresh.EndsWith("国", StringComparison.Ordinal) && !fresh.EndsWith("國", StringComparison.Ordinal))
            fresh += name[name.Length - 1];
        title.data.name = fresh;
        LogService.LogInfo($"[EmpireCraft][国号文化] 法理 {name}({culture}文化)名出自别的文化，改称 {fresh}");
        return true;
    }

    // 年度扫描(CityPatch.WorldYearlyScans)
    public static void TryYearlyScan()
    {
        if (World.world?.kingdoms == null || ModClass.IS_CLEAR) return;
        if (!ReferenceEquals(_world, World.world))
        {
            _world = World.world;
            _lastYear = -1d;
        }
        double now = World.world.getCurWorldTime();
        if (_lastYear >= 0d && now >= _lastYear && Date.getYearsSince(_lastYear) < 1) return;
        _lastYear = now;
        try
        {
            foreach (KingdomTitle title in ModClass.KINGDOM_TITLE_MANAGER?.ToList() ?? new List<KingdomTitle>()) RepairTitle(title);
            foreach (Kingdom kingdom in World.world.kingdoms.list.ToList()) RepairKingdom(kingdom);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][国号文化] 年度核对失败: {exception.Message}");
        }
    }
}
