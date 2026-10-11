using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

public enum HuaxiaCourtStage { None, Zhou, Han, Tang, Ming }

// 华夏朝廷从周到明的演进(朝堂博弈的议事会与中央官制随之变化)：
//   · 周：周制政体，天子临朝，三公(太师、太傅、太保)与卿士(司徒、司马、司寇、司士、宗伯)廷议，诸臣直陈；
//   · 秦汉：施行郡县官僚制改行律令制后，中央设三公九卿——丞相总揽政务、定议程，相权可以牵制皇帝的制可；
//     太尉掌兵，御史大夫监察百官(所属派系弹劾更易成)；
//   · 隋唐：施行「三省六部制」，分相权为三：中书起草、门下封驳、尚书领六部施行；
//   · 明：施行「内阁制」，内阁首辅票拟、皇帝批红，阁席由皇帝任命(见 CourtSystem.Year 的内阁部分)。
// 律令制的中央机构按阶段替换：秦汉阶段用三公九卿，此后用律令制配置里的三省六部。
// 本文化制度线里没有这几项制度的(非华夏的律令制)照旧：三省六部加内阁。
public static partial class CourtSystem
{
    public const string ZhongshuOffice = "zhongshusheng_all";
    public const string MenxiaOffice = "menxiasheng_all";
    public const string ShangshuOffice = "shangshusheng_all";
    public const string ChancellorOffice = "chengxiang_all";
    public const string CommandantOffice = "taiwei_all";
    public const string CensorOffice = "yushidafu_all";
    public const string CensorateOffice = "duchayuan_all";

    private const string ExaminationNode = "huaxia_examination_officialdom";
    private const string ThreeDepartmentsNode = "huaxia_three_departments";

    private static readonly string[][] StageKeySets =
    {
        new[] { "court_stage_propose", "court_stage_draft", "court_stage_debate", "court_stage_assent", "court_stage_execute" },
        new[] { "court_stage_propose", "court_stage_draft_zhou", "court_stage_debate_zhou", "court_stage_assent_zhou", "court_stage_execute" },
        new[] { "court_stage_propose", "court_stage_draft_han", "court_stage_debate", "court_stage_assent_han", "court_stage_execute" },
        new[] { "court_stage_propose", "court_stage_draft_three", "court_stage_debate", "court_stage_assent_three", "court_stage_execute_three" },
        new[] { "court_stage_propose", "court_stage_draft_ming", "court_stage_debate", "court_stage_assent_ming", "court_stage_execute" }
    };

    public static string StageKey(Empire empire, int step) => StageKeySets[(int)Stage(empire)][step];

    public static HuaxiaCourtStage Stage(Empire empire)
    {
        RegimeType? type = empire?.CoreKingdom?.GetRegime()?.type;
        if (type == RegimeType.ZhouFeudalism) return HuaxiaCourtStage.Zhou;
        if (type != RegimeType.LvLing) return HuaxiaCourtStage.None;
        bool hasThree = InstitutionSystem.GetLineNodesWithFeature(empire, InstitutionFeatures.ThreeDepartments).Any();
        bool hasCabinet = InstitutionSystem.GetLineNodesWithFeature(empire, InstitutionFeatures.GrandSecretariat).Any();
        if (!hasThree && !hasCabinet) return HuaxiaCourtStage.None;
        if (hasCabinet && InstitutionSystem.HasFeature(empire, InstitutionFeatures.GrandSecretariat)) return HuaxiaCourtStage.Ming;
        if (hasThree && !InstitutionSystem.HasFeature(empire, InstitutionFeatures.ThreeDepartments)) return HuaxiaCourtStage.Han;
        return HuaxiaCourtStage.Tang;
    }

    public static bool UsesThreeDepartments(Empire empire) => Stage(empire) == HuaxiaCourtStage.Tang;

    #region 中央官职

    public static OfficeObject CentralOffice(Empire empire, string pre)
    {
        CenterOffice center = empire?.data?.centerOffice;
        if (center == null || string.IsNullOrEmpty(pre)) return null;
        foreach (long id in (center.CoreOffices ?? new List<long>()).Concat(center.Divisions ?? new List<long>()))
            if (OfficeManager.Offices.TryGetValue(id, out OfficeObject office) && office?.pre == pre) return office;
        return null;
    }

    public static Actor DepartmentHead(Empire empire, string pre) => Holder(CentralOffice(empire, pre));

    private static Actor Holder(OfficeObject office)
    {
        Actor actor = office?.GetActor();
        return actor?.data != null && !actor.isRekt() ? actor : null;
    }

    public static List<OfficeObject> CoreOfficeObjects(Empire empire) => OfficeObjects(empire?.data?.centerOffice?.CoreOffices);

    public static List<OfficeObject> DivisionOfficeObjects(Empire empire) => OfficeObjects(empire?.data?.centerOffice?.Divisions);

    private static List<OfficeObject> OfficeObjects(List<long> ids)
    {
        var result = new List<OfficeObject>();
        foreach (long id in ids ?? new List<long>())
            if (OfficeManager.Offices.TryGetValue(id, out OfficeObject office) && office != null) result.Add(office);
        return result;
    }

