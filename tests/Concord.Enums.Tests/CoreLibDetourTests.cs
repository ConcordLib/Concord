using System.Reflection;
using Concord.Backend;
using Concord.Emit;
using Concord.Tests.Shared;
using Xunit;

namespace Concord.Enums.Tests;

[Collection(RegistryCollection.Name)]
public sealed class CoreLibDetourTests {
    [Fact]
    public void Install_DetoursTheWholeEnumApiSet() {
        RecordingDetourBackend recorder = Record();

        try {
            CoreLibEnumPatches.Install();

            Assert.Equal(Expected(includeFormat: true), Describe(recorder));
        } finally {
            CoreLibEnumPatches.Uninstall();
        }
    }

    [Fact]
    public void Install_WithoutFormat_LeavesFormatAlone() {
        RecordingDetourBackend recorder = Record();

        try {
            CoreLibEnumPatches.Install(includeFormat: false);

            Assert.Equal(Expected(includeFormat: false), Describe(recorder));
        } finally {
            CoreLibEnumPatches.Uninstall();
        }
    }

    [Fact]
    public void Install_Twice_DetoursNothingTheSecondTime() {
        RecordingDetourBackend recorder = Record();

        try {
            CoreLibEnumPatches.Install();
            int afterFirst = recorder.ComposedDetours.Count;

            CoreLibEnumPatches.Install();

            Assert.Equal(afterFirst, recorder.ComposedDetours.Count);
        } finally {
            CoreLibEnumPatches.Uninstall();
        }
    }

    [Fact]
    public void Uninstall_ReleasesEveryHandle_AndLetsInstallRunAgain() {
        RecordingDetourBackend recorder = Record();
        CoreLibEnumPatches.Install();
        int installed = recorder.ComposedDetours.Count;

        CoreLibEnumPatches.Uninstall();

        Assert.NotEmpty(recorder.Handles);
        Assert.All(recorder.Handles, handle => Assert.False(handle.IsApplied));

        try {
            CoreLibEnumPatches.Install();

            Assert.Equal(installed * 2, recorder.ComposedDetours.Count);
        } finally {
            CoreLibEnumPatches.Uninstall();
        }
    }

    private static RecordingDetourBackend Record() {
        RecordingDetourBackend recorder = new RecordingDetourBackend();
        PatchBackend.Current = recorder;
        return recorder;
    }

    private static List<(MethodBase Target, Type At)> Describe(RecordingDetourBackend recorder) {
        List<(MethodBase Target, Type At)> actual = new List<(MethodBase Target, Type At)>();
        foreach ((MethodBase Target, IReadOnlyList<Injection> Added) entry in recorder.ComposedDetours) {
            actual.Add((entry.Target, Assert.Single(entry.Added).At.GetType()));
        }

        return actual;
    }

    private static List<(MethodBase Target, Type At)> Expected(bool includeFormat) {
        List<(MethodBase Target, Type At)> expected = new List<(MethodBase Target, Type At)> {
            (EnumMethod("GetValues", typeof(Type)), typeof(InjectAt.Tail)),
            (EnumMethod("GetNames", typeof(Type)), typeof(InjectAt.Tail)),
            (EnumMethod("GetName", typeof(Type), typeof(object)), typeof(InjectAt.Head)),
            (EnumMethod("IsDefined", typeof(Type), typeof(object)), typeof(InjectAt.Head)),
            (EnumMethod("Parse", typeof(Type), typeof(string)), typeof(InjectAt.Head)),
            (EnumMethod("Parse", typeof(Type), typeof(string), typeof(bool)), typeof(InjectAt.Head)),
            (EnumMethod("TryParse", typeof(Type), typeof(string), typeof(object).MakeByRefType()), typeof(InjectAt.Head)),
        };

        if (includeFormat) {
            expected.Add((EnumMethod("Format", typeof(Type), typeof(object), typeof(string)), typeof(InjectAt.Head)));
        }

        return expected;
    }

    private static MethodBase EnumMethod(string name, params Type[] parameterTypes) {
        return typeof(Enum).GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, parameterTypes, null)!;
    }
}
