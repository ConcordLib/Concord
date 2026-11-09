using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Backend;
using Concord.Emit;
using Xunit;

namespace Concord.HarmonyX.Tests;

public sealed class PositionSubject {
    public int Value;
}

public sealed class PositionInstance {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Sum(int a, int b, int c, int d, int e) {
        return a + b + c + d + e;
    }
}

public static class PositionVictims {
    public static readonly List<string> Ran = [];

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Step(int value) {
        return value + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Heads() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Tails() {
        return 2;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ReturnsSeven() {
        return 7;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Wrapped(int seed) {
        return seed * 2;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Throws() {
        throw new InvalidOperationException("victim");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Guarded() {
        return 11;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int HasLiteral() {
        return Step(41);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int CallsStep() {
        return Step(1);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int CallsStepShifted() {
        return Step(1);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int MakesSubject() {
        PositionSubject subject = new PositionSubject { Value = 3 };
        return subject.Value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int StoresLocal() {
        int held = 5;
        return held + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Retuned() {
        return 100;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int RetunedFinal() {
        return 100;
    }
}

public static class PositionInjections {
    public static void Note() {
        PositionVictims.Ran.Add("note");
    }

    public static int AroundWrapped(int seed, Operation<int, int> original) {
        PositionVictims.Ran.Add("around:before");
        int inner = original.Invoke(seed);
        PositionVictims.Ran.Add("around:after");
        return inner + 1;
    }

    public static void OnCatch(Exception error, ControlHandle<int> control) {
        PositionVictims.Ran.Add("catch:" + error.GetType().Name);
        control.ReturnValue = 42;
        control.Cancel();
    }

    public static int BumpLiteral(int original) {
        int seen = original;
        PositionVictims.Ran.Add("constant:" + seen.ToString(CultureInfo.InvariantCulture));
        return seen + 1;
    }

    public static int HoldPlusTen(int original) {
        int seen = original;
        PositionVictims.Ran.Add("local:" + seen.ToString(CultureInfo.InvariantCulture));
        return seen + 10;
    }

    public static int ClampArg(int original) {
        int seen = original;
        PositionVictims.Ran.Add("arg:" + seen.ToString(CultureInfo.InvariantCulture));
        return seen + 10;
    }

    public static IEnumerable<CodeInstruction> Retune(IEnumerable<CodeInstruction> instructions) {
        PositionVictims.Ran.Add("transpiler");
        foreach (CodeInstruction instruction in instructions) {
            if (LoadsHundred(instruction)) {
                yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Ldc_I4, 200);
                continue;
            }

            yield return instruction;
        }
    }

    private static bool LoadsHundred(CodeInstruction instruction) {
        return instruction.operand is sbyte or byte or short or int
            && Convert.ToInt64(instruction.operand, CultureInfo.InvariantCulture) == 100L;
    }
}

public sealed class PositionTests : IDisposable {
    private readonly List<string> lines = [];

    private readonly Action<string> logged = HarmonyXBackend.Log;

    private readonly List<IBackendHandle> handles = [];

    public PositionTests() {
        HarmonyXBackend.Log = line => lines.Add(line);
        HarmonyXEntryPoint.Install(line => lines.Add(line), null);
        PositionVictims.Ran.Clear();
    }

    public void Dispose() {
        foreach (IBackendHandle handle in handles) {
            handle.Dispose();
        }

        HarmonyXBackend.Log = logged;
        PositionVictims.Ran.Clear();
    }

    [Fact]
    public void AHeadInjectionRunsBeforeTheVictimsBody() {
        MethodInfo target = Victim(nameof(PositionVictims.Heads));

        Applied(Patch(target, nameof(PositionInjections.Note), new InjectAt.Head()));

        Assert.Equal(1, PositionVictims.Heads());
        Assert.Equal(new[] { "note" }, PositionVictims.Ran.ToArray());
    }

    [Fact]
    public void ATailInjectionRunsAfterTheVictimsBody() {
        MethodInfo target = Victim(nameof(PositionVictims.Tails));

        Applied(Patch(target, nameof(PositionInjections.Note), new InjectAt.Tail()));

        Assert.Equal(2, PositionVictims.Tails());
        Assert.Equal(new[] { "note" }, PositionVictims.Ran.ToArray());
    }

    [Fact]
    public void AReturnInjectionRunsAfterTheVictimReturns() {
        MethodInfo target = Victim(nameof(PositionVictims.ReturnsSeven));

        Applied(Patch(target, nameof(PositionInjections.Note), new InjectAt.Return()));

        Assert.Equal(7, PositionVictims.ReturnsSeven());
        Assert.Equal(new[] { "note" }, PositionVictims.Ran.ToArray());
    }

    [Fact]
    public void AnAroundInjectionWrapsTheVictimAndSeesItsResult() {
        MethodInfo target = Victim(nameof(PositionVictims.Wrapped));

        Applied(Patch(target, nameof(PositionInjections.AroundWrapped), new InjectAt.Around()));

        Assert.Equal(21, PositionVictims.Wrapped(10));
        Assert.Equal(new[] { "around:before", "around:after" }, PositionVictims.Ran.ToArray());
    }

    [Fact]
    public void AFinallyInjectionRunsWhenTheVictimLeaves() {
        MethodInfo target = Victim(nameof(PositionVictims.Guarded));

        Applied(Patch(target, nameof(PositionInjections.Note), new InjectAt.Finally()));

        Assert.Equal(11, PositionVictims.Guarded());
        Assert.Equal(new[] { "note" }, PositionVictims.Ran.ToArray());
    }

    [Fact]
    public void ACatchInjectionSeesTheThrownExceptionAndCanReplaceTheResult() {
        MethodInfo target = Victim(nameof(PositionVictims.Throws));

        Applied(Patch(target, nameof(PositionInjections.OnCatch), new InjectAt.Catch()));

        Assert.Equal(42, PositionVictims.Throws());
        Assert.Equal(new[] { "catch:InvalidOperationException" }, PositionVictims.Ran.ToArray());
    }

    [Fact]
    public void AConstantInjectionRewritesTheInlinedLiteral() {
        MethodInfo target = Victim(nameof(PositionVictims.HasLiteral));

        Applied(Patch(target, nameof(PositionInjections.BumpLiteral), new InjectAt.Constant(41)));

        Assert.Equal(43, PositionVictims.HasLiteral());
        Assert.Equal(new[] { "constant:41" }, PositionVictims.Ran.ToArray());
    }

    [Fact]
    public void AnInvokeInjectionAtTheHeadShiftRunsBeforeTheCall() {
        MethodInfo target = Victim(nameof(PositionVictims.CallsStep));

        Applied(Patch(
            target,
            nameof(PositionInjections.Note),
            new InjectAt.Invoke(typeof(PositionVictims), nameof(PositionVictims.Step), At.Head)));

        Assert.Equal(2, PositionVictims.CallsStep());
        Assert.Equal(new[] { "note" }, PositionVictims.Ran.ToArray());
    }

    [Fact]
    public void AnInvokeInjectionAtTheArgumentShiftRunsBeforeTheArgumentIsPushed() {
        MethodInfo target = Victim(nameof(PositionVictims.CallsStepShifted));

        Applied(Patch(
            target,
            nameof(PositionInjections.ClampArg),
            new InjectAt.Invoke(typeof(PositionVictims), nameof(PositionVictims.Step), At.Argument, 0, null, 1)));

        Assert.Equal(12, PositionVictims.CallsStepShifted());
        Assert.Equal(new[] { "arg:1" }, PositionVictims.Ran.ToArray());
    }

    [Fact]
    public void ANewObjInjectionRunsAtTheConstructionSite() {
        MethodInfo target = Victim(nameof(PositionVictims.MakesSubject));

        Applied(Patch(
            target,
            nameof(PositionInjections.Note),
            new InjectAt.NewObj(typeof(PositionSubject), At.Head)));

        Assert.Equal(3, PositionVictims.MakesSubject());
        Assert.Equal(new[] { "note" }, PositionVictims.Ran.ToArray());
    }

    [Fact]
    public void ALocalInjectionRunsAtTheStoreItSelects() {
        MethodInfo target = Victim(nameof(PositionVictims.StoresLocal));

        Applied(Patch(
            target,
            nameof(PositionInjections.HoldPlusTen),
            new InjectAt.Local(typeof(int), LocalAccess.Store, Ordinal: 1)));

        Assert.Equal(16, PositionVictims.StoresLocal());
        Assert.Equal(new[] { "local:5" }, PositionVictims.Ran.ToArray());
    }

    [Fact]
    public void ATranspilerRewritesTheVictimsOwnIl() {
        MethodInfo target = Victim(nameof(PositionVictims.Retuned));

        Applied(Patch(target, nameof(PositionInjections.Retune), new InjectAt.Transpiler()));

        Assert.Equal(200, PositionVictims.Retuned());
        Assert.Contains("transpiler", PositionVictims.Ran);
    }

    [Fact]
    public void ATranspilerFinalRewritesTheComposedBody() {
        MethodInfo target = Victim(nameof(PositionVictims.RetunedFinal));

        Applied(Patch(target, nameof(PositionInjections.Retune), new InjectAt.Transpiler(true)));

        Assert.Equal(200, PositionVictims.RetunedFinal());
        Assert.Contains("transpiler", PositionVictims.Ran);
    }

    [Fact]
    public void AnInstanceVictimWithHighArgumentSlotsStillComposes() {
        MethodInfo target = typeof(PositionInstance).GetMethod(nameof(PositionInstance.Sum))!;

        Applied(Patch(target, nameof(PositionInjections.Note), new InjectAt.Head()));

        Assert.Equal(15, new PositionInstance().Sum(1, 2, 3, 4, 5));
        Assert.Equal(new[] { "note" }, PositionVictims.Ran.ToArray());
    }

    private static MethodInfo Victim(string name) {
        return typeof(PositionVictims).GetMethod(name)!;
    }

    private void Applied(IBackendHandle handle) {
        Assert.True(
            handle.IsApplied,
            "the handle reports IsApplied false, so this position never reached the backend and the assertions " +
            "below would pass against the unpatched body. " + Lines());
    }

    private IBackendHandle Patch(MethodBase target, string injection, InjectAt at) {
        IBackendHandle handle = PatchBackend.Current.ApplyComposed(
            target,
            new[] {
                new Injection(typeof(PositionInjections).GetMethod(injection)!, at, "position", 0),
            });

        handles.Add(handle);
        return handle;
    }

    private string Lines() {
        return lines.Count == 0 ? "Nothing was logged." : "Logged: " + string.Join(" | ", lines);
    }
}