    // 周、汉、唐的议事会就是中央各官职的长官(三公卿士 / 三公九卿 / 三省长官与六部尚书)
    private static List<Actor> CentralHolders(Empire empire) =>
        CoreOfficeObjects(empire).Concat(DivisionOfficeObjects(empire)).Select(Holder)
            .Where(actor => actor != null).Distinct().ToList();

    #endregion

    #region 秦汉官制：三公九卿

    private static BureauSetting Office(string pre, int type, int merit, int honorary, params OfficerPowerType[] powers) =>
        new()
        {
            pre = pre, type = type, description = "", powers = powers.ToList(), merit = merit, honorary = honorary,
            select_from_local = false, condition = new List<string>(), leader_select_method = LeaderSelectMethod.Default,
            require_traits = new List<string>()
        };

    private static List<BureauSetting> HanCores() => new()
    {
        Office(ChancellorOffice, 23, 5, 4, OfficerPowerType.拟定, OfficerPowerType.执行),
        Office(CommandantOffice, 24, 5, 4, OfficerPowerType.军事),
        Office(CensorOffice, 25, 5, 4, OfficerPowerType.审核, OfficerPowerType.司法)
    };

    private static List<BureauSetting> HanDivisions() => new()
    {
        Office("taichang_all", 26, 7, 6, OfficerPowerType.教育),
        Office("guanglu_all", 27, 7, 6, OfficerPowerType.人事),
        Office("weiwei_all", 28, 7, 6, OfficerPowerType.军事),
        Office("taipu_all", 29, 7, 6, OfficerPowerType.军事),
        Office("tingwei_all", 30, 7, 6, OfficerPowerType.司法),
        Office("dahonglu_all", 31, 7, 6, OfficerPowerType.礼仪),
        Office("zongzheng_all", 32, 7, 6, OfficerPowerType.礼仪),
        Office("dasinong_all", 33, 7, 6, OfficerPowerType.财政),
        Office("shaofu_all", 34, 7, 6, OfficerPowerType.建设)
    };

    // 明制：洪武废中书省、罢丞相，六部直属天子；都察院、通政使司、大理寺与六部合称大九卿，票拟归内阁
    private static List<BureauSetting> MingCores() => new()
    {
        Office(CensorateOffice, 35, 5, 4, OfficerPowerType.审核, OfficerPowerType.司法),
        Office("tongzhengsi_all", 36, 6, 5, OfficerPowerType.拟定),
        Office("dalisi_all", 37, 6, 5, OfficerPowerType.司法)
    };

    // 律令制核心国在秦汉阶段改用三公九卿、明代改用大九卿；其他情况返回 null(用政体配置)
    public static BureauConfig GetBureauOverride(Kingdom kingdom, BureauConfig fallback)
    {
        Empire empire = kingdom?.GetEmpire();
        if (empire == null || empire.CoreKingdom != kingdom) return null;
        HuaxiaCourtStage stage = Stage(empire);
        if (stage == HuaxiaCourtStage.Ming)
            return new BureauConfig
            {
                cores = MingCores(),
                division = fallback?.division,
                harems = fallback?.harems,
                kingdoms = fallback?.kingdoms,
                cities = fallback?.cities,
                armies = fallback?.armies
            };
        if (stage != HuaxiaCourtStage.Han) return null;
        return new BureauConfig
        {
            cores = HanCores(),
            division = HanDivisions(),
            harems = fallback?.harems,
            kingdoms = fallback?.kingdoms,
            cities = fallback?.cities,
            armies = fallback?.armies
        };
    }

    // 制度阶段变了(施行三省六部制、旧档首次读入)就重建中央机构
    public static void EnsureBureau(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core?.GetRegime()?.type != RegimeType.LvLing) return;
        RepairInstitutionChain(empire);
        HuaxiaCourtStage stage = Stage(empire);
        if (stage == HuaxiaCourtStage.None) return;
        string wanted = stage switch
        {
            HuaxiaCourtStage.Han => "han",
            HuaxiaCourtStage.Ming => "ming",
            _ => "tang"
        };
        CourtState state = State(empire);
        if (state.bureau_stage == wanted) return;
        state.bureau_stage = wanted;
        bool matches = wanted switch
        {
            "han" => CentralOffice(empire, ChancellorOffice) != null,
            "ming" => CentralOffice(empire, CensorateOffice) != null,
            _ => CentralOffice(empire, ZhongshuOffice) != null
        };
        if (matches) return;
        empire.data.centerOffice ??= new CenterOffice();
        empire.data.centerOffice.Init(core);
        Log(state, "court_log_bureau_" + wanted);
        empire.RecordHistory(directContent: string.Format(LM.Get("court_history_bureau_" + wanted), empire.GetEmpireName()),
            kingdomId: core.id);
    }

    // 旧档修复：科举取士如今以三省六部制为前置，已施行科举的文化补记三省六部制(只补状态，不重放效果)
    private static void RepairInstitutionChain(Empire empire)
    {
        string culture = InstitutionSystem.GetPrimaryCulture(empire);
        if (string.IsNullOrEmpty(culture) || !InstitutionSystem.IsEnacted(culture, ExaminationNode) ||
            InstitutionSystem.IsEnacted(culture, ThreeDepartmentsNode) ||
            InstitutionDefinitionRegistry.Get(ThreeDepartmentsNode) == null) return;
        InstitutionSystem.ForceEnactNode(culture, ThreeDepartmentsNode);
    }

    #endregion
}
