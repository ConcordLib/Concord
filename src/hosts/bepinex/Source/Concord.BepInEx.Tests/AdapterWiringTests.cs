using System.Collections.Generic;
using Concord.Backend;
using Concord.BepInEx;
using Xunit;

namespace Concord.BepInEx.Tests;

[Collection("AdapterWiringSerial")]
public class AdapterWiringTests {
    private static WireContext NewContext(List<string> log, System.Func<bool> installBackend) {
        return new WireContext {
            InstallBackend = installBackend,
            Log = log.Add,
            LogError = log.Add
        };
    }

    [Fact]
    public void Wire_InstallsTheBackendItWasHandedAndReportsWired() {
        FakePatchBackend fake = new FakePatchBackend();
        List<string> log = new List<string>();

        try {
            Adapter.Wire(NewContext(log, () => {
                PatchBackend.Current = fake;
                return true;
            }));

            Assert.True(Adapter.Wired);
            Assert.Same(fake, PatchBackend.Current);
        } finally {
            Adapter.ResetForTests();
        }
    }

    [Fact]
    public void Wire_StaysUnwiredWhenTheBackendRefusesToInstall() {
        List<string> log = new List<string>();

        try {
            Adapter.Wire(NewContext(log, () => false));

            Assert.False(Adapter.Wired);
        } finally {
            Adapter.ResetForTests();
        }
    }

    [Fact]
    public void Wire_SecondCallDoesNotInstallAgain() {
        FakePatchBackend fake = new FakePatchBackend();
        int installs = 0;

        try {
            Adapter.Wire(NewContext(new List<string>(), () => {
                installs++;
                PatchBackend.Current = fake;
                return true;
            }));
            Adapter.Wire(NewContext(new List<string>(), () => {
                installs++;
                PatchBackend.Current = new FakePatchBackend();
                return true;
            }));

            Assert.Equal(1, installs);
            Assert.Same(fake, PatchBackend.Current);
        } finally {
            Adapter.ResetForTests();
        }
    }

    [Fact]
    public void Wire_RoutesConcordReportsToTheHostLog() {
        List<string> log = new List<string>();

        try {
            Adapter.Wire(NewContext(log, () => {
                PatchBackend.Current = new FakePatchBackend();
                return true;
            }));

            Patcher.WriteLog("a patch report");

            Assert.Contains("a patch report", log);
        } finally {
            Adapter.ResetForTests();
            Patcher.UseLog(null);
        }
    }
}

[CollectionDefinition("AdapterWiringSerial", DisableParallelization = true)]
public sealed class AdapterWiringSerialCollection {
}
