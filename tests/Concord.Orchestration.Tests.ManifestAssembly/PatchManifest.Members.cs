using Concord.Orchestration.Tests.ManifestAssembly;

namespace Concord.Generated;

internal static partial class PatchManifest {
    static partial void DescribeMembers(IPatchManifestSink sink) {
        sink.OnMember("T:Concord.Orchestration.Tests.ManifestAssembly.InjectFieldMemberPatch", new PatchAttribute());
        sink.OnMember(
            "M:Concord.Orchestration.Tests.ManifestAssembly.InjectFieldMemberPatch.OnFuel(Concord.ControlHandle{System.Int32},System.Int32)",
            new InjectAttribute(At.Head, nameof(MemberTarget.Fuel)));
        sink.OnMember("F:Concord.Orchestration.Tests.ManifestAssembly.InjectFieldMemberPatch.injectedFuel", new InjectFieldAttribute("fuel"));

        sink.OnMember("T:Concord.Orchestration.Tests.ManifestAssembly.InjectPropertyMemberPatch", new PatchAttribute());
        sink.OnMember(
            "M:Concord.Orchestration.Tests.ManifestAssembly.InjectPropertyMemberPatch.OnReadMood(Concord.ControlHandle{System.Int32})",
            new InjectAttribute(At.Head, nameof(MemberTarget.ReadMood)));
        sink.OnMember("P:Concord.Orchestration.Tests.ManifestAssembly.InjectPropertyMemberPatch.HiddenMood", new InjectPropertyAttribute("Mood"));

        sink.OnMember("T:Concord.Orchestration.Tests.ManifestAssembly.InjectMethodMemberPatch", new PatchAttribute());
        sink.OnMember(
            "M:Concord.Orchestration.Tests.ManifestAssembly.InjectMethodMemberPatch.OnRecalc(Concord.ControlHandle{System.Int32},System.Int32)",
            new InjectAttribute(At.Head, nameof(MemberTarget.Recalc)));
        sink.OnMember(
            "M:Concord.Orchestration.Tests.ManifestAssembly.InjectMethodMemberPatch.Recompute(System.Int32)",
            new InjectMethodAttribute("Recalculate"));

        sink.OnMember("T:Concord.Orchestration.Tests.ManifestAssembly.InjectInstanceMemberPatch", new PatchAttribute());
        sink.OnMember(
            "M:Concord.Orchestration.Tests.ManifestAssembly.InjectInstanceMemberPatch.OnMe(Concord.ControlHandle{Concord.Orchestration.Tests.ManifestAssembly.MemberTarget})",
            new InjectAttribute(At.Head, nameof(MemberTarget.Me)));
        sink.OnMember("P:Concord.Orchestration.Tests.ManifestAssembly.InjectInstanceMemberPatch.Self", new InjectInstanceAttribute());

        sink.OnMember("T:Concord.Orchestration.Tests.ManifestAssembly.AttachedMemberPatch", new PatchAttribute());
        sink.OnMember(
            "M:Concord.Orchestration.Tests.ManifestAssembly.AttachedMemberPatch.OnVisit(Concord.ControlHandle{System.Int32})",
            new InjectAttribute(At.Head, nameof(MemberTarget.Visit)));
        sink.OnMember("F:Concord.Orchestration.Tests.ManifestAssembly.AttachedMemberPatch.Seen", new AttachedAttribute());
    }
}
