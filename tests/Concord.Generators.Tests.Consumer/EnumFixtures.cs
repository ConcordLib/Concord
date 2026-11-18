namespace Concord.Generators.Tests.Consumer;

public enum Weather {
    Clear = 0,
    Rain = 1,
}

/// <summary>Proves [EnumMember] next to a member with the default id.</summary>
[Patch]
public abstract class ConsumerWeathers : ExtendedEnum<Weather> {
    public const string FrostId = "consumer.weather.frost";

    [EnumMember(FrostId)]
    public static Weather Frost;

    /// <summary>Saved under the type name plus <c>.Hail</c>.</summary>
    public static Weather Hail;
}
