using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Concord.Backend;
using Concord.MonoMod;
using Concord.MonoMod.Harmony;
using Xunit;

namespace Concord.MonoMod.Tests
{
    [Collection("HarmonySerial")]
    public sealed class LockScopeTests
    {
        [Fact]
        public void For_TheLoadedHarmony_HoldsThatCopysLocker()
        {
            Assembly loaded = typeof(HarmonyLib.Harmony).Assembly;
            object locker = Locker(loaded);

            Assert.False(Monitor.IsEntered(locker));

            IDisposable? scope = HarmonyLockScope.For(loaded);
            Assert.NotNull(scope);

            using (scope)
            {
                Assert.True(Monitor.IsEntered(locker));
            }

            Assert.False(Monitor.IsEntered(locker));
        }

        [Fact]
        public void For_ASecondCopy_HoldsItsOwnLocker_AndLeavesTheCompileTimeOneFree()
        {
            Assembly compileTime = typeof(HarmonyLib.Harmony).Assembly;
            Assembly second = SecondCopy();

            object secondLocker = Locker(second);
            object compileTimeLocker = Locker(compileTime);
            Assert.NotSame(compileTimeLocker, secondLocker);

            IDisposable? scope = HarmonyLockScope.For(second);
            Assert.NotNull(scope);

            using (scope)
            {
                Assert.True(Monitor.IsEntered(secondLocker));
                Assert.False(Monitor.IsEntered(compileTimeLocker));
            }

            Assert.False(Monitor.IsEntered(secondLocker));
        }

        [Fact]
        public void For_AnAssemblyWithNoPatchProcessor_ReturnsNull()
        {
            Assert.Null(HarmonyLockScope.For(typeof(LockScopeTests).Assembly));
        }

        [Fact]
        public void EnterAllArmed_WithTheLoadedHarmonyArmed_HoldsItsLocker()
        {
            List<string> log = new List<string>();
            Assembly compileTime = typeof(HarmonyLib.Harmony).Assembly;
            object locker = Locker(compileTime);

            try
            {
                MonoModEntryPoint.Install(log.Add, null);
                Assert.True(UpdateWrapperHook.IsHookedOn(compileTime), string.Join(" | ", log));
                Assert.False(Monitor.IsEntered(locker));

                using (UpdateWrapperHook.EnterAllArmed())
                {
                    Assert.True(Monitor.IsEntered(locker));
                }

                Assert.False(Monitor.IsEntered(locker));
            }
            finally
            {
                MonoModEntryPoint.ResetForTests();
            }
        }

        [Fact]
        public void EnterAllArmed_WithASecondCopyArmed_HoldsBothLockers()
        {
            List<string> log = new List<string>();
            Assembly compileTime = typeof(HarmonyLib.Harmony).Assembly;

            try
            {
                MonoModEntryPoint.Install(log.Add, null);

                Assembly second = SecondCopy();
                Assert.True(
                    UpdateWrapperHook.IsHookedOn(second),
                    "the second Harmony copy did not arm, so both lockers were never in one scope: " + string.Join(" | ", log));

                object compileTimeLocker = Locker(compileTime);
                object secondLocker = Locker(second);

                using (UpdateWrapperHook.EnterAllArmed())
                {
                    Assert.True(Monitor.IsEntered(compileTimeLocker));
                    Assert.True(Monitor.IsEntered(secondLocker));
                }

                Assert.False(Monitor.IsEntered(compileTimeLocker));
                Assert.False(Monitor.IsEntered(secondLocker));
            }
            finally
            {
                MonoModEntryPoint.ResetForTests();
            }
        }

        private static Assembly SecondCopy()
        {
            byte[] image = File.ReadAllBytes(typeof(HarmonyLib.Harmony).Assembly.Location);
            Assembly second = Assembly.Load(AssemblyImage.WithFreshModuleId(image));
            Assert.NotSame(typeof(HarmonyLib.Harmony).Assembly, second);
            return second;
        }

        private static object Locker(Assembly copy)
        {
            Type patchProcessor = copy.GetType("HarmonyLib.PatchProcessor")
                                  ?? throw new InvalidOperationException("HarmonyLib.PatchProcessor not found in " + copy.FullName);
            FieldInfo field = patchProcessor.GetField("locker", BindingFlags.NonPublic | BindingFlags.Static)
                              ?? throw new InvalidOperationException("HarmonyLib.PatchProcessor.locker not found in " + copy.FullName);

            return field.GetValue(null) ?? throw new InvalidOperationException("HarmonyLib.PatchProcessor.locker is null in " + copy.FullName);
        }
    }
}
