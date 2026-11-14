// 存读档的三处入口：名字照原版的(模组的存读档补丁挂在这三个方法上)，实现是服务器自己的(见 Server/WorldSave.cs)。
//   存：模组的前置补丁先把它那份数据写进文件夹，这里再写世界核心的；
//   读：这里把"建地形、建国城、建人、接战事"几步排进加载队列，模组的后置补丁接着把它那份数据的步骤排在后面。
//# hand SaveManager: loadActors loadData saveMapData
using System.Runtime.CompilerServices;
using EmpireCraft.Server;

public partial class SaveManager
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static SavedMap saveMapData(string pFolder, bool pCompress = true)
    {
        WorldSave.WriteWorld(pFolder);
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void loadData(SavedMap pData, string pPath = null) => WorldSave.QueueSteps(pPath);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void loadActors() => WorldSave.LoadActors();
}
