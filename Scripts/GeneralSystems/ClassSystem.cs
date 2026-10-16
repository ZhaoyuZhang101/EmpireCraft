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
    Noble,      //贵族
    Landlord,   //地主
    Citizen,    //市民；追加在末尾以保持旧存档枚举值稳定
    Slave,      //奴隶(见 SlaverySystem)
    SlaveOwner, //奴隶主
    Guoren,     //国人(见 GuoyeSystem)
    Yeren       //野人
}
public static class ClassSystem
{
    public static string ToTranslate(this SocialClass socialClass)
    {
        return LM.Get("class_"+socialClass.ToString().ToLower());
    }

    // 种地的庶民：农民，以及国野之制下的国人、野人(见 GuoyeSystem)
    public static bool IsPeasantry(this SocialClass socialClass) =>
        socialClass is SocialClass.Peasant or SocialClass.Guoren or SocialClass.Yeren;
}
