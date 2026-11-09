using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Emit;
using Concord.MonoMod;
using Concord.MonoMod.Harmony;
using HarmonyLib;
using Xunit;

namespace Concord.MonoMod.Tests
{
    public static class InnerPatchLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class InnerPatchHelper
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Step(int x)
        {
            InnerPatchLog.Entries.Add("step:" + x);
            return x + 1;
        }
    }

    public static class InnerPatchTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run()
        {
            InnerPatchLog.Entries.Add("body");
            return InnerPatchHelper.Step(1) + InnerPatchHelper.Step(2);
        }
    }

    public static class InnerPatchHeadTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run()
        {
            InnerPatchLog.Entries.Add("body");
            return InnerPatchHelper.Step(1) + InnerPatchHelper.Step(2);
        }
    }

    public static class InnerPatchInvokeTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run()
        {
            InnerPatchLog.Entries.Add("body");
            return InnerPatchHelper.Step(1) + InnerPatchHelper.Step(2);
        }
    }

    public static class InnerPatchMods
    {
        public static void HarmonyInnerPrefix()
        {
            InnerPatchLog.Entries.Add("harmony-inner");
        }

        public static void ConcordHead(ControlHandle ch)
        {
            InnerPatchLog.Entries.Add("concord-head");
        }

        public static void BeforeStep(ControlHandle ch)
        {
            InnerPatchLog.Entries.Add("concord-invoke");
        }

        public static void HeadThatCallsStep(ControlHandle ch)
        {
            InnerPatchLog.Entries.Add("concord-head-calls:" + InnerPatchHelper.Step(9));
        }
    }

    [Collection("HarmonySerial")]
    public sealed class InnerPatchCoexistenceTests
    {
        private static Patches FakeInnerPrefixes(MethodInfo innerMethod)
        {
            Patch patch = new Patch(
                typeof(InnerPatchMods).GetMethod(nameof(InnerPatchMods.HarmonyInnerPrefix)),
                0,
                "test.inner.owner",
                0,
                null,
                null,
                false);
            HarmonyVersions.Field(typeof(Patch), nameof(Patch.innerMethod)).SetValue(patch, new InnerMethod(innerMethod));
            return new Patches(null, null, null, null, new Patch[] { patch }, null);
        }

        [InnerPatchFact]
        public void HarmonyAlone_InnerPrefix_ThrowsInHarmonyAndNeverLands()
        {
            if (HarmonyVersions.InfixesAreReal)
            {
                return;
            }

            MethodInfo target = HarmonyVersions.Method(typeof(InnerPatchTarget), nameof(InnerPatchTarget.Run));
            MethodInfo innerPrefix = HarmonyVersions.Method(typeof(InnerPatchMods), nameof(InnerPatchMods.HarmonyInnerPrefix));
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("test.inner.baseline");

            try
            {
                Exception failure = Record.Exception(() =>
                    new PatchProcessor(harmony, target).AddInnerPrefix(innerPrefix).Patch());

                Assert.IsType<NullReferenceException>(failure);
                Assert.Contains("Infix", failure.StackTrace);
                Assert.Null(PatchProcessor.GetPatchInfo(target));

                InnerPatchLog.Entries.Clear();
                Assert.Equal(5, InnerPatchTarget.Run());
                Assert.Equal(new List<string> { "body", "step:1", "step:2" }, InnerPatchLog.Entries);
            }
            finally
            {
                TestUnpatch.Own(harmony, "test.inner.baseline");
            }
        }
    }

    public static class InfixHelperState
    {
        public static int Counter = 4;
    }

    public static class InfixOrderTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run()
        {
            InnerPatchLog.Entries.Add("body");
            return InnerPatchHelper.Step(1) + InnerPatchHelper.Step(2);
        }
    }

    public static class InfixInvokeTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run()
        {
            InnerPatchLog.Entries.Add("body");
            return InnerPatchHelper.Step(1) + InnerPatchHelper.Step(2);
        }
    }

    public static class InfixHazardTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run()
        {
            InnerPatchLog.Entries.Add("body");
            return InnerPatchHelper.Step(1) + InnerPatchHelper.Step(2);
        }
    }

    public static class InfixFieldTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run()
        {
            int first = InfixHelperState.Counter;
            InnerPatchLog.Entries.Add("mid");
            int second = InfixHelperState.Counter;
            return first + second;
        }
    }

    public static class InfixMods
    {
        public static void InnerPrefix()
        {
            InnerPatchLog.Entries.Add("harmony-inner");
        }
    }

    internal static class HarmonyVersions
    {
        internal static readonly Assembly HarmonyAssembly = typeof(HarmonyLib.Harmony).Assembly;

        internal static bool InfixesAreReal => HarmonyAssembly.GetName().Version!.Major >= 3;

        internal static MethodInfo Method(Type type, string name)
        {
            return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
        }

        internal static FieldInfo Field(Type type, string name)
        {
            return type.GetField(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
        }

        internal static Type HarmonyType(string name)
        {
            return HarmonyAssembly.GetType(name) ?? throw new InvalidOperationException(name + " not found in " + HarmonyAssembly.GetName().Name);
        }

        internal static object InnerMethodSelector(MethodInfo inner, params int[] positions)
        {
            Type type = HarmonyType("HarmonyLib.InnerMethod");
            return Activator.CreateInstance(type, new object[] { inner, positions })!;
        }

        internal static object InnerTargetSelector(FieldInfo field, string kind, params int[] positions)
        {
            Type targetType = HarmonyType("HarmonyLib.InnerTarget");
            Type kindType = HarmonyType("HarmonyLib.InnerTargetKind");
            return Activator.CreateInstance(targetType, new object[] { field, Enum.Parse(kindType, kind), positions })!;
        }

        internal static HarmonyMethod Fix(MethodInfo fixMethod, object selector, string fieldName)
        {
            HarmonyMethod method = new HarmonyMethod(fixMethod);
            Field(typeof(HarmonyMethod), fieldName).SetValue(method, selector);
            return method;
        }

        internal static Patch InfixPatch(HarmonyMethod fix)
        {
            return (Patch)Activator.CreateInstance(typeof(Patch), new object[] { fix, 0, "test.inner.owner" })!;
        }

        internal static Patches WithInnerFinalizer(Patch patch)
        {
            ConstructorInfo seven = typeof(Patches).GetConstructor(new[]
            {
                typeof(Patch[]), typeof(Patch[]), typeof(Patch[]), typeof(Patch[]), typeof(Patch[]), typeof(Patch[]), typeof(Patch[]),
            }) ?? throw new InvalidOperationException("Patches constructor taking seven Patch[] parameters not found");
            return (Patches)seven.Invoke(new object?[] { null, null, null, null, null, null, new[] { patch } })!;
        }
    }

    [Collection("HarmonySerial")]
    public sealed class InfixV3CoexistenceTests
    {
        private static readonly MethodInfo Step = HarmonyVersions.Method(typeof(InnerPatchHelper), nameof(InnerPatchHelper.Step));

        private static readonly MethodInfo InnerPrefix = HarmonyVersions.Method(typeof(InfixMods), nameof(InfixMods.InnerPrefix));
    }
}
