using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;

// 帝国核心国号：现代政体的国家控制某个帝国核心 80% 以上的城市时，直接以这个核心的名字作国号，
// 国号后缀照旧按执政理念取。比如核心"中华帝国"的现代国家叫"中华人民共和国"。
// 由军阀时期的年度扫描在判定"统一"时调用(WarlordEraSystem.UpdateCore)，不再每个帝国各扫一遍全部核心。
// 同时控制多个核心时优先用本帝国自己的核心。玩家自定义过国号的不改；每个核心只改一次，失去控制后保留已用的国号。
public static class EmpireCoreNamingSystem
{
    private const float ControlShare = 0.8f;

    public static void TryApplyCoreName(Empire empire, EmpireCore core)
    {
        ConstitutionalEconomyState state = empire?.data?.constitutional_economy;
        if (state == null || core == null || empire.CoreKingdom == null || empire.CoreKingdom.isRekt()) return;
        if (empire.CoreKingdom.GetRegime()?.type != RegimeType.Modern) return;
        if (empire.CoreKingdom.HasCustomCountryNaming() || core.false_core_against_empire_id > 0) return;
        EmpireCore own = EmpireCoreManager.Get(empire);
        if (own != null && own != core && EmpireCoreControl.GetControlShare(empire, own) >= ControlShare) return;
        string name = GetCoreBaseName(core);
        if (string.IsNullOrWhiteSpace(name)) return;
        if (core.id == state.named_after_core_id &&
            string.Equals(empire.data.core_name, name, StringComparison.Ordinal)) return;
        state.named_after_core_id = core.id;
        if (string.Equals(empire.data.core_name, name, StringComparison.Ordinal)) return;

        string before = empire.GetEmpireFullName();
        empire.data.core_name = name;
        EventRecorder.Record(empire, string.Format(LM.Get("empire_core_country_name_history"), before,
            EmpireCoreManager.GetDisplayName(core), empire.GetEmpireFullName()));
    }

    // 兼容旧存档：旧版核心名会在末尾附加“帝国”，新核心直接保存无后缀的本名。
    public static string GetCoreBaseName(EmpireCore core)
    {
        return EmpireCoreManager.NormalizeCoreName(EmpireCoreManager.GetDisplayName(core));
    }
}
