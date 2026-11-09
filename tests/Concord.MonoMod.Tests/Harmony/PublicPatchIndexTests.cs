using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Backend;
using Concord.MonoMod;
using Concord.MonoMod.Harmony;
using HarmonyLib;
using Xunit;

namespace Concord.MonoMod.Tests
{
    [Collection("HarmonySerial")]
    public sealed class PublicPatchIndexTests
    {
        private const string ModId = "test.publicpatchindex.mod";

        [Fact]
        public void HasPatch_OnAnUnpatchedTarget_ResolvesNothingNewOnTheSecondCall()
        {
            MethodInfo target = Method(typeof(IndexTargets), nameof(IndexTargets.Unpatched));

            Assert.False(UpdateWrapperHook.HasPatch(target));
            int afterFirst = UpdateWrapperHook.PublicPatchIndexForTests().Count;

            Assert.False(UpdateWrapperHook.HasPatch(target));
            int afterSecond = UpdateWrapperHook.PublicPatchIndexForTests().Count;

            Assert.True(afterFirst >= 1, "the first HasPatch indexed no assembly at all, so this measured nothing");
            Assert.Equal(afterFirst, afterSecond);
        }

        [Fact]
        public void AnAssemblyLoadedAfterTheInstall_GetsANullEntry_AndTheCompileTimePatchIsStillSeen()
        {
            List<string> log = new List<string>();
            MethodInfo target = Method(typeof(IndexTargets), nameof(IndexTargets.Patched));
            Assert.Null(PatchProcessor.GetPatchInfo(target));

            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(ModId);

            try
            {
                MonoModEntryPoint.Install(log.Add, null);

                Assembly notHarmony = Assembly.Load(AssemblyImage.WithFreshModuleId(
                    File.ReadAllBytes(typeof(UpdateWrapperHook).Assembly.Location)));

                IReadOnlyDictionary<Assembly, MethodInfo?> index = UpdateWrapperHook.PublicPatchIndexForTests();
                Assert.True(
                    index.ContainsKey(notHarmony),
                    "the assembly that loaded after the install never reached the public patch index");
                Assert.Null(index[notHarmony]);

                harmony.Patch(target, postfix: new HarmonyMethod(Method(typeof(IndexMods), nameof(IndexMods.Postfix))));
                Assert.True(UpdateWrapperHook.HasPatch(target));
            }
            finally
            {
                TestUnpatch.Own(harmony, ModId);
                MonoModEntryPoint.ResetForTests();
            }
        }

        private static MethodInfo Method(Type type, string name)
        {
            return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
        }

        public static class IndexTargets
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Unpatched()
            {
                return 11;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Patched()
            {
                return 12;
            }
        }

        public static class IndexMods
        {
            public static void Postfix()
            {
            }
        }
    }
}
