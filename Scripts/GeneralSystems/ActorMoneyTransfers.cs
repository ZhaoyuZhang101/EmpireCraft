using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace EmpireCraft.Scripts.GeneralSystems;

// Main-thread wallet transfers. Known advances and refunds are not earned income.
// Thread-local markers prevent unrelated actors' receipts from being suppressed.
public static class ActorMoneyTransfers
{
    private sealed class ReferenceComparer : IEqualityComparer<Actor>
    {
        public bool Equals(Actor a, Actor b) => ReferenceEquals(a, b);
        public int GetHashCode(Actor actor) => RuntimeHelpers.GetHashCode(actor);
    }

    [ThreadStatic] private static HashSet<Actor> _nonIncomeCredits;

    public static bool IsNonIncomeCredit(Actor actor) => actor != null &&
        _nonIncomeCredits?.Contains(actor) == true;

    public static int CreditNonIncome(Actor actor, int requested)
    {
        if (actor?.data == null || requested <= 0) return 0;
        int before = actor.money;
        _nonIncomeCredits ??= new HashSet<Actor>(new ReferenceComparer());
        bool ownsMarker = _nonIncomeCredits.Add(actor);
        try
        {
            actor.addMoney(requested);
            return Received(before, actor.money, requested);
        }
        catch
        {
            // addMoney may mutate the wallet before an effect/other callback throws.
            // Roll back only this advance, without firing another failing effect.
            if (actor.data != null) actor.data.money -= Received(before, actor.money, requested);
            throw;
        }
        finally
        {
            if (ownsMarker) _nonIncomeCredits.Remove(actor);
        }
    }

    public static int Received(int before, int after, int requested) =>
        (int)Math.Max(0L, Math.Min(Math.Max(0, requested), (long)after - before));

    // An asset purchase transfers existing cash, not occupational income. Commit
    // ownership only after both wallets settled the full agreed price.
    public static bool TryTransferNonIncome(Actor payer, Actor recipient, int amount)
    {
        if (payer?.data == null || recipient?.data == null || ReferenceEquals(payer, recipient) ||
            amount <= 0 || payer.money < amount || recipient.money < 0) return false;
        int before = payer.money;
        int credited = 0;
        bool committed = false;
        try
        {
            payer.addMoney(-amount);
            if ((long)before - payer.money != amount) return false;
            credited = CreditNonIncome(recipient, amount);
            committed = credited == amount;
            return committed;
        }
        finally
        {
            if (!committed)
            {
                // Vanilla effects can throw after mutating a wallet. Undo the
                // actual transfer without invoking those effects a second time.
                if (credited > 0 && recipient.data != null) recipient.data.money -= credited;
                if (payer.data != null) payer.data.money = before;
            }
        }
    }
}
