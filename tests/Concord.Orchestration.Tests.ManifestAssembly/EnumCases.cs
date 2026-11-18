namespace Concord.Orchestration.Tests.ManifestAssembly;

public enum ManifestSeason {
    Spring = 0,
    Summer = 1,
}

public abstract class ManifestSeasons : ExtendedEnum<ManifestSeason> {
    public const string MonsoonId = "manifest.season.monsoon";

    public static ManifestSeason Monsoon;

    public static ManifestSeason Harvest;
}
