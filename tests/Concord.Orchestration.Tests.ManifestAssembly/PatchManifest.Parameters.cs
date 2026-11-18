using System.Threading;
using Concord.Orchestration.Tests.ManifestAssembly;

namespace Concord.Generated;

internal static partial class PatchManifest {
    static partial void DescribeParameters(IPatchManifestSink sink) {
        Interlocked.Increment(ref ParameterManifestRuns.Count);

        const string readByIndex = "M:Concord.Orchestration.Tests.ManifestAssembly.LocalParameterPatch.ReadByIndex(System.Int32)";
        const string readByName = "M:Concord.Orchestration.Tests.ManifestAssembly.LocalParameterPatch.ReadByName(System.Int32)";
        sink.OnMember("T:Concord.Orchestration.Tests.ManifestAssembly.LocalParameterPatch", new PatchAttribute());
        sink.OnMember(readByIndex, new InjectAttribute(At.Return, nameof(ParameterTarget.TwoLocals)));
        sink.OnParameter(readByIndex, 0, new LocalAttribute { Index = 1 });
        sink.OnMember(readByName, new InjectAttribute(At.Return, nameof(ParameterTarget.TwoLocals)));
        sink.OnParameter(readByName, 0, new LocalAttribute { Name = "doubled" });

        const string afterApply = "M:Concord.Orchestration.Tests.ManifestAssembly.CaptureParameterPatch.AfterApply(System.Int32,System.Single)";
        sink.OnMember("T:Concord.Orchestration.Tests.ManifestAssembly.CaptureParameterPatch", new PatchAttribute());
        sink.OnMember(
            afterApply,
            new InjectAttribute(nameof(ParameterTarget.Priced), typeof(ParameterRules), nameof(ParameterRules.Apply), At.Tail));
        sink.OnParameter(afterApply, 0, new CaptureAttribute(1));
        sink.OnParameter(afterApply, 1, new CaptureAttribute(2));

        const string onPlain = "M:Concord.Orchestration.Tests.ManifestAssembly.BoundParameterPatch.OnPlain(System.Int32)";
        sink.OnMember("T:Concord.Orchestration.Tests.ManifestAssembly.BoundParameterPatch", new PatchAttribute());
        sink.OnMember(onPlain, new InjectAttribute(At.Head, nameof(ParameterTarget.Plain)));
        sink.OnParameter(onPlain, 0, new BoundAttribute());

        const string beforeTotal = "M:Concord.Orchestration.Tests.ManifestAssembly.SliceParameterPatch.BeforeTotal(System.Int32)";
        sink.OnMember("T:Concord.Orchestration.Tests.ManifestAssembly.SliceParameterPatch", new PatchAttribute());
        sink.OnMember(
            beforeTotal,
            new InjectAttribute(nameof(ParameterTarget.Checkout), typeof(ParameterRules), nameof(ParameterRules.Total), At.Head, 1));
        sink.OnMember(
            beforeTotal,
            new SliceAttribute(typeof(ParameterSliceFrom), nameof(ParameterSliceFrom.Begin), 1, typeof(ParameterSliceTo), nameof(ParameterSliceTo.End), 1));
        sink.OnParameter(beforeTotal, 0, new CaptureAttribute(1));

        const string slicedAtHead = "M:Concord.Orchestration.Tests.ManifestAssembly.HeadSliceParameterPatch.SlicedAtHead";
        sink.OnMember("T:Concord.Orchestration.Tests.ManifestAssembly.HeadSliceParameterPatch", new PatchAttribute());
        sink.OnMember(slicedAtHead, new InjectAttribute(At.Head, nameof(ParameterTarget.Plain)));
        sink.OnMember(
            slicedAtHead,
            new SliceAttribute(typeof(ParameterSliceFrom), nameof(ParameterSliceFrom.Begin), 1, typeof(ParameterSliceTo), nameof(ParameterSliceTo.End), 1));
    }
}
