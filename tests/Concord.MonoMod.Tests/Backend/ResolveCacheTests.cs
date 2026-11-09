using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Concord.Backend;
using Concord.Emit;
using Xunit;

namespace Concord.MonoMod.Tests;

public static class ResolveCacheHead {
    public static int Calls;

    public static void Bump() {
        Calls++;
    }
}

public sealed class ResolveCacheTests {
    private const int Constant = 7;

    [Fact]
    public void ForgetReflectionOnly_FindsMonoModCaches() {
        Assert.True(ResolveCache.ForgetReflectionOnly() >= 0);
    }

    [Fact]
    public void TwoLiveCopiesOfOneName_TheSecondComposeResolvesItsOwnCopy() {
        Twins twins = Twins.Load();
        MethodInfo bump = typeof(ResolveCacheHead).GetMethod(nameof(ResolveCacheHead.Bump))!;
        IBackendHandle? first = null;
        IBackendHandle? second = null;

        try {
            first = Compose(twins.First, bump);
            Assert.Equal(Constant, twins.First.Invoke(null, null));

            ResolveCache.ForgetReflectionOnly();

            ResolveCacheHead.Calls = 0;
            second = Compose(twins.Second, bump);
            Assert.Equal(Constant, twins.Second.Invoke(null, null));
            Assert.Equal(1, ResolveCacheHead.Calls);
        } finally {
            second?.Dispose();
            first?.Dispose();
        }
    }

    [Fact]
    public void ACopyLoadedAfterTheFirstCompose_ResolvesItselfWithNoHandPurge() {
        byte[] image = Twins.Image();
        MethodInfo bump = typeof(ResolveCacheHead).GetMethod(nameof(ResolveCacheHead.Bump))!;
        MethodInfo first = Twins.LoadOne(image);
        IBackendHandle? composedFirst = null;
        IBackendHandle? composedSecond = null;

        try {
            composedFirst = Compose(first, bump);
            Assert.Equal(Constant, first.Invoke(null, null));

            MethodInfo second = Twins.LoadOne(image);
            Assert.NotSame(first.DeclaringType, second.DeclaringType);

            ResolveCacheHead.Calls = 0;
            composedSecond = Compose(second, bump);
            Assert.Equal(Constant, second.Invoke(null, null));
            Assert.Equal(1, ResolveCacheHead.Calls);
        } finally {
            composedSecond?.Dispose();
            composedFirst?.Dispose();
        }
    }

    [Fact]
    public void ForgetReflectionOnly_DropsWhatTheFirstCopyCached() {
        Twins twins = Twins.Load();
        MethodInfo bump = typeof(ResolveCacheHead).GetMethod(nameof(ResolveCacheHead.Bump))!;
        IBackendHandle? composed = null;

        try {
            composed = Compose(twins.First, bump);

            Assert.True(ResolveCache.ForgetReflectionOnly() > 0);
        } finally {
            composed?.Dispose();
        }
    }

    private static IBackendHandle Compose(MethodInfo target, MethodInfo head) {
        return new MonoModBackend().ApplyComposed(target, [new Injection(head, new InjectAt.Head(), "resolve-cache", 0)]);
    }

    private sealed class Twins {
        private Twins(MethodInfo first, MethodInfo second) {
            First = first;
            Second = second;
        }

        internal MethodInfo First { get; }

        internal MethodInfo Second { get; }

        internal static Twins Load() {
            byte[] image = Image();
            MethodInfo one = LoadOne(image);
            MethodInfo two = LoadOne(image);
            Assert.NotSame(one.DeclaringType, two.DeclaringType);

            return new Twins(one, two);
        }

        internal static byte[] Image() {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            return RenameAwayFromTheBuilder(EmitUnderBuilderName("Bldr" + suffix, suffix), "Bldr" + suffix, "Twin" + suffix);
        }

        internal static MethodInfo LoadOne(byte[] image) {
            Assembly copy = Assembly.Load(AssemblyImage.WithFreshModuleId(image));
            return copy.GetType("TwinHolder")!.GetMethod("Probe")!;
        }

        private static byte[] RenameAwayFromTheBuilder(byte[] image, string from, string to) {
            byte[] sought = Encoding.ASCII.GetBytes(from);
            byte[] written = Encoding.ASCII.GetBytes(to);
            int found = 0;
            for (int at = 0; at + sought.Length <= image.Length; at++) {
                bool hit = true;
                for (int index = 0; index < sought.Length; index++) {
                    if (image[at + index] != sought[index]) {
                        hit = false;
                        break;
                    }
                }

                if (!hit) {
                    continue;
                }

                Array.Copy(written, 0, image, at, written.Length);
                found++;
            }

            Assert.True(found > 0, "the emitted image does not carry its own assembly name");
            return image;
        }

        private static byte[] EmitUnderBuilderName(string name, string suffix) {
            string directory = Path.Combine(Path.GetTempPath(), "concord-" + name);
            Directory.CreateDirectory(directory);
            try {
                string file = name + ".dll";
                AssemblyBuilder assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(
                    new AssemblyName(name), AssemblyBuilderAccess.Save, directory);
                ModuleBuilder module = assembly.DefineDynamicModule(file, file);

                TypeBuilder twin = module.DefineType("TwinType" + suffix, TypeAttributes.Public | TypeAttributes.Class);
                twin.CreateType();

                TypeBuilder holder = module.DefineType(
                    "TwinHolder", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
                MethodBuilder probe = holder.DefineMethod(
                    "Probe", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);

                Type list = typeof(List<>).MakeGenericType(twin);
                ILGenerator il = probe.GetILGenerator();
                il.DeclareLocal(typeof(List<int>.Enumerator).GetGenericTypeDefinition().MakeGenericType(twin));
                il.Emit(OpCodes.Newobj, TypeBuilder.GetConstructor(list, typeof(List<>).GetConstructor(Type.EmptyTypes)!));
                il.Emit(OpCodes.Callvirt, TypeBuilder.GetMethod(list, typeof(List<>).GetMethod("GetEnumerator")!));
                il.Emit(OpCodes.Stloc_0);
                il.Emit(OpCodes.Ldc_I4, Constant);
                il.Emit(OpCodes.Ret);
                holder.CreateType();

                assembly.Save(file);
                return File.ReadAllBytes(Path.Combine(directory, file));
            } finally {
                try {
                    Directory.Delete(directory, true);
                } catch (Exception leftBehind) when (leftBehind is IOException or UnauthorizedAccessException) {
                }
            }
        }
    }
}
