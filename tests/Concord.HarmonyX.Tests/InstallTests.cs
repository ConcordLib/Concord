using System.Reflection;
using Concord.Backend;
using Xunit;

namespace Concord.HarmonyX.Tests;

public abstract class AbstractVictim {
    public abstract int Unimplemented();
}

public sealed class InstallTests {
    [Fact]
    public void InstallReportsItTookAndNamesTheBackend() {
        HarmonyXInstallResult result = HarmonyXEntryPoint.Install(null, null);

        Assert.True(result.Installed, result.Message);
        Assert.Equal(HarmonyXBackend.BackendName, PatchBackend.Current.Name);
        Assert.Equal("Concord.HarmonyX", PatchBackend.Current.Name);
    }

    [Fact]
    public void JudgeRefusesAnAbstractMethodWithACode() {
        HarmonyXEntryPoint.Install(null, null);
        MethodInfo target = typeof(AbstractVictim).GetMethod(nameof(AbstractVictim.Unimplemented))!;

        TargetVerdict verdict = PatchBackend.Current.Judge(target);

        Assert.Equal(TargetHandling.Refuse, verdict.Handling);
        Assert.Equal("CONC184", verdict.Code);
        Assert.Contains("abstract", verdict.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void JudgeAcceptsAnOrdinaryMethod() {
        HarmonyXEntryPoint.Install(null, null);
        MethodInfo target = typeof(InstallTests).GetMethod(nameof(JudgeAcceptsAnOrdinaryMethod))!;

        Assert.Equal(TargetHandling.Accept, PatchBackend.Current.Judge(target).Handling);
    }
}
