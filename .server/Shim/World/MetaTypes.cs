// 各类对象报自己是什么(同原版：城报 City、国报 Kingdom……)。模组拿它分辨一个"集体"是城还是国(选官、史书、外交都用)
public partial class Actor
{
    public override MetaType meta_type { get => MetaType.Unit; set { } }
}

public partial class Kingdom
{
    public override MetaType meta_type { get => MetaType.Kingdom; set { } }
}

public partial class City
{
    public override MetaType meta_type { get => MetaType.City; set { } }
}

public partial class Clan
{
    public override MetaType meta_type { get => MetaType.Clan; set { } }
}

public partial class Culture
{
    public override MetaType meta_type { get => MetaType.Culture; set { } }
}

public partial class Religion
{
    public override MetaType meta_type { get => MetaType.Religion; set { } }
}

public partial class Language
{
    public override MetaType meta_type { get => MetaType.Language; set { } }
}

public partial class Family
{
    public override MetaType meta_type { get => MetaType.Family; set { } }
}

public partial class War
{
    public override MetaType meta_type { get => MetaType.War; set { } }
}

public partial class Alliance
{
    public override MetaType meta_type { get => MetaType.Alliance; set { } }
}

public partial class Army
{
    public override MetaType meta_type { get => MetaType.Army; set { } }
}

public partial class Plot
{
    public override MetaType meta_type { get => MetaType.Plot; set { } }
}

public partial class Subspecies
{
    public override MetaType meta_type { get => MetaType.Subspecies; set { } }
}

// 文化、宗教、语言、亚种各自的人数与该不该撤(同原版：这几类各有自己的一份)。模组在这几处挂了补丁，
// 把背景人口算进去、还有背景人口信着的不撤；方法得声明在各类自己身上，补丁才挂得上
public partial class Culture
{
    [System.Runtime.CompilerServices.MethodImpl(P.N)]
    public override int getPopulationPeople() => base.getPopulationPeople();

    [System.Runtime.CompilerServices.MethodImpl(P.N)]
    public override bool isReadyForRemoval() => units.Count == 0;
}

public partial class Religion
{
    [System.Runtime.CompilerServices.MethodImpl(P.N)]
    public override int getPopulationPeople() => base.getPopulationPeople();

    [System.Runtime.CompilerServices.MethodImpl(P.N)]
    public override int countUnits() => base.countUnits();

    [System.Runtime.CompilerServices.MethodImpl(P.N)]
    public override bool isReadyForRemoval() => units.Count == 0;
}

public partial class Language
{
    [System.Runtime.CompilerServices.MethodImpl(P.N)]
    public override int getPopulationPeople() => base.getPopulationPeople();

    [System.Runtime.CompilerServices.MethodImpl(P.N)]
    public override bool isReadyForRemoval() => units.Count == 0;
}

public partial class Subspecies
{
    [System.Runtime.CompilerServices.MethodImpl(P.N)]
    public override int getPopulationPeople() => base.getPopulationPeople();

    [System.Runtime.CompilerServices.MethodImpl(P.N)]
    public override bool isReadyForRemoval() => units.Count == 0;
}

// 国：没有城、也没有人了就撤(同原版；流亡政府有人在，留着)
public partial class Kingdom
{
    [System.Runtime.CompilerServices.MethodImpl(P.N)]
    public override bool isReadyForRemoval() => !wild && cities.Count == 0 && units.Count == 0;
}
