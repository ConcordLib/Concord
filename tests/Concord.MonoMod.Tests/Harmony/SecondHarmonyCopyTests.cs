using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord;
using Concord.Backend;
using Concord.Emit;
using Concord.MonoMod;
using Concord.MonoMod.Harmony;
using HarmonyLib;
using Xunit;

namespace Concord.MonoMod.Tests;

[Collection("HarmonySerial")]
public sealed class SecondHarmonyCopyTests
{
    private const string ModId = "test.secondcopy.mod";

    private const string ConcordOwner = "test.secondcopy.concord";

    private const string MonoReason =
        "A second Harmony copy cannot initialize at all under mono, and the failure is upstream of the " +
        "bridge: HarmonySharedState's static ctor builds FieldRefAccess<System.Diagnostics.StackFrame, " +
        "System.Int64> for a methodAddress field that mono's StackFrame does not have, so the first " +
        "PatchProcessor.Patch through the second copy throws TypeInitializationException wrapping " +
        "'Invalid generic arguments'. Harmony 2.4.2 fails the same way, and the single-copy path is " +
        "covered by the in-game probe, so this one only runs on CoreCLR until Harmony fixes the field ref.";

    [CoreClrFact(MonoReason)]
    public void AHarmonyCopyLoadedAfterTheInstall_StillReachesTheNotifier()
    {
        Recorder recorder = new Recorder();
        MonoModBackend backend = new MonoModBackend();

        try
        {
            Assert.True(UpdateWrapperHook.TryInstall(recorder, backend, recorder.Record), string.Join(" | ", recorder.Lines));

            Assembly second = SecondCopy();
            MethodInfo target = Method(typeof(LateCopyTarget), nameof(LateCopyTarget.Compute));
            PatchThrough(second, target, nameof(CopyMods.LateCopyPrefix));

            Assert.Equal(new[] { (MethodBase)target }, recorder.Pending);
            Assert.Contains(
                recorder.Lines,
                line => line.StartsWith(CoexistenceLogMarkers.HookInstalled, StringComparison.Ordinal)
                        && line.EndsWith("#" + second.GetHashCode(), StringComparison.Ordinal));
        }
        finally
        {
            UpdateWrapperHook.DetachObserver();
        }
    }

    [Fact]
    public void AHarmonyPatchArrivingLate_KeepsConcordsInjections_InsideHarmonysWrapper()
    {
        MethodInfo target = Method(typeof(RefusedRouteTarget), nameof(RefusedRouteTarget.Compute));
        Assert.Null(PatchProcessor.GetPatchInfo(target));

        Recorder recorder = new Recorder();
        MonoModBackend inner = new MonoModBackend();
        HarmonyBridge bridge = new HarmonyBridge(recorder.Record);
        RoutingBackend router = new RoutingBackend(inner, recorder.Record);
        HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);
        IBackendHandle? concord = null;

        try
        {
            router.ActivateHost(bridge);
            Assert.True(router.NotifierInstalled);

            concord = router.ApplyComposed(target, new[] { HeadOf(nameof(CopyMods.RefusedHead)) });
            Assert.Equal(RouteState.Raw, router.GetRoute(target));
            Assert.True(inner.OwnsBody(target));

            harmony.Patch(target, prefix: new HarmonyMethod(Method(typeof(CopyMods), nameof(CopyMods.RefusedPrefix))));

            CopyLog.Entries.Clear();
            Assert.Equal(7, RefusedRouteTarget.Compute(1));
            Assert.Equal(new[] { "harmony", "concord", "body" }, CopyLog.Entries);
            Assert.Equal(RouteState.HarmonyOwned, router.GetRoute(target));
        }
        finally
        {
            UpdateWrapperHook.DetachObserver();
            concord?.Dispose();
            TestUnpatch.Own(harmony, ModId);
        }
    }

    internal static Assembly SecondCopy()
    {
        byte[] image = File.ReadAllBytes(typeof(HarmonyLib.Harmony).Assembly.Location);
        Assembly second = Assembly.Load(AssemblyImage.WithFreshModuleId(image));
        Assert.NotSame(typeof(HarmonyLib.Harmony).Assembly, second);
        return second;
    }

    internal static MethodInfo PatchMethodOf(Type harmonyType, Type harmonyMethodType)
    {
        foreach (MethodInfo candidate in harmonyType.GetMethods())
        {
            ParameterInfo[] parameters = candidate.GetParameters();
            if (candidate.Name == "Patch" && parameters.Length >= 2 && parameters[1].ParameterType == harmonyMethodType)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Harmony.Patch(MethodBase, HarmonyMethod, ...) not found in " + harmonyType.Assembly.FullName);
    }

    private static void PatchThrough(Assembly copy, MethodBase target, string prefixName)
    {
        Type harmonyType = TypeFrom(copy, "HarmonyLib.Harmony");
        Type harmonyMethodType = TypeFrom(copy, "HarmonyLib.HarmonyMethod");
        object harmony = Instance(harmonyType, "test.secondcopy." + prefixName);
        object prefix = Instance(harmonyMethodType, Method(typeof(CopyMods), prefixName));

        MethodInfo patch = PatchMethodOf(harmonyType, harmonyMethodType);
        object[] args = new object[patch.GetParameters().Length];
        args[0] = target;
        args[1] = prefix;

        try
        {
            patch.Invoke(harmony, args);
        }
        catch (TargetInvocationException thrown) when (thrown.GetBaseException() is TypeLoadException)
        {
            CopyLog.Entries.Add("copy-build-failed");
        }
    }

    private static Injection HeadOf(string name)
    {
        return new Injection(Method(typeof(CopyMods), name), new InjectAt.Head(), ConcordOwner, 0);
    }

    private static MethodInfo Method(Type type, string name)
    {
        return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
    }

    internal static Type TypeFrom(Assembly assembly, string name)
    {
        return assembly.GetType(name) ?? throw new InvalidOperationException(name + " not found in " + assembly.FullName);
    }

    private static object Instance(Type type, params object?[] args)
    {
        return Activator.CreateInstance(type, args) ?? throw new InvalidOperationException(type.Name + " was not constructed");
    }

    private sealed class Recorder : IForeignPatchObserver
    {
        public List<MethodBase> Pending { get; } = new List<MethodBase>();

        public List<string> Lines { get; } = new List<string>();

        public void Record(string line)
        {
            Lines.Add(line);
        }

        public void OnForeignPatchPending(MethodBase target, object hostPatchState)
        {
            Pending.Add(target);
        }
    }

    public static class CopyLog
    {
        public static List<string> Entries { get; } = new List<string>();
    }

    public static class LateCopyTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute()
        {
            CopyLog.Entries.Add("body");
            return 3;
        }
    }

    public static class RefusedRouteTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Compute(int seed)
        {
            CopyLog.Entries.Add("body");
            return seed + 6;
        }
    }

    public static class CopyMods
    {
        public static void LateCopyPrefix()
        {
            CopyLog.Entries.Add("late-copy-harmony");
        }

        public static void RefusedHead(ControlHandle<int> ch)
        {
            CopyLog.Entries.Add("concord");
        }

        public static void RefusedPrefix(object[] __args)
        {
            GC.KeepAlive(__args);
            CopyLog.Entries.Add("harmony");
        }
    }
}
