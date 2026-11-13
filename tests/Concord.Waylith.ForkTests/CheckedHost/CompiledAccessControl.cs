using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Orchestration;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Xunit;

namespace Concord.Waylith.ForkTests;

public static class CompiledControlTargets {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool CallsPublicTwin(string code) {
        return string.IsNullOrEmpty(code);
    }
}

[Collection(ForkFieldInjection.ApplyCollection)]
public sealed partial class CheckedHostAccessTests {
    private static readonly MethodInfo ForeignPrivate = typeof(PatchDeclarationScanner).GetMethod(
        "IsDeclarationScopedCode",
        BindingFlags.NonPublic | BindingFlags.Static,
        [typeof(string)])!;

    [Fact]
    public void ACompiledMethodCallingAPrivateMemberOfAnotherAssemblyThrowsOnlyOnAHostThatLeavesTheSwitchOut() {
        Assert.True(ForeignPrivate is { IsPrivate: true }, "PatchDeclarationScanner.IsDeclarationScopedCode is not a private method in this Concord.dll.");
        bool skips = RuntimeCapability.SkipsAccessChecks(
            AppContext.GetData(RuntimeCapability.AccessCheckKey),
            Environment.GetEnvironmentVariable(RuntimeCapability.AccessCheckSwitch));
        DirectoryInfo folder = Directory.CreateTempSubdirectory("concord-control-");
        try {
            MethodInfo control = Repointed(ForeignPrivate, folder.FullName);

            bool returned = false;
            Exception? thrown = Unwrap(Record.Exception(() => returned = (bool)control.Invoke(null, ["CONC061"])!));
            Assert.True(
                skips ? thrown is null && returned : thrown is MethodAccessException,
                $"switch says skip: {skips}; first call returned {returned}; threw {Describe(thrown)}");
        } finally {
            folder.Delete(true);
        }
    }

    private static MethodInfo Repointed(MethodInfo callee, string folder) {
        Assembly source = typeof(CompiledControlTargets).Assembly;
        string name = source.GetName().Name + ".Control";
        string path = Path.Combine(folder, name + ".dll");
        using (ModuleDefMD module = ModuleDefMD.Load(source.Location)) {
            module.Assembly.Name = name;
            module.Name = name + ".dll";
            MethodDef method = module.Find(typeof(CompiledControlTargets).FullName, false)
                .FindMethod(nameof(CompiledControlTargets.CallsPublicTwin));
            Instruction call = method.Body.Instructions.Single(instruction => instruction.OpCode == OpCodes.Call);
            call.Operand = module.Import(callee);
            module.Write(path);
        }

        return Assembly.LoadFrom(path)
            .GetType(typeof(CompiledControlTargets).FullName!, true)!
            .GetMethod(nameof(CompiledControlTargets.CallsPublicTwin))!;
    }

    private static Exception? Unwrap(Exception? thrown) {
        return thrown is TargetInvocationException { InnerException: Exception inner } ? inner : thrown;
    }

    private static string Describe(Exception? thrown) {
        return thrown is null ? "nothing" : $"{thrown.GetType().FullName}: {thrown.Message}";
    }
}
