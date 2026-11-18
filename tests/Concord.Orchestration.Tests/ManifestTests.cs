using System.Reflection;
using System.Reflection.Emit;
using Concord.Backend;
using Concord.Emit;
using Concord.Orchestration.Tests.ManifestAssembly;
using Concord.Orchestration.Tests.RegistryAssembly;
using Xunit;

namespace Concord.Orchestration.Tests;

[Collection(SharedAssemblyApplyCollection.Name)]
public sealed class ManifestTests : IDisposable {
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Assembly Manifested = typeof(ManifestTarget).Assembly;

    private readonly RecordingDetourBackend backend = new RecordingDetourBackend();
    private readonly List<string> logs = [];

    public ManifestTests() {
        PatchBackend.Current = backend;
        Patcher.ForgetAttachedPropertyRegistries();
        Patcher.UseLog(logs.Add);
    }

    public void Dispose() {
        Patcher.UseLog(null);
        Patcher.ForgetAttachedPropertyRegistries();
        PatchBackend.Reset();
    }

    [Fact]
    public void Apply_ManifestAndNoAttributesInMetadata_ComposesTheHeadInjectionOntoItsTarget() {
        MethodInfo bumpHead = typeof(HeadPatch).GetMethod("BumpHead", Declared)!;
        Assert.DoesNotContain(typeof(HeadPatch).GetCustomAttributesData(), IsConcordAttribute);
        Assert.DoesNotContain(bumpHead.GetCustomAttributesData(), IsConcordAttribute);

        using (Patcher.Apply(Manifested)) {
            Injection injection = Assert.Single(backend.InjectionsOn(TargetMethod(nameof(ManifestTarget.Bump))));
            Assert.Equal(bumpHead, injection.InjectionMethod);
            Assert.IsType<InjectAt.Head>(injection.At);
            Assert.Equal(typeof(HeadPatch).FullName, injection.Owner);
        }

        Assert.Empty(logs);
    }

    [Fact]
    public void Apply_Manifest_ScansOnlyTheTypesItDescribes() {
        Assert.NotNull(typeof(UnlistedPatch).GetCustomAttribute<PatchAttribute>());

        using (Patcher.Apply(Manifested)) {
            Assert.DoesNotContain(backend.Applied, applied => applied.Injection.InjectionMethod.DeclaringType == typeof(UnlistedPatch));
        }
    }

    [Fact]
    public void Apply_ManifestOrderAttributes_RunTheFirstPatchBeforeTheSecond() {
        using (Patcher.Apply(Manifested)) {
            IReadOnlyList<Injection> injections = backend.InjectionsOn(TargetMethod(nameof(ManifestTarget.Ordered)));
            Injection first = Assert.Single(injections, injection => injection.Owner == typeof(FirstOrderedPatch).FullName);
            Injection second = Assert.Single(injections, injection => injection.Owner == typeof(SecondOrderedPatch).FullName);

            Assert.Equal([typeof(SecondOrderedPatch).FullName!, "Optional.Mod.Before"], first.BeforeOwners);
            Assert.Equal([typeof(FirstOrderedPatch).FullName!, "Optional.Mod.After"], second.AfterOwners);
            Assert.Equal([second, first], InjectionOrderer.OrderForComposition([(0, first), (1, second)]));
        }
    }

    [Fact]
    public void Apply_ManifestPatchDebug_MarksTheInjectionForIlDump() {
        using (Patcher.Apply(Manifested)) {
            Injection debugged = Assert.Single(backend.InjectionsOn(TargetMethod(nameof(ManifestTarget.Debugged))));
            Injection plain = Assert.Single(backend.InjectionsOn(TargetMethod(nameof(ManifestTarget.Bump))));

            Assert.True(debugged.Debug);
            Assert.False(plain.Debug);
        }
    }

    [Fact]
    public void Apply_ManifestInjectNewWithSlice_RecordsTheSlicedNewObjInjection() {
        using (Patcher.Apply(Manifested)) {
            Injection injection = Assert.Single(backend.InjectionsOn(TargetMethod(nameof(ManifestTarget.Build))));
            InjectAt.NewObj at = Assert.IsType<InjectAt.NewObj>(injection.At);
            SliceRange range = Assert.IsType<SliceRange>(at.Slice);

            Assert.Equal(typeof(ManifestBuilt), at.ConstructedType);
            Assert.Equal(At.Tail, at.Shift);
            Assert.Equal(typeof(ManifestSliceFrom), range.FromType);
            Assert.Equal(nameof(ManifestSliceFrom.Start), range.FromMember);
            Assert.Equal(typeof(ManifestSliceTo), range.ToType);
            Assert.Equal(nameof(ManifestSliceTo.End), range.ToMember);
        }
    }

    [Fact]
    public void Apply_ManifestAttachedField_RegistersTheAttachedProperty() {
        FakeAttachedPropertyRegistry registry = new FakeAttachedPropertyRegistry();
        Patcher.UseAttachedPropertyRegistry(registry);

        using (Patcher.Apply(Manifested)) {
            Assert.Contains(new PropCall(typeof(ManifestTarget), nameof(AttachedPatch.Visits), typeof(int)), registry.Calls);
        }
    }

    [Fact]
    public void Apply_MemberInMetadataAndManifest_UsesTheManifestEntry() {
        MethodInfo onOverridden = typeof(OverriddenPatch).GetMethod("OnOverridden", Declared)!;
        InjectAttribute inMetadata = onOverridden.GetCustomAttribute<InjectAttribute>()!;
        Assert.Equal(nameof(ManifestTarget.Bump), inMetadata.Method);
        Assert.Equal(At.Head, inMetadata.At);

        using (Patcher.Apply(Manifested)) {
            AppliedInjection applied = Assert.Single(backend.Applied, entry => entry.Injection.InjectionMethod.Equals(onOverridden));

            Assert.Equal(TargetMethod(nameof(ManifestTarget.Overridden)), applied.Target);
            Assert.IsType<InjectAt.Tail>(applied.Injection.At);
        }
    }

