using System.Collections.Generic;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 推行制度改革的"政权"：帝国，或独立王国/部落(简化朝堂：君主亲裁，以君威代替正统，见 RealmAuthority)。
// 制度仍然归文化所有(见 InstitutionSystem)，政权只持有"正在推的这一次改革"和"本政权落地过哪些节点的持久效果"；
// 以前只有帝国能推，部落时代只好把每个独立的部落都建成帝国(见 TribalFissionService.FormTribes)。
// 只是个轻量包装，用时现取，不进存档：帝国的状态存在 EmpireData，王国的存在 KingdomExtraData.institution_state。
public sealed class InstitutionRealm
{
    // 战国时离开周室的诸侯(ZhouEraSystem.IsDetachedVassal)算独立王国，自己推制度(用户定)；改成 false 就不算
    public const bool DetachedZhouVassalsAreSovereign = true;

    public readonly Empire Empire;
    public readonly Kingdom Kingdom;

    private InstitutionRealm(Empire empire, Kingdom kingdom)
    {
        Empire = empire;
        Kingdom = kingdom;
    }

    public static InstitutionRealm Of(Empire empire) => empire == null ? null : new InstitutionRealm(empire, null);

    // 帝国核心、成员国一律归到所在的帝国；其余(独立王国、附庸、部落)按本国，是否独立另看 IsSovereign
    public static InstitutionRealm Of(Kingdom kingdom)
    {
        if (kingdom == null) return null;
        if (kingdom.IsEmpire() || kingdom.IsInEmpire())
        {
            Empire empire = kingdom.GetEmpire();
            if (empire != null) return new InstitutionRealm(empire, null);
        }
        return new InstitutionRealm(null, kingdom);
    }

    // 能自己推制度的独立王国：开化、非野、有都城、君主是本国的(不是别国君主兼领)、不是帝国也不在帝国里、
    // 没有封建宗主、不奉部落共主、不归古战模组管。朝贡国照样算(朝贡不管内政)；战国离开周室的诸侯按上面的常量
    public static bool IsSovereignKingdom(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || !kingdom.isCiv() || kingdom.wild) return false;
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(kingdom)) return false;
        if (kingdom.capital == null || kingdom.capital.isRekt()) return false;
        if (kingdom.hasKing() && kingdom.king != null && kingdom.king.kingdom != kingdom) return false;
        if (kingdom.IsEmpire() || kingdom.IsInEmpire()) return false;
        if (FeudalVassalService.GetOverlord(kingdom) != null) return false;
        if (TribalFissionService.OverlordOf(kingdom) != null) return false;
#pragma warning disable CS0162
        if (!DetachedZhouVassalsAreSovereign && ZhouEraSystem.IsDetachedVassal(kingdom)) return false;
#pragma warning restore CS0162
        return true;
    }

    public bool IsEmpire => Empire != null;

    // 推制度的那个王国：帝国的核心王国，或王国本身
    public Kingdom Core => Empire != null ? Empire.CoreKingdom : Kingdom;

    public bool IsAlive => Empire != null
        ? Empire.data != null && !Empire.isRekt() && !Empire.IsArchived() && Empire.CoreKingdom != null &&
          !Empire.CoreKingdom.isRekt()
        : Kingdom?.data != null && !Kingdom.isRekt();

    // 王国是独立政权才推制度；帝国总是
    public bool IsSovereign => Empire != null ? IsAlive : IsSovereignKingdom(Kingdom);

    // 制度文化：帝国按制度文化(征服王朝、统治多数派，见 InstitutionSystem.GetPrimaryCulture)，王国按本国文化
    public string Culture => Empire != null
        ? InstitutionSystem.GetPrimaryCulture(Empire)
        : CultureService.GetRealmCulture(Kingdom);

    // 制度效果、阶层人口所及的国家：帝国全体成员，王国只有自己
    public IEnumerable<Kingdom> Scope => Empire != null
        ? (IEnumerable<Kingdom>)Empire.kingdoms_hashset ?? new Kingdom[0]
        : Kingdom == null ? new Kingdom[0] : new[] { Kingdom };

    // 现有的改革状态(可能为空；要建出来用 InstitutionSystem.EnsureRealmState)
    public InstitutionEmpireState State => Empire != null
        ? Empire.data?.institution_state
        : Kingdom?.data == null ? null : Kingdom.GetOrCreate().institution_state;

    // 推改革要够的威望：帝国看正统；王国暂无
    public int Authority => Empire != null ? Empire.Legitimacy : 0;

    public string Name => Empire != null ? Empire.GetEmpireName() : Kingdom?.GetKingdomName() ?? "";

    public Actor Ruler => Empire != null ? Empire.Emperor : Kingdom?.king;

    // 字典键、日志用；帝国与王国编号各成一套，前缀分开
    public string Key => Empire != null ? "e" + Empire.id : "k" + (Kingdom?.id ?? -1L);

    // 记一笔：帝国写进史书；王国没有史书，只进日志(世界消息由调用方另发)
    public void Record(string content, long actorId = -1L, long kingdomId = -1L)
    {
        if (string.IsNullOrEmpty(content)) return;
        if (Empire != null) Empire.RecordHistory(directContent: content, actorId: actorId, kingdomId: kingdomId);
        else LogService.LogInfo($"[EmpireCraft][制度] {content}");
    }

    public bool Is(InstitutionRealm other) =>
        other != null && ReferenceEquals(Empire, other.Empire) && ReferenceEquals(Kingdom, other.Kingdom);

    public override bool Equals(object obj) => obj is InstitutionRealm other && Is(other);

    public override int GetHashCode() => Empire != null ? Empire.GetHashCode() : Kingdom?.GetHashCode() ?? 0;
}
