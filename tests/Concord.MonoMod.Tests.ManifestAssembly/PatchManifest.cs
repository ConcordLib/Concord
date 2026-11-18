using Concord.MonoMod.Tests.ManifestAssembly;

namespace Concord.Generated;

internal static class PatchManifest {
    public static void Describe(object sink) {
        IPatchManifestSink s = (IPatchManifestSink)sink;

        s.OnMember("T:Concord.MonoMod.Tests.ManifestAssembly.HeadPatch", new PatchAttribute());
        s.OnMember(
            "M:Concord.MonoMod.Tests.ManifestAssembly.HeadPatch.BumpHead",
            new InjectAttribute(At.Head, nameof(MonoTarget.Bump)));

        s.OnMember("T:Concord.MonoMod.Tests.ManifestAssembly.FieldPatch", new PatchAttribute());
        s.OnMember(
            "M:Concord.MonoMod.Tests.ManifestAssembly.FieldPatch.OnFuel(Concord.ControlHandle{System.Int32},System.Int32)",
            new InjectAttribute(At.Head, nameof(MonoTarget.Fuel)));
        s.OnMember("F:Concord.MonoMod.Tests.ManifestAssembly.FieldPatch.injectedFuel", new InjectFieldAttribute("fuel"));

        const string afterApply = "M:Concord.MonoMod.Tests.ManifestAssembly.CapturePatch.AfterApply(System.Int32,System.Single)";
        s.OnMember("T:Concord.MonoMod.Tests.ManifestAssembly.CapturePatch", new PatchAttribute());
        s.OnMember(afterApply, new InjectAttribute(nameof(MonoTarget.Priced), typeof(MonoRules), nameof(MonoRules.Apply), At.Tail));
        s.OnParameter(afterApply, 0, new CaptureAttribute(1));
        s.OnParameter(afterApply, 1, new CaptureAttribute(2));

        s.OnMember("T:Concord.MonoMod.Tests.ManifestAssembly.AOrderedEarlyPatch", new PatchAttribute());
        s.OnMember("T:Concord.MonoMod.Tests.ManifestAssembly.AOrderedEarlyPatch", new PatchBeforeAttribute(typeof(ZOrderedLatePatch)));
        s.OnMember(
            "M:Concord.MonoMod.Tests.ManifestAssembly.AOrderedEarlyPatch.OnOrdered",
            new InjectAttribute(At.Head, nameof(MonoTarget.Ordered)));

        s.OnMember("T:Concord.MonoMod.Tests.ManifestAssembly.ZOrderedLatePatch", new PatchAttribute());
        s.OnMember(
            "M:Concord.MonoMod.Tests.ManifestAssembly.ZOrderedLatePatch.OnOrdered",
            new InjectAttribute(At.Head, nameof(MonoTarget.Ordered)));
    }
}
