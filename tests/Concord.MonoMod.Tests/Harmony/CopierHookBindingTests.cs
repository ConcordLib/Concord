using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using Concord;
using Concord.Backend;
using Concord.Emit;
using Concord.MonoMod;
using Concord.MonoMod.Harmony;
using HarmonyLib;
using Xunit;

namespace Concord.MonoMod.Tests;

public sealed class CopierMemberFactAttribute : FactAttribute {
    public CopierMemberFactAttribute(string member) {
        string? refusal = CopierMembers.Refusal(member);
        if (refusal != null) {
            Skip = refusal;
        }
    }
}

public static class CopierMembers {
    public static string? Refusal(string member) {
        Assembly harmony = typeof(HarmonyLib.Harmony).Assembly;
        Type? copier = harmony.GetType("HarmonyLib.MethodCopier");
        Type? reader = harmony.GetType("HarmonyLib.MethodBodyReader");

        if (copier == null) {
            return "the Harmony this run loaded carries no HarmonyLib.MethodCopier, so Concord's body cannot reach its wrapper build";
        }

        if (reader == null) {
            return "the Harmony this run loaded carries no HarmonyLib.MethodBodyReader, so the copier's source method cannot be read";
        }

        switch (member) {
            case "MethodCopier.transpilers":
                return copier.GetField("transpilers", BindingFlags.NonPublic | BindingFlags.Instance) == null
                    ? "the Harmony this run loaded carries no HarmonyLib.MethodCopier.transpilers field"
                    : null;
            case "MethodCopier.reader":
                return copier.GetField("reader", BindingFlags.NonPublic | BindingFlags.Instance) == null
                    ? "the Harmony this run loaded carries no HarmonyLib.MethodCopier.reader field"
                    : null;
            case "MethodBodyReader.method":
                return reader.GetField("method", BindingFlags.NonPublic | BindingFlags.Instance) == null
                    ? "the Harmony this run loaded carries no HarmonyLib.MethodBodyReader.method field"
                    : null;
            case "MethodCopier..ctor":
                foreach (ConstructorInfo candidate in copier.GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)) {
                    ParameterInfo[] parameters = candidate.GetParameters();
                    if (parameters.Length == 3 && parameters[0].ParameterType == typeof(MethodBase) && parameters[1].ParameterType == typeof(ILGenerator)) {
                        return null;
                    }

                    if (parameters.Length == 1 && parameters[0].ParameterType.FullName == "HarmonyLib.MethodCreatorConfig") {
                        return null;
                    }
                }

                return "the Harmony this run loaded carries no HarmonyLib.MethodCopier constructor Concord can hook";
            default:
                throw new InvalidOperationException("unknown copier member " + member);
        }
    }
}

