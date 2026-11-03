using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public static class FactoryVictims {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Early() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Late() {
        return 1;
    }
}

/// <summary>
///     One recorded <c>IDetourFactory.CreateDetour</c> call, flattened to names and flags so the
///     fact can read it without naming any MonoMod type.
/// </summary>
public sealed record FactoryDetourCall(string Source, string Target, bool ApplyByDefault, bool SourceClone);

/// <summary>
///     A <c>MonoMod.Core.IDetourFactory</c> that records every call and forwards it to the factory
///     that was current when the swap happened.
/// </summary>
public class RecordingDetourFactory : DispatchProxy {
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) {
        if (targetMethod == null) {
            throw new InvalidOperationException("DispatchProxy dispatched a call with no MethodInfo.");
        }

        switch (targetMethod.Name) {
            case "get_SupportsNativeDetourOrigEntrypoint":
                object? supports = targetMethod.Invoke(FactorySwap.Previous, args);
                FactorySwap.SupportsReads++;
                FactorySwap.SupportsValue = (bool?)supports;
                return supports;
            case "CreateDetour":
                FactorySwap.Record(FactorySwap.Describe(args![0]!));
                return targetMethod.Invoke(FactorySwap.Previous, args);
            case "CreateNativeDetour":
                FactorySwap.NativeDetours++;
                return targetMethod.Invoke(FactorySwap.Previous, args);
            default:
                return targetMethod.Invoke(FactorySwap.Previous, args);
        }
    }
}

public static class FactorySwap {
    private static readonly List<FactoryDetourCall> Detours = [];

    public static object? Previous;

    public static int NativeDetours;

    public static int SupportsReads;

    public static bool? SupportsValue;

    private static readonly Assembly HarmonyAssembly = typeof(Harmony).Assembly;

    private static object? proxy;

    public static void Install() {
        Type factory = Interface();
        MethodInfo create = typeof(DispatchProxy)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == nameof(DispatchProxy.Create) && m.IsGenericMethodDefinition);

        proxy = create.MakeGenericMethod(factory, typeof(RecordingDetourFactory)).Invoke(null, null);
        SetCurrentFactory(factory, nameof(Wrap));
    }

    public static void Restore() {
        if (proxy == null) {
            return;
        }

        SetCurrentFactory(Interface(), nameof(Unwrap));
        proxy = null;
        Previous = null;
        lock (Detours) {
            Detours.Clear();
        }

        NativeDetours = 0;
        SupportsReads = 0;
        SupportsValue = null;
    }

    public static FactoryDetourCall Describe(object request) {
        Type shape = request.GetType();
        return new FactoryDetourCall(
            Name(Read<MethodBase>(shape, request, "Source")),
            Name(Read<MethodBase>(shape, request, "Target")),
            Read<bool>(shape, request, "ApplyByDefault"),
            Read<bool>(shape, request, "CreateSourceCloneIfNotILClone"));
    }

    public static void Record(FactoryDetourCall call) {
        lock (Detours) {
            Detours.Add(call);
        }
    }

    public static List<FactoryDetourCall> Matching(MethodBase method) {
        string source = Name(method);
        lock (Detours) {
            return Detours.FindAll(d => d.Source == source);
        }
    }

    public static string Dump() {
        lock (Detours) {
            return Detours.Count == 0
                ? "<no CreateDetour call was recorded>"
                : string.Join("; ", Detours.Select(d => $"{d.Source} -> {d.Target}"));
        }
    }

    public static bool Saw(MethodBase method) {
        return Matching(method).Count > 0;
    }

    public static string Name(MethodBase? method) {
        return method == null ? "<null>" : $"{method.DeclaringType?.FullName}.{method.Name}";
    }

    private static Type Interface() {
        return HarmonyAssembly.GetType("MonoMod.Core.IDetourFactory", throwOnError: true)!;
    }

    private static void SetCurrentFactory(Type factory, string creator) {
        MethodInfo swap = typeof(FactorySwap)
            .GetMethod(creator, BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(factory);

        Type func = typeof(Func<,>).MakeGenericType(factory, factory);
        MethodInfo set = HarmonyAssembly
            .GetType("MonoMod.Core.DetourFactory", throwOnError: true)!
            .GetMethod("SetCurrentFactory", BindingFlags.Public | BindingFlags.Static)!;

        set.Invoke(null, [Delegate.CreateDelegate(func, swap)]);
    }

    private static T Wrap<T>(T previous) {
        Previous = previous;
        return (T)proxy!;
    }

    private static T Unwrap<T>(T current) {
        return (T)Previous!;
    }

    private static T Read<T>(Type shape, object request, string name) {
        PropertyInfo property = shape.GetProperty(name)
            ?? throw new InvalidOperationException(
                $"'{shape.FullName}' has no readable '{name}', so the request shape this fact reports is wrong.");

        return (T)property.GetValue(request)!;
    }
}

