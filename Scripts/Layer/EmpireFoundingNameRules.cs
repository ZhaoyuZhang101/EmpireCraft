using System;

namespace EmpireCraft.Scripts.Layer;

public enum EmpireFoundingNameSource
{
    MainTitle,
    AncestralEmpire,
    AncestralTitle,
    OwnedTitle,
    EmpireCore,
    KingdomName
}

public sealed class EmpireFoundingNameChoice
{
    public string Name { get; }
    public EmpireFoundingNameSource Source { get; }
    public string SourceName { get; }

    public string SourceLocalizationKey => Source switch
    {
        EmpireFoundingNameSource.MainTitle => "empire_name_source_main_title",
        EmpireFoundingNameSource.AncestralEmpire => "empire_name_source_ancestral_empire",
        EmpireFoundingNameSource.AncestralTitle => "empire_name_source_ancestral_title",
        EmpireFoundingNameSource.OwnedTitle => "empire_name_source_owned_title",
        EmpireFoundingNameSource.EmpireCore => "empire_name_source_empire_core",
        _ => "empire_name_source_kingdom_name"
    };

    public EmpireFoundingNameChoice(string name, EmpireFoundingNameSource source)
    {
        Name = name ?? "";
        Source = source;
        SourceName = Name;
    }
}

public static class EmpireFoundingNameRules
{
    public static EmpireFoundingNameChoice Select(string mainTitleName, string ancestralEmpireName,
        string ancestralTitleName, string ownedTitleName, string empireCoreName, string kingdomName)
    {
        if (!string.IsNullOrWhiteSpace(mainTitleName))
            return new EmpireFoundingNameChoice(mainTitleName, EmpireFoundingNameSource.MainTitle);
        if (!string.IsNullOrWhiteSpace(ancestralEmpireName))
            return new EmpireFoundingNameChoice(ancestralEmpireName, EmpireFoundingNameSource.AncestralEmpire);
        if (!string.IsNullOrWhiteSpace(ancestralTitleName))
            return new EmpireFoundingNameChoice(ancestralTitleName, EmpireFoundingNameSource.AncestralTitle);
        if (!string.IsNullOrWhiteSpace(ownedTitleName))
            return new EmpireFoundingNameChoice(ownedTitleName, EmpireFoundingNameSource.OwnedTitle);
        if (!string.IsNullOrWhiteSpace(empireCoreName))
            return new EmpireFoundingNameChoice(empireCoreName, EmpireFoundingNameSource.EmpireCore);
        return new EmpireFoundingNameChoice(kingdomName, EmpireFoundingNameSource.KingdomName);
    }
}
