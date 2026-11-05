using System;
using System.Collections.Generic;
using System.Reflection;
using Concord.Detour;
using Concord.Emit;
using Xunit;

namespace Concord.Orchestration.Tests;

[Collection(SharedAssemblyApplyCollection.Name)]
public class ApplyInjectionsRollbackTests {
    private sealed class FailSecondBackend : IDetourBackend {
        public int Applied;
        public int Disposed;

        public IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added) {
            Applied++;
            if (Applied == 2) {
                throw new InvalidOperationException("second injection fails");
            }

            return new CountingHandle(this, target);
        }

        public IReadOnlyList<string> OwnersOf(MethodBase target) {
            return [];
        }

        public int PatchedTargetCount => 0;

        private sealed class CountingHandle(FailSecondBackend owner, MethodBase original) : IDetourHandle {
            public MethodBase Original { get; } = original;

            public bool IsApplied => true;

            public void Dispose() {
                owner.Disposed++;
            }
        }
    }

    public static int TargetA(int x) {
        return x;
    }

    private static void Head() {
    }

    [Fact]
    public void SecondFailureDisposesFirstHandle() {
        FailSecondBackend backend = new FailSecondBackend();
        DetourBackend.Current = backend;
        try {
            MethodBase target = typeof(ApplyInjectionsRollbackTests).GetMethod(nameof(TargetA))!;
            MethodBase head = typeof(ApplyInjectionsRollbackTests).GetMethod(nameof(Head), BindingFlags.Static | BindingFlags.NonPublic)!;
            Injection first = new Injection(head, new InjectAt.Head(), "test", 0);
            Injection second = new Injection(head, new InjectAt.Tail(), "test", 0);
            Assert.Throws<InvalidOperationException>(() => Patcher.ApplyInjections([(target, first), (target, second)]));
            Assert.Equal(1, backend.Disposed);
        } finally {
            DetourBackend.Reset();
        }
    }
}
