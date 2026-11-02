using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public sealed class StaleReportSubject {
    public int Before;
}

public static class StaleReportProbe {
    public static int Counted;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ReadWrapped(StaleReportSubject subject) {
        return subject.Before;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ReadInPlace(StaleReportSubject subject) {
        return -1;
    }

    public static void Count() {
        Counted++;
    }
}

[Collection(ForkFieldInjection.ApplyCollection)]
public sealed class StaleReportTests : IDisposable {
    private const string NoInjection =
        "No field-injection runtime here, so no type can be relaid out and nothing can be reported stale.";

    private const int Attempts = 40;

    private const int SettleMs = 25;

    private static readonly string[] Transient = ["CompileGateBusy", "GCBusy", "GCNotLatched", "NoGCRegions"];

    private static readonly MethodInfo Wrapped = typeof(StaleReportProbe).GetMethod(nameof(StaleReportProbe.ReadWrapped))!;

    private static readonly MethodInfo InPlace = typeof(StaleReportProbe).GetMethod(nameof(StaleReportProbe.ReadInPlace))!;

    private static readonly MethodInfo Counter = typeof(StaleReportProbe).GetMethod(nameof(StaleReportProbe.Count))!;

    private readonly Func<MethodBase, bool>? ownsBody = ReversePatchFactory.OwnsBody;

    private IDetourHandle? patch;

    private bool written;

    public static bool Unavailable =>
        ForkRuntime.Unavailable || ForkFieldInjection.Unavailable || ForkInjection.Unavailable;

    public void Dispose() {
        patch?.Dispose();
        if (written) {
            ForkBodyWriter.Shared.Restore(InPlace);
        }

        ReversePatchFactory.OwnsBody = ownsBody;
        StaleReportProbe.Counted = 0;
    }

    [Fact(Skip = NoInjection, SkipWhen = nameof(Unavailable))]
    public void AStaleReportHandsBackTheDynamicMethodConcordMinted() {
        MethodInfo minted = PatchWrapped();

        ForkFieldInjection.ApplyResult result = ApplyPendingToReadTheResultLists(Field("handsBack"));

        List<MethodBase> reported = ReportsOf(result.StaleDynamicMethods, minted);
        Assert.True(
            reported.Count > 0,
            $"the relayout reported {Describe(result.StaleDynamicMethods)} as stale and " +
            $"{Describe(result.RejittedMethods)} as rejitted, and none of them is Concord's wrapper " +
            $"'{minted.Name}' ({minted.GetType().FullName}). An empty stale list means either the runtime never " +
            "treats a Concord wrapper as dependent on the layout it was compiled against, or another test class's " +
            "ApplyPending drained this batch and got the lists instead. Either way the stale channel cannot be " +
            "the seam phase 3 hangs the handler on.");

        Assert.True(
            reported.Any(one => ReferenceEquals(one, minted)),
            $"the stale list holds {reported.Count} entry/entries named '{minted.Name}' and none of them is the " +
            $"{minted.GetType().FullName} #{minted.GetHashCode()} Concord minted ({Keys(minted)}): " +
            $"{DescribeIdentities(reported, minted)}. Whole stale list: {Describe(result.StaleDynamicMethods)}. " +
            "WaylithClr.FieldInjection.AppendMethods adds keepAlive[i] straight to the list when handles[i] is " +
            "zero and rebuilds a MethodBase through RuntimeType.GetMethodBase otherwise, so a non-identical " +
            "object means the runtime took the handle path and phase 3's handler cannot key on object identity. " +
            "Entries past the first are expected: TargetDetourRegistry roots every wrapper it ever composed and " +
            "never releases one, so each retired wrapper for this target is reported too, resolving to nothing " +
            "because Forget dropped its keys when its patch came off.");
    }

