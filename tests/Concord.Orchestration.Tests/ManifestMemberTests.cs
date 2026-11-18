using System.Reflection;
using Concord.Backend;
using Concord.Emit;
using Concord.Orchestration.Tests.ManifestAssembly;
using Xunit;

namespace Concord.Orchestration.Tests;

[Collection(SharedAssemblyApplyCollection.Name)]
public sealed class ManifestMemberTests : IDisposable {
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Assembly Manifested = typeof(MemberTarget).Assembly;

    private readonly RecordingDetourBackend backend = new RecordingDetourBackend();

    public ManifestMemberTests() {
        PatchBackend.Current = backend;
        Patcher.ForgetAttachedPropertyRegistries();
    }

    public void Dispose() {
        Patcher.ForgetAttachedPropertyRegistries();
        PatchBackend.Reset();
    }

    [Fact]
    public void Apply_ManifestInjectField_ReadsAndWritesThePrivateTargetField() {
        AssertNoConcordAttributes(typeof(InjectFieldMemberPatch));
        MemberTarget instance = new MemberTarget();

        int result = (int)Compose(nameof(MemberTarget.Fuel)).Invoke(null, [instance, 5])!;

        Assert.Equal(15, result);
        Assert.Equal(15, instance.FuelSnapshot);
    }

    [Fact]
    public void Apply_ManifestInjectProperty_ReadsThePrivateTargetProperty() {
        AssertNoConcordAttributes(typeof(InjectPropertyMemberPatch));

        int result = (int)Compose(nameof(MemberTarget.ReadMood)).Invoke(null, [new MemberTarget()])!;

        Assert.Equal(2, result);
    }

    [Fact]
    public void Apply_ManifestInjectMethod_CallsThePrivateTargetMethod() {
        AssertNoConcordAttributes(typeof(InjectMethodMemberPatch));
        MemberTarget instance = new MemberTarget();

        int result = (int)Compose(nameof(MemberTarget.Recalc)).Invoke(null, [instance, 4])!;

        Assert.Equal(16, result);
        Assert.Equal(14, instance.FuelSnapshot);
    }

    [Fact]
    public void Apply_ManifestInjectInstance_GivesTheTargetInstance() {
        AssertNoConcordAttributes(typeof(InjectInstanceMemberPatch));
        MemberTarget instance = new MemberTarget();

        object? result = Compose(nameof(MemberTarget.Me)).Invoke(null, [instance]);

        Assert.Same(instance, result);
    }

    [Fact]
    public void Apply_ManifestAttachedField_KeepsItsOwnValuePerTargetInstance() {
        AssertNoConcordAttributes(typeof(AttachedMemberPatch));
        MethodInfo wrapper = Compose(nameof(MemberTarget.Visit));
        MemberTarget first = new MemberTarget();
        MemberTarget second = new MemberTarget();

        wrapper.Invoke(null, [first]);
        int firstVisits = (int)wrapper.Invoke(null, [first])!;
        int secondVisits = (int)wrapper.Invoke(null, [second])!;

        Assert.Equal(2, firstVisits);
        Assert.Equal(1, secondVisits);
    }

    private MethodInfo Compose(string targetName) {
        MethodBase target = typeof(MemberTarget).GetMethod(targetName)!;
        using (Patcher.Apply(Manifested)) {
            Injection injection = Assert.Single(backend.InjectionsOn(target));
            return WrapperComposer.Compose(target, [injection]).Wrapper;
        }
    }

    private static void AssertNoConcordAttributes(Type patch) {
        Assert.DoesNotContain(patch.GetCustomAttributesData(), IsConcordAttribute);
        foreach (MemberInfo member in patch.GetMembers(Declared)) {
            Assert.DoesNotContain(member.GetCustomAttributesData(), IsConcordAttribute);
        }
    }

    private static bool IsConcordAttribute(CustomAttributeData attribute) {
        return attribute.AttributeType.Namespace == "Concord";
    }
}
