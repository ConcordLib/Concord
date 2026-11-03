using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Emit;
using MonoMod.Core;
using Xunit;

namespace Concord.Detour.Tests;

public static class RecomposeLiveTarget {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return 1;
    }
}

public static class RecomposeLiveInjections {
    public static void AddOne(ControlHandle<int> ch) {
        ch.ReturnValue += 1;
    }
}

public sealed class RecomposeLiveTests {
    [Fact]
    public void RecomposeLive_WhenComposeThrows_ReturnsFalseAndLogsTheTarget() {
        MethodBase target = typeof(RecomposeLiveTarget).GetMethod(nameof(RecomposeLiveTarget.Value))!;
        MethodInfo addOne = typeof(RecomposeLiveInjections).GetMethod(nameof(RecomposeLiveInjections.AddOne))!;
        Injection injection = new Injection(addOne, new InjectAt.Tail(), "A", 0);

        using IDetourHandle handle = TargetDetourRegistry.Add(target, [injection]);
        Assert.True(handle.IsApplied);

        WrapperPrecompile.Enabled = true;
        WrapperPrecompile.UseGuard(() => throw new InvalidOperationException("Simulated compile failure."));

        List<string> reported = new List<string>();
        Action<string>? previous = PatchLog.Sink;
        PatchLog.Sink = reported.Add;
        try {
            bool result = TargetDetourRegistry.RecomposeLive(target);

            Assert.False(result);
            Assert.Contains(reported, line => line.Contains(nameof(RecomposeLiveTarget.Value)));
        } finally {
            PatchLog.Sink = previous;
            WrapperPrecompile.UseGuard(null);
            WrapperPrecompile.Enabled = false;
        }
    }

    [Fact]
    public void RecomposeLive_WhenGivenTheComposedWrapperInsteadOfTheTarget_ReturnsTrue() {
        MethodBase target = typeof(RecomposeLiveTarget).GetMethod(nameof(RecomposeLiveTarget.Value))!;
        MethodInfo addOne = typeof(RecomposeLiveInjections).GetMethod(nameof(RecomposeLiveInjections.AddOne))!;
        Injection injection = new Injection(addOne, new InjectAt.Tail(), "A", 0);

        using IDetourHandle handle = TargetDetourRegistry.Add(target, [injection]);
        Assert.True(handle.IsApplied);

        MethodBase wrapper = LastAppliedWrapper(target);

        Assert.True(TargetDetourRegistry.RecomposeLive(wrapper));
    }

    [Fact]
    public void RecomposeLive_WhenForeignOwned_ReturnsFalseAndLogsTheTarget() {
        MethodBase target = typeof(RecomposeLiveTarget).GetMethod(nameof(RecomposeLiveTarget.Value))!;
        MethodInfo addOne = typeof(RecomposeLiveInjections).GetMethod(nameof(RecomposeLiveInjections.AddOne))!;
        Injection injection = new Injection(addOne, new InjectAt.Tail(), "A", 0);

        using IDetourHandle handle = TargetDetourRegistry.Add(target, [injection]);
        Assert.True(handle.IsApplied);

        TargetDetourRegistry.MarkForeignOwned(MethodIdentity.SharedBodyKey(target));

        List<string> reported = new List<string>();
        Action<string>? previous = PatchLog.Sink;
        PatchLog.Sink = reported.Add;
        try {
            bool result = TargetDetourRegistry.RecomposeLive(target);

            Assert.False(result);
            Assert.Contains(reported, line => line.Contains(nameof(RecomposeLiveTarget.Value)));
        } finally {
            PatchLog.Sink = previous;
        }
    }

    private static MethodBase LastAppliedWrapper(MethodBase target) {
        FieldInfo registriesField = typeof(TargetDetourRegistry).GetField("Registries", BindingFlags.NonPublic | BindingFlags.Static)!;
        Dictionary<MethodBase, TargetDetourRegistry> registries = (Dictionary<MethodBase, TargetDetourRegistry>)registriesField.GetValue(null)!;
        TargetDetourRegistry registry = registries[MethodIdentity.SharedBodyKey(target)];

        FieldInfo detourField = typeof(TargetDetourRegistry).GetField("detour", BindingFlags.NonPublic | BindingFlags.Instance)!;
        ICoreDetour detour = (ICoreDetour)detourField.GetValue(registry)!;
        return detour.Target;
    }
}
