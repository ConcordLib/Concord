using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Concord;
using Concord.Backend;
using Concord.Emit;
using Concord.MonoMod;
using Concord.MonoMod.Harmony;
using HarmonyLib;
using Xunit;

namespace Concord.MonoMod.Tests
{
    using CodeInstruction = HarmonyLib.CodeInstruction;

    public static class DiscordSequenceLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class DiscordSequenceTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Foo()
        {
            DiscordSequenceLog.Entries.Add("body");
            return 7;
        }
    }

    public static class DiscordSequenceMods
    {
        public static void PostfixA()
        {
            DiscordSequenceLog.Entries.Add("postfixA");
        }

        public static void PostfixB()
        {
            DiscordSequenceLog.Entries.Add("postfixB");
        }

        public static void ConcordHead(ControlHandle ch)
        {
            DiscordSequenceLog.Entries.Add("head");
        }
    }

    public static class UncontestedTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Bare()
        {
            return 3;
        }
    }

    public static class UncontestedMods
    {
        public static void ConcordHead(ControlHandle ch)
        {
        }
    }

    public static class ForceRouteTarget
    {
        public static List<string> Entries = new List<string>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Bare()
        {
            return 5;
        }
    }

    public static class ForceRouteMods
    {
        public static void ConcordHead(ControlHandle ch)
        {
            ForceRouteTarget.Entries.Add("head");
        }
    }

    public static class ForeignOrderLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class ForeignOrderTargets
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int BeforeCase()
        {
            return 7;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int AfterCase()
        {
            ForeignOrderLog.Entries.Add("body");
            return 7;
        }
    }

    public static class ForeignOrderInjectionMethods
    {
        public static void ObserveReturn(ControlHandle<int> ch)
        {
            ForeignOrderLog.Entries.Add("return:" + ch.ReturnValue);
            ch.ReturnValue += 1;
        }

        public static void Head(ControlHandle ch)
        {
            ForeignOrderLog.Entries.Add("head");
        }
    }

    public static class ForeignOrderTranspilers
    {
        public static IEnumerable<CodeInstruction> SwapSevenTo42(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_I4_7 || (instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int value && value == 7))
                {
                    CodeInstruction replaced = new CodeInstruction(OpCodes.Ldc_I4, 42);
                    replaced.labels.AddRange(instruction.labels);
                    replaced.blocks.AddRange(instruction.blocks);
                    yield return replaced;
                }
                else
                {
                    yield return instruction;
                }
            }
        }
    }

    public static class PrefixSkipLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class PrefixSkipTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Skippy()
        {
            PrefixSkipLog.Entries.Add("body");
            return 7;
        }
    }

    public static class PrefixSkipMods
    {
        public static bool SkipPrefix(ref int __result)
        {
            PrefixSkipLog.Entries.Add("prefix");
            __result = -100;
            return false;
        }

        public static void ConcordHead(ControlHandle ch)
        {
            PrefixSkipLog.Entries.Add("head");
        }
    }

    public static class AroundGapLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class AroundGapTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Triple(int x)
        {
            AroundGapLog.Entries.Add("body");
            return x * 3;
        }
    }

    public static class AroundGapMods
    {
        public static int WrapTriple(int x, Operation<int, int> original)
        {
            AroundGapLog.Entries.Add("pre");
            int inner = original.Invoke(x);
            AroundGapLog.Entries.Add("post");
            return inner + 1;
        }

        public static void ForeignPostfix()
        {
            AroundGapLog.Entries.Add("foreignPostfix");
        }
    }

    public static class InvokeGapHelper
    {
        public static List<string> Entries = new List<string>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Step()
        {
            Entries.Add("step");
        }
    }

    public static class InvokeGapTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Run()
        {
            InvokeGapHelper.Entries.Add("before");
            InvokeGapHelper.Step();
            InvokeGapHelper.Entries.Add("after");
        }
    }

    public static class InvokeGapMods
    {
        public static void BeforeStep(ControlHandle ch)
        {
            InvokeGapHelper.Entries.Add("injected");
        }
    }

    public static class ConstantGapTarget
    {
        public static List<string> Entries = new List<string>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int AddBonus(int x)
        {
            return x + 100;
        }
    }

    public static class ConstantGapMods
    {
        public static int ReplaceBonus(int original)
        {
            return 200;
        }

        public static void ForeignPostfix()
        {
            ConstantGapTarget.Entries.Add("foreignPostfix");
        }
    }

    public struct CompactStructLocal
    {
        public int A;
        public int B;
    }

    public static class CompactStructTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static CompactStructLocal Make(int a)
        {
            return new CompactStructLocal { A = a, B = a * 2 };
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Sum(CompactStructLocal value)
        {
            return value.A + value.B;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run(int a)
        {
            CompactStructLocal held = Make(a);
            return Sum(held);
        }
    }

    public static class CompactStructMods
    {
        public static int WrapRun(int a, Operation<int, int> original)
        {
            return original.Invoke(a) + 1000;
        }
    }

    public static class SelfOwnedTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Bare()
        {
            return 6;
        }
    }

    public static class SelfOwnedMods
    {
        public static void ConcordHead(ControlHandle ch)
        {
        }
    }

    public static class ForeignOwnersTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Bare()
        {
            return 8;
        }
    }

    public static class ForeignOwnersMods
    {
        public static void ConcordHead(ControlHandle ch)
        {
        }

        public static void ForeignPostfix()
        {
        }
    }

    public sealed class InstanceGapTarget
    {
        public int Seed;

        public InstanceGapTarget(int seed)
        {
            Seed = seed;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int AddOne()
        {
            return Seed + 1;
        }
    }

    public static class InstanceGapMods
    {
        public static void ConcordHead(ControlHandle ch)
        {
            InstanceGapLog.Entries.Add("head");
        }
    }

    public static class InstanceGapLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class DisposalGranularityTarget
    {
        public static List<string> Entries = new List<string>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Bare()
        {
            return 9;
        }
    }

    public static class DisposalGranularityMods
    {
        public static void First(ControlHandle ch)
        {
            DisposalGranularityTarget.Entries.Add("first");
        }

        public static void Second(ControlHandle ch)
        {
            DisposalGranularityTarget.Entries.Add("second");
        }

        public static void ForeignPostfix()
        {
            DisposalGranularityTarget.Entries.Add("foreignPostfix");
        }
    }

    public static class ApplyToRoutedTarget
    {
        public static List<string> Entries = new List<string>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Bare()
        {
            return 4;
        }
    }

    public static class ApplyToRoutedMods
    {
        public static void First(ControlHandle ch)
        {
            ApplyToRoutedTarget.Entries.Add("first");
        }

        public static void Second(ControlHandle ch)
        {
            ApplyToRoutedTarget.Entries.Add("second");
        }
    }

    public abstract class AbstractRollbackTarget
    {
        public abstract int DoIt();
    }

    public static class AbstractRollbackMods
    {
        public static void ConcordHead(ControlHandle ch)
        {
        }
    }

    public static class RebuildFailureRecoveredTarget
    {
        public static List<string> Entries = new List<string>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Bare()
        {
            return 1;
        }
    }

    public static class RebuildFailureRecoveredMods
    {
        public static void First(ControlHandle ch)
        {
            RebuildFailureRecoveredTarget.Entries.Add("first");
        }

        public static void Second(ControlHandle ch)
        {
            RebuildFailureRecoveredTarget.Entries.Add("second");
        }
    }

    public static class RebuildFailureLostTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Bare()
        {
            return 1;
        }
    }

    public static class RebuildFailureLostMods
    {
        public static void First(ControlHandle ch)
        {
        }

        public static void Second(ControlHandle ch)
        {
        }
    }

    public static class DisposalFailureTarget
    {
        public static List<string> Entries = new List<string>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Bare()
        {
            return 1;
        }
    }

    public static class DisposalFailureMods
    {
        public static void First(ControlHandle ch)
        {
            DisposalFailureTarget.Entries.Add("first");
        }
    }

    public static class ComposeFailureLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class ComposeFailureHelper
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Step()
        {
            ComposeFailureLog.Entries.Add("step");
        }
    }

    public static class ComposeFailureTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run(int x)
        {
            ComposeFailureHelper.Step();
            ComposeFailureLog.Entries.Add("body");
            return x * 3;
        }
    }

    public static class ComposeFailureMods
    {
        public static int WrapRun(int x, Operation<int, int> original)
        {
            ComposeFailureLog.Entries.Add("pre");
            int inner = original.Invoke(x);
            ComposeFailureLog.Entries.Add("post");
            return inner;
        }

        public static void BeforeStep(ControlHandle ch)
        {
            ComposeFailureLog.Entries.Add("injected");
        }

        public static void ForeignPostfix()
        {
            ComposeFailureLog.Entries.Add("foreignPostfix");
        }
    }

    public static class IteratorReturnTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static IEnumerable<int> Numbers()
        {
            yield return 1;
            yield return 2;
        }
    }

    public static class IteratorReturnMods
    {
        public static void PassThrough(ControlHandle<IEnumerable<int>> ch)
        {
            IEnumerable<int> current = ch.ReturnValue;
            ch.ReturnValue = current;
        }
    }

    public static class ExceptionRegionInjectionLog
    {
        public static List<string> Entries = new List<string>();
    }

    public static class ExceptionRegionInjectionMods
    {
        public static void TryFinallyHead(ControlHandle ch)
        {
            try
            {
                ExceptionRegionInjectionLog.Entries.Add("try");
            }
            finally
            {
                ExceptionRegionInjectionLog.Entries.Add("finally");
            }
        }
    }

    [Collection("HarmonySerial")]
    public sealed class CoexistenceTests
    {
        private static MethodInfo Method(Type type, string name)
        {
            return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
        }

        private static Injection MakeHeadInjection(MethodInfo method, string owner)
        {
            return new Injection(method, new InjectAt.Head(), owner, 0);
        }

        [Fact]
        public void DiscordSequence_ConcordSurvivesForeignPatchAndUnpatchCycles()
        {
            MethodInfo target = Method(typeof(DiscordSequenceTarget), nameof(DiscordSequenceTarget.Foo));
            MethodInfo headMethod = Method(typeof(DiscordSequenceMods), nameof(DiscordSequenceMods.ConcordHead));
            MethodInfo postfixA = Method(typeof(DiscordSequenceMods), nameof(DiscordSequenceMods.PostfixA));
            MethodInfo postfixB = Method(typeof(DiscordSequenceMods), nameof(DiscordSequenceMods.PostfixB));

            HarmonyLib.Harmony harmonyA = new HarmonyLib.Harmony("test.modA");
            HarmonyLib.Harmony harmonyB = new HarmonyLib.Harmony("test.modB");
            CoexRouter bridge = new CoexRouter(_ => { });

            RouteOutcome? result = null;
            try
            {
                harmonyA.Patch(target, postfix: new HarmonyMethod(postfixA));

                DiscordSequenceLog.Entries.Clear();
                Assert.Equal(7, DiscordSequenceTarget.Foo());
                Assert.Equal(new List<string> { "body", "postfixA" }, DiscordSequenceLog.Entries);

                result = bridge.Route(target, new[] { MakeHeadInjection(headMethod, "test.concord") }, false);
                Assert.Equal(RouteKind.Routed, result.Kind);

                DiscordSequenceLog.Entries.Clear();
                Assert.Equal(7, DiscordSequenceTarget.Foo());
                Assert.Equal(new List<string> { "head", "body", "postfixA" }, DiscordSequenceLog.Entries);

                harmonyB.Patch(target, postfix: new HarmonyMethod(postfixB));

                DiscordSequenceLog.Entries.Clear();
                Assert.Equal(7, DiscordSequenceTarget.Foo());
                Assert.Equal(new List<string> { "head", "body", "postfixA", "postfixB" }, DiscordSequenceLog.Entries);

                TestUnpatch.Own(harmonyA, "test.modA");

                DiscordSequenceLog.Entries.Clear();
                Assert.Equal(7, DiscordSequenceTarget.Foo());
                Assert.Equal(new List<string> { "head", "body", "postfixB" }, DiscordSequenceLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
                TestUnpatch.Own(harmonyA, "test.modA");
                TestUnpatch.Own(harmonyB, "test.modB");
            }
        }

        [Fact]
        public void TryRoute_NoForeignPatches_NotContested_NoConcordTranspilerInstalled()
        {
            MethodInfo target = Method(typeof(UncontestedTarget), nameof(UncontestedTarget.Bare));
            MethodInfo headMethod = Method(typeof(UncontestedMods), nameof(UncontestedMods.ConcordHead));
            CoexRouter bridge = new CoexRouter(_ => { });

            try
            {
                RouteOutcome result = bridge.Route(target, new[] { MakeHeadInjection(headMethod, "test.concord") }, false);

                Assert.Equal(RouteKind.NotContested, result.Kind);
                Assert.Null(PatchProcessor.GetPatchInfo(target));
            }
            finally
            {
                TranspilerParticipant.Registry.Clear(target);
            }
        }

        [Fact]
        public void TryRoute_ForceRoute_NoForeignPatches_Routes_HeadFires()
        {
            MethodInfo target = Method(typeof(ForceRouteTarget), nameof(ForceRouteTarget.Bare));
            MethodInfo headMethod = Method(typeof(ForceRouteMods), nameof(ForceRouteMods.ConcordHead));
            CoexRouter bridge = new CoexRouter(_ => { });

            RouteOutcome? result = null;
            try
            {
                result = bridge.Route(target, new[] { MakeHeadInjection(headMethod, "test.concord") }, true);

                Assert.Equal(RouteKind.Routed, result.Kind);

                ForceRouteTarget.Entries.Clear();
                Assert.Equal(5, ForceRouteTarget.Bare());
                Assert.Equal(new List<string> { "head" }, ForceRouteTarget.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
            }
        }

        [Fact]
        public void ForeignTranspilerBeforeConcord_CompositionSeesRewrittenStream()
        {
            MethodInfo target = Method(typeof(ForeignOrderTargets), nameof(ForeignOrderTargets.BeforeCase));
            MethodInfo foreignTranspiler = Method(typeof(ForeignOrderTranspilers), nameof(ForeignOrderTranspilers.SwapSevenTo42));
            MethodInfo returnMethod = Method(typeof(ForeignOrderInjectionMethods), nameof(ForeignOrderInjectionMethods.ObserveReturn));

            HarmonyLib.Harmony harmonyForeign = new HarmonyLib.Harmony("test.foreign.before");
            CoexRouter bridge = new CoexRouter(_ => { });

            RouteOutcome? result = null;
            try
            {
                harmonyForeign.Patch(target, transpiler: new HarmonyMethod(foreignTranspiler) { priority = Priority.High });

                Injection returnInjection = new Injection(returnMethod, new InjectAt.Return(), "test.concord.before", 0);
                result = bridge.Route(target, new[] { returnInjection }, true);
                Assert.Equal(RouteKind.Routed, result.Kind);

                ForeignOrderLog.Entries.Clear();
                int value = ForeignOrderTargets.BeforeCase();

                Assert.Equal(43, value);
                Assert.Equal(new List<string> { "return:42" }, ForeignOrderLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
                TestUnpatch.Own(harmonyForeign, "test.foreign.before");
            }
        }

        [Fact]
        public void ForeignTranspilerAfterConcord_BenignRewriteOverComposedStreamStaysCorrect()
        {
            MethodInfo target = Method(typeof(ForeignOrderTargets), nameof(ForeignOrderTargets.AfterCase));
            MethodInfo foreignTranspiler = Method(typeof(ForeignOrderTranspilers), nameof(ForeignOrderTranspilers.SwapSevenTo42));
            MethodInfo headMethod = Method(typeof(ForeignOrderInjectionMethods), nameof(ForeignOrderInjectionMethods.Head));

            HarmonyLib.Harmony harmonyForeign = new HarmonyLib.Harmony("test.foreign.after");
            CoexRouter bridge = new CoexRouter(_ => { });

            RouteOutcome? result = null;
            try
            {
                result = bridge.Route(target, new[] { MakeHeadInjection(headMethod, "test.concord.after") }, true);
                Assert.Equal(RouteKind.Routed, result.Kind);

                harmonyForeign.Patch(target, transpiler: new HarmonyMethod(foreignTranspiler) { after = new[] { "concord.bridge" } });

                ForeignOrderLog.Entries.Clear();
                int value = ForeignOrderTargets.AfterCase();

                Assert.Equal(42, value);
                Assert.Equal(new List<string> { "head", "body" }, ForeignOrderLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
                TestUnpatch.Own(harmonyForeign, "test.foreign.after");
            }
        }

        [Fact]
        public void ForeignPrefixReturningFalse_WhileHarmonyOwnsTheEntryPoint_SkipsConcordsWholeBody()
        {
            MethodInfo target = Method(typeof(PrefixSkipTarget), nameof(PrefixSkipTarget.Skippy));
            MethodInfo prefix = Method(typeof(PrefixSkipMods), nameof(PrefixSkipMods.SkipPrefix));
            MethodInfo headMethod = Method(typeof(PrefixSkipMods), nameof(PrefixSkipMods.ConcordHead));

            HarmonyLib.Harmony harmonyForeign = new HarmonyLib.Harmony("test.prefix.foreign");
            CoexRouter bridge = new CoexRouter(_ => { });

            RouteOutcome? result = null;
            try
            {
                result = bridge.Route(target, new[] { MakeHeadInjection(headMethod, "test.prefix.concord") }, true);
                Assert.Equal(RouteKind.Routed, result.Kind);

                PrefixSkipLog.Entries.Clear();
                Assert.Equal(7, PrefixSkipTarget.Skippy());
                Assert.Equal(new List<string> { "head", "body" }, PrefixSkipLog.Entries);

                harmonyForeign.Patch(target, prefix: new HarmonyMethod(prefix));

                PrefixSkipLog.Entries.Clear();
                Assert.Equal(-100, PrefixSkipTarget.Skippy());
                Assert.Equal(new List<string> { "prefix" }, PrefixSkipLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
                TestUnpatch.Own(harmonyForeign, "test.prefix.foreign");
            }
        }

        [Fact]
        public void WholeMethodAround_PreAndPostFireAndForeignPostfixStillRunsOncePerOuterInvocation()
        {
            MethodInfo target = Method(typeof(AroundGapTarget), nameof(AroundGapTarget.Triple));
            MethodInfo wrapMethod = Method(typeof(AroundGapMods), nameof(AroundGapMods.WrapTriple));
            MethodInfo foreignPostfix = Method(typeof(AroundGapMods), nameof(AroundGapMods.ForeignPostfix));

            HarmonyLib.Harmony harmonyForeign = new HarmonyLib.Harmony("test.around.foreign");
            CoexRouter bridge = new CoexRouter(_ => { });

            RouteOutcome? result = null;
            try
            {
                harmonyForeign.Patch(target, postfix: new HarmonyMethod(foreignPostfix));

                Injection around = new Injection(wrapMethod, new InjectAt.Around(), "test.concord.around", 0);
                result = bridge.Route(target, new[] { around }, true);
                Assert.Equal(RouteKind.Routed, result.Kind);

                AroundGapLog.Entries.Clear();
                int value = AroundGapTarget.Triple(5);

                Assert.Equal(16, value);
                Assert.Equal(new List<string> { "pre", "body", "post", "foreignPostfix" }, AroundGapLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
                TestUnpatch.Own(harmonyForeign, "test.around.foreign");
            }
        }

        [Fact]
        public void InvokeCallSiteInjection_FiresUnderHarmonyRouting()
        {
            MethodInfo target = Method(typeof(InvokeGapTarget), nameof(InvokeGapTarget.Run));
            MethodInfo stepMethod = Method(typeof(InvokeGapHelper), nameof(InvokeGapHelper.Step));
            MethodInfo beforeStep = Method(typeof(InvokeGapMods), nameof(InvokeGapMods.BeforeStep));
            CoexRouter bridge = new CoexRouter(_ => { });

            RouteOutcome? result = null;
            try
            {
                Injection invoke = new Injection(
                    beforeStep,
                    new InjectAt.Invoke(typeof(InvokeGapHelper), nameof(InvokeGapHelper.Step), At.Head, 0),
                    "test.concord.invoke",
                    0);
                result = bridge.Route(target, new[] { invoke }, true);
                Assert.Equal(RouteKind.Routed, result.Kind);

                InvokeGapHelper.Entries.Clear();
                InvokeGapTarget.Run();

                Assert.Equal(new List<string> { "before", "injected", "step", "after" }, InvokeGapHelper.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
            }
        }

        [Fact]
        public void ConstantInjection_ReplacesInlinedInt32Constant_UnderGenuinelyContestedTarget()
        {
            MethodInfo target = Method(typeof(ConstantGapTarget), nameof(ConstantGapTarget.AddBonus));
            MethodInfo replaceBonus = Method(typeof(ConstantGapMods), nameof(ConstantGapMods.ReplaceBonus));
            MethodInfo foreignPostfix = Method(typeof(ConstantGapMods), nameof(ConstantGapMods.ForeignPostfix));

            HarmonyLib.Harmony harmonyForeign = new HarmonyLib.Harmony("test.constant.foreign");
            CoexRouter bridge = new CoexRouter(_ => { });

            RouteOutcome? result = null;
            try
            {
                harmonyForeign.Patch(target, postfix: new HarmonyMethod(foreignPostfix));
                Assert.True(HasForeignPatch(target));

                Injection constant = new Injection(replaceBonus, new InjectAt.Constant(100, 0), "test.concord.constant", 0);
                result = bridge.Route(target, new[] { constant }, false);
                Assert.Equal(RouteKind.Routed, result.Kind);

                ConstantGapTarget.Entries.Clear();
                int value = Assert.IsType<int>(target.Invoke(null, new object[] { 5 }));

                Assert.Equal(205, value);
                Assert.Equal(new List<string> { "foreignPostfix" }, ConstantGapTarget.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
                TestUnpatch.Own(harmonyForeign, "test.constant.foreign");
            }
        }

        [Fact]
        public void ATargetOnlyConcordPatched_NeverLooksForeignOwned()
        {
            MethodInfo target = Method(typeof(SelfOwnedTarget), nameof(SelfOwnedTarget.Bare));
            MethodInfo headMethod = Method(typeof(SelfOwnedMods), nameof(SelfOwnedMods.ConcordHead));
            CoexRouter bridge = new CoexRouter(_ => { });

            RouteOutcome? firstResult = null;
            try
            {
                firstResult = bridge.Route(target, new[] { MakeHeadInjection(headMethod, "test.selfowned") }, false);
                Assert.Equal(RouteKind.NotContested, firstResult.Kind);

                RouteOutcome secondResult = bridge.Route(target, new[] { MakeHeadInjection(headMethod, "test.selfowned.second") }, false);

                Assert.Equal(RouteKind.NotContested, secondResult.Kind);
                secondResult.Handle?.Dispose();
            }
            finally
            {
                firstResult?.Handle?.Dispose();
            }
        }

        private static bool HasForeignPatch(MethodBase target)
        {
            Patches patchInfo = PatchProcessor.GetPatchInfo(target);
            if (patchInfo == null)
            {
                return false;
            }

            foreach (Patch patch in patchInfo.Postfixes)
            {
                if (patch.PatchMethod != TranspilerParticipant.TranspileMethod)
                {
                    return true;
                }
            }

            return false;
        }

        [Fact]
        public void InstanceMethodTarget_ThisFlowsThroughArgumentSlotZero()
        {
            MethodInfo target = Method(typeof(InstanceGapTarget), nameof(InstanceGapTarget.AddOne));
            MethodInfo headMethod = Method(typeof(InstanceGapMods), nameof(InstanceGapMods.ConcordHead));
            CoexRouter bridge = new CoexRouter(_ => { });

            RouteOutcome? result = null;
            try
            {
                result = bridge.Route(target, new[] { MakeHeadInjection(headMethod, "test.concord.instance") }, true);
                Assert.Equal(RouteKind.Routed, result.Kind);

                InstanceGapLog.Entries.Clear();
                InstanceGapTarget instance = new InstanceGapTarget(41);
                int value = instance.AddOne();

                Assert.Equal(42, value);
                Assert.Equal(new List<string> { "head" }, InstanceGapLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
            }
        }

        [Fact]
        public void DisposalGranularity_DisposeFirstThenLast_ForeignPostfixIntactThroughout()
        {
            MethodInfo target = Method(typeof(DisposalGranularityTarget), nameof(DisposalGranularityTarget.Bare));
            MethodInfo first = Method(typeof(DisposalGranularityMods), nameof(DisposalGranularityMods.First));
            MethodInfo second = Method(typeof(DisposalGranularityMods), nameof(DisposalGranularityMods.Second));
            MethodInfo foreignPostfix = Method(typeof(DisposalGranularityMods), nameof(DisposalGranularityMods.ForeignPostfix));

            HarmonyLib.Harmony harmonyForeign = new HarmonyLib.Harmony("test.disposal.foreign");
            CoexRouter bridge = new CoexRouter(_ => { });

            RouteOutcome? result = null;
            try
            {
                harmonyForeign.Patch(target, postfix: new HarmonyMethod(foreignPostfix));

                Injection firstInjection = MakeHeadInjection(first, "test.disposal.first");
                Injection secondInjection = MakeHeadInjection(second, "test.disposal.second");
                result = bridge.Route(target, new[] { firstInjection }, true);
                Assert.Equal(RouteKind.Routed, result.Kind);
                Assert.NotNull(result.Handle);

                IBackendHandle secondHandle = bridge.Apply(target, new[] { secondInjection });

                DisposalGranularityTarget.Entries.Clear();
                DisposalGranularityTarget.Bare();
                Assert.Contains("first", DisposalGranularityTarget.Entries);
                Assert.Contains("second", DisposalGranularityTarget.Entries);
                Assert.Contains("foreignPostfix", DisposalGranularityTarget.Entries);

                result.Handle.Dispose();

                DisposalGranularityTarget.Entries.Clear();
                DisposalGranularityTarget.Bare();
                Assert.DoesNotContain("first", DisposalGranularityTarget.Entries);
                Assert.Contains("second", DisposalGranularityTarget.Entries);
                Assert.Contains("foreignPostfix", DisposalGranularityTarget.Entries);
                Assert.NotNull(PatchProcessor.GetPatchInfo(target));

                secondHandle.Dispose();

                Patches patchInfoAfter = PatchProcessor.GetPatchInfo(target);
                Assert.True(patchInfoAfter == null || !ContainsConcordTranspiler(patchInfoAfter));

                DisposalGranularityTarget.Entries.Clear();
                DisposalGranularityTarget.Bare();
                Assert.DoesNotContain("first", DisposalGranularityTarget.Entries);
                Assert.DoesNotContain("second", DisposalGranularityTarget.Entries);
                Assert.Contains("foreignPostfix", DisposalGranularityTarget.Entries);
            }
            finally
            {
                TestUnpatch.Own(harmonyForeign, "test.disposal.foreign");
            }
        }

        [Fact]
        public void ApplyToRouted_SecondInjectionLandsAfterTryRoute_BothFireInOrdererOrder()
        {
            MethodInfo target = Method(typeof(ApplyToRoutedTarget), nameof(ApplyToRoutedTarget.Bare));
            MethodInfo first = Method(typeof(ApplyToRoutedMods), nameof(ApplyToRoutedMods.First));
            MethodInfo second = Method(typeof(ApplyToRoutedMods), nameof(ApplyToRoutedMods.Second));
            CoexRouter bridge = new CoexRouter(_ => { });

            RouteOutcome? result = null;
            IBackendHandle? secondHandle = null;
            try
            {
                result = bridge.Route(target, new[] { MakeHeadInjection(first, "test.apply.first") }, true);
                Assert.Equal(RouteKind.Routed, result.Kind);

                secondHandle = bridge.Apply(target, new[] { MakeHeadInjection(second, "test.apply.second") });

                ApplyToRoutedTarget.Entries.Clear();
                ApplyToRoutedTarget.Bare();

                Assert.Equal(new List<string> { "second", "first" }, ApplyToRoutedTarget.Entries);
            }
            finally
            {
                secondHandle?.Dispose();
                result?.Handle?.Dispose();
            }
        }

        private static bool ContainsConcordTranspiler(Patches patchInfo)
        {
            foreach (Patch patch in patchInfo.Transpilers)
            {
                if (patch.PatchMethod == TranspilerParticipant.TranspileMethod)
                {
                    return true;
                }
            }

            return false;
        }

        // Harmony hands the bridge the stream for the method it opened. On an iterator that is the
        // compiler's stub, which is what a PatchBody.Declared injection is shaped against, so the
        // composition has to keep the stub's identity instead of resolving through to MoveNext -
        // composing against MoveNext's bool return drops the IEnumerable and the target returns null.
        [Fact]
        public void TryRoute_IteratorTarget_DeclaredBody_ComposesAgainstTheStub()
        {
            MethodInfo target = Method(typeof(IteratorReturnTarget), nameof(IteratorReturnTarget.Numbers));
            MethodInfo passThrough = Method(typeof(IteratorReturnMods), nameof(IteratorReturnMods.PassThrough));

            CoexRouter bridge = new CoexRouter(_ => { });

            RouteOutcome? result = null;
            try
            {
                Injection returnInjection = new Injection(passThrough, new InjectAt.Return(), "test.concord.iterator", 0);

                result = bridge.Route(target, new[] { returnInjection }, harmonyOwns: true);

                Assert.Equal(RouteKind.Routed, result.Kind);
                Assert.Equal(new List<int> { 1, 2 }, new List<int>(IteratorReturnTarget.Numbers()));
            }
            finally
            {
                result?.Handle?.Dispose();
                TranspilerParticipant.Registry.Clear(MethodIdentity.Normalize(target));
                TranspilerParticipant.LastStreamFailure = null;
                TranspilerParticipant.Log = null;
            }
        }

        // Harmony wants EndExceptionBlock on the region's last instruction; Concord puts it on the
        // one after. Composition can introduce a region the incoming stream never had, so that
        // translation cannot be gated on the incoming stream having had one.
        [Fact]
        public void TryRoute_InjectionBodyWithExceptionRegion_ComposesValidStream()
        {
            MethodInfo target = Method(typeof(ForceRouteTarget), nameof(ForceRouteTarget.Bare));
            MethodInfo tryFinallyHead = Method(typeof(ExceptionRegionInjectionMods), nameof(ExceptionRegionInjectionMods.TryFinallyHead));

            CoexRouter bridge = new CoexRouter(_ => { });

            RouteOutcome? result = null;
            try
            {
                result = bridge.Route(target, new[] { MakeHeadInjection(tryFinallyHead, "test.concord.tryfinally") }, harmonyOwns: true);

                Assert.Equal(RouteKind.Routed, result.Kind);

                ExceptionRegionInjectionLog.Entries.Clear();
                Assert.Equal(5, ForceRouteTarget.Bare());
                Assert.Equal(new List<string> { "try", "finally" }, ExceptionRegionInjectionLog.Entries);
            }
            finally
            {
                result?.Handle?.Dispose();
                TranspilerParticipant.Registry.Clear(MethodIdentity.Normalize(target));
                TranspilerParticipant.LastStreamFailure = null;
                TranspilerParticipant.Log = null;
            }
        }

        [Fact]
        public void WholeMethodAround_CompactOnlyStructLocalKeepsItsType()
        {
            MethodInfo target = Method(typeof(CompactStructTarget), nameof(CompactStructTarget.Run));
            MethodInfo wrapMethod = Method(typeof(CompactStructMods), nameof(CompactStructMods.WrapRun));
            MethodInfo foreignPostfix = Method(typeof(AroundGapMods), nameof(AroundGapMods.ForeignPostfix));

            HarmonyLib.Harmony harmonyForeign = new HarmonyLib.Harmony("test.around.structlocal");
            CoexRouter bridge = new CoexRouter(_ => { });

            RouteOutcome? result = null;
            try
            {
                harmonyForeign.Patch(target, postfix: new HarmonyMethod(foreignPostfix));

                Injection around = new Injection(wrapMethod, new InjectAt.Around(), "test.concord.around.structlocal", 0);
                result = bridge.Route(target, new[] { around }, true);
                Assert.Equal(RouteKind.Routed, result.Kind);

                Assert.Equal(1015, CompactStructTarget.Run(5));
            }
            finally
            {
                result?.Handle?.Dispose();
                TestUnpatch.Own(harmonyForeign, "test.around.structlocal");
            }
        }
    }
}
