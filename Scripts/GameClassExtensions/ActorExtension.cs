namespace EmpireCraft.Scripts.GameClassExtensions;
public static class ActorExtension
{
    public class ActorExtraData:ExtraDataBase
    {
        // todo: 添加需要存储的角色数据
    }
    
    public static ActorExtraData GetOrCreate(this Actor a, bool isSave=false)
    {
        var ed = a.GetOrCreate< Actor, ActorExtraData>(isSave);
        return ed;
    } 
    public static void Clear()
    {
        ExtensionManager<Actor, ActorExtraData>.Clear();
    }
}