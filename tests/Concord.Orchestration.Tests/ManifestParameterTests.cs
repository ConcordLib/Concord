using System.Reflection;
using Concord.Backend;
using Concord.Emit;
using Concord.Orchestration.Tests.ManifestAssembly;
using Xunit;

namespace Concord.Orchestration.Tests;

[Collection(SharedAssemblyApplyCollection.Name)]
public sealed class ManifestParameterTests : IDisposable {
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Assembly Manifested = typeof(ParameterTarget).Assembly;

    private readonly RecordingDetourBackend backend = new RecordingDetourBackend();

    public ManifestParameterTests() {
        PatchBackend.Current = backend;
        Patcher.ForgetAttachedPropertyRegistries();
    }

    public void Dispose() {
        Patcher.ForgetAttachedPropertyRegistries();
        PatchBackend.Reset();
    }

    [Fact]
    public void Apply_ManifestLocalByIndex_ReadsThatSlotOfTheTarget() {
        AssertNoConcordAttributes(typeof(LocalParameterPatch));

        Compose(nameof(ParameterTarget.TwoLocals), typeof(LocalParameterPatch), "ReadByIndex").Invoke(null, [new ParameterTarget(), 5]);

        Assert.Equal(15, ParameterRecorder.Indexed);
    }

    [Fact]
    public void Apply_ManifestLocalByName_ReadsTheLocalWithThatSourceName() {
        AssertNoConcordAttributes(typeof(LocalParameterPatch));

        Compose(nameof(ParameterTarget.TwoLocals), typeof(LocalParameterPatch), "ReadByName").Invoke(null, [new ParameterTarget(), 5]);

        Assert.Equal(10, ParameterRecorder.Named);
    }

    [Fact]
    public void Apply_ManifestCapture_GetsTheArgumentsOfTheMatchedCall() {
        AssertNoConcordAttributes(typeof(CaptureParameterPatch));

        Compose(nameof(ParameterTarget.Priced), typeof(CaptureParameterPatch), "AfterApply").Invoke(null, [new ParameterTarget(), 4]);

        Assert.Equal(5, ParameterRecorder.SeenBase);
        Assert.Equal(2f, ParameterRecorder.SeenRate);
    }

    [Fact]
    public void Apply_ManifestBound_GetsTheValueBoundToIt() {
        AssertNoConcordAttributes(typeof(BoundParameterPatch));
        Injection injection = Applied(nameof(ParameterTarget.Plain), typeof(BoundParameterPatch), "OnPlain");
        injection.BoundArguments = new Dictionary<string, object?> { ["sectionId"] = 77 };

        int result = (int)WrapperComposer.Compose(TargetMethod(nameof(ParameterTarget.Plain)), [injection]).Wrapper.Invoke(null, [new ParameterTarget(), 3])!;

        Assert.Equal(6, result);
        Assert.Equal(77, ParameterRecorder.Section);
    }

    [Fact]
    public void Apply_ManifestSlice_LandsTheInjectionOnlyInsideTheRange() {
        AssertNoConcordAttributes(typeof(SliceParameterPatch));
        ParameterRecorder.SliceHits = 0;

        Compose(nameof(ParameterTarget.Checkout), typeof(SliceParameterPatch), "BeforeTotal").Invoke(null, [new ParameterTarget(), 5]);

        Assert.Equal(1, ParameterRecorder.SliceHits);
        Assert.Equal(6, ParameterRecorder.SliceSeed);
    }

    [Fact]
    public void Apply_ManifestSliceOnAHeadInjection_IsRejectedWithConc134() {
        AssertNoConcordAttributes(typeof(HeadSliceParameterPatch));
        Injection injection = Applied(nameof(ParameterTarget.Plain), typeof(HeadSliceParameterPatch), "SlicedAtHead");

        ConcordEmitException failure = Assert.Throws<ConcordEmitException>(
            () => WrapperComposer.Compose(TargetMethod(nameof(ParameterTarget.Plain)), [injection]));

        Assert.Equal("CONC134", failure.Code);
    }

    [Fact]
    public void Patch_InjectionMethodFromAFreshManifestAssembly_GetsItsParameterAttributes() {
        Assembly copy = FreshCopy();
        Type target = copy.GetType(typeof(ParameterTarget).FullName!)!;
        MethodBase twoLocals = target.GetMethod(nameof(ParameterTarget.TwoLocals))!;
        MethodBase readByIndex = copy.GetType(typeof(LocalParameterPatch).FullName!)!.GetMethod("ReadByIndex", Declared)!;

        using (Patcher.Patch(twoLocals, readByIndex, At.Return)) {
            Injection injection = Assert.Single(backend.InjectionsOn(twoLocals));
            WrapperComposer.Compose(twoLocals, [injection]).Wrapper.Invoke(null, [Activator.CreateInstance(target), 5]);
        }

        Assert.Equal(15, Recorded<int>(copy, nameof(ParameterRecorder.Indexed)));
        Assert.Equal(1, Recorded<int>(copy, typeof(ParameterManifestRuns), nameof(ParameterManifestRuns.Count)));
    }

