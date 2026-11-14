using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 时代锁定(用户 2026-11-13 "管理员可以锁定时代, 比如锁定在古典, 那么最多只能到律令制科技只能发展到火绳枪, 没发立宪")：
// 终端管理员挑一个科技时代(见 TerminalHost.Admin)。问答定——
//   · 按科技的十一个时代逐个选；锁了就不再研究这个时代以后的科技(正在研的停在那里)；
//     制度跟着科技走：要锁定时代以后科技的制度不再新行；
//   · 立宪、政党、理念、废君主这类近代的东西要锁在工业时代或以上才有新的(锁在火药时代：火绳枪研得出，立宪立不了，
//     律令制这类古代政体照常)；
//   · 已经超过的只冻住、不倒退：研成的科技、已行的制度、宪政与政党都留着，照旧能用；
//   · 跟着存档走(SaveData.era_lock_tier)，不开终端也照样锁着；0 是不锁。上帝模式直接研成、直接立宪不受它限
public static partial class TechnologySystem
{
    private static int _eraLock;

    public static int EraLockTier => _eraLock;
    public static bool EraLocked => _eraLock > 0;

    public static void SetEraLock(int tier) => _eraLock = Math.Max(0, Math.Min(MaxEraTier, tier));

    // 读档：配置可能还没读，原样记下(0 以下当不锁)
    public static void ImportEraLock(int tier) => _eraLock = Math.Max(0, tier);

    public static int MaxEraTier => Config.eras.Count == 0 ? 0 : Config.eras.Max(era => era.tier);

    // 近代从哪个时代起：工业化那一档(配置里没有就按第 7 档)
    public static int IndustrialTier => TryGetTech("industrialization", out TechNodeConfig tech) ? tech.tier : 7;

    // 锁在工业时代以前：近代制度、立宪、废君、开党禁都不再新有
    public static bool EraLockBlocksModern => _eraLock > 0 && _eraLock < IndustrialTier;

    // 不许新走近代化：禁止近代化开着，或时代锁在工业时代以前
    public static bool NoNewModern => PremodernLocked || EraLockBlocksModern;

    public static bool BeyondEraLock(TechNodeConfig tech) => _eraLock > 0 && tech != null && tech.tier > _eraLock;

    // 这项制度时代锁定下不能新行：要锁定时代以后的科技，或锁在工业时代以前而它是近代制度
    public static bool InstitutionBeyondEraLock(InstitutionNodeConfig node) =>
        _eraLock > 0 && node != null &&
        (EraLockBlocksModern && IsModernInstitution(node) ||
         node.requires_techs != null && node.requires_techs.Any(id => TryGetTech(id, out TechNodeConfig tech) && tech.tier > _eraLock));

    // 近代制度不许新行(禁止近代化)，或时代锁定拦着
    public static bool ModernInstitutionBlocked(InstitutionNodeConfig node) =>
        PremodernLocked && IsModernInstitution(node) || InstitutionBeyondEraLock(node);

    // 拦着的原因(本地化键)：禁止近代化在先
    public static string ModernInstitutionReason(InstitutionNodeConfig node) =>
        PremodernLocked && IsModernInstitution(node) ? "institution_reform_premodern" : "institution_reform_era_lock";

    // 某一档时代的名字(0 = 原始)
    public static string EraNameOfTier(int tier)
    {
        TechEraConfig era = Config.eras.Where(e => e.tier <= tier).OrderByDescending(e => e.tier).FirstOrDefault();
        return era == null ? LM.Get("tech_era_primitive") : LM.Get(era.key);
    }

    // 可锁的各档(从低到高)
    public static List<(int tier, string name)> EraChoices() =>
        Config.eras.OrderBy(era => era.tier).Select(era => (era.tier, LM.Get(era.key))).ToList();

    // 眼下最先进的文化到了哪个时代(给管理员看锁不锁得住)
    public static int TopEraTier()
    {
        int top = 0;
        foreach (string culture in CultureService.GetActiveCultureKeys())
            top = Math.Max(top, GetEraTier(culture));
        return top;
    }
}
