namespace EmpireCraft.Scripts.Data;

// 一条战报(见 WarChronicle)：记进这场战争，存进存档
public sealed class WarChronicleEntry
{
    public double time;
    public int kind;
    public string text = "";
}

// 岁供(见 ResourceTributeSystem)：争夺资源的仗没打下产地、攻方占优议和时，败方每年把这种资源送到胜方都城
public sealed class ResourceTribute
{
    public long creditor_kingdom_id = -1L;
    public string resource = "";
    public int amount;
    public int years_left;
    public double last_paid = -1d;
}
