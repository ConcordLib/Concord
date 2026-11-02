using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace Concord.Emit.Tests;

public sealed class DeclaredBodySeamTests : IDisposable {
    private static readonly MethodInfo Target =
        typeof(DeclaredBodySeamTests).GetMethod(nameof(ReturnsOne), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly Func<MethodBase, ComposedBody?>? reader = OriginalBody.HostOriginalBody;

    public void Dispose() {
        OriginalBody.HostOriginalBody = reader;
    }

    [Fact]
    public void WithNoHostReaderACloneStillRunsTheBodyReflectionReports() {
        OriginalBody.HostOriginalBody = null;

        Assert.Equal(1, Invoke(OriginalBody.Clone(Target)));
    }

    [Fact]
    public void AHostReaderSuppliesTheBodyTheCloneRuns() {
        OriginalBody.HostOriginalBody = _ => Returning(7);

        Assert.Equal(7, Invoke(OriginalBody.Clone(Target)));
    }

    [Fact]
    public void AHostReaderThatRefusesAbortsTheCloneRatherThanFallingBackToReflection() {
        OriginalBody.HostOriginalBody = _ => throw new ConcordEmitException("CONC178", "the host would not read it back");

        ConcordEmitException refused = Assert.Throws<ConcordEmitException>(() => OriginalBody.Clone(Target));

        Assert.Equal("CONC178", refused.Code);
    }

    [Fact]
    public void AHostReaderIsAskedForEveryReadRatherThanOnceAndCached() {
        int asked = 0;
        OriginalBody.HostOriginalBody = _ => {
            asked++;
            return Returning(7);
        };

        OriginalBody.Clone(Target);
        OriginalBody.Clone(Target);

        Assert.Equal(2, asked);
    }

    [Fact]
    public void ACloneBuiltFromASuppliedBodyStillReportsTheTargetsOwnAssembly() {
        MethodInfo target = typeof(DeclaredBodySeamTests)
            .GetMethod(nameof(ReportsItsAssembly), BindingFlags.NonPublic | BindingFlags.Static)!;
        OriginalBody.HostOriginalBody = _ => new ComposedBody(
            [
                new ComposedInstruction(
                    new CodeInstruction(OpCodes.Call, typeof(Assembly).GetMethod(nameof(Assembly.GetExecutingAssembly))!),
                    0,
                    null),
                new ComposedInstruction(new CodeInstruction(OpCodes.Ret), 5, null),
            ],
            [],
            [],
            false);

        Assembly reported = (Assembly)OriginalBody.Clone(target).Invoke(null, null)!;

        Assert.True(
            ReferenceEquals(typeof(DeclaredBodySeamTests).Assembly, reported),
            $"a clone of '{target.Name}' reported '{reported.GetName().Name}' as its executing assembly instead of " +
            $"'{typeof(DeclaredBodySeamTests).Assembly.GetName().Name}'. A supplied body is written into a dynamic " +
            "method Concord owns, so every GetExecutingAssembly call in it has to be repointed at the target's own " +
            "declaring type the way a copied body's is.");
    }

    private static Assembly ReportsItsAssembly() {
        return Assembly.GetExecutingAssembly();
    }

    private static ComposedBody Returning(int value) {
        return new ComposedBody(
            [
                new ComposedInstruction(new CodeInstruction(OpCodes.Ldc_I4, value), 0, null),
                new ComposedInstruction(new CodeInstruction(OpCodes.Ret), 5, null),
            ],
            [],
            [],
            false);
    }

    private static int Invoke(MethodInfo body) {
        return (int)body.Invoke(null, null)!;
    }

    private static int ReturnsOne() {
        return 1;
    }
}
