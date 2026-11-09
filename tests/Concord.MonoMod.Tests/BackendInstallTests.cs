using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Backend;
using Concord.Emit;
using Concord.MonoMod;
using Concord.MonoMod.Harmony;
using Xunit;

namespace Concord.MonoMod.Tests;

public class BackendInstallTests {
    [Fact]
    public void Install_SetsTheMonoModBackendAsCurrent() {
        MonoModEntryPoint.ResetForTests();
        List<string> log = [];

        try {
            Selection chosen = MonoModEntryPoint.Install(log.Add, null);

            Assert.True(chosen.Installed, string.Join("\n", log));
            Assert.Equal("Concord.MonoMod", PatchBackend.Current.Name);
        } finally {
            MonoModEntryPoint.ResetForTests();
        }
    }

    [Fact]
    public void ApplyComposed_RunsAHeadInjectionAndOwnsTheBody() {
        MonoModEntryPoint.ResetForTests();
        List<string> log = [];
        try {
            MonoModEntryPoint.Install(log.Add, null);
            Probe.Calls = 0;

            MethodInfo target = typeof(Probe).GetMethod(nameof(Probe.Run))!;
            MethodInfo head = typeof(Probe).GetMethod(nameof(Probe.Count))!;
            Injection injection = new Injection(head, new InjectAt.Head(), "test", 0);

            using IBackendHandle handle = PatchBackend.Current.ApplyComposed(target, [injection]);

            Probe.Run();
            Assert.Equal(1, Probe.Calls);
            Assert.True(PatchBackend.Current.OwnsBody(target), string.Join("\n", log));
        } finally {
            MonoModEntryPoint.ResetForTests();
        }
    }

    public static class Probe {
        public static int Calls { get; set; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Run() {
            return Calls;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Count() {
            Calls++;
        }
    }
}
