using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Orchestration;
using Xunit;

namespace Concord.Waylith.ForkTests;

public enum ForkSeason {
    Spring = 0,
    Summer = 1,
}

[Patch]
public abstract class ForkSeasonExtension : ExtendedEnum<ForkSeason> {
    public static ForkSeason Monsoon;
}

public static class SeasonUtility {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ForkSeason[] All() => [ForkSeason.Spring, ForkSeason.Summer];

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IEnumerable<ForkSeason> Listed() => [ForkSeason.Spring, ForkSeason.Summer];

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ForkSeason Named(string text) => text == "Summer" ? ForkSeason.Summer : ForkSeason.Spring;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string Label(ForkSeason season) => "season " + (int)season;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Known(ForkSeason season) => season is ForkSeason.Spring or ForkSeason.Summer;
}

[Collection(ForkFieldInjection.ApplyCollection)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2263", Justification = "CoreLibEnumPatches only detours the Type-taking overloads, so the generic ones would not exercise the detour.")]
public sealed class EnumStageTests {
    private const string NoPipeline = "No transformer pipeline here, so nothing can be staged over a method body.";

    private static readonly Lazy<ForkSeason> Added = new Lazy<ForkSeason>(() => {
        ExtendedEnumRegistry.UseStore(new MemoryEnumStore());
        PatchDeclarationScanner.ScanExtendedEnums([typeof(ForkSeasonExtension)]);
        return ForkSeasonExtension.Monsoon;
    });

    public static bool Unavailable => ForkRuntime.Unavailable;

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void TheEnumListingsReportAnAddedMemberAfterTheCompiledOnes() {
        ForkSeason monsoon = Added.Value;

        WithCoreLibPatches(() => {
            Assert.Equal(
                new[] { ForkSeason.Spring, ForkSeason.Summer, monsoon },
                Enum.GetValues(typeof(ForkSeason)).Cast<ForkSeason>());
            Assert.Equal(new[] { "Spring", "Summer", "Monsoon" }, Enum.GetNames(typeof(ForkSeason)));
        });
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void AnAddedMemberIsDefinedAndParsesByName() {
        ForkSeason monsoon = Added.Value;

        WithCoreLibPatches(() => {
            Assert.True(Enum.IsDefined(typeof(ForkSeason), monsoon));
            Assert.False(Enum.IsDefined(typeof(ForkSeason), (ForkSeason)99));
            Assert.Equal(monsoon, Enum.Parse(typeof(ForkSeason), "Monsoon"));
            Assert.Equal(monsoon, Enum.Parse(typeof(ForkSeason), "monsoon", true));
            Assert.Equal(ForkSeason.Summer, Enum.Parse(typeof(ForkSeason), "Summer"));
            Assert.Throws<ArgumentException>(() => Enum.Parse(typeof(ForkSeason), "monsoon", false));

            Assert.True(Enum.TryParse(typeof(ForkSeason), "Monsoon", out object? parsed));
            Assert.Equal(monsoon, parsed);
            Assert.False(Enum.TryParse(typeof(ForkSeason), "Drought", out object? _));
        });
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void FormatNamesAnAddedMemberOnlyForTheNameFormats() {
        ForkSeason monsoon = Added.Value;

        WithCoreLibPatches(() => {
            Assert.Equal("Monsoon", Enum.Format(typeof(ForkSeason), monsoon, "G"));
            Assert.Equal("Monsoon", Enum.Format(typeof(ForkSeason), monsoon, "f"));
            Assert.Equal(((int)monsoon).ToString(System.Globalization.CultureInfo.InvariantCulture), Enum.Format(typeof(ForkSeason), monsoon, "D"));
            Assert.Equal("Summer", Enum.Format(typeof(ForkSeason), ForkSeason.Summer, "G"));
            Assert.Equal("Friday", Enum.Format(typeof(DayOfWeek), DayOfWeek.Friday, "G"));
        });
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void AnEnumNoDeclarationExtendsReadsTheSameWithThePatchesIn() {
        _ = Added.Value;

        WithCoreLibPatches(() => {
            Assert.Equal(7, Enum.GetValues(typeof(DayOfWeek)).Length);
            Assert.Equal(7, Enum.GetNames(typeof(DayOfWeek)).Length);
            Assert.Equal("Monday", Enum.GetName(typeof(DayOfWeek), DayOfWeek.Monday));
            Assert.True(Enum.IsDefined(typeof(DayOfWeek), DayOfWeek.Monday));
            Assert.Equal(DayOfWeek.Monday, Enum.Parse(typeof(DayOfWeek), "Monday"));
            Assert.Equal(DayOfWeek.Monday, Enum.Parse(typeof(DayOfWeek), "monday", true));
            Assert.True(Enum.TryParse(typeof(DayOfWeek), "Monday", out object? _));
        });
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void UninstallTakesTheAddedMemberBackOutOfTheListings() {
        _ = Added.Value;

        CoreLibEnumPatches.Install();
        CoreLibEnumPatches.Uninstall();

        Assert.Equal(new[] { "Spring", "Summer" }, Enum.GetNames(typeof(ForkSeason)));
    }

    [Fact(Skip = NoPipeline, SkipWhen = nameof(Unavailable))]
    public void RegisteredConsumersReportTheAddedMember() {
        ForkSeason monsoon = Added.Value;
        MethodInfo[] targets = [.. new[] {
            nameof(SeasonUtility.All), nameof(SeasonUtility.Listed), nameof(SeasonUtility.Named),
            nameof(SeasonUtility.Label), nameof(SeasonUtility.Known),
        }.Select(name => typeof(SeasonUtility).GetMethod(name)!)];

        try {
            EnumConsumers.For(typeof(ForkSeason))
                .Values(typeof(SeasonUtility), nameof(SeasonUtility.All))
                .Values(typeof(SeasonUtility), nameof(SeasonUtility.Listed))
                .Parse(typeof(SeasonUtility), nameof(SeasonUtility.Named))
                .Display(typeof(SeasonUtility), nameof(SeasonUtility.Label))
                .IsDefined(typeof(SeasonUtility), nameof(SeasonUtility.Known));

            Assert.Equal(new[] { ForkSeason.Spring, ForkSeason.Summer, monsoon }, SeasonUtility.All());
            Assert.Equal(new[] { ForkSeason.Spring, ForkSeason.Summer, monsoon }, SeasonUtility.Listed());
            Assert.Equal(monsoon, SeasonUtility.Named("Monsoon"));
            Assert.Equal(ForkSeason.Summer, SeasonUtility.Named("Summer"));
            Assert.True(SeasonUtility.Known(monsoon));
            Assert.False(SeasonUtility.Known((ForkSeason)99));
            Assert.Equal("Monsoon", SeasonUtility.Label(monsoon));
            Assert.Equal("season 1", SeasonUtility.Label(ForkSeason.Summer));
        } finally {
            foreach (MethodInfo target in targets) {
                ForkRuntime.Restore(target);
            }
        }
    }

    private static void WithCoreLibPatches(Action check) {
        CoreLibEnumPatches.Install();
        try {
            check();
        } finally {
            CoreLibEnumPatches.Uninstall();
        }
    }

    private sealed class MemoryEnumStore : IEnumValueStore {
        private Dictionary<string, long> saved = new Dictionary<string, long>(StringComparer.Ordinal);

        public bool TryLoad(out IReadOnlyDictionary<string, long> map) {
            map = new Dictionary<string, long>(saved, StringComparer.Ordinal);
            return true;
        }

        public void Save(IReadOnlyDictionary<string, long> map) {
            saved = new Dictionary<string, long>(map, StringComparer.Ordinal);
        }
    }
}
