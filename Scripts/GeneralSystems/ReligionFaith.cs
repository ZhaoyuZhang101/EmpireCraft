using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 宗教类型(用户 2026-10-26 选"按宗教分类型"，见 Docs/plan-religion.md)。以前宗教本身没有类型，教廷、哈里发、圣战看的是
// 信者的文化线：华夏创的教传到法兰克也有教宗，犹太文化走罗马制度线也有教廷、十字军。现在创教时按创教者文化定类型，记在宗教上，
// 行为跟着宗教走：
//   · 教阶：普世教会有教廷、伊斯兰有哈里发座，其余没有(PapacySystem.Papacy 返回空)；
//   · 圣战：普世教会、伊斯兰；骑士团只有普世教会；
//   · 排他：普世教会、伊斯兰、犹太按制度定宽容与否，其余向来宽容(见 ReligiousTension.PolicyOf)；
//   · 传教力、惰性：邻城与国教的势能乘传教力，本城的惯性乘惰性(见 BackgroundReligionSystem)；
//   · 名库：有名库的类型创教时从名库取名(普世教会不再取到摩门教、科学教)，多神民间照旧用本文化名库。
// 文化规则的 faith 可指定类型；没写的按天下标识取。旧档没记的按圣地(或创教之国)的文化补上，分出去的教会随母宗教。
public enum FaithType
{
    Folk,
    Church,
    Islam,
    Judaism,
    Dharmic,
    Sanjiao,
    Shinto,
    Tengri
}

public static class ReligionFaith
{
    private static readonly Dictionary<string, FaithType> BySphere = new(StringComparer.Ordinal)
    {
        ["Roma"] = FaithType.Church,
        ["Umma"] = FaithType.Islam,
        ["Kosher"] = FaithType.Judaism,
        ["India"] = FaithType.Dharmic,
        ["Huaxia"] = FaithType.Sanjiao,
        ["Tenka"] = FaithType.Shinto,
        ["Steppe"] = FaithType.Tengri
    };

    // 各类型名库的条数(文字键 faith_name_<类型小写>_<序号>)；没列的(多神民间)用本文化名库
    private static readonly Dictionary<FaithType, int> NamePools = new()
    {
        [FaithType.Church] = 5,
        [FaithType.Islam] = 2,
        [FaithType.Judaism] = 2,
        [FaithType.Dharmic] = 4,
        [FaithType.Sanjiao] = 4,
        [FaithType.Shinto] = 2,
        [FaithType.Tengri] = 2
    };

    public static FaithType OfCulture(string culture)
    {
        if (!CultureService.IsValidCulture(culture)) return FaithType.Folk;
        if (OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting) &&
            !string.IsNullOrWhiteSpace(setting?.faith) && Enum.TryParse(setting.faith.Trim(), true, out FaithType own))
            return own;
        return BySphere.TryGetValue(culture.GetTianxiaId(), out FaithType type) ? type : FaithType.Folk;
    }

    public static FaithType Of(Religion religion) => Of(religion, 0);

    private static FaithType Of(Religion religion, int depth)
    {
        if (religion?.data == null) return FaithType.Folk;
        ReligionExtension.ReligionExtraData data = religion.GetOrCreate();
        if (!string.IsNullOrEmpty(data.Faith) && Enum.TryParse(data.Faith, out FaithType stored)) return stored;
        FaithType type;
        Religion parent = data.ParentReligionId >= 0 && depth < 8 ? World.world.religions.get(data.ParentReligionId) : null;
        if (parent?.data != null && parent != religion) type = Of(parent, depth + 1);
        else type = OfCulture(ReligionSphere.OriginCulture(religion));
        data.Faith = type.ToString();
        return type;
    }

    public static void Record(Religion religion, FaithType type)
    {
        if (religion?.data != null) religion.GetOrCreate().Faith = type.ToString();
    }

    // 教廷(教宗)、哈里发座
    public static bool IsPapal(Religion religion) => Of(religion) == FaithType.Church;
    public static bool IsCaliphal(Religion religion) => Of(religion) == FaithType.Islam;
    public static bool HasHierarchy(Religion religion) => Of(religion) is FaithType.Church or FaithType.Islam;

    public static bool WagesHolyWar(Religion religion) => Of(religion) is FaithType.Church or FaithType.Islam;

    public static bool FoundsOrders(Religion religion) => Of(religion) == FaithType.Church;

    // 排他：异教是异端，按制度定宽容与否；不排他的向来宽容
    public static bool IsExclusive(Religion religion) => Of(religion) is FaithType.Church or FaithType.Islam or FaithType.Judaism;

    // 世界性宗教(成体系、有经典，君主皈依的对象)
    public static bool IsWorldFaith(Religion religion) =>
        Of(religion) is FaithType.Church or FaithType.Islam or FaithType.Dharmic or FaithType.Sanjiao;

    // 普世宗教(向别的文明传教的：教会、伊斯兰、法教)；其余是民族宗教(三教、神道、腾格里、犹太、民间)，几乎不向外传
    public static bool IsUniversal(Religion religion) =>
        Of(religion) is FaithType.Church or FaithType.Islam or FaithType.Dharmic;

    // 一神普世教(教会、伊斯兰)：史上出现得晚，本土立教要再研究冶铁(见 NativeFaithSystem)
    public static bool IsLateFaith(FaithType type) => type is FaithType.Church or FaithType.Islam;

    public static float Mission(Religion religion) => Of(religion) switch
    {
        FaithType.Church or FaithType.Islam => 1f,
        FaithType.Judaism => 0.2f,
        FaithType.Dharmic => 0.7f,
        FaithType.Sanjiao => 0.6f,
        FaithType.Shinto or FaithType.Tengri => 0.3f,
        _ => 0.4f
    };

    public static float Inertia(Religion religion) => Of(religion) switch
    {
        FaithType.Church or FaithType.Dharmic or FaithType.Shinto => 1f,
        FaithType.Islam => 1.2f,
        FaithType.Judaism => 2f,
        FaithType.Sanjiao => 0.9f,
        _ => 0.5f
    };

    // 君主皈依外教的意愿：多神、萨满、无教最易，三教、法教、神道较难，排他的一神教极少
    public static float Receptiveness(Religion current) => current?.data == null ? 1f : Of(current) switch
    {
        FaithType.Folk or FaithType.Tengri => 1f,
        FaithType.Dharmic or FaithType.Sanjiao or FaithType.Shinto => 0.15f,
        _ => 0.03f
    };

    // 从本类型名库里挑一个名字；多神民间没有名库，返回空(用本文化名库)
    public static string PickName(FaithType type)
    {
        if (!NamePools.TryGetValue(type, out int count) || count <= 0) return null;
        string key = $"faith_name_{type.ToString().ToLowerInvariant()}_{UnityEngine.Random.Range(1, count + 1)}";
        string name = LM.Get(key);
        return string.IsNullOrEmpty(name) || name == key ? null : name;
    }

    // 分裂出去的教会的名字(正教会、什叶派)
    public static string PickName(string pool, int count)
    {
        string key = $"faith_name_{pool}_{UnityEngine.Random.Range(1, count + 1)}";
        string name = LM.Get(key);
        return string.IsNullOrEmpty(name) || name == key ? null : name;
    }
}
