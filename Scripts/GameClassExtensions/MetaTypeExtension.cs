namespace EmpireCraft.Scripts.GameClassExtensions;

public static class MetaTypeExtension
{
    public const MetaType Empire = (MetaType)100;
    public const MetaType KingdomTitle = (MetaType)101;
    
    public static string ToMetaString(this MetaType type)
    {
        switch (type)
        {
            case (MetaType)100:
                return "Empire";
            case (MetaType)101:
                return "KingdomTitle";
            default:
                return type.ToString();
        }
    }
}