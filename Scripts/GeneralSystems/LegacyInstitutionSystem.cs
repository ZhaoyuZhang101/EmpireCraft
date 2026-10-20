using System;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 旧制犹存：国家改从一种文化(易俗改制、法理文化同化、称帝改从君主之俗……)时，新文化的文明等级(制度树 1~4 级，
// 见 InstitutionSystem.GetCultureLevel)低于本国现用制度所属的文化，不再当场退回落后的政体与制度：
//   · 本国记下制度文化(KingdomExtraData.institution_culture)：政体、制度效果(政体选项、税、继承法、律法、特质)、
//     制度特性与制度改革都照它算(CultureService.GetInstitutionCulture；帝国核心有旧制的，帝国的制度文化也是它，
//     见 InstitutionSystem.GetPrimaryCulture)；风俗、命名、宗教、科技照新文化；
//   · 新文化的文明等级追上旧制所属的文化(年度扫描)，改行新文化之制：政体按新文化反推，记一笔；新文化的制度树还停在原始政体、
//     本国已是成形政体的再等等，免得一追上就退回部落；
//   · 又改从不低于旧制的文化(主动易俗)时一步改行新制；复归旧制所属之俗时旧制自然作罢；
//   · 不保留旧制的：新立之国(叛乱自立、分封，立国不满一年的都算)、水土的胡化(分出来的本是新立的游牧之国)与整国游牧化
//     (只改政体并记作手选)、征服王朝的核心(另有统治/制度文化之分)、神力改俗与一键易俗(连制度一并改)、
//     归化改制(主动改行帝国之制)；手选、锁定政体的国家照旧，不记旧制。
// 旧档没有 institution_culture，照空，行为不变。
public static class LegacyInstitutionSystem
{
    private const string Tag = "[EmpireCraft][旧制]";
    private static object _scanWorld;
    private static double _lastScan = -1d;

    #region 查询

    // 存着的旧制所属文化(没有或已无效为 null)。不跟国家文化比——两者相同时取哪个都一样
    public static string Stored(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return null;
        string legacy = kingdom.GetOrCreate().institution_culture;
        return !string.IsNullOrEmpty(legacy) && CultureService.IsValidCulture(legacy) ? legacy : null;
    }

    // 正在沿用别的文化的旧制(界面用)：存着旧制，且与不论旧制时本该用的制度文化不同
    public static bool TryGet(Kingdom kingdom, out string legacy)
    {
        legacy = Stored(kingdom);
        return legacy != null && !string.Equals(legacy, NaturalInstitutionCulture(kingdom), StringComparison.Ordinal);
    }

    // 换文化之前取本国现用的制度文化。只读存着的值，不走 GetRealmCulture 的旧档推断——新立之国这时还没有国家文化，
    // 推断出来的君主、都城文化会被当成"旧制"
    public static string CurrentInstitutionCulture(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return "";
        if (Stored(kingdom) is string legacy) return legacy;
        Empire empire = CoreEmpireOf(kingdom);
        // 帝国核心按帝国的制度文化；帝国文化还没定下来的不取(GetEmpireDefaultCulture 会按眼下的国家文化定下来，这时还是旧的)
        if (empire != null && !CompositeEmpireService.IsComposite(empire) &&
            CultureService.IsValidCulture(EmpireCoreManager.Get(empire)?.default_culture))
            return InstitutionSystem.GetPrimaryCulture(empire);
        string realm = kingdom.GetOrCreate().realm_culture;
        return CultureService.IsValidCulture(realm) ? realm : "";
    }

    // 改从 target 时会不会旧制犹存：target 的文明等级低于现用的制度文化，且不属例外
    public static bool WouldKeep(Kingdom kingdom, string current, string target)
    {
        if (kingdom?.data == null || kingdom.isRekt() || !CultureService.IsValidCulture(current) ||
            !CultureService.IsValidCulture(target) || string.Equals(current, target, StringComparison.Ordinal)) return false;
        if (Exempt(kingdom)) return false;
        return InstitutionSystem.GetCultureLevel(target) < InstitutionSystem.GetCultureLevel(current);
    }

    // 不论旧制时本国该用的制度文化：帝国核心(征服王朝除外)按帝国的制度文化，其余按国家文化
    private static string NaturalInstitutionCulture(Kingdom kingdom)
    {
        Empire empire = CoreEmpireOf(kingdom);
        return empire != null && !CompositeEmpireService.IsComposite(empire)
            ? InstitutionSystem.GetPrimaryCulture(empire, honorLegacy: false)
            : CultureService.GetRealmCulture(kingdom);
    }

    private static Empire CoreEmpireOf(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        return empire?.data != null && !empire.isRekt() && !empire.IsArchived() && empire.CoreKingdom == kingdom ? empire : null;
    }

    // 例外：古战模组的国、新立之国、征服王朝的核心(另有制度文化之分)、手选或被锁定政体的国家(政体本就不随文化动)
    private static bool Exempt(Kingdom kingdom)
    {
        if (AncientWarfareCompatibility.Owns(kingdom)) return true;
        // 立国不满一年(叛乱自立、分封、水土胡化分出来的游牧之国……)：没有旧制可沿，照新文化立制
        if (World.world != null && Date.getYearsSince(kingdom.data.created_time) < 1) return true;
        KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
        if (data.regime_manually_selected || RepublicSystem.IsRegimeLocked(kingdom)) return true;
        Empire empire = CoreEmpireOf(kingdom);
        return empire != null && CompositeEmpireService.IsComposite(empire);
    }

    #endregion

    #region 改俗时定夺

    // 国家文化改了(见 CultureService.SetRealmCulture)：previous 是改之前现用的制度文化。返回 true = 旧制犹存，调用方不再按新文化改政体
    internal static bool OnRealmCultureChanged(Kingdom kingdom, string previous, string target)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return false;
        string held = Stored(kingdom);
        string natural = NaturalInstitutionCulture(kingdom);
        if (WouldKeep(kingdom, previous, natural))
        {
            Keep(kingdom, previous, target, natural);
            return true;
        }
        if (held != null)
            End(kingdom, held, natural, string.Equals(held, natural, StringComparison.Ordinal)
                ? "复归旧制所属之俗"
                : "改从的文化不落后，一步改行新制");
        return false;
    }

    // 不留旧制的改俗(新立之国、胡化、征服王朝、神力改俗、归化改制)：原有的旧制一并作罢
    internal static void Drop(Kingdom kingdom, string target)
    {
        if (Stored(kingdom) is not string held) return;
        End(kingdom, held, target, "这次改俗不留旧制");
    }

    private static void Keep(Kingdom kingdom, string legacy, string target, string natural)
    {
        kingdom.GetOrCreate().institution_culture = legacy;
        LogService.LogInfo($"{Tag} {kingdom.GetKingdomName()}({kingdom.id}) 改从 {target}，制度文化 {natural}(文明{Level(natural)}) " +
                           $"不及 {legacy}(文明{Level(legacy)})，仍沿用旧制，政体 {kingdom.GetRegime()?.type}");
        string content = string.Format(LM.Get("legacy_institution_kept_history"), RealmName(kingdom), Translate(target),
            Translate(legacy));
        TranslateHelper.LogEventMessage(content, kingdom);
    }

    private static void End(Kingdom kingdom, string legacy, string natural, string reason)
    {
        kingdom.GetOrCreate().institution_culture = "";
        LogService.LogInfo($"{Tag} {kingdom.GetKingdomName()}({kingdom.id}) 不再沿用 {legacy} 旧制：{reason}，制度改照 {natural}");
    }

    #endregion

    #region 年度：新文化追上了就改行新制

    // 登记在城市钩子的年度扫描里(见 CityPatch.WorldYearlyScans"旧制")；只看存着旧制的国家
    public static void TryYearlyScan()
    {
        if (World.world?.kingdoms == null || ModClass.IS_CLEAR) return;
        double now = World.world.getCurWorldTime();
        if (!ReferenceEquals(_scanWorld, World.world) || now < _lastScan)
        {
            _scanWorld = World.world;
            _lastScan = now;
            return;
        }
        if (Date.getYearsSince(_lastScan) < 1) return;
        _lastScan = now;
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt() ||
                string.IsNullOrEmpty(kingdom.GetOrCreate().institution_culture)) continue;
            try
            {
                Check(kingdom);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"{Tag} {kingdom.data?.name} 旧制结算失败: {exception.Message}");
            }
        }
    }

    private static void Check(Kingdom kingdom)
    {
        string legacy = Stored(kingdom);
        // 配置里已没有这个文化
        if (legacy == null)
        {
            kingdom.GetOrCreate().institution_culture = "";
            return;
        }
        string natural = NaturalInstitutionCulture(kingdom);
        if (!CultureService.IsValidCulture(natural)) return;
        if (string.Equals(natural, legacy, StringComparison.Ordinal))
        {
            End(kingdom, legacy, natural, "已复归旧制所属之俗");
            return;
        }
        if (AncientWarfareCompatibility.Owns(kingdom) || CoreEmpireOf(kingdom) is Empire empire && CompositeEmpireService.IsComposite(empire))
        {
            End(kingdom, legacy, natural, "归古战模组或成了征服王朝，另有制度文化");
            return;
        }
        if (InstitutionSystem.GetCultureLevel(natural) < InstitutionSystem.GetCultureLevel(legacy) || WouldRegress(kingdom, natural))
            return;
        CatchUp(kingdom, legacy, natural);
    }

    // 新文化的制度树还停在原始政体，本国已是成形的政体：再等等(同 TryAdvanceRegimeTransition 不往原始政体改)
    private static bool WouldRegress(Kingdom kingdom, string culture)
    {
        RegimeType? current = kingdom.GetRegime()?.type;
        if (current == null || RegimeManager.IsPrimitive(current)) return false;
        return InstitutionSystem.TryResolveCultureRegime(culture, out RegimeType target) && RegimeManager.IsPrimitive(target);
    }

    // 改行新文化之制：政体按新文化反推(帝国核心照易俗改制的写法重算帝国的制度文化)，手选、锁定的政体不动；记一笔
    private static void CatchUp(Kingdom kingdom, string legacy, string natural)
    {
        KingdomExtension.KingdomExtraData data = kingdom.GetOrCreate();
        data.institution_culture = "";
        RegimeType? before = kingdom.GetRegime()?.type;
        Empire empire = CoreEmpireOf(kingdom);
        if (empire != null) InstitutionSystem.ResyncCulture(empire);
        else if (!data.regime_manually_selected && !RepublicSystem.IsRegimeLocked(kingdom))
            CultureService.ApplyCulturePoliticalSystem(kingdom, natural);
        RegimeType? after = kingdom.GetRegime()?.type;
        if (after != before) EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
        if (empire == null) InstitutionSystem.ResyncKingdomEffects(kingdom, regimeReloaded: after != before);
        LogService.LogInfo($"{Tag} {kingdom.GetKingdomName()}({kingdom.id}) {natural}(文明{Level(natural)}) 追上 {legacy}" +
                           $"(文明{Level(legacy)})，不再沿用旧制，政体 {before} -> {after}");
        string content = string.Format(LM.Get("legacy_institution_ended_history"), RealmName(kingdom), Translate(natural),
            Translate(legacy));
        TranslateHelper.LogEventMessage(content, kingdom);
    }

    #endregion

    #region 界面与文字

    // "沿用某文化旧制"一行
    public static string Label(string legacy) => string.Format(LM.Get("legacy_institution_label"), Translate(legacy));

    // 悬停说明：为什么沿用、什么时候作罢
    public static string Tooltip(Kingdom kingdom, string legacy)
    {
        string natural = NaturalInstitutionCulture(kingdom);
        return string.Format(LM.Get("legacy_institution_tip"), Translate(natural), LevelName(natural), Translate(legacy),
            LevelName(legacy));
    }

    // 帝国核心按帝国的名号记，其余按国名
    private static string RealmName(Kingdom kingdom) =>
        CoreEmpireOf(kingdom) is Empire empire ? empire.GetEmpireName() : kingdom.GetKingdomName();

    private static string Translate(string culture) =>
        CultureService.IsValidCulture(culture) && OnomasticsRule.ALL_CULTURE_TRANSLATE.ContainsKey(culture)
            ? culture.GetCultureTranslate()
            : culture ?? "";

    private static int Level(string culture) =>
        CultureService.IsValidCulture(culture) ? InstitutionSystem.GetCultureLevel(culture) : 0;

    private static string LevelName(string culture) =>
        CultureService.IsValidCulture(culture) ? InstitutionSystem.GetCultureRanking(culture).LevelName : "";

    #endregion
}
