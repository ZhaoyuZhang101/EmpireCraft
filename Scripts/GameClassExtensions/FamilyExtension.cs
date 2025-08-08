using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EmpireCraft.Scripts.GameClassExtensions.WarExtension;

namespace EmpireCraft.Scripts.GameClassExtensions;
public static class FamilyExtension
{
    public class FamilyExtraData : ExtraDataBase
    {
        // todo: 添加需要存储的家庭数据
    }
    public static FamilyExtraData GetOrCreate(this Family family, bool isSave=false)
    {
        return family.GetOrCreate<Family, FamilyExtraData>(isSave);
    }
}
