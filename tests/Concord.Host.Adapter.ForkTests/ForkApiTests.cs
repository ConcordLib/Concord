using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public static class ManyClausesVictim {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Sink() {
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static void Victim() {
        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }

        try {
            Sink();
        } catch (Exception) {
            Sink();
        }
    }
}

public sealed class ForkApiTests {
    private const string NoFork = "No body-replacement runtime here, so there is no fork api to check.";

    private static readonly MethodInfo Victim = typeof(ManyClausesVictim).GetMethod(
        "Victim",
        BindingFlags.NonPublic | BindingFlags.Static)!;

    public static bool Unavailable => ForkRuntime.Unavailable;

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void MissingStageMemberFindsEveryNeededMemberOnTheEarlyBuild() {
        Assert.Null(ForkApi.MissingStageMember());
    }

    [Fact(Skip = NoFork, SkipWhen = nameof(Unavailable))]
    public void ReadingAVictimPastTheScratchBufferReportsTheRealClauseCountTwice() {
        int expected = Victim.GetMethodBody()!.ExceptionHandlingClauses.Count;
        Assert.True(
            expected > 64,
            $"the victim method declares {expected} clauses, which is not past the 64-entry scratch buffer " +
            "the read starts from, so the overflow retry this fact measures never runs.");

        HostBody? first = ForkBodyReader.Shared.Read(Victim);
        Assert.True(first is not null, "the host refused to read a body with more clauses than the scratch buffer holds.");
        Assert.Equal(expected, first!.Clauses.Count);

        HostBody? second = ForkBodyReader.Shared.Read(Victim);
        Assert.True(second is not null, "a second read of the same victim refused.");
        Assert.Equal(expected, second!.Clauses.Count);
    }
}
