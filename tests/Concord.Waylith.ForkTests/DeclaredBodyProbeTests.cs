using Xunit;

namespace Concord.Waylith.ForkTests;

public sealed class DeclaredBodyProbeTests {
    private const string NoFork = "No host body read here, so nothing reads a declared body back.";

    public static bool Unavailable => ForkRuntime.Unavailable;

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void TheRuntimeUnderTheseFactsReadsItsOwnDeclaredBodyBack() {
        Assert.True(
            AdapterEntryPoint.ConcordDeclaredBodyReads(),
            "the probe reports false on a runtime this project's own facts depend on binding, so the install " +
            "would refuse the backend here with no composed body ever staged.");
    }
}
