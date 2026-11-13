using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime;
using System.Runtime.CompilerServices;
using Concord.Orchestration;
using Xunit;

namespace Concord.Waylith.ForkTests;

public static class CheckedHostTargets {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool CallsForeignPrivate() {
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Type? CallsForeignInternal() {
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool FlipsForeignPrivateStatic() {
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static long WritesForeignPrivateField(Stopwatch watch) {
        return watch.ElapsedTicks;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object? BuildsForeignClosure() {
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Type? NamesForeignInternalType() {
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void CallsNeighbourPrivate() {
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    public static bool OptimizedCallsForeignPrivate() {
        return false;
    }

    public static bool InlineCandidate() {
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    public static bool CallsInlineCandidate() {
        return InlineCandidate();
    }
}

public static class CoreLibFriend {
    private static int reached;

    public static int Reached => Volatile.Read(ref reached);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Reach() {
        Interlocked.Increment(ref reached);
    }
}

public sealed partial class CheckedHostAccessTests : IDisposable {
    private const string NoPipeline = "No transformer pipeline here, so nothing can be staged over a method body.";

    private const string ChecksOff =
        "This host sets WaylithClr.SkipAccessChecks or DOTNET_SkipAccessChecks, so these facts measure nothing. Build it with -p:ForkCheckedHost=true and leave DOTNET_SkipAccessChecks unset.";

    private const long Written = 4242;

    private static readonly MethodInfo ForeignInternal = typeof(PatchDeclarationScanner).GetMethod(
        "ResolveTypeByName",
        BindingFlags.NonPublic | BindingFlags.Static,
        [typeof(string)])!;

    private static readonly MethodInfo CoreLibTarget = typeof(ProfileOptimization).GetMethod(
        nameof(ProfileOptimization.StartProfile),
        [typeof(string)])!;

    private static readonly MethodInfo NeighbourPrivate = typeof(CoreLibFriend).GetMethod(
        "Reach",
        BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly List<MethodBase> written = [];

    public static bool Unavailable => ForkRuntime.Unavailable;

    public void Dispose() {
        foreach (MethodBase target in written) {
            ForkRuntime.Restore(target);
        }
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void ABodyCallingAPrivateMemberOfAnotherAssemblyRunsOnTheFirstCall() {
        RequireSwitchLeftOut();
        Assert.True(ForeignPrivate is { IsPrivate: true }, "PatchDeclarationScanner.IsDeclarationScopedCode is not a private method in this Concord.dll.");
        MethodInfo target = Target(nameof(CheckedHostTargets.CallsForeignPrivate));

        Publish(target, Calling(target.Module, ForeignPrivate, "CONC061"));

        bool staged = !ForkRuntime.IsDefaultBody(target);
        bool returned = false;
        Exception? thrown = Record.Exception(() => returned = CheckedHostTargets.CallsForeignPrivate());
        Assert.True(
            staged && returned && thrown is null,
            $"staged body published: {staged}; first call returned {returned}; threw {Describe(thrown)}");
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void ABodyCallingAnInternalMemberOfAnotherAssemblyWithNoGrantRunsOnTheFirstCall() {
        RequireSwitchLeftOut();
        Assert.True(ForeignInternal is { IsAssembly: true }, "PatchDeclarationScanner.ResolveTypeByName is not an internal method in this Concord.dll.");
        Assert.DoesNotContain(
            typeof(PatchDeclarationScanner).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>(),
            grant => grant.AssemblyName.StartsWith(typeof(CheckedHostTargets).Assembly.GetName().Name!, StringComparison.Ordinal));
        MethodInfo target = Target(nameof(CheckedHostTargets.CallsForeignInternal));

        Publish(target, Calling(target.Module, ForeignInternal, "System.Int32"));

        bool staged = !ForkRuntime.IsDefaultBody(target);
        Type? returned = null;
        Exception? thrown = Record.Exception(() => returned = CheckedHostTargets.CallsForeignInternal());
        Assert.True(
            staged && returned == typeof(int) && thrown is null,
            $"staged body published: {staged}; first call returned {returned?.FullName ?? "null"}; threw {Describe(thrown)}");
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void ACoreLibBodyCallingAPrivateMemberOfAnAssemblyThatGrantsNothingRuns() {
        RequireSwitchLeftOut();
        Assert.True(NeighbourPrivate is { IsPrivate: true }, "CoreLibFriend.Reach is not a private method.");
        Assert.Empty(typeof(CoreLibFriend).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>());
        int before = CoreLibFriend.Reached;

        Publish(CoreLibTarget, Calling(CoreLibTarget.Module, NeighbourPrivate, null));

        Exception? thrown = Record.Exception(() => CoreLibTarget.Invoke(null, [null]));
        Assert.True(
            Unwrap(thrown) is null && CoreLibFriend.Reached == before + 1,
            $"reached {CoreLibFriend.Reached - before} times; threw {Describe(Unwrap(thrown))}");
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void ABodyReadingAndWritingAPrivateStaticFieldOfAnotherAssemblyRuns() {
        RequireSwitchLeftOut();
        FieldInfo? flag = typeof(ExtendedEnumRegistry).GetField("warnedNoStore", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.True(
            flag is { IsPrivate: true, IsInitOnly: false } && flag.FieldType == typeof(bool),
            "ExtendedEnumRegistry.warnedNoStore is not a private static bool field in this Concord.dll.");
        bool before = (bool)flag.GetValue(null)!;
        MethodInfo target = Target(nameof(CheckedHostTargets.FlipsForeignPrivateStatic));

        Publish(target, Encode(
            target.Module,
            new CodeInstruction(OpCodes.Ldsfld, flag),
            new CodeInstruction(OpCodes.Ldc_I4_0),
            new CodeInstruction(OpCodes.Ceq),
            new CodeInstruction(OpCodes.Stsfld, flag),
            new CodeInstruction(OpCodes.Ldsfld, flag),
            new CodeInstruction(OpCodes.Ret)));

        try {
            bool returned = before;
            Exception? thrown = Record.Exception(() => returned = CheckedHostTargets.FlipsForeignPrivateStatic());
            bool after = (bool)flag.GetValue(null)!;
            Assert.True(
                thrown is null && returned != before && after != before,
                $"field was {before}; first call returned {returned}; field is now {after}; threw {Describe(thrown)}");
        } finally {
            flag.SetValue(null, before);
        }
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void ABodyReadingAndWritingAPrivateInstanceFieldOfAnotherAssemblyRuns() {
        RequireSwitchLeftOut();
        FieldInfo? elapsed = typeof(Stopwatch).GetField("_elapsed", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.True(
            elapsed is { IsPrivate: true, IsInitOnly: false } && elapsed.FieldType == typeof(long),
            "Stopwatch._elapsed is not a private long field in this CoreLib.");
        MethodInfo target = Target(nameof(CheckedHostTargets.WritesForeignPrivateField));

        Publish(target, Encode(
            target.Module,
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Ldc_I8, Written),
            new CodeInstruction(OpCodes.Stfld, elapsed),
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Ldfld, elapsed),
            new CodeInstruction(OpCodes.Ret)));

        Stopwatch watch = new Stopwatch();
        long returned = 0;
        Exception? thrown = Record.Exception(() => returned = CheckedHostTargets.WritesForeignPrivateField(watch));
        Assert.True(
            thrown is null && returned == Written && watch.ElapsedTicks == Written,
            $"first call returned {returned}; the watch reads {watch.ElapsedTicks} ticks; threw {Describe(thrown)}");
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void ABodyBuildingAPrivateClosureClassOfAnotherAssemblyRuns() {
        RequireSwitchLeftOut();
        Type? closure = typeof(Patcher).GetNestedTypes(BindingFlags.NonPublic).FirstOrDefault(nested =>
            nested.Name.StartsWith("<>c__DisplayClass", StringComparison.Ordinal)
            && nested.GetConstructor(Type.EmptyTypes) is not null
            && nested.GetFields().Any(field => !field.FieldType.IsValueType)
            && nested.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length > 0);
        Assert.True(
            closure is { IsNestedPrivate: true },
            "Patcher has no private closure class with a reference field and a method in this Concord.dll.");
        FieldInfo field = closure.GetFields().First(candidate => !candidate.FieldType.IsValueType);
        MethodInfo method = closure.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)[0];
        MethodInfo target = Target(nameof(CheckedHostTargets.BuildsForeignClosure));

        Publish(target, Encode(
            target.Module,
            new CodeInstruction(OpCodes.Newobj, closure.GetConstructor(Type.EmptyTypes)!),
            new CodeInstruction(OpCodes.Dup),
            new CodeInstruction(OpCodes.Ldnull),
            new CodeInstruction(OpCodes.Stfld, field),
            new CodeInstruction(OpCodes.Ldftn, method),
            new CodeInstruction(OpCodes.Pop),
            new CodeInstruction(OpCodes.Ret)));

        object? returned = null;
        Exception? thrown = Record.Exception(() => returned = CheckedHostTargets.BuildsForeignClosure());
        Assert.True(
            thrown is null && returned?.GetType() == closure,
            $"first call returned {returned?.GetType().FullName ?? "null"}; threw {Describe(thrown)}");
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void ABodyNamingAnInternalTypeOfAnotherAssemblyRuns() {
        RequireSwitchLeftOut();
        Type? hidden = typeof(Patcher).Assembly.GetType("Concord.Orchestration.PatchHandle");
        Assert.True(hidden is { IsNotPublic: true }, "Concord.Orchestration.PatchHandle is not an internal type in this Concord.dll.");
        MethodInfo target = Target(nameof(CheckedHostTargets.NamesForeignInternalType));

        Publish(target, Encode(
            target.Module,
            new CodeInstruction(OpCodes.Ldtoken, hidden),
            new CodeInstruction(OpCodes.Call, typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))!),
            new CodeInstruction(OpCodes.Dup),
            new CodeInstruction(OpCodes.Isinst, hidden),
            new CodeInstruction(OpCodes.Pop),
            new CodeInstruction(OpCodes.Ret)));

        Type? returned = null;
        Exception? thrown = Record.Exception(() => returned = CheckedHostTargets.NamesForeignInternalType());
        Assert.True(
            thrown is null && returned == hidden,
            $"first call returned {returned?.FullName ?? "null"}; threw {Describe(thrown)}");
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void ABodyCallingAPrivateMemberOfAnotherClassInItsOwnAssemblyRuns() {
        RequireSwitchLeftOut();
        int before = CoreLibFriend.Reached;
        MethodInfo target = Target(nameof(CheckedHostTargets.CallsNeighbourPrivate));

        Publish(target, Calling(target.Module, NeighbourPrivate, null));

        Exception? thrown = Record.Exception(CheckedHostTargets.CallsNeighbourPrivate);
        Assert.True(
            thrown is null && CoreLibFriend.Reached == before + 1,
            $"reached {CoreLibFriend.Reached - before} times; threw {Describe(thrown)}");
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void AnAggressivelyOptimizedBodyCallingAPrivateMemberOfAnotherAssemblyRuns() {
        RequireSwitchLeftOut();
        MethodInfo target = Target(nameof(CheckedHostTargets.OptimizedCallsForeignPrivate));

        Publish(target, Calling(target.Module, ForeignPrivate, "CONC061"));

        bool returned = false;
        Exception? thrown = Record.Exception(() => returned = CheckedHostTargets.OptimizedCallsForeignPrivate());
        Assert.True(thrown is null && returned, $"first call returned {returned}; threw {Describe(thrown)}");
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void AnInlinableBodyCallingAPrivateMemberOfAnotherAssemblyRunsInsideAnOptimizedCallerCompiledAfterTheStage() {
        RequireSwitchLeftOut();
        MethodInfo target = Target(nameof(CheckedHostTargets.InlineCandidate));

        Publish(target, Calling(target.Module, ForeignPrivate, "CONC061"));

        bool returned = false;
        Exception? thrown = Record.Exception(() => returned = CheckedHostTargets.CallsInlineCandidate());
        Assert.True(thrown is null && returned, $"the caller's first call returned {returned}; threw {Describe(thrown)}");
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void RuntimeCapabilityReportsAccessChecksOffOnAHostThatLeavesTheSwitchOut() {
        RequireSwitchLeftOut();

        Assert.True(RuntimeCapability.IgnoresAccessChecks);
    }

    private static void RequireSwitchLeftOut() {
        Assert.True(
            AppContext.GetData(RuntimeCapability.AccessCheckKey) is null
                && Environment.GetEnvironmentVariable(RuntimeCapability.AccessCheckSwitch) is null,
            ChecksOff);
    }

    private static MethodInfo Target(string name) {
        return typeof(CheckedHostTargets).GetMethod(name)!;
    }

    private static byte[] Calling(Module module, MethodInfo callee, string? argument) {
        List<CodeInstruction> code = [];
        if (argument is not null) {
            code.Add(new CodeInstruction(OpCodes.Ldstr, argument));
        }

        code.Add(new CodeInstruction(OpCodes.Call, callee));
        code.Add(new CodeInstruction(OpCodes.Ret));
        return Encode(module, [.. code]);
    }

    private static byte[] Encode(Module module, params CodeInstruction[] code) {
        ComposedBody body = new ComposedBody([.. code.Select(static instruction => new ComposedInstruction(instruction, null, null))], [], [], false);
        return BodyEncoder.Encode(body, new UngatedTokens(module), new ComputedStack()).Bytes;
    }

    private void Publish(MethodBase target, byte[] body) {
        ForkRuntime.Write(target, body, []);
        written.Add(target);
    }

    private sealed class UngatedTokens : ITokenSource {
        private readonly Module module;

        private readonly ForkMinter minter = new ForkMinter();

        public UngatedTokens(Module module) {
            this.module = module;
        }

        public int TokenFor(MethodBase method) => minter.Token(module, method);

        public int TokenFor(FieldInfo field) => minter.Token(module, field);

        public int TokenFor(Type type) => minter.Token(module, type);

        public int TokenFor(string literal) => minter.Token(module, literal);

        public int TokenForLocals(byte[] signature) => minter.LocalsToken(module, signature);
    }
}
