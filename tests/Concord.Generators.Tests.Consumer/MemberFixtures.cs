using System.Runtime.CompilerServices;

namespace Concord.Generators.Tests.Consumer;

/// <summary>The target the injected member fixtures patch, with private state they reach.</summary>
public class LedgerBook {
    private int balance = 10;

    /// <summary>The true balance.</summary>
    public int BalanceSnapshot => balance;

    private int Fee { get; } = 2;

    /// <summary>Patched by <see cref="DepositFieldPatch" />.</summary>
    /// <param name="amount">The amount to add.</param>
    /// <returns>Minus one when unpatched.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Deposit(int amount) {
        return -1;
    }

    /// <summary>Patched by <see cref="FeePropertyPatch" />.</summary>
    /// <returns>Minus one when unpatched.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int ReadFee() {
        return -1;
    }

    /// <summary>Patched by <see cref="SettleMethodPatch" />.</summary>
    /// <param name="add">The amount to add.</param>
    /// <returns>Minus one when unpatched.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Settle(int add) {
        return -1;
    }

    /// <summary>Patched by <see cref="OwnerInstancePatch" />.</summary>
    /// <returns>Null when unpatched.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public LedgerBook? Owner() {
        return null;
    }

    /// <summary>Patched by <see cref="VisitAttachedPatch" />.</summary>
    /// <returns>Minus one when unpatched.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Visit() {
        return -1;
    }

    private int Recalculate(int add) {
        balance += add;
        return balance + Fee;
    }
}

/// <summary>Writes the private balance through an injected field.</summary>
[Patch]
public abstract class DepositFieldPatch : LedgerBook {
    [InjectField("balance")]
    private int stored;

    [Inject(At.Head, nameof(Deposit))]
    private void OnDeposit(ControlHandle<int> ch, int amount) {
        stored += amount;
        ch.ReturnValue = stored;
        ch.Cancel();
    }
}

/// <summary>Reads the private fee through an injected property.</summary>
[Patch]
public abstract class FeePropertyPatch : LedgerBook {
    [InjectProperty("Fee")]
    protected abstract int HiddenFee { get; }

    [Inject(At.Head, nameof(ReadFee))]
    private void OnReadFee(ControlHandle<int> ch) {
        ch.ReturnValue = HiddenFee;
        ch.Cancel();
    }
}

/// <summary>Calls the private recalculation through an injected method.</summary>
[Patch]
public abstract class SettleMethodPatch : LedgerBook {
    [InjectMethod("Recalculate")]
    protected abstract int Recompute(int add);

    [Inject(At.Head, nameof(Settle))]
    private void OnSettle(ControlHandle<int> ch, int add) {
        ch.ReturnValue = Recompute(add);
        ch.Cancel();
    }
}

/// <summary>Hands back the target instance through an injected instance.</summary>
[Patch]
public abstract class OwnerInstancePatch : LedgerBook {
    [InjectInstance]
    protected abstract LedgerBook Self { get; }

    [Inject(At.Head, nameof(Owner))]
    private void OnOwner(ControlHandle<LedgerBook?> ch) {
        ch.ReturnValue = Self;
        ch.Cancel();
    }
}

/// <summary>Counts visits in state attached to each target instance.</summary>
[Patch]
public abstract class VisitAttachedPatch : LedgerBook {
    /// <summary>The visits of one target instance.</summary>
    [Attached]
    public int Visits;

    [Inject(At.Head, nameof(Visit))]
    private void OnVisit(ControlHandle<int> ch) {
        Visits++;
        ch.ReturnValue = Visits;
        ch.Cancel();
    }
}
