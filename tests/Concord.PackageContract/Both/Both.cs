using Concord.Emit;
using Concord.Orchestration;

namespace Concord.PackageContract.Both {
    public class BothTarget {
        public void Method() { }
    }

    [Patch]
    public abstract class BothPatch : BothTarget {
        [Inject(At.Head, nameof(BothTarget.Method))]
        private void Head() { }
    }
}
