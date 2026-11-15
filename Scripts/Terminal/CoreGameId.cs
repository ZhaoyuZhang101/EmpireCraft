namespace EmpireCraft.Scripts.Terminal;

// 这一局的编号(游戏交给服务器核心接手时生成，见 CoreSaveExport)：随模组数据存档，核心那边存档时也照写在 world.json 顶上，
// 游戏与核心据此认"是不是同一局"。没交给过核心的局为 null
public static class CoreGameId
{
    public static string Value;
}
