using System.Runtime.CompilerServices;

namespace Concord.Orchestration.Tests.ManifestAssembly;

public class MemberTarget {
    private int fuel = 10;

    public int FuelSnapshot => fuel;

    private int Mood { get; } = 2;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Fuel(int delta) {
        return -1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int ReadMood() {
        return -1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Recalc(int add) {
        return -1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public MemberTarget? Me() {
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Visit() {
        return -1;
    }

    private int Recalculate(int add) {
        fuel += add;
        return fuel + Mood;
    }
}

public abstract class InjectFieldMemberPatch : MemberTarget {
    private int injectedFuel;

    private void OnFuel(ControlHandle<int> ch, int delta) {
        injectedFuel += delta;
        ch.ReturnValue = injectedFuel;
        ch.Cancel();
    }
}

public abstract class InjectPropertyMemberPatch : MemberTarget {
    protected abstract int HiddenMood { get; }

    private void OnReadMood(ControlHandle<int> ch) {
        ch.ReturnValue = HiddenMood;
        ch.Cancel();
    }
}

public abstract class InjectMethodMemberPatch : MemberTarget {
    protected abstract int Recompute(int add);

    private void OnRecalc(ControlHandle<int> ch, int add) {
        ch.ReturnValue = Recompute(add);
        ch.Cancel();
    }
}

public abstract class InjectInstanceMemberPatch : MemberTarget {
    protected abstract MemberTarget Self { get; }

    private void OnMe(ControlHandle<MemberTarget?> ch) {
        ch.ReturnValue = Self;
        ch.Cancel();
    }
}

public abstract class AttachedMemberPatch : MemberTarget {
    public int Seen;

    private void OnVisit(ControlHandle<int> ch) {
        Seen++;
        ch.ReturnValue = Seen;
        ch.Cancel();
    }
}
