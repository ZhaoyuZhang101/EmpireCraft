namespace EmpireCraft.Scripts.GameClassExtensions;

public static class MetaTypeExtension
{
    public const MetaType Empire = (MetaType)100;
    public const MetaType KingdomTitle = (MetaType)101;
    // 理念图层(独立于原版宗教)
    public const MetaType Ideology = (MetaType)102;
    // 民族情绪图层(见 Layer/NationMap.cs)
    public const MetaType Nation = (MetaType)103;
    // 矿产图层(见 Layer/MineralMap.cs)
    public const MetaType Mineral = (MetaType)104;
    // 财富图层(见 Layer/WealthMap.cs)
    public const MetaType Wealth = (MetaType)105;
    // 城市图层：只显示模拟的城市(见 Layer/CityViewMap.cs)
    public const MetaType CityView = (MetaType)106;
    
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
            case (MetaType)104:
                return "Mineral";
            case (MetaType)105:
                return "Wealth";
            case (MetaType)106:
                return "CityView";
            default:
                return type.ToString();
        }
    }
}