    [Fact]
    public void Apply_ManifestOverloads_ResolveEachEntryToItsOwnOverload() {
        List<MethodInfo> overloads = [];
        foreach (MethodInfo method in typeof(OverloadPatch).GetMethods(Declared)) {
            if (method.Name == "On") {
                overloads.Add(method);
            }
        }

        using (Patcher.Apply(Manifested)) {
            IReadOnlyList<Injection> injections = backend.InjectionsOn(TargetMethod(nameof(ManifestTarget.Overloaded)));

            Assert.Equal(8, overloads.Count);
            Assert.Equal(overloads.Count, injections.Count);
            Assert.All(overloads, overload => Assert.Single(injections, injection => injection.InjectionMethod.Equals(overload)));
        }
    }

    [Fact]
    public void Apply_AssemblyWithoutManifest_AppliesItsPatchesFromMetadata() {
        MethodBase bump = typeof(RegistryTarget).GetMethod(nameof(RegistryTarget.Bump))!;

        using (Patcher.Apply(typeof(RegistryTarget).Assembly)) {
            AppliedInjection applied = Assert.Single(backend.Applied);

            Assert.Equal(bump, applied.Target);
            Assert.Equal(typeof(RegistryListedPatch), applied.Injection.InjectionMethod.DeclaringType);
        }

        Assert.Empty(logs);
    }

    [Fact]
    public void Apply_AssemblyWithNeitherManifestNorDeclaration_LogsThatItAppliedNothing() {
        Assembly plain = DefineAssembly(module => module.DefineType("Plain.Helper", TypeAttributes.Public).CreateType());

        using (Patcher.Apply(plain)) {
            string line = Assert.Single(logs);
            Assert.Contains(plain.GetName().Name!, line);
            Assert.Empty(backend.Applied);
        }
    }

    [Fact]
    public void Apply_ManifestNamingAMissingMember_FailsWithTheAssemblyAndTheMember() {
        Assembly broken = DefineManifestAssembly(describe => {
            EmitOnMember(describe, "T:Broken.Patch");
            EmitOnMember(describe, "M:Broken.Patch.Gone");
            describe.Emit(OpCodes.Ret);
        });

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => Patcher.Apply(broken));

        Assert.Contains(broken.GetName().Name!, failure.Message);
        Assert.Contains("M:Broken.Patch.Gone", failure.Message);
        Assert.Empty(backend.Applied);
    }

    [Fact]
    public void Apply_ManifestThatThrows_FailsWithTheAssemblyAndTheLastMemberItDescribed() {
        Assembly broken = DefineManifestAssembly(describe => {
            EmitOnMember(describe, "T:Broken.Patch");
            describe.Emit(OpCodes.Ldstr, "the manifest broke");
            describe.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
            describe.Emit(OpCodes.Throw);
        });

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => Patcher.Apply(broken));

        Assert.Contains(broken.GetName().Name!, failure.Message);
        Assert.Contains("T:Broken.Patch", failure.Message);
        Assert.Equal("the manifest broke", failure.InnerException?.Message);
        Assert.Empty(backend.Applied);
    }

    [Fact]
    public void Dispose_HandleFromApplyWithManifest_RemovesThePatches() {
        MethodBase bump = TargetMethod(nameof(ManifestTarget.Bump));

        IPatchHandle handle = Patcher.Apply(Manifested);
        Assert.Equal([typeof(HeadPatch).FullName!], backend.OwnersOf(bump));

        handle.Dispose();

        Assert.False(handle.IsApplied);
        Assert.Empty(backend.OwnersOf(bump));
        Assert.Equal(0, backend.PatchedTargetCount);
    }

    private static MethodBase TargetMethod(string name) {
        return typeof(ManifestTarget).GetMethod(name)!;
    }

    private static bool IsConcordAttribute(CustomAttributeData attribute) {
        return attribute.AttributeType.Namespace == "Concord";
    }

    private static void EmitOnMember(ILGenerator describe, string memberId) {
        describe.Emit(OpCodes.Ldarg_0);
        describe.Emit(OpCodes.Castclass, typeof(IPatchManifestSink));
        describe.Emit(OpCodes.Ldstr, memberId);
        describe.Emit(OpCodes.Newobj, typeof(PatchAttribute).GetConstructor(Type.EmptyTypes)!);
        describe.Emit(OpCodes.Callvirt, typeof(IPatchManifestSink).GetMethod(nameof(IPatchManifestSink.OnMember))!);
    }

    private static Assembly DefineManifestAssembly(Action<ILGenerator> describe) {
        return DefineAssembly(module => {
            module.DefineType("Broken.Patch", TypeAttributes.Public | TypeAttributes.Abstract).CreateType();
            TypeBuilder manifest = module.DefineType(
                "Concord.Generated.PatchManifest",
                TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed);
            MethodBuilder method = manifest.DefineMethod(
                "Describe",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void),
                [typeof(object)]);
            describe(method.GetILGenerator());
            manifest.CreateType();
        });
    }

    private static Assembly DefineAssembly(Action<ModuleBuilder> define) {
        AssemblyName name = new AssemblyName("ManifestMod_" + Guid.NewGuid().ToString("N"));
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        define(assembly.DefineDynamicModule(name.Name!));
        return assembly;
    }
}
