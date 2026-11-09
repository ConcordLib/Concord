using System.Collections.Generic;
using System.Reflection;
using Concord;
using Concord.Emit;
using Concord.Orchestration;

namespace Concord.RimWorld;

public sealed class RimWorldPatchApplier : IPatchApplier {
  private readonly List<IPatchHandle> handles = [];

  public void ApplyPatch(MethodBase target, Injection injection) {
    handles.Add(Patcher.PatchInjection(target, injection));
  }
}
