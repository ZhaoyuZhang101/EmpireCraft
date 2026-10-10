namespace EmpireCraft.Scripts.Data;

// Only confirmed existing actor balances, never stock valuations or new income.
// Cash and loot remain separate; nullable owner fields keep old saves unchanged.
public sealed class WalletReserve
{
    public long cash;
    public long loot;
}
