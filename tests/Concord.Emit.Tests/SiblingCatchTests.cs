using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public static class SiblingCatchTargets {
    public static string Log = string.Empty;

    public static void Throw() {
        throw new IOException("boom");
    }

    public static int RethrowFromFirstCatch() {
        try {
            Throw();
            return 1;
        } catch (IOException) {
            Log += "logged;";
            throw;
        } catch (Exception) {
            Log += "fallback;";
            return 2;
        }
    }

    public static int ThrowNewFromFirstCatch() {
        try {
            Throw();
            return 1;
        } catch (IOException) {
            Log += "logged;";
            throw new ArgumentException("from the first catch");
        } catch (ArgumentException) {
            Log += "fallback;";
            return 2;
        }
    }
}

public sealed class SiblingCatchTests {
    [Fact]
    public void ARethrowInsideTheFirstCatch_EscapesTheSiblingCatch() {
        MethodBase target = typeof(SiblingCatchTargets).GetMethod(nameof(SiblingCatchTargets.RethrowFromFirstCatch))!;
        ComposeResult result = WrapperComposer.Compose(target, []);
        SiblingCatchTargets.Log = string.Empty;

        TargetInvocationException escaped = Assert.Throws<TargetInvocationException>(() => result.Wrapper.Invoke(null, []));

        Assert.IsType<IOException>(escaped.InnerException);
        Assert.Equal("logged;", SiblingCatchTargets.Log);
    }

    [Fact]
    public void AThrowInsideTheFirstCatch_IsNotCaughtByTheSiblingCatch() {
        MethodBase target = typeof(SiblingCatchTargets).GetMethod(nameof(SiblingCatchTargets.ThrowNewFromFirstCatch))!;
        ComposeResult result = WrapperComposer.Compose(target, []);
        SiblingCatchTargets.Log = string.Empty;

        TargetInvocationException escaped = Assert.Throws<TargetInvocationException>(() => result.Wrapper.Invoke(null, []));

        Assert.IsType<ArgumentException>(escaped.InnerException);
        Assert.Equal("logged;", SiblingCatchTargets.Log);
    }
}
