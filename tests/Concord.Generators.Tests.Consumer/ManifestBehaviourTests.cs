using System.Reflection;
using Concord.Backend;
using Concord.Emit;
using Concord.Orchestration;
using Concord.Tests.Shared;
using Xunit;

namespace Concord.Generators.Tests.Consumer;

public sealed class ManifestBehaviourTests : IDisposable {
    private static readonly Assembly Consumer = typeof(SecretCounter).Assembly;

    private readonly RecordingDetourBackend recorder = new RecordingDetourBackend();
    private readonly List<string> logs = [];

    public ManifestBehaviourTests() {
        PatchBackend.Current = recorder;
        Patcher.UseLog(logs.Add);
    }

    public void Dispose() {
        Patcher.UseLog(null);
    }

    [Fact]
    public void Apply_ShadowDeclaration_ReadsThePrivateStateThroughItsGeneratedMembers() {
        SecretCounter counter = new SecretCounter();
        counter.Tick();

        Run(Method<SecretCounter>(nameof(SecretCounter.Tick)), typeof(SecretCounterPatch), counter);

        Assert.Equal(2, SecretCounterPatch.Observed);
        Assert.Equal(2, counter.CurrentHits());
    }

    [Fact]
    public void Apply_CaptureOnTheFirstMatchedCall_GetsItsLabel() {
        SurfaceCapturePatch.CapturedLabel = string.Empty;

        object? result = Run(Method(typeof(SurfaceRegister), nameof(SurfaceRegister.Ring)), typeof(SurfaceCapturePatch), 5);

        Assert.Equal(6, result);
        Assert.Equal("outside", SurfaceCapturePatch.CapturedLabel);
    }

    [Fact]
    public void Apply_InjectNew_RunsOnceForTheOneConstruction() {
        SurfaceNewPatch.Constructions = 0;

        Run(Method(typeof(SurfaceRegister), nameof(SurfaceRegister.Ring)), typeof(SurfaceNewPatch), 5);

        Assert.Equal(1, SurfaceNewPatch.Constructions);
    }

    [Fact]
    public void Apply_Slice_LandsTheInjectionOnlyOnTheFencedCall() {
        SurfaceSlicePatch.FencedRecords = 0;

        Run(Method(typeof(SurfaceRegister), nameof(SurfaceRegister.Ring)), typeof(SurfaceSlicePatch), 5);

        Assert.Equal(1, SurfaceSlicePatch.FencedRecords);
    }

    [Fact]
    public void Apply_HeadAndTailOfOneDeclaration_ShareTheStateSlot() {
        SurfaceStatePatch.Observed = 0;

        Run(Method(typeof(SurfaceRegister), nameof(SurfaceRegister.Ring)), typeof(SurfaceStatePatch), 5);

        Assert.Equal(105L, SurfaceStatePatch.Observed);
    }

    [Fact]
    public void Apply_PatchBeforeAndPatchAfter_RunTheFirstPatchBeforeTheSecond() {
        MethodBase ordered = Method<DispatchQueue>(nameof(DispatchQueue.Ordered));
        Injection first;
        Injection second;
        using (Patcher.Apply(Consumer)) {
            first = Single(ordered, typeof(FirstQueuePatch));
            second = Single(ordered, typeof(SecondQueuePatch));
        }

        Assert.Equal([typeof(SecondQueuePatch).FullName!, "Optional.Mod.Before"], first.BeforeOwners);
        Assert.Equal([typeof(FirstQueuePatch).FullName!, "Optional.Mod.After"], second.AfterOwners);
        foreach (Injection[] composition in new[] {
                     InjectionOrderer.OrderForComposition([(0, first), (1, second)]),
                     InjectionOrderer.OrderForComposition([(0, second), (1, first)]),
                 }) {
            DispatchQueue.Runs.Clear();
            WrapperComposer.Compose(ordered, composition).Wrapper.Invoke(null, [new DispatchQueue()]);
            Assert.Equal(["first", "second", "target"], DispatchQueue.Runs);
        }
    }

    [Fact]
    public void Apply_PatchDebug_MarksOnlyItsOwnInjectionForIlDump() {
        using (Patcher.Apply(Consumer)) {
            Assert.True(Single(Method<DispatchQueue>(nameof(DispatchQueue.Debugged)), typeof(DebuggedQueuePatch)).Debug);
            Assert.False(Single(Method<DispatchQueue>(nameof(DispatchQueue.Ordered)), typeof(FirstQueuePatch)).Debug);
        }
    }

