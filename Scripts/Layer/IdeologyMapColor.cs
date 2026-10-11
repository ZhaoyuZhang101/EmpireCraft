using EmpireCraft.Scripts.GeneralSystems;

namespace EmpireCraft.Scripts.Layer;

// Transient color source for the ideology map layer (independent of vanilla religion).
// These objects never enter the world's religion collection or the save file.
public sealed class IdeologyMapColorData : MetaObjectData { }

public sealed class IdeologyMapColor : MetaObject<IdeologyMapColorData>
{
    public override MetaType meta_type => EmpireCraft.Scripts.GameClassExtensions.MetaTypeExtension.Ideology;

    public override ColorLibrary getColorLibrary() => AssetManager.culture_colors_library;

    public static IdeologyMapColor Create(PartyIdeology ideology)
    {
        int count = AssetManager.culture_colors_library.list.Count;
        var color = new IdeologyMapColor();
        var data = new IdeologyMapColorData { name = ideology.ToString() };
        data.setColorID(count == 0 ? 0 : (int)ideology * count / 13 % count);
        color.loadData(data);
        return color;
    }
}
