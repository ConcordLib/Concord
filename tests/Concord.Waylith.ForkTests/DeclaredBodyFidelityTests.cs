using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Concord.Waylith.ForkTests;

public static class Declared {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static T[] Make<T>(int n) {
        T[] held = new T[n];
        return held;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Several(int n) {
        int a = n + 1;
        int b = a * 2;
        return a + b;
    }

    public static void Tail() {
    }
}

public sealed class DeclaredBodyFidelityTests {
    private const string NoFork = "No host body read here, so nothing reads a declared body.";

    private static readonly FieldInfo Recorded =
        typeof(PatchBuilder).GetField("injections", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static bool Unavailable =>
        ForkRuntime.Unavailable || !ForkRuntime.HostRead;

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void AClosedGenericMethodReadsBackWithItsOwnGenericArgumentsBound() {
        MethodInfo closed = typeof(Declared).GetMethod(nameof(Declared.Make))!.MakeGenericMethod(typeof(int));

        MethodInfo clone = OriginalBody.Clone(closed);

        Assert.Equal(new int[3], (int[])clone.Invoke(null, [3])!);
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void EveryTargetInstructionKeepsItsOwnOriginSoTheOffsetMapDoesNotCollapse() {
        MethodInfo target = typeof(Declared).GetMethod(nameof(Declared.Several))!;

        ComposeResult composed = WrapperComposer.Compose(target, [Only(target)]);
        ComposedBody body = composed.Body!;
        EncodedBody encoded = BodyEncoder.Encode(body, new WaylithTokenSource(target.Module), new ComputedStack());
        IReadOnlyList<OffsetPair> pairs = WaylithClrBackend.OffsetMap(target, body, encoded);

        int[] origins = body.Instructions
            .Where(step => step.OriginOffset is not null)
            .Select(step => step.OriginOffset!.Value)
            .ToArray();

        Assert.True(origins.Length > 4, $"the target contributed only {origins.Length} instructions to prove this on");
        Assert.True(
            origins.Distinct().Count() == origins.Length,
            $"the {origins.Length} target instructions report origins [{string.Join(",", origins)}], which hold " +
            "duplicates. Every one should carry its own offset in the target's own IL.");
        Assert.True(
            pairs.Count == body.Instructions.Count,
            $"the composed body holds {body.Instructions.Count} instructions and the offset map holds " +
            $"{pairs.Count} pairs. OffsetMap drops a pair whose origin repeats the one before it, so a short map " +
            "means instructions are sharing an origin and every frame landing in them reports the same IL offset.");
    }

    private static Injection Only(MethodInfo target) {
        PatchBuilder builder = Patcher.For(target).Tail(typeof(Declared).GetMethod(nameof(Declared.Tail))!);
        return ((List<Injection>)Recorded.GetValue(builder)!)[^1];
    }
}
