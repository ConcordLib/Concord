using Concord.AttachedData;
using Xunit;

namespace Concord.Orchestration.Tests;

[Collection("PatcherStatics")]
public sealed class PropertyRegistryTests : IDisposable {
    public PropertyRegistryTests() {
        Patcher.ForgetAttachedPropertyRegistries();
    }

    public void Dispose() {
        Patcher.ForgetAttachedPropertyRegistries();
        Patcher.UseLog(null);
    }

    [Fact]
    public void Register_BeforeAnyRegistry_IsDeliveredWhenOneArrives() {
        PropertyRegistry.Register(typeof(FirstDeclaration), typeof(SomeBase), "early", typeof(int), Slot("early"));
        RecordingRegistry host = new RecordingRegistry();

        Patcher.UseAttachedPropertyRegistry(host);

        Assert.Equal("early", Assert.Single(host.Names));
    }

    [Fact]
    public void SameRegistryTwice_ReceivesEachEntryOnce() {
        PropertyRegistry.Register(typeof(FirstDeclaration), typeof(SomeBase), "once", typeof(int), Slot("once"));
        RecordingRegistry host = new RecordingRegistry();

        Patcher.UseAttachedPropertyRegistry(host);
        Patcher.UseAttachedPropertyRegistry(host);

        Assert.Equal("once", Assert.Single(host.Names));
    }

    [Fact]
    public void Register_AfterARegistryArrives_IsDeliveredAtOnce() {
        RecordingRegistry host = new RecordingRegistry();
        Patcher.UseAttachedPropertyRegistry(host);

        PropertyRegistry.Register(typeof(FirstDeclaration), typeof(SomeBase), "late", typeof(int), Slot("late"));

        Assert.Equal("late", Assert.Single(host.Names));
    }

    [Fact]
    public void Register_BadKey_LogsTheDeclaringAssembly() {
        List<string> logs = [];
        Patcher.UseLog(logs.Add);

        PropertyRegistry.Register(typeof(FirstDeclaration), typeof(SomeBase), "not a key", typeof(int), Slot("spaced"));
        RecordingRegistry host = new RecordingRegistry();
        Patcher.UseAttachedPropertyRegistry(host);

        Assert.Empty(host.Names);
        Assert.Single(logs);
        Assert.Contains(typeof(FirstDeclaration).Assembly.GetName().Name!, logs[0]);
        Assert.Contains("not a key", logs[0]);
    }

    [Fact]
    public void Register_BadValueType_IsRefusedAndLogged() {
        List<string> logs = [];
        Patcher.UseLog(logs.Add);

        PropertyRegistry.Register(typeof(FirstDeclaration), typeof(SomeBase), "empty", typeof(void), Slot("empty"));
        RecordingRegistry host = new RecordingRegistry();
        Patcher.UseAttachedPropertyRegistry(host);

        Assert.Empty(host.Names);
        Assert.Single(logs);
        Assert.Contains("System.Void", logs[0]);
    }

    [Fact]
    public void Register_HostThrows_LogsAndKeepsForwarding() {
        List<string> logs = [];
        Patcher.UseLog(logs.Add);
        Patcher.UseAttachedPropertyRegistry(new ThrowingRegistry());

        PropertyRegistry.Register(typeof(FirstDeclaration), typeof(SomeBase), "broken", typeof(int), Slot("broken"));
        PropertyRegistry.Register(typeof(SecondDeclaration), typeof(SomeBase), "after", typeof(int), Slot("after"));

        Assert.Equal(2, logs.Count);
        Assert.Contains("broken", logs[0]);
        Assert.Contains("after", logs[1]);
    }

    private static IAttachedSlot Slot(string key) {
        return AttachedStorage.SlotAt(AttachedStorage.SlotFor(typeof(SomeBase), key, typeof(int)));
    }
}
