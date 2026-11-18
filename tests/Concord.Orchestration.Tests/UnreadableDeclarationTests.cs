using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Concord.Emit;
using Mono.Cecil;
using Xunit;

namespace Concord.Orchestration.Tests;

public sealed class UnreadableDeclarationTests {
    [Fact]
    public void ScanType_PatchTargetAssemblyMissing_SkipsAndSaysWhy() {
        Type declaration = BuildDeclarationTargetingAMissingAssembly();
        List<string> logs = [];
        Patcher.UseLog(logs.Add);

        try {
            PatchDeclarationScanner.ScanType(declaration, new RecordingApplier(), new FakeAttachedPropertyRegistry());
        } finally {
            Patcher.UseLog(null);
        }

        Assert.Single(logs);
        Assert.Contains(declaration.FullName!, logs[0]);
        Assert.Contains("not loaded", logs[0]);
    }

    [Fact]
    public void Apply_PatchTargetAssemblyMissing_AddsNoSkippedLineOfItsOwn() {
        Type declaration = BuildDeclarationTargetingAMissingAssembly();
        List<string> scanLogs = [];
        List<string> applyLogs = [];

        try {
            Patcher.UseLog(scanLogs.Add);
            PatchDeclarationScanner.ScanExtendedEnums([declaration]);
            PatchDeclarationScanner.ScanDeclarations([declaration], new RecordingApplier(), new FakeAttachedPropertyRegistry());

            Patcher.UseLog(applyLogs.Add);
            using (Patcher.Apply(declaration.Assembly)) { }
        } finally {
            Patcher.UseLog(null);
        }

        Assert.Equal(Skipped(scanLogs), Skipped(applyLogs));
    }

    [Fact]
    public void Apply_OnlyDeclarationIsUnreadable_DoesNotSayItFoundNoDeclaration() {
        Type declaration = BuildDeclarationTargetingAMissingAssembly();
        List<string> logs = [];

        try {
            Patcher.UseLog(logs.Add);
            using (Patcher.Apply(declaration.Assembly)) { }
        } finally {
            Patcher.UseLog(null);
        }

        Assert.Contains(logs, line => line.Contains("skipped declaration"));
        Assert.DoesNotContain(logs, line => line.Contains("found no [Patch] declaration"));
    }

    private static int Skipped(List<string> logs) {
        return logs.Count(line => line.Contains("skipped declaration"));
    }

    private static Type BuildDeclarationTargetingAMissingAssembly() {
        AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("GhostedDeclarations", new Version(1, 0, 0, 0)),
            "GhostedDeclarations",
            ModuleKind.Dll);

        ModuleDefinition module = assembly.MainModule;
        AssemblyNameReference absent = new AssemblyNameReference("ModThatIsNotInstalled", new Version(1, 0, 0, 0));
        module.AssemblyReferences.Add(absent);

        TypeDefinition declaration = new TypeDefinition(
            "Ghosted",
            "Declaration",
            Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Class,
            module.ImportReference(typeof(object)));

        CustomAttribute patch = new CustomAttribute(
            module.ImportReference(typeof(PatchAttribute).GetConstructor([typeof(Type)])));
        patch.ConstructorArguments.Add(new CustomAttributeArgument(
            module.ImportReference(typeof(Type)),
            new TypeReference("NotInstalled", "Target", module, absent)));
        declaration.CustomAttributes.Add(patch);
        module.Types.Add(declaration);

        using MemoryStream image = new MemoryStream();
        assembly.Write(image);
        return Assembly.Load(image.ToArray()).GetType("Ghosted.Declaration")!;
    }

    private sealed class RecordingApplier : IPatchApplier {
        public void ApplyPatch(MethodBase target, Injection injection) {
            throw new InvalidOperationException("nothing should be applied");
        }
    }
}
