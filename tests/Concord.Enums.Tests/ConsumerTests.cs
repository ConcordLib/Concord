using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Backend;
using Concord.Emit;
using Concord.Tests.Shared;
using Xunit;

namespace Concord.Enums.Tests;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2263", Justification = "CoreLibEnumPatches only detours the Type-taking overloads, so the generic ones would not exercise the detour.")]
public static class WeatherUtility {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static WeatherKind[] All() => [WeatherKind.Clear, WeatherKind.Rain, WeatherKind.Storm];

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static WeatherKind Parse(string name) => (WeatherKind)Enum.Parse(typeof(WeatherKind), name);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string Display(WeatherKind kind) => kind.ToString();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool IsValid(WeatherKind kind) => Enum.IsDefined(typeof(WeatherKind), kind);

    public static int WrongShape(int unrelated) => unrelated;
}

public abstract class ConsumerWeather : ExtendedEnum<WeatherKind> {
    public static WeatherKind Frozen;
}

[Collection(RegistryCollection.Name)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2263", Justification = "CoreLibEnumPatches only detours the Type-taking overloads, so the generic ones would not exercise the detour.")]
public sealed class ConsumerTests {
    [Fact]
    public void EachConsumerShape_PatchesItsMethod_AtTheDeclaredPosition() {
        ApplyMembers();
        RecordingDetourBackend recorder = new RecordingDetourBackend();
        PatchBackend.Current = recorder;

        EnumConsumers.For(typeof(WeatherKind))
            .Values(typeof(WeatherUtility), nameof(WeatherUtility.All))
            .Parse(typeof(WeatherUtility), nameof(WeatherUtility.Parse))
            .Display(typeof(WeatherUtility), nameof(WeatherUtility.Display))
            .IsDefined(typeof(WeatherUtility), nameof(WeatherUtility.IsValid));

        List<(MethodBase Target, Type At)> expected = new List<(MethodBase Target, Type At)> {
            (Utility(nameof(WeatherUtility.All)), typeof(InjectAt.Tail)),
            (Utility(nameof(WeatherUtility.Parse)), typeof(InjectAt.Head)),
            (Utility(nameof(WeatherUtility.Display)), typeof(InjectAt.Head)),
            (Utility(nameof(WeatherUtility.IsValid)), typeof(InjectAt.Head)),
        };

        List<(MethodBase Target, Type At)> actual = new List<(MethodBase Target, Type At)>();
        foreach ((MethodBase Target, IReadOnlyList<Injection> Added) entry in recorder.ComposedDetours) {
            actual.Add((entry.Target, Assert.Single(entry.Added).At.GetType()));
        }

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void WrongShape_ThrowsConc137() {
        ApplyMembers();

        ConcordEnumException ex = Assert.Throws<ConcordEnumException>(
            () => EnumConsumers.For(typeof(WeatherKind)).Values(typeof(WeatherUtility), nameof(WeatherUtility.WrongShape)));

        Assert.Contains("CONC137", ex.ToString(), StringComparison.Ordinal);
    }

    private static MethodBase Utility(string name) {
        return typeof(WeatherUtility).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;
    }

    private static void ApplyMembers() {
        ExtendedEnumRegistry.ResetForTests();
        ExtendedEnumRegistry.UseStore(new FakeStore());
        ExtendedEnumRegistry.ApplyTo(ExtendedEnumRegistry.ReadDeclaration(typeof(ConsumerWeather)));
    }
}
