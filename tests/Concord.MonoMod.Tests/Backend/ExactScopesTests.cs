using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Concord.Backend;
using MonoMod.Utils;
using Xunit;
using Xunit.Abstractions;

namespace Concord.MonoMod.Tests;

public static class ExactScopesLatch {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Hold() {
        return 3;
    }
}

public sealed class TwinHashCollisionFactAttribute : FactAttribute {
    internal const int Cap = 64;

    internal static readonly (MethodInfo First, MethodInfo Second, int Load)? Found = Probe();

    public TwinHashCollisionFactAttribute() {
        if (Found is null) {
            Skip = $"mono's identity hash did not collide in {Cap} loads of one twin image, so no two live copies share a scope stamp";
        }
    }

    private static (MethodInfo First, MethodInfo Second, int Load)? Probe() {
        byte[] image = ResolveCacheTests.Twins.Image();
        Dictionary<int, MethodInfo> byHash = new Dictionary<int, MethodInfo>();
        for (int load = 1; load <= Cap; load++) {
            MethodInfo probe = ResolveCacheTests.Twins.LoadOne(image);
            int hash = probe.DeclaringType!.Assembly.GetHashCode();
            if (byHash.TryGetValue(hash, out MethodInfo? earlier)) {
                return (earlier, probe, load);
            }

            byHash[hash] = probe;
            GC.Collect();
        }

        return null;
    }
}

public sealed class ExactScopesTests {
    private const int Constant = 7;

    private readonly ITestOutputHelper output;

    public ExactScopesTests(ITestOutputHelper output) {
        this.output = output;
    }

    [Fact]
    public void ThePass_BindsThroughTheImporterAndTheCacheItNames() {
        Assert.Equal("ReflectionImporter", ExactScopes.ImporterProperty);
        Assert.Equal("CachedAsms", ExactScopes.StampedAssembliesField);
        Assert.Equal("MonoMod.Utils.ReflectionHelper", ExactScopes.HelperType);
        Assert.Equal("AssemblyCache", ExactScopes.AssemblyCacheField);

        using DynamicMethodDefinition definition = new DynamicMethodDefinition("ExactScopesProbe", typeof(void), Type.EmptyTypes);
        definition.Definition.Module.ImportReference(typeof(ResolveCacheHead).GetMethod(nameof(ResolveCacheHead.Bump)));

        int bound = ExactScopes.Bind(definition.Definition.Module);
        output.WriteLine($"bound {bound}");
        Assert.True(bound > 0);
    }

    [TwinHashCollisionFact]
    public void ABodyOnTheSecondCopyCallingAHelperOfTheFirst_EachCallRunsInTheCopyItWasWrittenAgainst() {
        (MethodInfo First, MethodInfo Second, int Load) twins = Collided();

        AssertEachCallRunsInItsOwnCopy(twins.Second, twins.First);
    }

    [TwinHashCollisionFact]
    public void ABodyOnTheFirstCopyCallingAHelperOfTheSecond_EachCallRunsInTheCopyItWasWrittenAgainst() {
        (MethodInfo First, MethodInfo Second, int Load) twins = Collided();

        AssertEachCallRunsInItsOwnCopy(twins.First, twins.Second);
    }

    [TwinHashCollisionFact]
    public void BindOnAModuleWhereTwoCopiesOwnOneReference_ReportsTheSharedReference() {
        (MethodInfo First, MethodInfo Second, int Load) twins = Collided();
        Type first = twins.First.DeclaringType!;
        Type second = twins.Second.DeclaringType!;

        using DynamicMethodDefinition shared = new DynamicMethodDefinition("ExactScopesShared", typeof(void), Type.EmptyTypes);
        Assert.Same(shared.Definition.Module.ImportReference(first).Scope, shared.Definition.Module.ImportReference(second).Scope);

        ExactScopes.Bind(shared.Definition.Module, out int stillShared);
        output.WriteLine($"shared references after importing both copies unseeded: {stillShared}");
        Assert.Equal(1, stillShared);

        using DynamicMethodDefinition seeded = new DynamicMethodDefinition("ExactScopesSeeded", typeof(void), Type.EmptyTypes);
        Assert.True(ExactScopes.Seed(seeded.Definition.Module) >= 2);
        Assert.NotSame(seeded.Definition.Module.ImportReference(first).Scope, seeded.Definition.Module.ImportReference(second).Scope);

        ExactScopes.Bind(seeded.Definition.Module, out int afterSeed);
        Assert.Equal(0, afterSeed);
    }

