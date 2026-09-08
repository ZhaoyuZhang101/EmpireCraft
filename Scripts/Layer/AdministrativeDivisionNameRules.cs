using System.Collections.Generic;
using System.Linq;

namespace EmpireCraft.Scripts.Layer;

public static class AdministrativeDivisionNameRules
{
    public static long SelectProvinceNameHolder(long capitalControllerId,
        IEnumerable<long> administrationIds)
    {
        if (administrationIds == null) return -1L;
        List<long> ids = administrationIds.Where(id => id >= 0).Distinct().OrderBy(id => id).ToList();
        if (ids.Count == 0) return -1L;
        return ids.Contains(capitalControllerId) ? capitalControllerId : ids[0];
    }
}
