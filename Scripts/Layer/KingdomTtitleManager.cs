using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.Layer;
public class KingdomTitleManager : MetaSystemManager<KingdomTitle, KingdomTitleData>
{
    public KingdomTitleManager()
    {
        this.type_id = "empire";
    }

    public override void updateDirtyUnits()
    {
    }

    public KingdomTitle newKingdomTitle(Actor pFounder)
    {
        KingdomTitle title = base.newObject();
        title.newKingdomTitle(pFounder);
        return title;
    }
}