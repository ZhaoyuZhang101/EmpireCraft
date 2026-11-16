namespace EmpireCraft.Scripts.GeneralSystems;

// 行军、航行的节拍：世界时间每过一秒结一拍(见 CorpsSystem.Update)。
// 一步跨了几个世界秒(世界走得快、这一步算得久)就把欠的拍补上，一步至多补 MaxPerStep 拍；
// 欠得比这还多(暂停回来、卡了很久)才丢掉余下的、重新对齐。
// 以前一步只结一拍，落后三秒就对齐、把欠的拍丢掉：流速调到十倍、一步跨两秒半时，
// 军队每世界秒只走 0.4 拍，日期走得快、军队反而比常速慢(用户 2026-11-16 "移动速度和进攻时间依旧很慢")
public static class SecondBeats
{
    public const int MaxPerStep = 8;

    // 这一步该结几拍；last 是上一拍的世界时刻(负数为还没开始)，照结的拍数往前推
    public static int Due(ref double last, double now)
    {
        if (last < 0d || now < last)
        {
            last = now;
            return 1;
        }
        double behind = now - last;
        if (behind < 1d) return 0;
        int beats = (int)behind;
        if (beats > MaxPerStep)
        {
            last = now;
            return MaxPerStep;
        }
        // 按整秒往前推，不按本步时刻重置(零头留给下一步，兵牌按一拍一秒插值才不顿)
        last += beats;
        return beats;
    }
}
