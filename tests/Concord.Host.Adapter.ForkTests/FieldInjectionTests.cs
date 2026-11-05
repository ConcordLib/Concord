using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Concord.Host.Adapter.Tests;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public sealed class ModLocalTarget {
    public int Before;
}

public sealed class ReflectionTarget {
    public int Before;
}

public sealed class RelayoutTarget {
    public int Before;
}

public sealed class DependentTarget {
    public int Before;
}

public static class RelayoutProbe {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Read(RelayoutTarget subject) {
        return -1;
    }
}

public static class DependentProbe {
    private const int Attempts = 40;

    private const int SettleMs = 25;

    private static readonly string[] Transient = ["CompileGateBusy", "GCBusy", "GCNotLatched", "NoGCRegions"];

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string Descend(DependentTarget subject, int depth) {
        if (depth > 0) {
            return Descend(subject, depth - 1) + (subject.Before == int.MinValue ? "!" : string.Empty);
        }

        ForkFieldInjection.Batch batch = ForkFieldInjection.NewBatch();
        batch.AddField(typeof(DependentTarget), FieldInjectionTests.Named(nameof(DependentTarget)), typeof(int));
        batch.Commit();

        for (int attempt = 0; attempt < Attempts; attempt++) {
            try {
                ForkFieldInjection.ApplyPending();
            } catch (Exception thrown) {
                return "threw " + thrown.GetType().Name + ": " + thrown.Message;
            }

            if (batch.Status != "Queued" || !Transient.Contains(batch.LastRefusal)) {
                break;
            }

            Thread.Sleep(SettleMs);
        }

        return batch.Status + " " + batch.LastRefusal + ": " + (batch.PendingReason ?? "no reason given")
            + (subject.Before == int.MinValue ? "!" : string.Empty);
    }
}

[Collection(ForkFieldInjection.ApplyCollection)]
public sealed class FieldInjectionTests {
    private const string NoInjection = "No field-injection runtime here, so nothing can be injected.";

    private const int Depth = 8;

    private const BindingFlags AnyField = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public static bool Unavailable => ForkRuntime.Unavailable || ForkFieldInjection.Unavailable;

    public static string Named(string subject) {
        return "ConcordForkTests_" + subject + "_x";
    }

    [Fact(Skip = NoInjection, SkipWhen = nameof(Unavailable))]
    public void AFieldCanBeInjectedOntoATypeInAPatchableAssembly() {
        ForkFieldInjection.Injected injected = ApplyOnto(typeof(ModLocalTarget), Named(nameof(ModLocalTarget)));

        Assert.NotNull(injected.Field);
    }

    [Fact(Skip = NoInjection, SkipWhen = nameof(Unavailable))]
    public void AnAssemblyOutsideJitPatchableAssembliesIsRefusedAsAFrameworkAssembly() {
        ForkFieldInjection.Batch batch = ForkFieldInjection.NewBatch();
        batch.AddField(typeof(WaylithDetourBackend), Named(nameof(WaylithDetourBackend)), typeof(int));
        batch.Commit();

        Exception thrown = Assert.ThrowsAny<Exception>(ForkFieldInjection.ApplyPending);

        Assert.Equal("Failed", batch.Status);
        Assert.Contains("the type is in a framework assembly", thrown.Message, StringComparison.Ordinal);
    }

    [Fact(Skip = NoInjection, SkipWhen = nameof(Unavailable))]
    public void ReflectionSeesAnInjectedFieldOnItsDeclaringType() {
        string name = Named(nameof(ReflectionTarget));

        ForkFieldInjection.Injected injected = ApplyOnto(typeof(ReflectionTarget), name);

        FieldInfo? found = typeof(ReflectionTarget).GetField(name, AnyField);

        Assert.NotNull(found);
        Assert.Equal(injected.MetadataToken, found.MetadataToken);
        Assert.Equal(typeof(int), found.FieldType);
        Assert.Equal([11, 3], RoundTrip(found));
    }

