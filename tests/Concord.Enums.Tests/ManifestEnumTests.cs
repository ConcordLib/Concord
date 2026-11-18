using System.Reflection;
using Concord.Backend;
using Concord.Orchestration.Tests.ManifestAssembly;
using Concord.Tests.Shared;
using Xunit;

namespace Concord.Enums.Tests;

[Collection(RegistryCollection.Name)]
public sealed class ManifestEnumTests : IDisposable {
    private static readonly Assembly Manifested = typeof(ManifestSeasons).Assembly;

    private readonly FakeStore store = new FakeStore();

    public ManifestEnumTests() {
        ExtendedEnumRegistry.ResetForTests();
        ExtendedEnumRegistry.UseStore(store);
        PatchBackend.Current = new RecordingDetourBackend();
    }

    public void Dispose() {
        ExtendedEnumRegistry.ResetForTests();
    }

    private static bool IsConcordAttribute(CustomAttributeData attribute) {
        return attribute.AttributeType.Namespace == "Concord";
    }

    [Fact]
    public void Apply_ManifestAndNoAttributesInMetadata_AddsTheDeclaredMembersToTheEnum() {
        Assert.DoesNotContain(typeof(ManifestSeasons).GetCustomAttributesData(), IsConcordAttribute);
        Assert.DoesNotContain(
            typeof(ManifestSeasons).GetField(nameof(ManifestSeasons.Monsoon))!.GetCustomAttributesData(),
            IsConcordAttribute);

        using (Patcher.Apply(Manifested)) {
            Assert.True(ExtendedEnumRegistry.Extends(typeof(ManifestSeason)));
            (long Value, string Name)[] expected = [
                (Value(ManifestSeasons.Harvest), nameof(ManifestSeasons.Harvest)),
                (Value(ManifestSeasons.Monsoon), nameof(ManifestSeasons.Monsoon)),
            ];
            Assert.Equal(expected, ExtendedEnumRegistry.MembersOf(typeof(ManifestSeason)).OrderBy(member => member.Name, StringComparer.Ordinal));
            Assert.Equal(new long[] { 2, 3 }, expected.Select(member => member.Value).OrderBy(value => value));
        }
    }

    [Fact]
    public void Apply_ManifestEnumMemberId_PersistsTheMemberUnderThatId() {
        using (Patcher.Apply(Manifested)) {
            Assert.Equal(Value(ManifestSeasons.Monsoon), store.Snapshot()[ManifestSeasons.MonsoonId]);
            Assert.DoesNotContain(typeof(ManifestSeasons).FullName + "." + nameof(ManifestSeasons.Monsoon), store.Snapshot().Keys);
        }
    }

    [Fact]
    public void Apply_MemberWithoutManifestEnumMember_PersistsTheMemberUnderTheDefaultId() {
        using (Patcher.Apply(Manifested)) {
            Assert.Equal(Value(ManifestSeasons.Harvest), store.Snapshot()[typeof(ManifestSeasons).FullName + "." + nameof(ManifestSeasons.Harvest)]);
        }
    }

    private static long Value(ManifestSeason season) {
        return (long)season;
    }
}
