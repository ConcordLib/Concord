using System.Reflection;
using Concord;
using Concord.AttachedData;
using Concord.Backend;
using Concord.Emit;
using Concord.Orchestration;

namespace Concord.PackageContract;

public class ContractTarget {
    public void Method() { }
}

[Patch]
public abstract class ContractPatch : ContractTarget {
    [Inject(At.Head, nameof(ContractTarget.Method))]
    private void Head() { }
}

public sealed class StubBackend : IPatchBackend {
    public string Name => "stub";

    public int PatchedTargetCount => 0;

    public TargetVerdict Judge(MethodBase target) {
        return TargetVerdict.Accept();
    }

    public IBackendHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added) {
        throw new NotSupportedException();
    }

    public IReadOnlyList<string> OwnersOf(MethodBase target) {
        return Array.Empty<string>();
    }

    public bool OwnsBody(MethodBase target) {
        return false;
    }

    public bool Recompose(MethodBase target) {
        return false;
    }
}

public static class Contract {
    private sealed class StubSlot : IAttachedSlot {
        public object? Get(object target) {
            return null;
        }

        public void Set(object target, object? value) { }
    }

#if NET472
    private static readonly Func<Action<string>?, string[]?, Concord.MonoMod.Selection> MonoModInstall =
        Concord.MonoMod.MonoModEntryPoint.Install;
#else
    private static readonly Func<Action<string>?, string[]?, Concord.Waylith.Selection> WaylithInstall =
        Concord.Waylith.AdapterEntryPoint.Install;
#endif

    public static void Run() {
        TargetVerdict.Accept();
        TargetVerdict.Refuse("CONC000", "x");
        PatchBackend.TryGetCurrent(out _);
        _ = ScratchGenerator.Current;
        PropertyRegistry.Register(typeof(Contract), typeof(ContractTarget), "contractField", typeof(int), new StubSlot());
#if NET472
        _ = MonoModInstall;
#else
        _ = WaylithInstall;
#endif
    }
}
