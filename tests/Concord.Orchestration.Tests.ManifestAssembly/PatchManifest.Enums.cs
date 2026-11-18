using Concord.Orchestration.Tests.ManifestAssembly;

namespace Concord.Generated;

internal static partial class PatchManifest {
    static partial void DescribeEnums(IPatchManifestSink sink) {
        sink.OnMember("T:Concord.Orchestration.Tests.ManifestAssembly.ManifestSeasons", new PatchAttribute());
        sink.OnMember("F:Concord.Orchestration.Tests.ManifestAssembly.ManifestSeasons.Monsoon", new EnumMemberAttribute(ManifestSeasons.MonsoonId));
    }
}