    [Fact(Skip = NoInjection, SkipWhen = nameof(Unavailable))]
    public void AReplacedBodyKeepsRunningAfterItsOperandTypeIsRelaidOut() {
        MethodInfo probe = typeof(RelayoutProbe).GetMethod(nameof(RelayoutProbe.Read))!;
        RelayoutTarget subject = NewRelayout(7);
        ForkRuntime.Write(probe, ReadingBefore(probe.Module), []);
        Assert.Equal(7, RelayoutProbe.Read(subject));
        int epoch = ForkFieldInjection.LayoutEpoch;

        ForkFieldInjection.ApplyResult result = ApplyResultFor(typeof(RelayoutTarget), Named(nameof(RelayoutTarget)));

        Assert.NotEqual(epoch, ForkFieldInjection.LayoutEpoch);
        Assert.Equal(7, RelayoutProbe.Read(subject));
        Assert.Equal(7, RelayoutProbe.Read(NewRelayout(7)));
        Assert.True(
            result.RejittedMethods.Count + result.StaleDynamicMethods.Count > 0,
            $"the relayout reported {result.RejittedMethods.Count} rejitted and " +
            $"{result.StaleDynamicMethods.Count} stale methods, so nothing on this process's code was treated as " +
            "dependent and this fact does not measure what it claims. Field injection appends, so a pre-existing " +
            "field keeps its offset and a body reading it was never stale.");
    }

    [Fact(Skip = NoInjection, SkipWhen = nameof(Unavailable))]
    public void AFrameBelowTheApplyThatReadsTheTargetTypeRefusesTheRelayout() {
        DependentTarget subject = NewDependent(5);
        string verdict = "never ran";
        Thread worker = new Thread(() => verdict = DependentProbe.Descend(subject, Depth));

        worker.Start();
        worker.Join();

        Assert.StartsWith("Queued DependentFrames: ", verdict, StringComparison.Ordinal);
        Assert.Contains(nameof(DependentProbe.Descend), verdict, StringComparison.Ordinal);
    }

    private static ForkFieldInjection.Injected ApplyOnto(Type declaringType, string name) {
        ForkFieldInjection.Batch batch = ForkFieldInjection.NewBatch();
        ForkFieldInjection.Injected injected = batch.AddField(declaringType, name, typeof(int));
        batch.Commit();

        ForkFieldInjection.ApplyPending();

        Assert.True(batch.Verdict == "Applied", batch.Verdict);
        return injected;
    }

    private static ForkFieldInjection.ApplyResult ApplyResultFor(Type declaringType, string name) {
        ForkFieldInjection.Batch batch = ForkFieldInjection.NewBatch();
        batch.AddField(declaringType, name, typeof(int));
        batch.Commit();

        ForkFieldInjection.ApplyResult result = ForkFieldInjection.ApplyPending();

        Assert.True(batch.Verdict == "Applied", batch.Verdict);
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RelayoutTarget NewRelayout(int value) {
        return new RelayoutTarget { Before = value };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static DependentTarget NewDependent(int value) {
        return new DependentTarget { Before = value };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int[] RoundTrip(FieldInfo injected) {
        ReflectionTarget subject = new ReflectionTarget { Before = 3 };
        injected.SetValue(subject, 11);
        return [(int)injected.GetValue(subject)!, subject.Before];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[] ReadingBefore(Module target) {
        FieldInfo before = typeof(RelayoutTarget).GetField(nameof(RelayoutTarget.Before))!;
        ComposedBody body = new ComposedBody(
            [
                new ComposedInstruction(new CodeInstruction(OpCodes.Ldarg_0), null, null),
                new ComposedInstruction(new CodeInstruction(OpCodes.Ldfld, before), null, null),
                new ComposedInstruction(new CodeInstruction(OpCodes.Ret), null, null),
            ],
            [],
            [],
            false);

        return BodyEncoder.Encode(body, new WaylithTokenSource(target), new FixedStack(1)).Bytes;
    }
}
