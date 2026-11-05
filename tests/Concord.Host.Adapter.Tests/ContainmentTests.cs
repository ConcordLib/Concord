using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.AttachedData;
using Concord.Detour;
using Concord.Emit;
using Concord.Orchestration;
using Xunit;

namespace Concord.Host.Adapter.Tests;

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
    private readonly IDetourBackend? backendBefore = AmbientSwitches.InstalledBackend();

    public void Dispose() {
        AmbientSwitches.RestoreBackend(backendBefore);
    }

    [Fact]
    public void AnAbstractTargetIsRefusedWithoutStoppingTheOrdinaryTargetInTheSameDeclaration() {
        StagePublisher publisher = new StagePublisher(method => method.MethodHandle, static _ => { });
        publisher.UseRebuild(static _ => null);
        DetourBackend.Current = new WaylithDetourBackend(publisher);

        TestPatchApplier applier = new TestPatchApplier();
        List<string> logged = new List<string>();

        PatchDeclarationScanner.ScanDeclarations(
            [typeof(ContainmentDeclaration)],
            applier,
            new NoopAttachedPropertyRegistry(),
            logged.Add);

        IDetourHandle applied = Assert.Single(applier.Handles);
        Assert.Equal(typeof(ContainmentBase).GetMethod(nameof(ContainmentBase.Ordinary)), applied.Original);
        Assert.True(applied.IsApplied);

        string refusal = Assert.Single(logged);
        Assert.Contains(nameof(ContainmentBase.Refused), refusal);
        Assert.Contains(TargetEligibility.NoBodyCode, refusal);
    }

    private sealed class TestPatchApplier : IPatchApplier {
        public List<IDetourHandle> Handles { get; } = new List<IDetourHandle>();

        public void ApplyPatch(MethodBase target, Injection injection) {
            Injection[] composed = [injection];
            WrapperComposer.RejectSharedGenericInstantiation(target, composed);
            MethodBase canonical = WrapperComposer.ResolveBodyTarget(target, injection.Body);
            WrapperComposer.RejectSharedGenericInstantiation(canonical, composed);
            WrapperComposer.ValidateBodySelection(target, canonical, injection);
            Handles.Add(DetourBackend.Current.ApplyComposed(canonical, [injection]));
        }
    }

    private sealed class NoopAttachedPropertyRegistry : IAttachedPropertyRegistry {
        public void RegisterAttachedProperty(Type declarationType, Type baseType, string name, Type valueType, IAttachedSlot slot) {
        }
    }
}
