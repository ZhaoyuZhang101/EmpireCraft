using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.services;
using System.Linq;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions.Claims;

public class TempFac_禁党 : TemporaryFaction
{
    public override TemporaryFaction Clone(FixedFaction faction)
    {
        var res = new TempFac_禁党();
        res.Init(faction);
        res.ShowAsPlot = ShowAsPlot;
        res.Hide = Hide;
        res.Active = Active;
        return res;
    }

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        
        Empire empire = GetEmpire();
        if (empire == null)
        {
            End();
            return;
        }

        Regime regime = empire.CoreKingdom?.GetRegime();
        if (regime == null)
        {
            End();
            return;
        }

        // 获取当前主导派系 (革命党)
        FixedFaction dominantFaction = regime.GetDominateFaction();
        
        // 确保是革命党在主导
        if (dominantFaction != null && dominantFaction.Type == FactionType.革命)
        {
            if (regime.PlayerFactions != null)
            {
                foreach (var faction in regime.PlayerFactions)
                {
                    // 禁用所有非革命党派系
                    if (faction.Type != FactionType.革命)
                    {
                        faction.BanFaction(); // 移除成员并清空领袖
                        faction.Ban = true;   // 标记为禁用
                    }
                    else
                    {
                        faction.Ban = false; // 确保革命党未被禁用
                    }
                }
            }
            ActionLibrary.showWhisperTip("革命党已取缔所有其他政党!");
        }

        CountDown = 20; // 冷却20年
        End();
    }

    public override bool CheckCondition()
    {
        Empire empire = GetEmpire();
        if (empire == null) return false;
        
        // 必须是现代政体
        Regime regime = empire.CoreKingdom?.GetRegime();
        if (regime == null || regime.type != RegimeType.Modern) return false;

        // 必须由革命党主导
        FixedFaction dominant = regime.GetDominateFaction();
        if (dominant == null || dominant.Type != FactionType.革命) return false;

        // 检查是否还有其他未被禁用的派系存在
        bool hasOtherFactions = regime.PlayerFactions?.Any(f => f.Type != FactionType.革命 && !f.Ban) ?? false;
        
        return hasOtherFactions;
    }
}