public static class CopierBindTargets {
    public static readonly List<string> Ran = [];

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Bound(int seed) {
        Ran.Add("body");
        return seed + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Read(int seed) {
        Ran.Add("body");
        return seed + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Unread(int seed) {
        Ran.Add("body");
        return seed + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ReadFromTranspiler(int seed) {
        Ran.Add("body");
        return seed + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ThrowingRead(int seed) {
        Ran.Add("body");
        return seed + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int ThrowingReadApply(int seed) {
        Ran.Add("body");
        return seed + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int RestoredRead(int seed) {
        Ran.Add("body");
        return seed + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int UnboundStateRun(int seed) {
        Ran.Add("body");
        return seed + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int UnboundStateRead(int seed) {
        Ran.Add("body");
        return seed + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int UnboundStateHarmonyFirst(int seed) {
        Ran.Add("body");
        return seed + 1;
    }
}

public static class CopierBindMods {
    public static MethodBase? ReadTarget { get; set; }

    public static int Reads { get; set; }

    public static bool SawConcordsBody { get; set; }

    public static void ConcordNote() {
        CopierBindTargets.Ran.Add("concord");
    }

    public static void HarmonyPrefix() {
        CopierBindTargets.Ran.Add("harmony");
    }

    public static void HarmonyPostfix() {
        CopierBindTargets.Ran.Add("harmony");
    }

    public static IEnumerable<HarmonyLib.CodeInstruction> ReadingTranspiler(IEnumerable<HarmonyLib.CodeInstruction> instructions) {
        Reads++;
        SawConcordsBody = CopierHookBindingTests.CarriesConcordsNote(
            PatchProcessor.GetOriginalInstructions(ReadTarget!, PatchProcessor.CreateILGenerator(ReadTarget!)));
        return instructions;
    }
}

[Collection("HarmonySerial")]
public sealed class CopierHookBindingTests {
    private static IPatchBackend? replaced;

    private static RoutingBackend? armed;

    [CopierMemberFact("MethodCopier.transpilers")]
    public void TheTranspilersFieldIsPresent() {
        Assert.Null(CopierMembers.Refusal("MethodCopier.transpilers"));
    }

    [CopierMemberFact("MethodCopier.reader")]
    public void TheReaderFieldIsPresent() {
        Assert.Null(CopierMembers.Refusal("MethodCopier.reader"));
    }

    [CopierMemberFact("MethodBodyReader.method")]
    public void TheReadersMethodFieldIsPresent() {
        Assert.Null(CopierMembers.Refusal("MethodBodyReader.method"));
    }

    [CopierMemberFact("MethodCopier..ctor")]
    public void AHookableConstructorIsPresent() {
        Assert.Null(CopierMembers.Refusal("MethodCopier..ctor"));
    }

    [Fact]
    public void TheHookBindsOnTheLoadedCopy_AndFeedsConcordsBodyToHarmonysWrapper() {
        List<string> log = [];
        MethodInfo target = typeof(CopierBindTargets).GetMethod(nameof(CopierBindTargets.Bound))!;
        const string id = "test.copierbind";
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony(id);
        IBackendHandle? handle = null;

        try {
            MonoModEntryPoint.Install(log.Add, null);
            Assembly copy = typeof(HarmonyLib.Harmony).Assembly;

            Assert.True(MethodCopierHook.IsHookedOn(copy), "the copier hook did not bind. Logged: " + string.Join(" | ", log));
            Assert.Contains("MethodCopier.transpilers", MethodCopierHook.BoundMembers(copy));
            Assert.Contains("MethodCopier.reader", MethodCopierHook.BoundMembers(copy));
            Assert.Contains("MethodBodyReader.method", MethodCopierHook.BoundMembers(copy));

            handle = PatchBackend.Current.ApplyComposed(target, new[] {
                new Injection(typeof(CopierBindMods).GetMethod(nameof(CopierBindMods.ConcordNote))!, new InjectAt.Head(), "test.copierbind.concord", 0),
            });

            foreign.Patch(target, prefix: new HarmonyMethod(typeof(CopierBindMods).GetMethod(nameof(CopierBindMods.HarmonyPrefix))!));

            CopierBindTargets.Ran.Clear();
            Assert.Equal(2, CopierBindTargets.Bound(1));
            Assert.Equal(new[] { "harmony", "concord", "body" }, CopierBindTargets.Ran.ToArray());
        } finally {
            handle?.Dispose();
            TestUnpatch.Own(foreign, id);
            MonoModEntryPoint.ResetForTests();
        }
    }

    [Fact]
    public void AHarmonyReadOfAConcordInjectedMethod_SeesConcordsBody_AndTheFeedStaysSingle() {
        List<string> log = [];
        MethodInfo target = typeof(CopierBindTargets).GetMethod(nameof(CopierBindTargets.Read))!;
        MethodInfo untouched = typeof(CopierBindTargets).GetMethod(nameof(CopierBindTargets.Unread))!;
        const string id = "test.copierread";
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony(id);
        IBackendHandle? handle = null;

        try {
            MonoModEntryPoint.Install(log.Add, null);

            handle = PatchBackend.Current.ApplyComposed(target, new[] {
                new Injection(typeof(CopierBindMods).GetMethod(nameof(CopierBindMods.ConcordNote))!, new InjectAt.Head(), "test.copierread.concord", 0),
            });

            List<HarmonyLib.CodeInstruction> injected = PatchProcessor.GetOriginalInstructions(target, PatchProcessor.CreateILGenerator(target));
            Assert.True(
                CarriesConcordsNote(injected),
                "GetOriginalInstructions returned a stream without Concord's injection in it. Logged: " + string.Join(" | ", log));

            List<HarmonyLib.CodeInstruction> declared = PatchProcessor.GetOriginalInstructions(untouched, PatchProcessor.CreateILGenerator(untouched));
            Assert.NotEmpty(declared);
            Assert.False(CarriesConcordsNote(declared), "a target with no injections read back a composed stream");

            foreign.Patch(target, postfix: new HarmonyMethod(typeof(CopierBindMods).GetMethod(nameof(CopierBindMods.HarmonyPostfix))!));

            CopierBindTargets.Ran.Clear();
            Assert.Equal(2, CopierBindTargets.Read(1));
            Assert.Equal(new[] { "concord", "body", "harmony" }, CopierBindTargets.Ran.ToArray());
        } finally {
            handle?.Dispose();
            TestUnpatch.Own(foreign, id);
            MonoModEntryPoint.ResetForTests();
        }
    }

    [Fact]
    public void AForeignTranspilerReadingItsOwnTarget_SeesConcordsBody_AndTheWrapperStillBuilds() {
        List<string> log = [];
        MethodInfo target = typeof(CopierBindTargets).GetMethod(nameof(CopierBindTargets.ReadFromTranspiler))!;
        const string id = "test.copierread.reentrant";
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony(id);
        IBackendHandle? handle = null;

        try {
            MonoModEntryPoint.Install(log.Add, null);

            handle = PatchBackend.Current.ApplyComposed(target, new[] {
                new Injection(typeof(CopierBindMods).GetMethod(nameof(CopierBindMods.ConcordNote))!, new InjectAt.Head(), "test.copierread.reentrant.concord", 0),
            });

            CopierBindMods.ReadTarget = target;
            CopierBindMods.Reads = 0;
            CopierBindMods.SawConcordsBody = false;

            foreign.Patch(
                target,
                prefix: new HarmonyMethod(typeof(CopierBindMods).GetMethod(nameof(CopierBindMods.HarmonyPrefix))!),
                transpiler: new HarmonyMethod(typeof(CopierBindMods).GetMethod(nameof(CopierBindMods.ReadingTranspiler))!));

            Assert.Equal(1, CopierBindMods.Reads);
            Assert.True(
                CopierBindMods.SawConcordsBody,
                "the re-entrant read inside the transpiler missed Concord's injection. Logged: " + string.Join(" | ", log));

            CopierBindTargets.Ran.Clear();
            Assert.Equal(2, CopierBindTargets.ReadFromTranspiler(1));
            Assert.Equal(new[] { "harmony", "concord", "body" }, CopierBindTargets.Ran.ToArray());
        } finally {
            handle?.Dispose();
            TestUnpatch.Own(foreign, id);
            CopierBindMods.ReadTarget = null;
            MonoModEntryPoint.ResetForTests();
        }
    }

    [Fact]
    public void TheHookBindsEveryCopierConstructorTheLoadedCopyCarries() {
        List<string> log = [];

        try {
            MonoModEntryPoint.Install(log.Add, null);
            Assembly copy = typeof(HarmonyLib.Harmony).Assembly;

            Assert.Equal(ConstructorMembersOf(copy), ConstructorMembersIn(MethodCopierHook.BoundMembers(copy)));
        } finally {
            MonoModEntryPoint.ResetForTests();
        }
    }

    [Fact]
    public void TheHookBindsTheThreeArgumentConstructor_OnACopyThatCarriesOnlyThatOne() {
        List<string> log = [];
        Assembly image = Release("2.2.2");

        try {
            Assert.True(
                MethodCopierHook.TryInstallFor(image, new MonoModBackend(), log.Add, out string? missing),
                "the copier hook did not bind on Harmony 2.2.2: " + missing);

            Assert.Equal(
                new[] { "MethodCopier..ctor(MethodBase, ILGenerator, LocalBuilder[])" },
                ConstructorMembersIn(MethodCopierHook.BoundMembers(image)));
        } finally {
            MethodCopierHook.RemoveFor(image);
        }
    }

    [Fact]
    public void ACopyMissingACopierMember_IsRefusedWhole() {
        List<string> log = [];
        Assembly loaded = typeof(HarmonyLib.Harmony).Assembly;

        try {
            MethodCopierHook.DeniedMemberForTests = "MethodCopier.transpilers";
            UpdateWrapperHook.ResetForTests();

            RoutingBackend router = new RoutingBackend(new MonoModBackend(), log.Add);
            router.ActivateHost(new HarmonyBridge(log.Add));

            Assert.False(UpdateWrapperHook.IsHookedOn(loaded), "a copy whose copier member did not bind kept its notifier. Logged: " + string.Join(" | ", log));
            Assert.False(MethodCopierHook.IsHookedOn(loaded));
            Assert.Single(
                log,
                line => line.StartsWith(CoexistenceLogMarkers.HookUnavailable, StringComparison.Ordinal)
                        && line.Contains("#" + loaded.GetHashCode())
                        && line.Contains("'transpilers' field"));
            Assert.DoesNotContain(
                log,
                line => line.StartsWith(CoexistenceLogMarkers.HookInstalled, StringComparison.Ordinal)
                        && line.Contains("#" + loaded.GetHashCode()));
        } finally {
            Disarm();
        }
    }

    [Fact]
    public void ACopyMissingUpdateWrapper_IsRefusedWhole() {
        List<string> log = [];

        try {
            ArmWithout(null, log);

            Assembly doctored = Doctored("UpdateWrapper", "UpdateWrappeZ");

            Assert.False(UpdateWrapperHook.IsHookedOn(doctored), "a copy with no UpdateWrapper was kept. Logged: " + string.Join(" | ", log));
            Assert.Contains(
                log,
                line => line.StartsWith(CoexistenceLogMarkers.HookUnavailable, StringComparison.Ordinal)
                        && line.Contains("#" + doctored.GetHashCode())
                        && line.Contains("UpdateWrapper"));
        } finally {
            Disarm();
        }
    }

    [Fact]
    public void ACopyWhosePatchRecordReadThrows_StillHoldsThePatch_AndLogsOneLine() {
        List<string> log = [];
        MethodInfo target = typeof(CopierBindTargets).GetMethod(nameof(CopierBindTargets.ThrowingRead))!;
        const string id = "test.readthrows";
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony(id);
        Assembly loaded = typeof(HarmonyLib.Harmony).Assembly;

        try {
            HarmonyBridge bridge = ArmWithout(null, log);
            foreign.Patch(target, prefix: new HarmonyMethod(typeof(CopierBindMods).GetMethod(nameof(CopierBindMods.HarmonyPrefix))!));
            Assert.True(bridge.HasPatch(target), "Harmony's own patch was invisible before the read was made to throw. Logged: " + string.Join(" | ", log));

            UpdateWrapperHook.ThrowOnPatchInfoReadForTests = true;
            log.Clear();

            Assert.True(bridge.HasPatch(target), "the public PatchProcessor read did not cover for the hooked read throwing. Logged: " + string.Join(" | ", log));
            Assert.Equal(1, ReadFailureLines(log, loaded));

            Assert.True(bridge.HasPatch(target));
            Assert.Equal(1, ReadFailureLines(log, loaded));
        } finally {
            TestUnpatch.Own(foreign, id);
            Disarm();
        }
    }

    [Fact]
    public void ACopyWhosePatchRecordReadThrows_StillKeepsHarmonysPatchRunning() {
        List<string> log = [];
        MethodInfo target = typeof(CopierBindTargets).GetMethod(nameof(CopierBindTargets.ThrowingReadApply))!;
        const string id = "test.readthrows.apply";
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony(id);
        IBackendHandle? handle = null;

        try {
            foreign.Patch(target, prefix: new HarmonyMethod(typeof(CopierBindMods).GetMethod(nameof(CopierBindMods.HarmonyPrefix))!));

            ArmWithout(null, log);
            RoutingBackend router = armed!;
            UpdateWrapperHook.ThrowOnPatchInfoReadForTests = true;

            handle = router.ApplyComposed(target, new[] {
                new Injection(typeof(CopierBindMods).GetMethod(nameof(CopierBindMods.ConcordNote))!, new InjectAt.Head(), "test.readthrows.apply.concord", 0),
            });

            Assert.True(handle.IsApplied, "the apply returned a handle that is not applied. Logged: " + string.Join(" | ", log));
            Assert.Equal(RouteState.HarmonyOwned, router.GetRoute(target));

            CopierBindTargets.Ran.Clear();
            CopierBindTargets.ThrowingReadApply(1);
            Assert.Contains("harmony", CopierBindTargets.Ran);
        } finally {
            handle?.Dispose();
            TestUnpatch.Own(foreign, id);
            Disarm();
        }
    }

    [Fact]
    public void ACopyWhosePatchRecordReadIsRestored_HoldsThePatchAgain() {
        List<string> log = [];
        MethodInfo target = typeof(CopierBindTargets).GetMethod(nameof(CopierBindTargets.RestoredRead))!;
        const string id = "test.readthrows.restored";
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony(id);

        try {
            HarmonyBridge bridge = ArmWithout(null, log);
            foreign.Patch(target, prefix: new HarmonyMethod(typeof(CopierBindMods).GetMethod(nameof(CopierBindMods.HarmonyPrefix))!));

            UpdateWrapperHook.ThrowOnPatchInfoReadForTests = true;
            Assert.True(bridge.HasPatch(target), "the public PatchProcessor read did not cover for the hooked read throwing. Logged: " + string.Join(" | ", log));

            UpdateWrapperHook.ThrowOnPatchInfoReadForTests = false;
            Assert.True(bridge.HasPatch(target), "the restored read did not see Harmony's patch. Logged: " + string.Join(" | ", log));
        } finally {
            TestUnpatch.Own(foreign, id);
            Disarm();
        }
    }

    [Fact]
    public void ACopyMissingGetPatchInfo_StillArms_AndBothSidesRun() {
        List<string> log = [];
        MethodInfo target = typeof(CopierBindTargets).GetMethod(nameof(CopierBindTargets.UnboundStateRun))!;
        const string id = "test.statenotbound.run";
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony(id);
        Assembly loaded = typeof(HarmonyLib.Harmony).Assembly;

        try {
            UpdateWrapperHook.DeniedMemberForTests = "HarmonySharedState.GetPatchInfo";
            ArmWithout(null, log);

            Assert.True(
                UpdateWrapperHook.IsHookedOn(loaded),
                "a copy whose GetPatchInfo did not bind was refused whole. Logged: " + string.Join(" | ", log));
            Assert.Equal(1, StateNotBoundLines(log, loaded));
            Assert.DoesNotContain(
                log,
                line => line.StartsWith(CoexistenceLogMarkers.HookUnavailable, StringComparison.Ordinal)
                        && line.Contains(Tag(loaded)));

            IBackendHandle handle = armed!.ApplyComposed(target, new[] {
                new Injection(typeof(CopierBindMods).GetMethod(nameof(CopierBindMods.ConcordNote))!, new InjectAt.Head(), id + ".concord", 0),
            });

            foreign.Patch(target, postfix: new HarmonyMethod(typeof(CopierBindMods).GetMethod(nameof(CopierBindMods.HarmonyPostfix))!));

            CopierBindTargets.Ran.Clear();
            Assert.Equal(2, CopierBindTargets.UnboundStateRun(1));
            Assert.Equal(new[] { "concord", "body", "harmony" }, CopierBindTargets.Ran.ToArray());

            Assert.Throws<ConcordEmitException>(handle.Dispose);
        } finally {
            TestUnpatch.Own(foreign, id);
            Disarm();
        }
    }

    [Fact]
    public void ACopyMissingGetPatchInfo_RefusesARebuild_AndStillHoldsThePatch() {
        List<string> log = [];
        MethodInfo target = typeof(CopierBindTargets).GetMethod(nameof(CopierBindTargets.UnboundStateRead))!;
        const string id = "test.statenotbound.read";
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony(id);
        Assembly loaded = typeof(HarmonyLib.Harmony).Assembly;

        try {
            UpdateWrapperHook.DeniedMemberForTests = "HarmonySharedState.GetPatchInfo";
            HarmonyBridge bridge = ArmWithout(null, log);

            foreign.Patch(target, prefix: new HarmonyMethod(typeof(CopierBindMods).GetMethod(nameof(CopierBindMods.HarmonyPrefix))!));

            Assert.False(
                UpdateWrapperHook.RequestRebuild(target, out string refusals),
                "a copy that cannot read its shared patch state rebuilt a wrapper anyway. Logged: " + string.Join(" | ", log));
            Assert.Contains(Tag(loaded) + " cannot read its shared patch state", refusals);

            Assert.True(
                bridge.HasPatch(target),
                "the public PatchProcessor read did not cover for the unbound shared state read. Logged: " + string.Join(" | ", log));
        } finally {
            TestUnpatch.Own(foreign, id);
            Disarm();
        }
    }

    [Fact]
    public void ACopyMissingGetPatchInfo_HoldsAHarmonyOnlyPatch_SoConcordNeverDetoursOverItsWrapper() {
        List<string> log = [];
        MethodInfo target = typeof(CopierBindTargets).GetMethod(nameof(CopierBindTargets.UnboundStateHarmonyFirst))!;
        const string id = "test.statenotbound.harmonyfirst";
        HarmonyLib.Harmony foreign = new HarmonyLib.Harmony(id);

        try {
            UpdateWrapperHook.DeniedMemberForTests = "HarmonySharedState.GetPatchInfo";
            HarmonyBridge bridge = ArmWithout(null, log);

            foreign.Patch(target, prefix: new HarmonyMethod(typeof(CopierBindMods).GetMethod(nameof(CopierBindMods.HarmonyPrefix))!));

            Assert.True(
                bridge.HasPatch(target),
                "a Harmony patch on an armed copy whose shared state is unreadable stayed invisible. Logged: " + string.Join(" | ", log));
            Assert.Equal(RouteState.HarmonyOwned, armed!.GetRoute(target));

            ConcordEmitException refused = Assert.Throws<ConcordEmitException>(() => armed!.ApplyComposed(target, new[] {
                new Injection(typeof(CopierBindMods).GetMethod(nameof(CopierBindMods.ConcordNote))!, new InjectAt.Head(), id + ".concord", 0),
            }));

            Assert.Contains("would not rebuild its wrapper", refused.Message);
            Assert.Equal(RouteState.HarmonyOwned, armed!.GetRoute(target));

            CopierBindTargets.Ran.Clear();
            Assert.Equal(2, CopierBindTargets.UnboundStateHarmonyFirst(1));
            Assert.Equal(new[] { "harmony", "body" }, CopierBindTargets.Ran.ToArray());
        } finally {
            TestUnpatch.Own(foreign, id);
            Disarm();
        }
    }

    [Fact]
    public void ACopyCarryingGetPatchInfo_ArmsWithNoHookUnavailableLine() {
        List<string> log = [];
        Assembly loaded = typeof(HarmonyLib.Harmony).Assembly;

        try {
            UpdateWrapperHook.DeniedMemberForTests = null;
            ArmWithout(null, log);

            Assert.True(UpdateWrapperHook.IsHookedOn(loaded), "the loaded copy did not arm. Logged: " + string.Join(" | ", log));
            Assert.DoesNotContain(
                log,
                line => line.StartsWith(CoexistenceLogMarkers.HookUnavailable, StringComparison.Ordinal)
                        && line.Contains(Tag(loaded)));
        } finally {
            Disarm();
        }
    }

    internal static bool CarriesConcordsNote(List<HarmonyLib.CodeInstruction> stream) {
        MethodInfo note = typeof(CopierBindMods).GetMethod(nameof(CopierBindMods.ConcordNote))!;

        foreach (HarmonyLib.CodeInstruction instruction in stream) {
            if (instruction.operand is MethodBase called && called.Name == note.Name && called.DeclaringType == note.DeclaringType) {
                return true;
            }

            if (instruction.opcode == OpCodes.Ldstr && (instruction.operand as string) == "concord") {
                return true;
            }
        }

        return false;
    }

    private static List<string> ConstructorMembersOf(Assembly copy) {
        Type copier = copy.GetType("HarmonyLib.MethodCopier")!;
        List<string> members = [];

        foreach (ConstructorInfo candidate in copier.GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)) {
            ParameterInfo[] parameters = candidate.GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType.FullName == "HarmonyLib.MethodCreatorConfig") {
                members.Add("MethodCopier..ctor(MethodCreatorConfig)");
            } else if (parameters.Length == 3 && parameters[0].ParameterType == typeof(MethodBase) && parameters[1].ParameterType == typeof(ILGenerator)) {
                members.Add("MethodCopier..ctor(MethodBase, ILGenerator, LocalBuilder[])");
            }
        }

        members.Sort(StringComparer.Ordinal);
        return members;
    }

    private static List<string> ConstructorMembersIn(IReadOnlyList<string> bound) {
        List<string> members = [];

        foreach (string member in bound) {
            if (member.StartsWith("MethodCopier..ctor", StringComparison.Ordinal)) {
                members.Add(member);
            }
        }

        members.Sort(StringComparer.Ordinal);
        return members;
    }

    private static Assembly Release(string package) {
        foreach (AssemblyMetadataAttribute metadata in typeof(CopierHookBindingTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()) {
            if (metadata.Key == "NuGetPackageRoot" && !string.IsNullOrWhiteSpace(metadata.Value)) {
                string dll = Path.Combine(metadata.Value!, "lib.harmony", package, "lib", "net472", "0Harmony.dll");
                return Assembly.Load(AssemblyImage.WithFreshModuleId(File.ReadAllBytes(dll)));
            }
        }

        throw new InvalidOperationException("the test assembly carries no NuGetPackageRoot metadata, so Harmony " + package + " cannot be found on disk");
    }

    private static HarmonyBridge ArmWithout(string? member, List<string> log) {
        UpdateWrapperHook.ResetForTests();
        MethodCopierHook.DeniedMemberForTests = member;

        HarmonyBridge bridge = new HarmonyBridge(log.Add);
        RoutingBackend router = new RoutingBackend(new MonoModBackend(), log.Add);
        router.ActivateHost(bridge);
        replaced = PatchBackend.TryGetCurrent(out IPatchBackend? installed) ? installed : null;
        armed = router;
        PatchBackend.Current = router;
        Assert.True(router.NotifierInstalled, "the notifier did not install. Logged: " + string.Join(" | ", log));
        return bridge;
    }

    private static int ReadFailureLines(List<string> log, Assembly copy) {
        int lines = 0;

        foreach (string line in log) {
            if (line.StartsWith(CoexistenceLogMarkers.HookDegraded, StringComparison.Ordinal)
                && line.Contains("#" + copy.GetHashCode())
                && line.Contains("failed to read its patch record")) {
                lines++;
            }
        }

        return lines;
    }

    private static string Tag(Assembly copy) {
        return copy.GetName().Version + " #" + copy.GetHashCode();
    }

    private static int StateNotBoundLines(List<string> log, Assembly copy) {
        int lines = 0;

        foreach (string line in log) {
            if (line.StartsWith(CoexistenceLogMarkers.HookDegraded, StringComparison.Ordinal)
                && line.Contains(Tag(copy))
                && line.Contains("carries no HarmonyLib.HarmonySharedState.GetPatchInfo")) {
                lines++;
            }
        }

        return lines;
    }

    private static void Disarm() {
        armed = null;
        UpdateWrapperHook.ThrowOnPatchInfoReadForTests = false;
        UpdateWrapperHook.DeniedMemberForTests = null;
        MethodCopierHook.DeniedMemberForTests = null;
        UpdateWrapperHook.ResetForTests();
        UpdateWrapperHook.DetachObserver();
        MonoModEntryPoint.ResetForTests();

        if (replaced != null) {
            PatchBackend.Current = replaced;
            replaced = null;
        }
    }

    private static Assembly Doctored(string member, string renamedTo) {
        byte[] image = File.ReadAllBytes(typeof(HarmonyLib.Harmony).Assembly.Location);
        byte[] from = Encoding.ASCII.GetBytes("\0" + member + "\0");
        byte[] to = Encoding.ASCII.GetBytes("\0" + renamedTo + "\0");
        int at = IndexOf(image, from);
        Assert.True(at >= 0, member + " was not found in the Harmony image's string heap");
        Array.Copy(to, 0, image, at, to.Length);
        Assert.True(IndexOf(image, from) < 0, member + " appears more than once in the Harmony image's string heap");
        return Assembly.Load(AssemblyImage.WithFreshModuleId(image));
    }

    private static int IndexOf(byte[] image, byte[] needle) {
        for (int i = 0; i <= image.Length - needle.Length; i++) {
            int j = 0;
            while (j < needle.Length && image[i + j] == needle[j]) {
                j++;
            }

            if (j == needle.Length) {
                return i;
            }
        }

        return -1;
    }
}
