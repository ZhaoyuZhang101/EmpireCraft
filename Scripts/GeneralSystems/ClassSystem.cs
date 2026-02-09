using System;
using NeoModLoader.General;

namespace EmpireCraft.Scripts.GeneralSystems;
public enum SocialClass 
{
    Labour,     //工人
    Peasant,    //农民
    Merchant,   //商人
    Army,       //军人
    Officer,    //官僚
    Noble       //贵族 
}
public static class ClassSystem
{
    public static string ToTranslate(this SocialClass socialClass)
    {
        return LM.Get("class_"+socialClass.ToString().ToLower());
    }
}