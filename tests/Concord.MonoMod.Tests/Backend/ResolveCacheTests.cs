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

    public static Assembly? Reached;

    [ThreadStatic]
    public static Assembly? Element;

    [ThreadStatic]
    private static List<Assembly>? trail;

    public static List<Assembly> Trail => trail ??= new List<Assembly>();

    public static void Bump() {
        Calls++;
    }

    public static void Reach(Assembly copy) {
        Reached = copy;
        Trail.Add(copy);
    }
}

public sealed class ResolveCacheTests {
    private const int Constant = 7;

    [Fact]
    public void ForgetReflectionOnly_FindsMonoModCaches() {
        Assert.True(ResolveCache.ForgetReflectionOnly() >= 0);
    }

    [Fact]
    public void AScopeKeyPointingAtAnotherCopy_IsReboundByTheNextCompose() {
        byte[] image = Twins.Image();
        MethodInfo target = Twins.LoadOne(image);
        Assembly first = target.DeclaringType!.Assembly;
        Assembly second = first;
        for (int attempt = 0; attempt < 5 && second.GetHashCode() == first.GetHashCode(); attempt++) {
            target = Twins.LoadOne(image);
            second = target.DeclaringType!.Assembly;
        }

        Assert.NotEqual(first.GetHashCode(), second.GetHashCode());

        MethodInfo bump = typeof(ResolveCacheHead).GetMethod(nameof(ResolveCacheHead.Bump))!;
        IBackendHandle? composed = null;

        PlantScope(second, first);
        Assert.Same(first, BoundScope(second));

        try {
            composed = Compose(target, bump);

            Assert.Same(second, BoundScope(second));
        } finally {
            composed?.Dispose();
        }
    }

    [Fact]
    public void BindScopes_FindsMonoModCaches() {
        Assert.True(ResolveCache.BindScopes() > 0);
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

    internal static IBackendHandle Compose(MethodInfo target, MethodInfo head) {
        return new MonoModBackend().ApplyComposed(target, [new Injection(head, new InjectAt.Head(), "resolve-cache", 0)]);
    }

    internal static void PlantScope(Assembly key, Assembly value) {
        System.Collections.IDictionary cache = AssemblyCache();
        lock (cache) {
            cache[ScopeKeyOf(key)] = new WeakReference(value);
        }
    }

    internal static Assembly? BoundScope(Assembly key) {
        System.Collections.IDictionary cache = AssemblyCache();
        lock (cache) {
            return (cache[ScopeKeyOf(key)] as WeakReference)?.Target as Assembly;
        }
    }

    private static string ScopeKeyOf(Assembly assembly) {
        return assembly.FullName + "@#" + assembly.GetHashCode();
    }

    private static System.Collections.IDictionary AssemblyCache() {
        Type helper = typeof(ResolveCache).Assembly.GetType("MonoMod.Utils.ReflectionHelper")
                      ?? throw new InvalidOperationException("MonoMod.Utils.ReflectionHelper is not in Concord.MonoMod");
        FieldInfo field = helper.GetField("AssemblyCache", BindingFlags.NonPublic | BindingFlags.Static)
                          ?? throw new InvalidOperationException("MonoMod.Utils.ReflectionHelper carries no AssemblyCache");

        return field.GetValue(null) as System.Collections.IDictionary
               ?? throw new InvalidOperationException("MonoMod's AssemblyCache is not a dictionary");
    }

    internal sealed class Twins {
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
                ConstructorBuilder made = twin.DefineDefaultConstructor(MethodAttributes.Public);
                twin.CreateType();

                TypeBuilder holder = module.DefineType(
                    "TwinHolder", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
                MethodBuilder mark = holder.DefineMethod(
                    "Mark", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
                ILGenerator marking = mark.GetILGenerator();
                marking.Emit(OpCodes.Ldtoken, holder);
                marking.Emit(OpCodes.Call, typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))!);
                marking.Emit(OpCodes.Callvirt, typeof(Type).GetProperty(nameof(Type.Assembly))!.GetGetMethod()!);
                marking.Emit(OpCodes.Call, typeof(ResolveCacheHead).GetMethod(nameof(ResolveCacheHead.Reach))!);
                marking.Emit(OpCodes.Ret);

                MethodBuilder probe = holder.DefineMethod(
                    "Probe", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);

                Type list = typeof(List<>).MakeGenericType(twin);
                Type enumerator = typeof(List<int>.Enumerator).GetGenericTypeDefinition().MakeGenericType(twin);
                ILGenerator il = probe.GetILGenerator();
                il.DeclareLocal(enumerator);
                il.Emit(OpCodes.Call, mark);
                il.Emit(OpCodes.Newobj, TypeBuilder.GetConstructor(list, typeof(List<>).GetConstructor(Type.EmptyTypes)!));
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Newobj, made);
                il.Emit(OpCodes.Callvirt, TypeBuilder.GetMethod(list, typeof(List<>).GetMethod("Add")!));
                il.Emit(OpCodes.Callvirt, TypeBuilder.GetMethod(list, typeof(List<>).GetMethod("GetEnumerator")!));
                il.Emit(OpCodes.Stloc_0);
                il.Emit(OpCodes.Ldloca_S, (byte)0);
                il.Emit(OpCodes.Call, TypeBuilder.GetMethod(enumerator, typeof(List<>.Enumerator).GetMethod("MoveNext")!));
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ldloca_S, (byte)0);
                il.Emit(OpCodes.Call, TypeBuilder.GetMethod(enumerator, typeof(List<>.Enumerator).GetProperty("Current")!.GetGetMethod()!));
                il.Emit(OpCodes.Callvirt, typeof(object).GetMethod(nameof(object.GetType))!);
                il.Emit(OpCodes.Callvirt, typeof(Type).GetProperty(nameof(Type.Assembly))!.GetGetMethod()!);
                il.Emit(OpCodes.Stsfld, typeof(ResolveCacheHead).GetField(nameof(ResolveCacheHead.Element))!);
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