    [Fact(Skip = NoInjection, SkipWhen = nameof(Unavailable))]
    public void ConcordsReverseMapResolvesAStaleReportBackToTheTargetItPatched() {
        MethodInfo minted = PatchWrapped();
        Assert.True(
            MethodIdentity.ResolveOriginal(minted) is not null,
            $"Concord's reverse map does not resolve the wrapper '{minted.Name}' it just minted, so this fact " +
            "cannot say anything about the stale report. TargetDetourRegistry.Recompose calls " +
            "MethodIdentity.Remember after it applies the detour, so a null here means the patch never landed.");

        ForkFieldInjection.ApplyResult result = ApplyPendingToReadTheResultLists(Field("resolves"));

        List<MethodBase> reported = ReportsOf(result.StaleDynamicMethods, minted);
        Assert.True(
            reported.Count > 0,
            $"nothing matching Concord's wrapper '{minted.Name}' was reported stale, so the reverse map was never " +
            $"asked. Stale: {Describe(result.StaleDynamicMethods)}. Rejitted: {Describe(result.RejittedMethods)}.");

        bool resolved = reported.Any(one => MethodIdentity.ResolveOriginal(one) is { } target
            && target.Name == Wrapped.Name
            && target.DeclaringType == Wrapped.DeclaringType);

        Assert.True(
            resolved,
            $"MethodIdentity.ResolveOriginal maps no stale report named '{minted.Name}' back to " +
            $"'{Wrapped.DeclaringType?.FullName}.{Wrapped.Name}'. Reports: {DescribeIdentities(reported, minted)}. " +
            $"Remember stored {Remembered()} for that target, and the minted wrapper itself keys to {Keys(minted)}. " +
            "The reverse map is keyed by runtime handle and entry point, so a mismatch means a stale report " +
            "carries neither of the two identities Concord recorded, and the phase 3 handler needs a different " +
            "reverse map than this one. One live report has to resolve; retired wrappers this target was patched " +
            "with earlier are reported alongside it and correctly resolve to nothing.");
    }

    [Fact(Skip = NoInjection, SkipWhen = nameof(Unavailable))]
    public void AReplacedBodyIsRejittedWhileTheWrapperOverTheSameFieldIsReportedStale() {
        MethodInfo minted = PatchWrapped();
        WriteInPlaceReadOfBefore();

        ForkFieldInjection.ApplyResult result = ApplyPendingToReadTheResultLists(Field("bothLists"));

        bool replacedRejitted = Named(result.RejittedMethods, nameof(StaleReportProbe.ReadInPlace));
        bool wrapperStale = ReportsOf(result.StaleDynamicMethods, minted).Any(one => ReferenceEquals(one, minted));

        Assert.True(
            replacedRejitted && wrapperStale,
            $"one relayout of '{typeof(StaleReportSubject).FullName}' reported " +
            $"{Describe(result.RejittedMethods)} as rejitted and {Describe(result.StaleDynamicMethods)} as stale. " +
            $"'{nameof(StaleReportProbe.ReadInPlace)}', whose body Concord replaced in place to read the same " +
            $"field, is {(replacedRejitted ? "in" : "missing from")} the rejitted list, and Concord's wrapper " +
            $"'{minted.Name}' (#{minted.GetHashCode()}) is {(wrapperStale ? "in" : "missing from")} the stale " +
            "list, which asks for that exact instance so a retired wrapper from an earlier fact cannot stand in " +
            "for it. Both read one field of " +
            "one relaid-out type, so the only thing that can split them is how the body was installed: a body in " +
            "metadata is rejitted and a DynamicMethod cannot be, which is the whole reason the in-place path " +
            "survives a relayout and the wrapper path does not. A wrapper in the rejitted list would mean Concord " +
            "needs no handler at all, and a replaced body in the stale list would mean the in-place path needs one too.");
    }

    private static string Field(string fact) {
        return "ConcordForkTests_StaleReport_" + fact + "_x";
    }

    private static ForkFieldInjection.ApplyResult ApplyPendingToReadTheResultLists(string fieldName) {
        ForkFieldInjection.Batch batch = ForkFieldInjection.NewBatch();
        batch.AddField(typeof(StaleReportSubject), fieldName, typeof(int));
        batch.Commit();

        ForkFieldInjection.ApplyResult result = ForkFieldInjection.ApplyPending();
        for (int attempt = 1; attempt < Attempts && batch.Status == "Queued" && Transient.Contains(batch.LastRefusal); attempt++) {
            Thread.Sleep(SettleMs);
            result = ForkFieldInjection.ApplyPending();
        }

        Assert.True(batch.Verdict == "Applied", batch.Verdict);
        return result;
    }

    private static List<MethodBase> ReportsOf(IReadOnlyList<MethodBase> reported, MethodInfo minted) {
        List<MethodBase> matches = [];
        foreach (MethodBase one in reported) {
            if (one.Name == minted.Name) {
                matches.Add(one);
            }
        }

        return matches;
    }

    private static string DescribeIdentities(IEnumerable<MethodBase> reported, MethodInfo minted) {
        return string.Join(
            ", ",
            reported.Select(one =>
                $"{one.GetType().FullName} #{one.GetHashCode()} ({(ReferenceEquals(one, minted) ? "the minted instance" : "not the minted instance")}, "
                + $"{Keys(one)}, resolves to '{MethodIdentity.ResolveOriginal(one)?.Name ?? "nothing"}')"));
    }

