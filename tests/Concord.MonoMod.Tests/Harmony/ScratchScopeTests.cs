using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Concord.Backend;
using Concord.Emit;
using Concord.MonoMod.Harmony;
using HarmonyLib;
using Xunit;

namespace Concord.MonoMod.Tests;

using CodeInstruction = HarmonyLib.CodeInstruction;

[Collection("HarmonySerial")]
public sealed class ScratchScopeTests
{
    private const string ForeignId = "test.scratchscope.foreign";

    [Fact]
    public void ATranspilerScratch_ResolvesLocalsOfASecondCopy_IntoThatCopy_PastAPlantedScope()
    {
        ResolveCacheTests.Twins twins = ResolveCacheTests.Twins.LoadDistinct();
        Assembly own = twins.Second.DeclaringType!.Assembly;
        Assembly other = twins.First.DeclaringType!.Assembly;

        Type holder = twins.Second.DeclaringType;
        Type twin = Array.Find(own.GetTypes(), type => type.Name.StartsWith("TwinType", StringComparison.Ordinal))
                    ?? throw new InvalidOperationException("the twin image carries no TwinType");

        using TranspilerScratch scratch = new TranspilerScratch();
        scratch.Generator.DeclareLocal(holder);
        scratch.Generator.DeclareLocal(twin);

        ResolveCacheTests.PlantScope(own, other);
        Assert.Same(other, ResolveCacheTests.BoundScope(own));

        IReadOnlyList<Type?> declared = scratch.DeclaredLocalTypes;

        Assert.Equal(2, declared.Count);
        Assert.Same(holder, declared[0]);
        Assert.Same(twin, declared[1]);
    }

    [TwinHashCollisionFact]
    public void ATranspilerScratch_ResolvesLocalsOfTwoCopiesSharingOneHash_EachIntoItsOwnCopy()
    {
        (MethodInfo First, MethodInfo Second, int Load) twins = ExactScopesTests.Collided();
        Type first = twins.First.DeclaringType!;
        Type second = twins.Second.DeclaringType!;

        using TranspilerScratch scratch = new TranspilerScratch();
        scratch.Generator.DeclareLocal(first);
        scratch.Generator.DeclareLocal(second);

        IReadOnlyList<Type?> declared = scratch.DeclaredLocalTypes;

        Assert.Equal(2, declared.Count);
        Assert.Same(first, declared[0]);
        Assert.Same(second, declared[1]);
    }

    [Fact]
    public void AForeignTranspilerOnASecondHarmonyCopy_BesideAConcordInjection_SeesCallsIntoThatCopy()
    {
        Assembly copy = SecondHarmonyCopyTests.SecondCopy();
        Assembly primary = typeof(HarmonyLib.Harmony).Assembly;
        Assert.Equal(primary.FullName, copy.FullName);

        Type tools = copy.GetType("HarmonyLib.AccessTools") ?? throw new InvalidOperationException("HarmonyLib.AccessTools not found in the copy");
        MethodInfo target = tools.GetMethod("IsStatic", new[] { typeof(MemberInfo) }) ?? throw new InvalidOperationException("AccessTools.IsStatic(MemberInfo) not found in the copy");
        MethodInfo recorder = typeof(CopyCallRecorder).GetMethod(nameof(CopyCallRecorder.Record))!;
        Injection head = new Injection(typeof(CopyCallRecorder).GetMethod(nameof(CopyCallRecorder.Head))!, new InjectAt.Head(), "test.scratchscope.concord", 0);

        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony(ForeignId);
        CoexRouter bridge = new CoexRouter(_ => { });
        RouteOutcome? result = null;
        try
        {
            result = bridge.Route(target, new[] { head }, true);
            Assert.Equal(RouteKind.Routed, result.Kind);

            CopyCallRecorder.Called.Clear();
            CopyCallRecorder.Heads = 0;
            foreign.Patch(target, transpiler: new HarmonyMethod(recorder) { after = new[] { "concord.bridge" } });

            List<MethodBase> intoHarmony = CopyCallRecorder.Called.FindAll(method => method.DeclaringType?.Assembly.FullName == primary.FullName);
            Assert.NotEmpty(intoHarmony);
            foreach (MethodBase called in intoHarmony)
            {
                Assert.Same(copy, called.DeclaringType!.Assembly);
            }

            Assert.Equal(true, target.Invoke(null, new object[] { typeof(CopyCallRecorder) }));
            Assert.Equal(1, CopyCallRecorder.Heads);
        }
        finally
        {
            result?.Handle?.Dispose();
            bridge.Dispose();
            TestUnpatch.Own(foreign, ForeignId);
            TranspilerParticipant.Registry.Clear(target);
        }
    }

    public static class CopyCallRecorder
    {
        public static List<MethodBase> Called { get; } = new List<MethodBase>();

        public static int Heads { get; set; }

        public static void Head(ControlHandle ch)
        {
            Heads++;
        }

        public static IEnumerable<CodeInstruction> Record(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) && instruction.operand is MethodBase method)
                {
                    Called.Add(method);
                }

                yield return instruction;
            }
        }
    }
}