    [Fact]
    public void TwoCopiesLoadedBeforeTheFirstCompose_EachBodyEnumeratesItsOwnCopy() {
        ResolveCacheTests.Twins twins = ResolveCacheTests.Twins.Load();
        MethodInfo bump = typeof(ResolveCacheHead).GetMethod(nameof(ResolveCacheHead.Bump))!;
        IBackendHandle? first = null;
        IBackendHandle? second = null;

        try {
            first = ResolveCacheTests.Compose(twins.First, bump);
            AssertEnumeratesItsOwnCopy(twins.First);

            second = ResolveCacheTests.Compose(twins.Second, bump);
            AssertEnumeratesItsOwnCopy(twins.Second);
            AssertEnumeratesItsOwnCopy(twins.First);
        } finally {
            second?.Dispose();
            first?.Dispose();
        }
    }

    [Fact]
    public void TwoCopiesComposedOnTwoThreadsAtOnce_EachBodyEnumeratesItsOwnCopy() {
        const int rounds = 6;
        MethodInfo bump = typeof(ResolveCacheHead).GetMethod(nameof(ResolveCacheHead.Bump))!;

        for (int round = 0; round < rounds; round++) {
            ResolveCacheTests.Twins twins = ResolveCacheTests.Twins.Load();
            MethodInfo[] copies = [twins.First, twins.Second];
            IBackendHandle?[] handles = new IBackendHandle?[copies.Length];
            Assembly?[] seen = new Assembly?[copies.Length];
            Exception?[] failures = new Exception?[copies.Length];

            using (Barrier start = new Barrier(copies.Length)) {
                Thread[] threads = new Thread[copies.Length];
                for (int index = 0; index < copies.Length; index++) {
                    int slot = index;
                    threads[slot] = new Thread(() => {
                        try {
                            start.SignalAndWait();
                            handles[slot] = ResolveCacheTests.Compose(copies[slot], bump);
                            ResolveCacheHead.Element = null;
                            Assert.Equal(Constant, copies[slot].Invoke(null, null));
                            seen[slot] = ResolveCacheHead.Element;
                        } catch (Exception failure) {
                            failures[slot] = failure;
                        }
                    });
                    threads[slot].Start();
                }

                foreach (Thread thread in threads) {
                    thread.Join();
                }
            }

            try {
                for (int index = 0; index < copies.Length; index++) {
                    Assert.True(failures[index] is null, $"round {round}, copy {index}: {failures[index]}");
                    Assert.Same(copies[index].DeclaringType!.Assembly, seen[index]);
                }
            } finally {
                foreach (IBackendHandle? handle in handles) {
                    handle?.Dispose();
                }
            }
        }
    }

    [Fact]
    public void AThirdCopyLoadedAfterTwoComposed_ResolvesIntoItself() {
        byte[] image = ResolveCacheTests.Twins.Image();
        MethodInfo bump = typeof(ResolveCacheHead).GetMethod(nameof(ResolveCacheHead.Bump))!;
        MethodInfo first = ResolveCacheTests.Twins.LoadOne(image);
        MethodInfo second = ResolveCacheTests.Twins.LoadOne(image);
        List<IBackendHandle> composed = [];

        try {
            composed.Add(ResolveCacheTests.Compose(first, bump));
            AssertEnumeratesItsOwnCopy(first);
            composed.Add(ResolveCacheTests.Compose(second, bump));
            AssertEnumeratesItsOwnCopy(second);

            MethodInfo third = ResolveCacheTests.Twins.LoadOne(image);
            composed.Add(ResolveCacheTests.Compose(third, bump));
            AssertEnumeratesItsOwnCopy(third);
        } finally {
            foreach (IBackendHandle handle in composed) {
                handle.Dispose();
            }
        }
    }

