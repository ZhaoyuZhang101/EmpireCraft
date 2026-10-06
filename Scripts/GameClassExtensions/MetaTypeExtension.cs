namespace EmpireCraft.Scripts.GameClassExtensions;

public static class MetaTypeExtension
{
    public const MetaType Empire = (MetaType)100;
    public const MetaType KingdomTitle = (MetaType)101;
    // 理念图层(独立于原版宗教)
    public const MetaType Ideology = (MetaType)102;
    // 民族情绪图层(见 Layer/NationMap.cs)
    public const MetaType Nation = (MetaType)103;
    
    public static string ToMetaString(this MetaType type)
    {
        switch (type)
        {
            case (MetaType)100:
                return "Empire";
            case (MetaType)101:
                return "KingdomTitle";
            case (MetaType)102:
                return "Ideology";
            case (MetaType)103:
                return "Nation";
            default:
                return type.ToString();
        }
    }
}