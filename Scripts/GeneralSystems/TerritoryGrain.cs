using System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 分地时每块地各自的走路代价(用户 2026-11-04 "我是想要前几版的版图效果,哪怕开了无人区也要保持这种边界风格")：
// 以前的版图是城一块块领出来的，边界在地块这一级就是碎的，平滑国界一画就是顺着地势的弯；剧本和无人区是按远近一次分完，
// 平地上最省的路是笔直往前走，分界成了一条条直边，平滑只能把角磨圆。在存档上比过：相邻地块连着起伏的噪声、每城不同速度、
// 扭曲坐标算远近、分完再挪边界，都到不了自然生长那局的样子，只有每块地的代价互不相干才到得了——同"谁先领到归谁"是一回事：
// 代价是领地的先后，有的地块很快领到，有的要等很久。
//   · 指数分布：多数地块很省，少数很费，路会绕开费的地块，分界跟着绕；
//   · 算出来的代价只用来分地(谁领哪块)，不用来量远近：单块代价会到两位数，算进"离辖地多远"，到处都成了该补小国的空地；
//   · 平地上连走多步，平均一步的实际代价约为 1(路会挑省的走，比均值低得多，Floor、Scale 按这个定)，
//     所以按代价定的铺地预算、走多远的上限还是原来的范围。
public static class TerritoryGrain
{
    private const double Floor = 0.15d, Scale = 2.2d, MaxDraw = 0.999d;

    // 同样的坐标和种子永远同样的结果
    public static float At(int x, int y, int seed) =>
        (float)(Floor + Scale * -Math.Log(1d - Math.Min(Draw(x, y, seed), MaxDraw)));

    private static double Draw(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215d;
        }
    }
}