[Collection(ForkFieldInjection.ApplyCollection)]
public sealed class DetourFactoryAccessTests : IDisposable {
    private const string WrongHarmonyBuild =
        "these facts are written against the workshop Harmony 3.0.x build. Pass " +
        "-p:HarmonyDir=<workshop 2009463077/1.7/Assemblies> so the run proves something about the build the game " +
        "actually loads.";

    private static readonly MethodInfo Shifter = typeof(Shift).GetMethod(nameof(Shift.Plus10))!;

    private static readonly MethodInfo Early = typeof(FactoryVictims).GetMethod(nameof(FactoryVictims.Early))!;

    private static readonly MethodInfo Late = typeof(FactoryVictims).GetMethod(nameof(FactoryVictims.Late))!;

    private readonly Harmony first = new Harmony("concord.fork.factory.first");

    private readonly Harmony second = new Harmony("concord.fork.factory.second");

    public static bool NotHarmonyThree => typeof(Harmony).Assembly.GetName().Version?.Major != 3;

    public void Dispose() {
        first.UnpatchAll(first.Id);
        second.UnpatchAll(second.Id);
        FactorySwap.Restore();
    }

    [Fact(Skip = WrongHarmonyBuild, SkipWhen = nameof(NotHarmonyThree))]
    public void ASwappedDetourFactorySeesEveryPatchComposedAfterItRegisters() {
        first.Patch(Early, postfix: new HarmonyMethod(Shifter));
        Assert.Equal(11, FactoryVictims.Early());

        FactorySwap.Install();

        Assert.False(
            FactorySwap.Saw(Early),
            "the swap recorded a patch that landed before it registered, so this fact cannot tell late " +
            $"registration from early. Recorded: {FactorySwap.Dump()}");

        first.Patch(Late, postfix: new HarmonyMethod(Shifter));

        Assert.True(
            FactorySwap.Saw(Late),
            "a plain Harmony patch ran while the swapped factory was current and the probe recorded nothing, " +
            $"so Harmony does not compose through DetourFactory.Current on this build. Recorded: {FactorySwap.Dump()}");

        Assert.Equal(11, FactoryVictims.Late());

        second.Patch(Early, postfix: new HarmonyMethod(Shifter));

        Assert.True(
            FactorySwap.Saw(Early),
            "re-patching a method that was already patched before the swap did not reach the swapped factory, " +
            $"so a late registration never catches up on startup patches. Recorded: {FactorySwap.Dump()}");

        Assert.Equal(21, FactoryVictims.Early());
    }

    [Fact(Skip = WrongHarmonyBuild, SkipWhen = nameof(NotHarmonyThree))]
    public void TheRequestHandedToTheFactoryCarriesItsSourceAndTargetAndNoNativeDetour() {
        FactorySwap.Install();
        first.Patch(Late, postfix: new HarmonyMethod(Shifter));

        FactoryDetourCall call = Assert.Single(FactorySwap.Matching(Late));

        Assert.Equal("Concord.Host.Adapter.ForkTests.FactoryVictims.Late", call.Source);
        Assert.NotEqual(call.Source, call.Target);
        Assert.True(call.ApplyByDefault, "Harmony handed a request it did not want applied, so installing it is not enough.");
        Assert.False(call.SourceClone, "Harmony asked the factory to clone the source, so the install path is not plain.");

        Assert.Equal(0, FactorySwap.NativeDetours);
        Assert.Equal(0, FactorySwap.SupportsReads);
        Assert.Null(FactorySwap.SupportsValue);
    }
}