    private static bool Named(IReadOnlyList<MethodBase> reported, string name) {
        foreach (MethodBase one in reported) {
            if (one.Name == name && one.DeclaringType == typeof(StaleReportProbe)) {
                return true;
            }
        }

        return false;
    }

    private static string Describe(IReadOnlyList<MethodBase> reported) {
        if (reported.Count == 0) {
            return "nothing";
        }

        return string.Join(
            ", ",
            reported.Select(one =>
                $"{one.GetType().Name} '{one.DeclaringType?.FullName ?? "<no declaring type>"}.{one.Name}'"));
    }

    private static string Keys(MethodBase method) {
        MethodInfo entry = typeof(MethodIdentity).GetMethod("TryKeysOf", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] arguments = [method, IntPtr.Zero, IntPtr.Zero];
        bool read = (bool)entry.Invoke(null, arguments)!;

        return read
            ? $"handle 0x{(IntPtr)arguments[1]!:x} and entry point 0x{(IntPtr)arguments[2]!:x}"
            : "no handle at all, which is what MethodIdentity.TryKeysOf returns when the runtime refuses one";
    }

    private static string Remembered() {
        Dictionary<IntPtr, MethodBase> map = (Dictionary<IntPtr, MethodBase>)typeof(MethodIdentity)
            .GetField("Wrappers", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        object gate = typeof(MethodIdentity)
            .GetField("WrappersGate", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        List<string> keys = [];
        lock (gate) {
            foreach (KeyValuePair<IntPtr, MethodBase> entry in map) {
                if (entry.Value.Name == Wrapped.Name && entry.Value.DeclaringType == Wrapped.DeclaringType) {
                    keys.Add($"0x{entry.Key:x}");
                }
            }
        }

        return keys.Count == 0 ? "no keys" : string.Join(" and ", keys);
    }

    private static MethodInfo MintedWrapper() {
        Type registry = typeof(MonoModDetourBackend).Assembly.GetType("Concord.Detour.TargetDetourRegistry")
            ?? throw new InvalidOperationException(
                "'Concord.Detour.TargetDetourRegistry' is not in the assembly that holds MonoModDetourBackend, so "
                + "this fact cannot reach the wrapper Concord minted.");

        object gate = registry.GetField("RootedGate", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        List<MethodInfo> rooted = (List<MethodInfo>)registry
            .GetField("RootedWrappers", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        lock (gate) {
            for (int i = rooted.Count - 1; i >= 0; i--) {
                if (rooted[i].Name.Contains(nameof(StaleReportProbe.ReadWrapped), StringComparison.Ordinal)) {
                    return rooted[i];
                }
            }
        }

        throw new InvalidOperationException(
            $"no rooted wrapper names '{nameof(StaleReportProbe.ReadWrapped)}', so the composed wrapper this fact "
            + "measures was never minted.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static StaleReportSubject NewSubject(int value) {
        return new StaleReportSubject { Before = value };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[] ReadingBefore(Module target) {
        FieldInfo before = typeof(StaleReportSubject).GetField(nameof(StaleReportSubject.Before))!;
        ComposedBody body = new ComposedBody(
            [
                new ComposedInstruction(new CodeInstruction(OpCodes.Ldarg_0), null, null),
                new ComposedInstruction(new CodeInstruction(OpCodes.Ldfld, before), null, null),
                new ComposedInstruction(new CodeInstruction(OpCodes.Ret), null, null),
            ],
            [],
            [],
            false);

        return BodyEncoder.Encode(body, new WaylithTokenSource(target), new ForkRuntime.FixedStack(1)).Bytes;
    }

    private MethodInfo PatchWrapped() {
        patch = new MonoModDetourBackend()
            .ApplyComposed(Wrapped, [new Injection(Counter, new InjectAt.Head(), "stale-report", 0)]);

        StaleReportProbe.Counted = 0;
        int read = StaleReportProbe.ReadWrapped(NewSubject(7));

        Assert.True(
            read == 7 && StaleReportProbe.Counted == 1,
            $"the patched target returned {read} and ran its injection {StaleReportProbe.Counted} time(s), wanted " +
            "7 and 1. A wrapper that never ran was never compiled against the old layout, so nothing about it " +
            "going stale would mean anything.");

        return MintedWrapper();
    }

    private void WriteInPlaceReadOfBefore() {
        ForkBodyWriter.Shared.Write(InPlace, ReadingBefore(InPlace.Module), []);
        written = true;

        int read = StaleReportProbe.ReadInPlace(NewSubject(9));

        Assert.True(
            read == 9,
            $"the in-place replaced body returned {read}, wanted 9, so it is not the body reading the field this " +
            "fact relays out.");
    }
}
