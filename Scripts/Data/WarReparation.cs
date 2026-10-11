namespace EmpireCraft.Scripts.Data;

// 一笔战争赔款(见 WarReparationsSystem)：按月付给收款国，付不起顺延
public sealed class WarReparation
{
    public long creditor_kingdom_id = -1L;
    public long monthly;
    public int months_left;
}
