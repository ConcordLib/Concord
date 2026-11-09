using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.AttachedData;
using Concord.Backend;
using Concord.Emit;
using Concord.Orchestration;
using Xunit;

namespace Concord.Waylith.Tests;

public abstract class ContainmentBase {
    public abstract void Refused();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public virtual void Ordinary() { }
}

[Patch]
public abstract class ContainmentDeclaration : ContainmentBase {
    [Inject(At.Head, nameof(Ordinary))]
    public void OnOrdinary(ControlHandle ch) { }

    [Inject(At.Head, nameof(Refused))]
    public void OnRefused(ControlHandle ch) { }
}

[Collection("AdapterStatics")]
public sealed class ContainmentTests : IDisposable {
    private readonly IPatchBackend? backendBefore = AmbientSwitches.InstalledBackend();

    public void Dispose() {
        AmbientSwitches.RestoreBackend(backendBefore);
    }

    [Fact]
    public void AnAbstractTargetIsRefusedWithoutStoppingTheOrdinaryTargetInTheSameDeclaration() {
        StagePublisher publisher = new StagePublisher(method => method.MethodHandle, static _ => { });
        publisher.UseRebuild(static _ => null);
        PatchBackend.Current = new WaylithClrBackend(publisher);

        TestPatchApplier applier = new TestPatchApplier();
        List<string> logged = new List<string>();

        PatchDeclarationScanner.ScanDeclarations(
            [typeof(ContainmentDeclaration)],
            applier,
            new NoopAttachedPropertyRegistry(),
            logged.Add);

        IBackendHandle applied = Assert.Single(applier.Handles);
        Assert.Equal(typeof(ContainmentBase).GetMethod(nameof(ContainmentBase.Ordinary)), applied.Original);
        Assert.True(applied.IsApplied);

        string refusal = Assert.Single(logged);
        Assert.Contains(nameof(ContainmentBase.Refused), refusal);
        Assert.Contains(TargetEligibility.NoBodyCode, refusal);
    }

    private sealed class TestPatchApplier : IPatchApplier {
        public List<IBackendHandle> Handles { get; } = new List<IBackendHandle>();

        public void ApplyPatch(MethodBase target, Injection injection) {
            Injection[] composed = [injection];
            WrapperComposer.RejectSharedGenericInstantiation(target, composed);
            MethodBase canonical = WrapperComposer.ResolveBodyTarget(target, injection.Body);
            WrapperComposer.RejectSharedGenericInstantiation(canonical, composed);
            WrapperComposer.ValidateBodySelection(target, canonical, injection);
            Handles.Add(PatchBackend.Current.ApplyComposed(canonical, [injection]));
        }
    }

    private sealed class NoopAttachedPropertyRegistry : IAttachedPropertyRegistry {
        public void RegisterAttachedProperty(Type declarationType, Type baseType, string name, Type valueType, IAttachedSlot slot) {
        }
    }
}
