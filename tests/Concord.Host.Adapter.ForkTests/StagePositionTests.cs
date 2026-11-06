using System.Reflection;
using System.Runtime.CompilerServices;
using Concord;
using Concord.Detour;
using Concord.Emit;
using Concord.Orchestration;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public sealed class StagePositionSubject {
    public int Value;
}

public static class StagePositionVictims {
    public static readonly List<string> Ran = [];

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Step(int value) {
        return value + 1;
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
    public static int MakesSubject() {
        StagePositionSubject subject = new StagePositionSubject { Value = 3 };
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

public static class StagePositionInjections {
    public static void Note() {
        StagePositionVictims.Ran.Add("note");
    }

    public static int AroundWrapped(int seed, Operation<int, int> original) {
        StagePositionVictims.Ran.Add("around:before");
        int inner = original.Invoke(seed);
        StagePositionVictims.Ran.Add("around:after");
        return inner + 1;
    }

    public static void OnCatch(Exception error, ControlHandle<int> control) {
        StagePositionVictims.Ran.Add("catch:" + error.GetType().Name);
        control.ReturnValue = 42;
        control.Cancel();
    }

    public static int BumpLiteral(int original) {
        int seen = original;
        StagePositionVictims.Ran.Add("constant:" + seen);
        return seen + 1;
    }

    public static int HoldPlusTen(int original) {
        int seen = original;
        StagePositionVictims.Ran.Add("local:" + seen);
        return seen + 10;
    }

    public static int ClampArg(int original) {
        int seen = original;
        StagePositionVictims.Ran.Add("arg:" + seen);
        return seen + 10;
    }

    public static IEnumerable<CodeInstruction> Retune(IEnumerable<CodeInstruction> instructions) {
        StagePositionVictims.Ran.Add("transpiler");
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
            && Convert.ToInt64(instruction.operand, System.Globalization.CultureInfo.InvariantCulture) == 100L;
    }
}

[Collection(ForkFieldInjection.ApplyCollection)]
public sealed class StagePositionTests : IDisposable {
    private const string NoPipeline = "No transformer pipeline here, so nothing can be staged over a method body.";

    private readonly Action<string> logged = WaylithDetourBackend.Log;

    private readonly List<string> lines = [];

    private readonly List<MethodBase> patched = [];

    public StagePositionTests() {
        WaylithDetourBackend.Log = line => lines.Add(line);
        StagePositionVictims.Ran.Clear();
    }

    public static bool Unavailable => ForkRuntime.Unavailable;

    public void Dispose() {
        WaylithDetourBackend.Log = logged;
        foreach (MethodBase target in patched) {
            ForkRuntime.Restore(target);
        }

        patched.Clear();
        StagePositionVictims.Ran.Clear();
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void AReturnInjectionRunsAfterTheVictimReturns() {
        MethodInfo target = Victim(nameof(StagePositionVictims.ReturnsSeven));

        IPatchHandle patch = Stage(target, nameof(StagePositionInjections.Note), new InjectAt.Return());

        Applied(patch);
        Assert.Equal(7, StagePositionVictims.ReturnsSeven());
        Assert.Equal(new[] { "note" }, StagePositionVictims.Ran.ToArray());
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void AnAroundInjectionWrapsTheVictimAndSeesItsResult() {
        MethodInfo target = Victim(nameof(StagePositionVictims.Wrapped));

        IPatchHandle patch = Stage(target, nameof(StagePositionInjections.AroundWrapped), new InjectAt.Around());

        Applied(patch);
        Assert.Equal(21, StagePositionVictims.Wrapped(10));
        Assert.Equal(new[] { "around:before", "around:after" }, StagePositionVictims.Ran.ToArray());
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void AFinallyInjectionRunsWhenTheVictimLeaves() {
        MethodInfo target = Victim(nameof(StagePositionVictims.Guarded));

        IPatchHandle patch = Stage(target, nameof(StagePositionInjections.Note), new InjectAt.Finally());

        Applied(patch);
        Assert.Equal(11, StagePositionVictims.Guarded());
        Assert.Equal(new[] { "note" }, StagePositionVictims.Ran.ToArray());
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void ACatchInjectionSeesTheThrownExceptionAndCanReplaceTheResult() {
        MethodInfo target = Victim(nameof(StagePositionVictims.Throws));

        IPatchHandle patch = Stage(target, nameof(StagePositionInjections.OnCatch), new InjectAt.Catch());

        Applied(patch);
        Assert.Equal(42, StagePositionVictims.Throws());
        Assert.Equal(new[] { "catch:InvalidOperationException" }, StagePositionVictims.Ran.ToArray());
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void AConstantInjectionRewritesTheInlinedLiteral() {
        MethodInfo target = Victim(nameof(StagePositionVictims.HasLiteral));

        IPatchHandle patch = Stage(target, nameof(StagePositionInjections.BumpLiteral), new InjectAt.Constant(41));

        Applied(patch);
        Assert.Equal(43, StagePositionVictims.HasLiteral());
        Assert.Equal(new[] { "constant:41" }, StagePositionVictims.Ran.ToArray());
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void AnInvokeInjectionAtTheArgumentShiftRunsBeforeTheArgumentIsPushed() {
        MethodInfo target = Victim(nameof(StagePositionVictims.CallsStep));

        IPatchHandle patch = Stage(
            target,
            nameof(StagePositionInjections.ClampArg),
            new InjectAt.Invoke(typeof(StagePositionVictims), nameof(StagePositionVictims.Step), At.Argument, 0, null, 1));

        Applied(patch);
        Assert.Equal(12, StagePositionVictims.CallsStep());
        Assert.Equal(new[] { "arg:1" }, StagePositionVictims.Ran.ToArray());
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void ANewObjInjectionRunsAtTheConstructionSite() {
        MethodInfo target = Victim(nameof(StagePositionVictims.MakesSubject));

        IPatchHandle patch = Stage(
            target,
            nameof(StagePositionInjections.Note),
            new InjectAt.NewObj(typeof(StagePositionSubject), At.Head));

        Applied(patch);
        Assert.Equal(3, StagePositionVictims.MakesSubject());
        Assert.Equal(new[] { "note" }, StagePositionVictims.Ran.ToArray());
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void ALocalInjectionRunsAtTheStoreItSelects() {
        MethodInfo target = Victim(nameof(StagePositionVictims.StoresLocal));

        IPatchHandle patch = Stage(
            target,
            nameof(StagePositionInjections.HoldPlusTen),
            new InjectAt.Local(typeof(int), LocalAccess.Store));

        Applied(patch);
        Assert.Equal(16, StagePositionVictims.StoresLocal());
        Assert.Equal(new[] { "local:5" }, StagePositionVictims.Ran.ToArray());
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void ATranspilerRewritesTheVictimsOwnIl() {
        MethodInfo target = Victim(nameof(StagePositionVictims.Retuned));

        IPatchHandle patch = Stage(target, nameof(StagePositionInjections.Retune), new InjectAt.Transpiler());

        Applied(patch);
        Assert.Equal(200, StagePositionVictims.Retuned());
        Assert.Contains("transpiler", StagePositionVictims.Ran);
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void ATranspilerFinalRewritesTheComposedBody() {
        MethodInfo target = Victim(nameof(StagePositionVictims.RetunedFinal));

        IPatchHandle patch = Stage(target, nameof(StagePositionInjections.Retune), new InjectAt.Transpiler(true));

        Applied(patch);
        Assert.Equal(200, StagePositionVictims.RetunedFinal());
        Assert.Contains("transpiler", StagePositionVictims.Ran);
    }

    private static MethodInfo Victim(string name) {
        return typeof(StagePositionVictims).GetMethod(name)!;
    }

    private void Applied(IPatchHandle patch) {
        Assert.True(
            patch.IsApplied,
            "the handle reports IsApplied false, so this position never reached the stage and the assertions " +
            "below would pass against the unpatched body. " + Lines());
    }

    private IPatchHandle Stage(MethodBase target, string injection, InjectAt at) {
        IPatchHandle handle = ForkRuntime.Track(
            target,
            Patcher.PatchInjection(
                target,
                new Injection(typeof(StagePositionInjections).GetMethod(injection)!, at, "stage-position", 0)));
        patched.Add(target);
        return handle;
    }

    private string Lines() {
        return lines.Count == 0 ? "Nothing was logged." : "Logged: " + string.Join(" | ", lines);
    }
}
