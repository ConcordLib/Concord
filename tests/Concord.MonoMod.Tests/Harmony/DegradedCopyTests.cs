using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Backend;
using Concord.MonoMod;
using Concord.MonoMod.Harmony;
using Xunit;

namespace Concord.MonoMod.Tests;

[Collection("HarmonySerial")]
public sealed class DegradedCopyTests
{
    [Theory]
    [InlineData("2.2.2", nameof(ProbeTargets.Through222))]
    [InlineData("2.3.3", nameof(ProbeTargets.Through233))]
    [InlineData("2.3.6", nameof(ProbeTargets.Through236))]
    [InlineData("2.4.2", nameof(ProbeTargets.Through242))]
    [InlineData("2.4.3", nameof(ProbeTargets.Through243))]
    [InlineData("3.0.0-rc.4", nameof(ProbeTargets.Through300))]
    public void ASecondCopyWhoseSharedStateConcordCannotRead_CannotPatchThroughHarmonyEither(string package, string targetName)
    {
        Assembly copy = SecondCopyOf(package);
        List<string> log = new List<string>();

        bool readable = UpdateWrapperHook.SharedStateReadableForTests(copy, log.Add);
        string? harmonyRefusal = PatchThroughHarmony(copy, typeof(ProbeTargets).GetMethod(targetName)!);

        Assert.False(
            !readable && harmonyRefusal == null,
            "Harmony " + package + " patches through a second copy, but Concord cannot read that copy's shared patch state, " +
            "so a rebuild through it is refused and an injection added after Harmony patched is lost: " + string.Join(" | ", log));
    }

    [Theory]
    [InlineData("2.2.2")]
    [InlineData("2.3.3")]
    [InlineData("2.3.6")]
    [InlineData("2.4.2")]
    [InlineData("2.4.3")]
    [InlineData("3.0.0-rc.4")]
    public void ASecondCopy_CarriesAPatchProcessorLockerTheLockScopeCanEnter(string package)
    {
        Assembly copy = SecondCopyOf(package);

        using IDisposable? scope = HarmonyLockScope.For(copy);

        Assert.True(
            scope != null,
            "Harmony " + package + " carries no HarmonyLib.PatchProcessor.locker, so Concord's wrapper build through it is not serialized " +
            "against its own patching. PatchProcessor's static fields: " + StaticFieldsOf(copy.GetType("HarmonyLib.PatchProcessor")));
    }

    [Fact]
    public void ResetForTests_AfterASecondCopyArmed_LeavesNoCopyHooked()
    {
        List<string> log = new List<string>();

        try
        {
            MonoModEntryPoint.Install(log.Add, null);
            Assembly second = Assembly.Load(AssemblyImage.WithFreshModuleId(File.ReadAllBytes(typeof(HarmonyLib.Harmony).Assembly.Location)));
            Assert.True(UpdateWrapperHook.IsHookedOn(second), "the second Harmony copy did not arm, so the reset had nothing to forget: " + string.Join(" | ", log));

            MonoModEntryPoint.ResetForTests();

            Assert.False(UpdateWrapperHook.IsHookedOn(second));
            Assert.Equal(0, UpdateWrapperHook.HookedCountForTests());
        }
        finally
        {
            MonoModEntryPoint.ResetForTests();
        }
    }

    private static Assembly SecondCopyOf(string package)
    {
        return Assembly.Load(AssemblyImage.WithFreshModuleId(File.ReadAllBytes(HarmonyReleaseMatrixTests.ReleasePath(package))));
    }

    private static string? PatchThroughHarmony(Assembly copy, MethodInfo target)
    {
        Type harmonyType = copy.GetType("HarmonyLib.Harmony") ?? throw new InvalidOperationException("HarmonyLib.Harmony not found in " + copy.FullName);
        Type harmonyMethodType = copy.GetType("HarmonyLib.HarmonyMethod") ?? throw new InvalidOperationException("HarmonyLib.HarmonyMethod not found in " + copy.FullName);
        string id = "test.degraded." + target.Name;
        object harmony = Activator.CreateInstance(harmonyType, id)!;
        object prefix = Activator.CreateInstance(harmonyMethodType, typeof(ProbeTargets).GetMethod(nameof(ProbeTargets.Prefix)))!;

        MethodInfo? patch = null;
        foreach (MethodInfo candidate in harmonyType.GetMethods())
        {
            ParameterInfo[] parameters = candidate.GetParameters();
            if (candidate.Name == "Patch" && parameters.Length >= 2 && parameters[1].ParameterType == harmonyMethodType)
            {
                patch = candidate;
                break;
            }
        }

        Assert.NotNull(patch);
        object?[] args = new object?[patch.GetParameters().Length];
        args[0] = target;
        args[1] = prefix;

        ProbeTargets.PrefixRan = false;
        try
        {
            patch.Invoke(harmony, args);
        }
        catch (TargetInvocationException thrown)
        {
            return thrown.GetBaseException().GetType().Name + ": " + thrown.GetBaseException().Message;
        }

        try
        {
            target.Invoke(null, null);
            return ProbeTargets.PrefixRan ? null : "the patch landed but the prefix did not run";
        }
        finally
        {
            harmonyType.GetMethod("UnpatchAll", new[] { typeof(string) })!.Invoke(harmony, new object[] { id });
        }
    }

    private static string StaticFieldsOf(Type? type)
    {
        if (type == null)
        {
            return "none, the copy carries no HarmonyLib.PatchProcessor";
        }

        List<string> names = new List<string>();
        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            names.Add(field.FieldType.Name + " " + field.Name);
        }

        return names.Count == 0 ? "none" : string.Join(", ", names.ToArray());
    }

    public static class ProbeTargets
    {
        public static bool PrefixRan { get; set; }

        public static void Prefix()
        {
            PrefixRan = true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Through222() => 222;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Through233() => 233;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Through236() => 236;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Through242() => 242;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Through243() => 243;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Through300() => 300;
    }
}
