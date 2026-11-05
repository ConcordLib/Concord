using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Detour;
using Concord.Orchestration;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public sealed class Pouch<T> {
    public T? Held;
}

public static class GenericCallVictims {
    public static readonly List<string> Ran = [];

    public static readonly List<string> Looked = [];

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Filtered() {
        List<int> list = [1, 2, 3];
        return list.Where(value => value > 1).Count();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Peeked() {
        int value = 7;
        Pouch<int> pouch = new Pouch<int> { Held = 9 };
        Look<int>(ref value, "bare");
        Look<int>(ref pouch, "pouched");
        return value + pouch.Held;
    }

    public static void Look<T>(ref T value, string label) {
        Looked.Add("bare:" + label + ":" + value);
    }

    public static void Look<T>(ref Pouch<T> value, string label) {
        Looked.Add("pouched:" + label + ":" + value.Held);
    }

    public static void First() {
        Ran.Add("first");
    }
}

[Collection(ForkFieldInjection.ApplyCollection)]
public sealed class GenericCallStageTests : IDisposable {
    private const string NoPipeline = "No transformer pipeline here, so nothing can be staged over a method body.";

    private readonly Action<string> logged = WaylithDetourBackend.Log;

    private readonly List<string> lines = [];

    private readonly List<MethodBase> patched = [];

    public GenericCallStageTests() {
        WaylithDetourBackend.Log = line => lines.Add(line);
    }

    public static bool Unavailable => ForkRuntime.Unavailable;

    public void Dispose() {
        WaylithDetourBackend.Log = logged;
        foreach (MethodBase target in patched) {
            ForkRuntime.Restore(target);
        }

        patched.Clear();
        GenericCallVictims.Ran.Clear();
        GenericCallVictims.Looked.Clear();
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void AVictimCallingALinqChainStagesAndStillFilters() {
        MethodInfo target = typeof(GenericCallVictims).GetMethod(nameof(GenericCallVictims.Filtered))!;

        IPatchHandle patch = Patch(target, nameof(GenericCallVictims.First));

        Assert.True(patch.IsApplied, "the handle reports IsApplied false, so nothing was staged: " + Lines());
        Assert.Equal(2, GenericCallVictims.Filtered());
        Assert.Equal(
            new[] { "first" },
            GenericCallVictims.Ran.ToArray());
        Assert.Empty(Reports());
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void AVictimCallingAnAmbiguousRefGenericOverloadPairStagesAndStillPicksBoth() {
        MethodInfo target = typeof(GenericCallVictims).GetMethod(nameof(GenericCallVictims.Peeked))!;

        IPatchHandle patch = Patch(target, nameof(GenericCallVictims.First));

        Assert.True(patch.IsApplied, "the handle reports IsApplied false, so nothing was staged: " + Lines());
        Assert.Equal(16, GenericCallVictims.Peeked());
        Assert.Equal(
            new[] { "bare:bare:7", "pouched:pouched:9" },
            GenericCallVictims.Looked.ToArray());
        Assert.Equal(
            new[] { "first" },
            GenericCallVictims.Ran.ToArray());
        Assert.Empty(Reports());
    }

    private IPatchHandle Patch(MethodBase target, string injection) {
        IPatchHandle handle = ForkRuntime.Apply(target, typeof(GenericCallVictims), injection, At.Head);
        patched.Add(target);
        return handle;
    }

    private string[] Reports() {
        return [
            .. lines.Where(line => line.StartsWith("[Concord.Waylith]", StringComparison.Ordinal))
                .Select(line => "a clean compose has to write no '[Concord.Waylith]' line and wrote: " + line),
        ];
    }

    private string Lines() {
        return lines.Count == 0 ? "Nothing was logged." : "Logged: " + string.Join(" | ", lines);
    }
}
