using EmpireCraft.Scripts.Data;
using NeoModLoader.api;
using NeoModLoader.General;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmpireCraft.Scripts.GamePatches
{
    public interface GamePatch
    {
        public ModDeclare declare { get; set; }
        public void Initialize();
    }

    public static class HelperFunc
    {
        public static string getFamilyName(this Family family, ActorSex sex = ActorSex.None, bool needSexTag = false)
        {

            var nameParts = family.data.name.Split(' ');
            if (ConfigData.speciesCulturePair.TryGetValue(family.data.species_id, out var culture))
            {
                if (OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting))
                {
                    if (nameParts.Length - 1 >= setting.Family.name_pos)
                    {
                        return needSexTag?nameParts[setting.Family.name_pos]+LM.Get($"{culture}_sex_post{sex.ToString()}"): nameParts[setting.Family.name_pos];
                    }
                }
            }
            return needSexTag ? nameParts[0]+ LM.Get($"{culture}_sex_post{sex.ToString()}"): nameParts[0];
        }
    }
}
