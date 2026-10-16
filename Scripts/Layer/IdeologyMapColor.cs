using System;
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
        // 近代十三种照旧均分色板；古代理念插在它们之间(避免同色)
        int modern = (int)PartyIdeology.Confucianism, ancient = Enum.GetValues(typeof(PartyIdeology)).Length - modern;
        int index = (int)ideology < modern ? (int)ideology * count / modern
            : ((int)ideology - modern) * count / Math.Max(1, ancient) + count / (2 * modern);
        data.setColorID(count == 0 ? 0 : index % count);
        color.loadData(data);
        return color;
    }
}