    [Fact]
    public void Apply_PatchRegistry_NamesTheSameDeclarationsTheManifestApplies() {
        Assert.True(PatchDeclarationScanner.TryGetRegistryDeclarations(Consumer, out IReadOnlyList<Type> declarations));
        Assert.Equal(ConsumerRegistry.All, declarations);

        using (Patcher.Apply(Consumer)) {
            HashSet<string> owners = recorder.ComposedDetours
                .SelectMany(entry => entry.Added)
                .Select(injection => injection.Owner)
                .Where(owner => owner.StartsWith(typeof(SecretCounter).Namespace + ".", StringComparison.Ordinal))
                .ToHashSet();
            Assert.Equal(
                declarations.Where(type => type != typeof(ConsumerWeathers)).Select(type => type.FullName!).Order(StringComparer.Ordinal),
                owners.Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void Apply_InjectField_WritesThePrivateTargetField() {
        LedgerBook book = new LedgerBook();

        object? result = Run(Method<LedgerBook>(nameof(LedgerBook.Deposit)), typeof(DepositFieldPatch), book, 5);

        Assert.Equal(15, result);
        Assert.Equal(15, book.BalanceSnapshot);
    }

    [Fact]
    public void Apply_InjectProperty_ReadsThePrivateTargetProperty() {
        object? result = Run(Method<LedgerBook>(nameof(LedgerBook.ReadFee)), typeof(FeePropertyPatch), new LedgerBook());

        Assert.Equal(2, result);
    }

    [Fact]
    public void Apply_InjectMethod_CallsThePrivateTargetMethod() {
        LedgerBook book = new LedgerBook();

        object? result = Run(Method<LedgerBook>(nameof(LedgerBook.Settle)), typeof(SettleMethodPatch), book, 4);

        Assert.Equal(16, result);
        Assert.Equal(14, book.BalanceSnapshot);
    }

    [Fact]
    public void Apply_InjectInstance_GivesTheTargetInstance() {
        LedgerBook book = new LedgerBook();

        object? result = Run(Method<LedgerBook>(nameof(LedgerBook.Owner)), typeof(OwnerInstancePatch), book);

        Assert.Same(book, result);
    }

    [Fact]
    public void Apply_AttachedField_KeepsItsOwnValuePerTargetInstance() {
        MethodInfo wrapper = Wrapper(Method<LedgerBook>(nameof(LedgerBook.Visit)), typeof(VisitAttachedPatch));
        LedgerBook first = new LedgerBook();
        LedgerBook second = new LedgerBook();

        wrapper.Invoke(null, [first]);

        Assert.Equal(2, wrapper.Invoke(null, [first]));
        Assert.Equal(1, wrapper.Invoke(null, [second]));
    }

    [Fact]
    public void Apply_LocalByIndex_ReadsThatSlotOfTheTarget() {
        TallyBoard.Recorded = 0;

        Run(Method<TallyBoard>(nameof(TallyBoard.TwoLocals)), typeof(LocalIndexPatch), new TallyBoard(), 5);

        Assert.Equal(15, TallyBoard.Recorded);
    }

    [Fact]
    public void Apply_LocalByOrdinal_ReadsTheFirstLocalOfItsType() {
        TallyBoard.Recorded = 0;

        Run(Method<TallyBoard>(nameof(TallyBoard.TwoLocals)), typeof(LocalOrdinalPatch), new TallyBoard(), 5);

        Assert.Equal(10, TallyBoard.Recorded);
    }

    [Fact]
    public void Apply_Bound_GetsTheValueBoundToItRatherThanTheArgument() {
        TallyBoard.Recorded = 0;
        MethodBase plain = Method<TallyBoard>(nameof(TallyBoard.Plain));
        Injection injection = Applied(plain, typeof(BoundPatch));
        injection.BoundArguments = new Dictionary<string, object?> { ["sectionId"] = 77 };

        object? result = WrapperComposer.Compose(plain, [injection]).Wrapper.Invoke(null, [new TallyBoard(), 3]);

        Assert.Equal(6, result);
        Assert.Equal(77, TallyBoard.Recorded);
    }

    [Fact]
    public void Apply_EnumMember_PersistsTheMemberUnderItsOwnId() {
        string hailId = typeof(ConsumerWeathers).FullName + "." + nameof(ConsumerWeathers.Hail);
        using (Patcher.Apply(Consumer)) {
            ExtendedEnumRegistry.Reload(new Dictionary<string, long> { [ConsumerWeathers.FrostId] = 40, [hailId] = 41 });

            Assert.True(ExtendedEnumRegistry.Extends(typeof(Weather)));
            Assert.Equal((Weather)40, ConsumerWeathers.Frost);
            Assert.Equal((Weather)41, ConsumerWeathers.Hail);
        }
    }

    private static MethodBase Method<T>(string name) {
        return Method(typeof(T), name);
    }

    private static MethodBase Method(Type type, string name) {
        return type.GetMethod(name)!;
    }

    private object? Run(MethodBase target, Type owner, params object?[] arguments) {
        return Wrapper(target, owner).Invoke(null, arguments);
    }

    private MethodInfo Wrapper(MethodBase target, Type owner) {
        List<Injection> injections;
        using (Patcher.Apply(Consumer)) {
            injections = InjectionsOn(target).Where(injection => injection.Owner == owner.FullName).ToList();
        }

        Assert.NotEmpty(injections);
        Assert.Empty(logs);
        return WrapperComposer.Compose(target, injections).Wrapper;
    }

    private Injection Applied(MethodBase target, Type owner) {
        using (Patcher.Apply(Consumer)) {
            return Single(target, owner);
        }
    }

    private Injection Single(MethodBase target, Type owner) {
        return Assert.Single(InjectionsOn(target), injection => injection.Owner == owner.FullName);
    }

    private List<Injection> InjectionsOn(MethodBase target) {
        return recorder.ComposedDetours.Where(entry => entry.Target.Equals(target)).SelectMany(entry => entry.Added).ToList();
    }
}
