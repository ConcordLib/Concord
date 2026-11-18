using Concord.Orchestration.Tests.ManifestAssembly;

namespace Concord.Generated;

internal static partial class PatchManifest {
    public static void Describe(object sink) {
        IPatchManifestSink s = (IPatchManifestSink)sink;

        s.OnMember("T:Concord.Orchestration.Tests.ManifestAssembly.HeadPatch", new PatchAttribute());
        s.OnMember(
            "M:Concord.Orchestration.Tests.ManifestAssembly.HeadPatch.BumpHead",
            new InjectAttribute(At.Head, nameof(ManifestTarget.Bump)));

        s.OnMember("T:Concord.Orchestration.Tests.ManifestAssembly.FirstOrderedPatch", new PatchAttribute());
        s.OnMember(
            "T:Concord.Orchestration.Tests.ManifestAssembly.FirstOrderedPatch",
            new PatchBeforeAttribute(typeof(SecondOrderedPatch)));
        s.OnMember("T:Concord.Orchestration.Tests.ManifestAssembly.FirstOrderedPatch", new PatchBeforeAttribute("Optional.Mod.Before"));
        s.OnMember(
            "M:Concord.Orchestration.Tests.ManifestAssembly.FirstOrderedPatch.OnOrdered",
            new InjectAttribute(At.Head, nameof(ManifestTarget.Ordered)));

        s.OnMember("T:Concord.Orchestration.Tests.ManifestAssembly.SecondOrderedPatch", new PatchAttribute());
        s.OnMember(
            "T:Concord.Orchestration.Tests.ManifestAssembly.SecondOrderedPatch",
            new PatchAfterAttribute(typeof(FirstOrderedPatch)));
        s.OnMember("T:Concord.Orchestration.Tests.ManifestAssembly.SecondOrderedPatch", new PatchAfterAttribute("Optional.Mod.After"));
        s.OnMember(
            "M:Concord.Orchestration.Tests.ManifestAssembly.SecondOrderedPatch.OnOrdered",
            new InjectAttribute(At.Head, nameof(ManifestTarget.Ordered)));

        s.OnMember("T:Concord.Orchestration.Tests.ManifestAssembly.DebugPatch", new PatchAttribute());
        s.OnMember("T:Concord.Orchestration.Tests.ManifestAssembly.DebugPatch", new PatchDebugAttribute());
        s.OnMember(
            "M:Concord.Orchestration.Tests.ManifestAssembly.DebugPatch.OnDebugged",
            new InjectAttribute(At.Head, nameof(ManifestTarget.Debugged)));

        s.OnMember("T:Concord.Orchestration.Tests.ManifestAssembly.BuildPatch", new PatchAttribute());
        s.OnMember(
            "M:Concord.Orchestration.Tests.ManifestAssembly.BuildPatch.OnBuilt",
            new InjectNewAttribute(nameof(ManifestTarget.Build), typeof(ManifestBuilt), At.Tail));
        s.OnMember(
            "M:Concord.Orchestration.Tests.ManifestAssembly.BuildPatch.OnBuilt",
            new SliceAttribute(typeof(ManifestSliceFrom), nameof(ManifestSliceFrom.Start), 1, typeof(ManifestSliceTo), nameof(ManifestSliceTo.End), 1));

        s.OnMember("T:Concord.Orchestration.Tests.ManifestAssembly.AttachedPatch", new PatchAttribute());
        s.OnMember("F:Concord.Orchestration.Tests.ManifestAssembly.AttachedPatch.Visits", new AttachedAttribute());

        s.OnMember("T:Concord.Orchestration.Tests.ManifestAssembly.OverriddenPatch", new PatchAttribute());
        s.OnMember(
            "M:Concord.Orchestration.Tests.ManifestAssembly.OverriddenPatch.OnOverridden",
            new InjectAttribute(At.Tail, nameof(ManifestTarget.Overridden)));

        s.OnMember("T:Concord.Orchestration.Tests.ManifestAssembly.OverloadPatch", new PatchAttribute());
        s.OnMember(
            "M:Concord.Orchestration.Tests.ManifestAssembly.OverloadPatch.On",
            new InjectAttribute(At.Head, nameof(ManifestTarget.Overloaded)));
        s.OnMember(
            "M:Concord.Orchestration.Tests.ManifestAssembly.OverloadPatch.On(System.Int32)",
            new InjectAttribute(At.Head, nameof(ManifestTarget.Overloaded)));
        s.OnMember(
            "M:Concord.Orchestration.Tests.ManifestAssembly.OverloadPatch.On(System.Int32@)",
            new InjectAttribute(At.Head, nameof(ManifestTarget.Overloaded)));
        s.OnMember(
            "M:Concord.Orchestration.Tests.ManifestAssembly.OverloadPatch.On(System.Int32*)",
            new InjectAttribute(At.Head, nameof(ManifestTarget.Overloaded)));
        s.OnMember(
            "M:Concord.Orchestration.Tests.ManifestAssembly.OverloadPatch.On``1(``0)",
            new InjectAttribute(At.Head, nameof(ManifestTarget.Overloaded)));
        s.OnMember(
            "M:Concord.Orchestration.Tests.ManifestAssembly.OverloadPatch.On(System.Collections.Generic.List{System.String})",
            new InjectAttribute(At.Head, nameof(ManifestTarget.Overloaded)));
        s.OnMember(
            "M:Concord.Orchestration.Tests.ManifestAssembly.OverloadPatch.On(System.Int32[0:,0:])",
            new InjectAttribute(At.Head, nameof(ManifestTarget.Overloaded)));
        s.OnMember(
            "M:Concord.Orchestration.Tests.ManifestAssembly.OverloadPatch.On(Concord.Orchestration.Tests.ManifestAssembly.OverloadPatch.Hidden)",
            new InjectAttribute(At.Head, nameof(ManifestTarget.Overloaded)));

        DescribeMembers(s);
        DescribeParameters(s);
        DescribeEnums(s);
    }

    static partial void DescribeMembers(IPatchManifestSink sink);

    static partial void DescribeParameters(IPatchManifestSink sink);

    static partial void DescribeEnums(IPatchManifestSink sink);
}