    [Fact]
    public void Compose_TwoThreadsFirstLookupOnAFreshManifestAssembly_BothGetTheAttributesAndDescribeRunsOnce() {
        Assembly copy = FreshCopy();
        Type target = copy.GetType(typeof(ParameterTarget).FullName!)!;
        Type rules = copy.GetType(typeof(ParameterRules).FullName!)!;
        MethodBase twoLocals = target.GetMethod(nameof(ParameterTarget.TwoLocals))!;
        MethodBase priced = target.GetMethod(nameof(ParameterTarget.Priced))!;
        Injection local = new Injection(
            copy.GetType(typeof(LocalParameterPatch).FullName!)!.GetMethod("ReadByIndex", Declared)!, new InjectAt.Return(0), "local", 0);
        Injection capture = new Injection(
            copy.GetType(typeof(CaptureParameterPatch).FullName!)!.GetMethod("AfterApply", Declared)!,
            new InjectAt.Invoke(rules, nameof(ParameterRules.Apply), At.Tail),
            "capture",
            0);

        using Barrier start = new Barrier(2);
        MethodInfo?[] wrappers = new MethodInfo?[2];
        Exception?[] failures = new Exception?[2];
        Thread[] threads = [
            new Thread(() => ComposeAfter(start, twoLocals, local, wrappers, failures, 0)),
            new Thread(() => ComposeAfter(start, priced, capture, wrappers, failures, 1)),
        ];
        foreach (Thread thread in threads) {
            thread.Start();
        }

        foreach (Thread thread in threads) {
            thread.Join();
        }

        Assert.Equal([null, null], failures);
        wrappers[0]!.Invoke(null, [Activator.CreateInstance(target), 5]);
        wrappers[1]!.Invoke(null, [Activator.CreateInstance(target), 4]);

        Assert.Equal(15, Recorded<int>(copy, nameof(ParameterRecorder.Indexed)));
        Assert.Equal(5, Recorded<int>(copy, nameof(ParameterRecorder.SeenBase)));
        Assert.Equal(2f, Recorded<float>(copy, nameof(ParameterRecorder.SeenRate)));
        Assert.Equal(1, Recorded<int>(copy, typeof(ParameterManifestRuns), nameof(ParameterManifestRuns.Count)));
    }

    private static void ComposeAfter(Barrier start, MethodBase target, Injection injection, MethodInfo?[] wrappers, Exception?[] failures, int slot) {
        try {
            start.SignalAndWait();
            wrappers[slot] = WrapperComposer.Compose(target, [injection]).Wrapper;
        } catch (Exception ex) {
            failures[slot] = ex;
        }
    }

    private MethodInfo Compose(string targetName, Type patch, string injectionName) {
        return WrapperComposer.Compose(TargetMethod(targetName), [Applied(targetName, patch, injectionName)]).Wrapper;
    }

    private Injection Applied(string targetName, Type patch, string injectionName) {
        MethodInfo injectionMethod = patch.GetMethod(injectionName, Declared)!;
        using (Patcher.Apply(Manifested)) {
            return Assert.Single(backend.InjectionsOn(TargetMethod(targetName)), injection => injection.InjectionMethod.Equals(injectionMethod));
        }
    }

    private static MethodBase TargetMethod(string name) {
        return typeof(ParameterTarget).GetMethod(name)!;
    }

    private static Assembly FreshCopy() {
        return Assembly.Load(File.ReadAllBytes(Manifested.Location));
    }

    private static T Recorded<T>(Assembly copy, string field) {
        return Recorded<T>(copy, typeof(ParameterRecorder), field);
    }

    private static T Recorded<T>(Assembly copy, Type holder, string field) {
        return (T)copy.GetType(holder.FullName!)!.GetField(field)!.GetValue(null)!;
    }

    private static void AssertNoConcordAttributes(Type patch) {
        Assert.DoesNotContain(patch.GetCustomAttributesData(), IsConcordAttribute);
        foreach (MethodInfo method in patch.GetMethods(Declared)) {
            Assert.DoesNotContain(method.GetCustomAttributesData(), IsConcordAttribute);
            foreach (ParameterInfo parameter in method.GetParameters()) {
                Assert.DoesNotContain(parameter.GetCustomAttributesData(), IsConcordAttribute);
            }
        }
    }

    private static bool IsConcordAttribute(CustomAttributeData attribute) {
        return attribute.AttributeType.Namespace == "Concord";
    }
}
