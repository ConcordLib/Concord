using Concord.Emit;
using Concord.Orchestration;

namespace Concord.PackageContract.Runtime {
    public class ContractTarget {
        public void Method() { }
    }

    [Patch]
    public abstract class RuntimePatch : ContractTarget {
        [Inject(At.Head, nameof(ContractTarget.Method))]
        private void Head() { }
    }
}
