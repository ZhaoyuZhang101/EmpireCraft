using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 华夏原始阶段的氏族(用户 2026-10-16："华夏缺少原始体制""早期是母系社会？部落联合还是什么"，选定：完整五段、随母入氏、女字旁古姓)：
//   制度线 母系氏族 → 父系氏族 → 部落联盟(禅让) → 家天下(夏商方国) → 宗法(周)，前四段用原始政体，宗法起才是周制分封；
//   母系氏族阶段(制度特性 matrilineal_descent)：孩子入母亲的氏族、随母姓(见 SpecificClan.Matrilineal、SurnameInheritanceSystem)；
//     新立的氏族取姬、姜、姒、嬴这类带女字旁的古姓；进入父系氏族后改为随父，已有的古姓照旧传下去。
public static class PrehistoricClanSystem
{
    public const string MatrilinealFeature = "matrilineal_descent";
    private const float CacheSeconds = 5f;
    private static readonly Dictionary<string, (float until, bool value)> Cache = new();

    // 带女字旁的上古之姓
    private static readonly string[] AncientSurnames =
        { "姬", "姜", "姒", "嬴", "妘", "妫", "姞", "姚", "嫘", "妊", "媿", "嫚", "姺", "婤", "姮", "嬉" };

    public static bool IsMatrilineal(string culture)
    {
        if (string.IsNullOrEmpty(culture)) return false;
        float now = Time.unscaledTime;
        if (Cache.TryGetValue(culture, out var hit) && hit.until > now) return hit.value;
        bool value;
        try
        {
            value = CultureService.IsValidCulture(culture) && InstitutionSystem.GetFeature(culture, MatrilinealFeature) > 0f;
        }
        catch
        {
            value = false;
        }
        Cache[culture] = (now + CacheSeconds, value);
        return value;
    }

    public static void ResetWorldState() => Cache.Clear();

    // 新氏族的古姓：优先取还没被用掉的
    public static string PickAncientSurname()
    {
        HashSet<string> used = SpecificClanManager.ClanNames();
        List<string> free = AncientSurnames.Where(name => !used.Contains(name)).ToList();
        IList<string> pool = free.Count > 0 ? free : AncientSurnames;
        return pool[Random.Range(0, pool.Count)];
    }
}