    internal static (MethodInfo First, MethodInfo Second, int Load) Collided() {
        (MethodInfo First, MethodInfo Second, int Load) twins = TwinHashCollisionFactAttribute.Found
                                                                ?? throw new InvalidOperationException("the collision probe found no pair in this app domain");
        Assert.Equal(twins.First.DeclaringType!.Assembly.GetHashCode(), twins.Second.DeclaringType!.Assembly.GetHashCode());
        return twins;
    }

    private static void AssertEachCallRunsInItsOwnCopy(MethodInfo target, MethodInfo helperCopy) {
        MethodInfo helper = helperCopy.DeclaringType!.GetMethod("Mark")!;
        IBackendHandle? composed = null;

        try {
            composed = ResolveCacheTests.Compose(target, helper);
            ResolveCacheHead.Trail.Clear();
            ResolveCacheHead.Element = null;

            Assert.Equal(Constant, target.Invoke(null, null));

            Assert.Equal(new[] { helperCopy.DeclaringType.Assembly, target.DeclaringType!.Assembly }, ResolveCacheHead.Trail);
            Assert.Same(target.DeclaringType.Assembly, ResolveCacheHead.Element);
        } finally {
            composed?.Dispose();
        }
    }

    private static void AssertEnumeratesItsOwnCopy(MethodInfo probe) {
        ResolveCacheHead.Element = null;
        ResolveCacheHead.Reached = null;

        Assert.Equal(Constant, probe.Invoke(null, null));

        Assert.Same(probe.DeclaringType!.Assembly, ResolveCacheHead.Reached);
        Assert.Same(probe.DeclaringType.Assembly, ResolveCacheHead.Element);
    }

    [Fact]
    public void AScopeKeyPlantedOnAnotherCopy_TheComposedBodyStillCallsIntoItsOwnCopy() {
        ResolveCacheTests.Twins twins = ResolveCacheTests.Twins.LoadDistinct();
        MethodInfo second = twins.Second;
        Assembly own = second.DeclaringType!.Assembly;
        Assembly other = twins.First.DeclaringType!.Assembly;

        MethodInfo bump = typeof(ResolveCacheHead).GetMethod(nameof(ResolveCacheHead.Bump))!;
        IBackendHandle? latch = null;
        IBackendHandle? composed = null;

        try {
            latch = ResolveCacheTests.Compose(typeof(ExactScopesLatch).GetMethod(nameof(ExactScopesLatch.Hold))!, bump);
            ResolveCacheTests.PlantScope(own, other);
            Assert.Same(other, ResolveCacheTests.BoundScope(own));

            composed = ResolveCacheTests.Compose(second, bump);
            Assert.Same(own, ResolveCacheTests.BoundScope(own));

            ResolveCacheHead.Reached = null;
            Assert.Equal(Constant, second.Invoke(null, null));
            Assert.Same(own, ResolveCacheHead.Reached);
        } finally {
            composed?.Dispose();
            latch?.Dispose();
        }
    }

    [TwinHashCollisionFact]
    public void TwoLiveCopiesSharingOneHash_EachComposedBodyCallsIntoItsOwnCopy() {
        (MethodInfo First, MethodInfo Second, int Load) twins = Collided();
        output.WriteLine($"collided at load {twins.Load} of {TwinHashCollisionFactAttribute.Cap}");

        MethodInfo bump = typeof(ResolveCacheHead).GetMethod(nameof(ResolveCacheHead.Bump))!;
        IBackendHandle? first = null;
        IBackendHandle? second = null;

        try {
            first = ResolveCacheTests.Compose(twins.First, bump);
            ResolveCacheHead.Reached = null;
            Assert.Equal(Constant, twins.First.Invoke(null, null));
            Assert.Same(twins.First.DeclaringType.Assembly, ResolveCacheHead.Reached);

            second = ResolveCacheTests.Compose(twins.Second, bump);
            ResolveCacheHead.Reached = null;
            Assert.Equal(Constant, twins.Second.Invoke(null, null));
            Assert.Same(twins.Second.DeclaringType.Assembly, ResolveCacheHead.Reached);
        } finally {
            second?.Dispose();
            first?.Dispose();
        }
    }
}
