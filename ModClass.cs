using NeoModLoader.api;

namespace CHANGEME
{
    public class ModClass : BasicMod<ModClass>
    {
        protected override void OnModLoad()
        {
            // Load your mod here
            // 加载你的mod内容
            
            // LogInfo(GetConfig()["Default"]["WhatToSay"].TextVal); // Call this only then you confirm it is a text config item
            LogInfo(GetConfig()["Default"]["WhatToSay"].GetValue() as string);
        }
    }
}