using System;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_神授君权 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_神授君权();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        res.canBePushByLocal = canBePushByLocal;
        return res;
    }
    
    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        Empire empire = GetEmpire();
        var kingdom = GetKingdomTarget();
        if (empire?.data != null && kingdom?.data != null && !kingdom.isRekt())
        {
            empire.data.directPre = OverallHelperFunc.HolyPrefixKey;
            // 天下之名开着(用户 2026-10-20 选定)：神授君权只加"神圣"，国号的名字归普世之名(教宗认可，见 RealmNamingSystem)；
            // 关着照旧：国号取教宗国都城的法理(原型"神圣罗马")，都城没有法理时国号照旧，只加前缀
            string seatName = EmpireCraft.Scripts.GeneralSystems.RealmNamingSystem.Enabled ? null : kingdom.capital?.GetTitle()?.name;
            empire.SetEmpireName(string.IsNullOrWhiteSpace(seatName) ? empire.GetEmpireName() : seatName);
        }
        
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire == null) return false;
        // 西方封建：确立国教 → 恢复圣地(建立教宗国) → 神授君权，只能向帝国内的教宗国(不是皇帝本国)求得教宗法理，方可称"神圣"
        bool western = empire.CoreKingdom?.GetRegime()?.type == RegimeType.Feudalism;
        if (String.IsNullOrEmpty(empire.data.directPre)&&!empire.Religion.isRekt())
        {
            
            var religionCoreCity = empire.Religion.GetCity();
            var religionKingdom = religionCoreCity?.kingdom;
            if (religionKingdom == null) return false;
            if (western && (religionKingdom == empire.CoreKingdom ||
                            religionKingdom.GetKingdomType() != KingdomType.Feudalism_papal_state)) return false;
            if (religionKingdom.GetRegime()?.GetReligionLevel() == ReligionLevel.High)
            {
                if (religionKingdom.IsInSameEmpire(empire.CoreKingdom))
                {
                    SetKingdomTarget(religionKingdom);
                    return true;
                }
            }
        }
        return false;
    }
}
