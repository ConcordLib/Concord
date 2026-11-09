using System.Collections.Generic;
using Concord.Elin;
using Xunit;

namespace Concord.Elin.Tests;

[Collection("RegistrySerial")]
public class SavedFieldTests {
    private sealed class Holder {
    }

    private sealed class LateHolder {
    }

    private static ElinAttachedPropertyRegistry Wire(List<string> errors) {
        ElinAttachedPropertyRegistry registry = new ElinAttachedPropertyRegistry(_ => { }, _ => { }, errors.Add);
        ElinRuntime.Registry = registry;
        Patcher.UseAttachedPropertyRegistry(registry);
        return registry;
    }

    [Fact]
    public void Register_BeforeWire_IsReplayedIntoTheRegistry() {
        SavedField<Holder, int> field = SavedField<Holder, int>.Register("pending", typeof(SavedFieldTests));
        Holder holder = new Holder();
        field.Set(holder, 7);

        ElinAttachedPropertyRegistry registry = Wire([]);

        IReadOnlyList<ElinPropertyEntry> entries = registry.ForBaseType(typeof(Holder));
        Assert.Contains(entries, entry => entry.Label == "concord.Concord.Elin.Tests.pending");
        Assert.Equal(7, field.Get(holder));
    }

    [Fact]
    public void Register_AfterWire_ArrivesStraightAway() {
        ElinAttachedPropertyRegistry registry = Wire([]);

        SavedField<LateHolder, int>.Register("late", typeof(SavedFieldTests));

        Assert.Contains(registry.ForBaseType(typeof(LateHolder)), entry => entry.Label == "concord.Concord.Elin.Tests.late");
    }

    [Fact]
    public void Register_RefusedTypeLogsInsteadOfThrowing() {
        List<string> errors = [];
        ElinAttachedPropertyRegistry registry = Wire(errors);

        SavedField<Zone, string> field = SavedField<Zone, string>.Register("refusedname", typeof(SavedFieldTests));

        Assert.NotNull(field);
        Assert.Empty(registry.ForBaseType(typeof(Zone)));
        Assert.Contains(errors, m => m.Contains("refusedname"));
    }

    [Fact]
    public void Register_ThenGetSetRoundTripsWithoutARegistry() {
        SavedField<Holder, int> field = SavedField<Holder, int>.Register("count", typeof(SavedFieldTests));
        Holder holder = new Holder();

        field.Set(holder, 3);

        Assert.Equal(3, field.Get(holder));
    }
}

[CollectionDefinition("RegistrySerial", DisableParallelization = true)]
public sealed class RegistrySerialCollection {
}
