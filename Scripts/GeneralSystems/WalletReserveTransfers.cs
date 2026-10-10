using System;
using EmpireCraft.Scripts.Data;

namespace EmpireCraft.Scripts.GeneralSystems;

// Main-thread ownership transfers. Direct data writes avoid effects/callbacks
// halfway through a transfer and do not record principal as earned income.
public static class WalletReserveTransfers
{
    private const int NativeWalletLimit = 99999;

    public static bool HasAssets(WalletReserve reserve) =>
        reserve != null && (reserve.cash != 0L || reserve.loot != 0L);

    public static bool TryCapture(Actor actor, WalletReserve reserve)
    {
        if (actor?.data == null || reserve == null || actor.money < 0 || actor.data.loot < 0 ||
            reserve.cash < 0L || reserve.loot < 0L) return false;
        int cash = actor.money, loot = actor.data.loot;
        if (reserve.cash > long.MaxValue - cash || reserve.loot > long.MaxValue - loot) return false;
        // Preflight both assets before mutating either side. Zeroing the removed
        // actor also prevents native removal callbacks from taking the same money.
        reserve.cash += cash;
        reserve.loot += loot;
        actor.data.money = 0;
        actor.data.loot = 0;
        return true;
    }

    public static void Restore(Actor actor, WalletReserve reserve)
    {
        if (actor?.data == null || reserve == null || actor.money < 0 || actor.data.loot < 0 ||
            reserve.cash < 0L || reserve.loot < 0L) return;
        int cash = (int)Math.Min(reserve.cash, Math.Max(0L, (long)NativeWalletLimit - actor.money));
        int loot = (int)Math.Min(reserve.loot, Math.Max(0L, (long)NativeWalletLimit - actor.data.loot));
        actor.data.money += cash;
        actor.data.loot += loot;
        reserve.cash -= cash;
        reserve.loot -= loot;
    }

    // Undo only this actor's captured amounts, never the rest of a city's pool.
    // Preserve pre-existing compatibility-mod balances above the native cap.
    public static void RollbackCapture(Actor actor, WalletReserve reserve, int capturedCash, int capturedLoot)
    {
        if (actor?.data == null || reserve == null || actor.money < 0 || actor.data.loot < 0 ||
            reserve.cash < 0L || reserve.loot < 0L) return;
        int cash = (int)Math.Min(reserve.cash, Math.Min(Math.Max(0, capturedCash), (long)int.MaxValue - actor.money));
        int loot = (int)Math.Min(reserve.loot, Math.Min(Math.Max(0, capturedLoot), (long)int.MaxValue - actor.data.loot));
        actor.data.money += cash;
        actor.data.loot += loot;
        reserve.cash -= cash;
        reserve.loot -= loot;
    }

    // 按比例(0~1)把一部分保管款转到另一个账户(迁民带走自己那份)；返回是否成功
    public static bool TryMoveShare(WalletReserve source, WalletReserve destination, double share)
    {
        if (source == null || !HasAssets(source) || share <= 0d) return true;
        if (destination == null || ReferenceEquals(source, destination) || source.cash < 0L || source.loot < 0L ||
            destination.cash < 0L || destination.loot < 0L) return false;
        share = Math.Min(1d, share);
        long cash = (long)Math.Floor(source.cash * share), loot = (long)Math.Floor(source.loot * share);
        if (destination.cash > long.MaxValue - cash || destination.loot > long.MaxValue - loot) return false;
        source.cash -= cash;
        source.loot -= loot;
        destination.cash += cash;
        destination.loot += loot;
        return true;
    }

    // 从背景人口里生成的平民领回人均一份(钱包上限以内)；直接写入钱包，不记为收入
    public static void GrantPerCapita(Actor actor, WalletReserve reserve, double people)
    {
        if (actor?.data == null || !HasAssets(reserve) || people < 1d || actor.money < 0 || actor.data.loot < 0 ||
            reserve.cash < 0L || reserve.loot < 0L) return;
        int cash = (int)Math.Min(Math.Floor(reserve.cash / people), Math.Max(0L, (long)NativeWalletLimit - actor.money));
        int loot = (int)Math.Min(Math.Floor(reserve.loot / people), Math.Max(0L, (long)NativeWalletLimit - actor.data.loot));
        if (cash <= 0 && loot <= 0) return;
        actor.data.money += cash;
        actor.data.loot += loot;
        reserve.cash -= cash;
        reserve.loot -= loot;
    }

    public static bool TryMove(WalletReserve source, WalletReserve destination)
    {
        if (source == null || !HasAssets(source)) return true;
        if (destination == null || ReferenceEquals(source, destination) || source.cash < 0L ||
            source.loot < 0L || destination.cash < 0L || destination.loot < 0L ||
            destination.cash > long.MaxValue - source.cash ||
            destination.loot > long.MaxValue - source.loot) return false;
        destination.cash += source.cash;
        destination.loot += source.loot;
        source.cash = source.loot = 0L;
        return true;
    }
}
