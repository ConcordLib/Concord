using System.Reflection;
using Concord.Backend;
using Concord.Emit;

namespace Concord.Orchestration;

internal sealed class CollectingPatchApplier : IPatchApplier {
    private readonly List<IBackendHandle> handles = [];

    public IReadOnlyList<IBackendHandle> Handles => handles;

    public void ApplyPatch(MethodBase target, Injection injection) {
        Injection[] composed = [injection];
        WrapperComposer.RejectSharedGenericInstantiation(target, composed);
        MethodBase canonical = WrapperComposer.ResolveBodyTarget(target, injection.Body);
        WrapperComposer.RejectSharedGenericInstantiation(canonical, composed);
        WrapperComposer.ValidateBodySelection(target, canonical, injection);
        IBackendHandle handle = PatchBackend.Current.ApplyComposed(canonical, [injection]);
        handles.Add(handle);
    }
}
