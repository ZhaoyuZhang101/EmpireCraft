using NeoModLoader.services;

namespace EmpireCraft.Scripts.Regimes.TemporaryFactions;

public class TempFac_收回地盘 : TemporaryFaction
{
    public override long EmpireID { get; protected set; }
    public override long TargetID { get; protected set; }
    public override MetaType TargetType { get; protected set; }

    public override void Execute()
    {
        LogService.LogInfo($"执行{this.type}");
        End();
    }

    public override bool CheckCondition()
    {
        return true;
    }
}
