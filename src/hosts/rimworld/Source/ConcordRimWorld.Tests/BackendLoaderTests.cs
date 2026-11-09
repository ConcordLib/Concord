using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace Concord.RimWorld.Tests;

[Collection(GameAssemblies.Name)]
public class BackendLoaderTests {
  public static Action<string> LastLog;
  public static string[] LastDirectories;
  public static int InstallCalls;

  private static readonly string runtimeBuild = ModLayout.BuildSha(typeof(Patcher).Assembly);

  [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "xUnit1013", Justification = "Emitted fixture IL calls this across assemblies, so it has to stay public and is not a test.")]
  public static void Record(Action<string> log, string[] assemblyDirectories) {
    InstallCalls++;
    LastLog = log;
    LastDirectories = assemblyDirectories;
  }

  [Fact]
  public void MissingBackendWarnsThatConcordWillNotPatch() {
    string root = NewRoot();
    List<string> log = new List<string>();
    List<string> warnings = new List<string>();

    bool installed = BackendLoader.TryInstall(root, null, log.Add, warnings.Add, () => []);

    Assert.False(installed);
    Assert.Single(warnings);
    Assert.Contains("will not patch", warnings[0], StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void PresentBackendGetsLogSinkAndAssemblyDirectories() {
    string root = NewRoot();
    WriteFixture(root, "Fixture.Backend.Good", Fixture.Installs, runtimeBuild);
    List<string> log = new List<string>();
    List<string> warnings = new List<string>();
    string[] directories = ["/one", "/two"];
    InstallCalls = 0;
    LastLog = null;
    LastDirectories = null;

    bool installed = BackendLoader.TryInstall(root, null, log.Add, warnings.Add, () => directories);

    Assert.True(installed);
    Assert.Empty(warnings);
    Assert.Equal(1, InstallCalls);
    Assert.Equal(directories, LastDirectories);
    Assert.NotNull(LastLog);
    LastLog("from the backend");
    Assert.Contains("from the backend", log);
  }

  [Fact]
  public void ThrowingBackendIsSwallowedSoWiringStillRuns() {
    string root = NewRoot();
    WriteFixture(root, "Fixture.Backend.Bad", Fixture.Throws, runtimeBuild);
    List<string> log = new List<string>();
    List<string> warnings = new List<string>();
    bool reachedWiring = false;

    bool installed = BackendLoader.TryInstall(root, null, log.Add, warnings.Add, () => []);
    reachedWiring = true;

    Assert.False(installed);
    Assert.True(reachedWiring);
    Assert.Single(warnings);
    Assert.Contains(BackendPath(root), warnings[0]);
    Assert.Contains("will not patch", warnings[0], StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void UnusableModRootIsSwallowedSoWiringStillRuns() {
    List<string> log = new List<string>();
    List<string> warnings = new List<string>();

    bool installed = BackendLoader.TryInstall(null, null, log.Add, warnings.Add, () => []);

    Assert.False(installed);
    Assert.Single(warnings);
    Assert.Contains("will not patch", warnings[0], StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void BackendWithoutMatchingEntryPointIsReported() {
    string root = NewRoot();
    WriteFixture(root, "Fixture.Backend.WrongSignature", Fixture.WrongSignature, runtimeBuild);
    List<string> log = new List<string>();
    List<string> warnings = new List<string>();

    bool installed = BackendLoader.TryInstall(root, null, log.Add, warnings.Add, () => []);

    Assert.False(installed);
    Assert.Single(warnings);
    Assert.Contains("no matching entry point", warnings[0]);
    Assert.Contains("will not patch", warnings[0], StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void BackendThatRefusesThisRuntimeWarnsThatConcordWillNotPatch() {
    string root = NewRoot();
    WriteFixture(root, "Fixture.Backend.Refuses", Fixture.Refuses, runtimeBuild);
    List<string> log = new List<string>();
    List<string> warnings = new List<string>();

    bool installed = BackendLoader.TryInstall(root, null, log.Add, warnings.Add, () => []);

    Assert.False(installed);
    Assert.Single(warnings);
    Assert.Contains("fixture backend refused this runtime", warnings[0]);
    Assert.Contains("will not patch", warnings[0], StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void BackendJoinsTheModsLoadedAssembliesOnceSoTheHostReloadsItWithHarmony() {
    string root = NewRoot();
    WriteFixture(root, "Fixture.Backend.Registered", Fixture.Installs, runtimeBuild);
    List<string> log = new List<string>();
    List<string> warnings = new List<string>();
    List<Assembly> loaded = new List<Assembly>();

    Assert.True(BackendLoader.TryInstall(root, loaded, log.Add, warnings.Add, () => []));
    Assert.True(BackendLoader.TryInstall(root, loaded, log.Add, warnings.Add, () => []));

    Assert.Empty(warnings);
    Assert.Single(loaded);
    Assert.Equal("Fixture.Backend.Registered", ModLayout.SimpleName(loaded[0]));
  }

  [Fact]
  public void BackendThatReportsItIsDeadWarnsThatConcordWillNotPatch() {
    string root = NewRoot();
    WriteFixture(root, "Fixture.Backend.Dead", Fixture.Dead, runtimeBuild);
    List<string> log = new List<string>();
    List<string> warnings = new List<string>();
    InstallCalls = 0;

    bool installed = BackendLoader.TryInstall(root, null, log.Add, warnings.Add, () => []);

    Assert.False(installed);
    Assert.Equal(1, InstallCalls);
    Assert.Single(warnings);
    Assert.Contains(BackendPath(root), warnings[0]);
    Assert.Contains("fixture backend is dead", warnings[0]);
    Assert.Contains("will not patch", warnings[0], StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void LiveCopyAlreadyInTheDomainIsInstalledAndTheFileOnDiskIsNeverLoaded() {
    string root = NewRoot();
    WriteFixture(root, "Fixture.Backend.OnDisk", Fixture.Refuses, runtimeBuild, "Concord.FixtureLive.dll");
    Assembly live = EmitLoadedFromBytes("Concord.FixtureLive", Fixture.Installs, runtimeBuild);
    List<string> log = new List<string>();
    List<string> warnings = new List<string>();
    List<Assembly> loaded = new List<Assembly>();
    InstallCalls = 0;

    bool installed = BackendLoader.TryInstall(root, loaded, log.Add, warnings.Add, () => []);

    Assert.True(installed);
    Assert.Empty(warnings);
    Assert.Equal(1, InstallCalls);
    Assert.Single(loaded);
    Assert.Same(live, loaded[0]);
    Assert.Equal(string.Empty, live.Location);
  }

  [Fact]
  public void BackendBuiltAgainstAnotherRuntimeIsRefused() {
    string root = NewRoot();
    WriteFixture(root, "Fixture.Backend.Mismatched", Fixture.Installs, "0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f");
    List<string> log = new List<string>();
    List<string> warnings = new List<string>();
    InstallCalls = 0;

    bool installed = BackendLoader.TryInstall(root, null, log.Add, warnings.Add, () => []);

    Assert.False(installed);
    Assert.Equal(0, InstallCalls);
    Assert.Single(warnings);
    Assert.Contains("0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f", warnings[0]);
    Assert.Contains(runtimeBuild, warnings[0]);
    Assert.Contains("will not patch", warnings[0], StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void BackendWithNoBuildStampSaysSoAndStillInstalls() {
    string root = NewRoot();
    WriteFixture(root, "Fixture.Backend.Unstamped", Fixture.Installs);
    List<string> log = new List<string>();
    List<string> warnings = new List<string>();
    InstallCalls = 0;

    bool installed = BackendLoader.TryInstall(root, null, log.Add, warnings.Add, () => []);

    Assert.True(installed);
    Assert.Equal(1, InstallCalls);
    Assert.Empty(warnings);
    Assert.Single(log);
    Assert.Contains(BackendLoader.RuntimeBuildKey, log[0]);
  }

  [Fact]
  public void AssemblyDirectoriesMapsEveryAssemblyToItsDirectoryAndDedupes() {
    Assembly self = typeof(BackendLoaderTests).Assembly;
    Assembly runtime = typeof(object).Assembly;
    string selfDir = Path.GetDirectoryName(Path.GetFullPath(self.Location));
    string runtimeDir = Path.GetDirectoryName(Path.GetFullPath(runtime.Location));

    string[] dirs = BackendLoader.AssemblyDirectories(new[] { self, self, runtime });

    Assert.NotEqual(selfDir, runtimeDir);
    Assert.Contains(selfDir, dirs);
    Assert.Contains(runtimeDir, dirs);
    Assert.Equal(2, dirs.Length);
  }

  [Fact]
  public void AssemblyDirectoriesSkipsAssembliesWithNoFileOnDisk() {
    Assembly inMemory = AssemblyBuilder.DefineDynamicAssembly(
        new AssemblyName("Fixture.InMemory"),
        AssemblyBuilderAccess.Run);

    Assert.Empty(BackendLoader.AssemblyDirectories(new Assembly[] { null, inMemory }));
  }

  [Fact]
  public void AssemblyDirectoriesFromFoldersOnlyIncludesFoldersWithAnAssembliesSubdirectory() {
    string root = NewRoot();
    string withAssemblies = Path.Combine(root, "WithAssemblies");
    string withoutAssemblies = Path.Combine(root, "WithoutAssemblies");
    Directory.CreateDirectory(Path.Combine(withAssemblies, "Assemblies"));
    Directory.CreateDirectory(withoutAssemblies);

    string[] dirs = BackendLoader.AssemblyDirectoriesFromFolders([withAssemblies, withoutAssemblies]);

    Assert.Equal([Path.Combine(withAssemblies, "Assemblies")], dirs);
  }

  private const string FixtureBackendFileName = "Concord.Fixture.dll";

  private static string BackendPath(string modRoot, string fileName = FixtureBackendFileName) {
    return Path.Combine(
        modRoot,
        ModLayout.VersionDir,
        "Backend",
        fileName);
  }

  private static string NewRoot() {
    string root = Path.Combine(Path.GetTempPath(), "concord-backend-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    return root;
  }

  private enum Fixture {
    Installs,
    Refuses,
    Throws,
    WrongSignature,
    Dead
  }

  private static void WriteFixture(
      string modRoot,
      string assemblyName,
      Fixture kind,
      string concordBuild = null,
      string fileName = FixtureBackendFileName) {
    WriteFixtureAt(BackendPath(modRoot, fileName), assemblyName, kind, concordBuild);
  }

  private static Assembly EmitLoadedFromBytes(string assemblyName, Fixture kind, string concordBuild) {
    string path = Path.Combine(NewRoot(), assemblyName + ".dll");
    WriteFixtureAt(path, assemblyName, kind, concordBuild);
    return Assembly.Load(File.ReadAllBytes(path));
  }

  private static void WriteFixtureAt(string path, string assemblyName, Fixture kind, string concordBuild) {
    string dir = Path.GetDirectoryName(path);
    string fileName = Path.GetFileName(path);
    Directory.CreateDirectory(dir);

#if NET472
    AssemblyBuilder assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(
        new AssemblyName(assemblyName), AssemblyBuilderAccess.Save, dir);
#else
    PersistedAssemblyBuilder assembly = new PersistedAssemblyBuilder(
        new AssemblyName(assemblyName),
        typeof(object).Assembly);
#endif

    if (concordBuild != null) {
      assembly.SetCustomAttribute(new CustomAttributeBuilder(
          typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)]),
          [BackendLoader.RuntimeBuildKey, concordBuild]));
    }

#if NET472
    ModuleBuilder module = assembly.DefineDynamicModule(fileName, fileName);
#else
    ModuleBuilder module = assembly.DefineDynamicModule(fileName);
#endif
    bool reportsDeath = kind == Fixture.Dead;
    ConstructorInfo selectionCtor = DefineFixtureSelection(module, reportsDeath);

    TypeBuilder type = module.DefineType(
        "Fixture.FixtureEntryPoint",
        TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
    MethodBuilder install = type.DefineMethod(
        "Install",
        MethodAttributes.Public | MethodAttributes.Static,
        kind == Fixture.WrongSignature ? typeof(void) : typeof(object),
        kind == Fixture.WrongSignature
            ? []
            : [typeof(Action<string>), typeof(string[])]);
    ILGenerator il = install.GetILGenerator();

    if (kind == Fixture.Throws) {
      il.Emit(OpCodes.Ldstr, "fixture backend refuses to install");
      il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)]));
      il.Emit(OpCodes.Throw);
    } else if (kind == Fixture.WrongSignature) {
      il.Emit(OpCodes.Ret);
    } else if (reportsDeath) {
      il.Emit(OpCodes.Ldarg_0);
      il.Emit(OpCodes.Ldarg_1);
      il.Emit(OpCodes.Call, typeof(BackendLoaderTests).GetMethod(nameof(Record)));

      il.Emit(OpCodes.Ldc_I4_0);
      il.Emit(OpCodes.Ldstr, "fixture backend is dead");
      il.Emit(OpCodes.Ldc_I4_1);
      il.Emit(OpCodes.Newobj, selectionCtor);
      il.Emit(OpCodes.Ret);
    } else {
      il.Emit(OpCodes.Ldarg_0);
      il.Emit(OpCodes.Ldarg_1);
      il.Emit(OpCodes.Call, typeof(BackendLoaderTests).GetMethod(nameof(Record)));
      il.Emit(kind == Fixture.Refuses ? OpCodes.Ldc_I4_0 : OpCodes.Ldc_I4_1);
      il.Emit(OpCodes.Ldstr, kind == Fixture.Refuses ? "fixture backend refused this runtime" : "fixture backend installed");
      il.Emit(OpCodes.Newobj, selectionCtor);
      il.Emit(OpCodes.Ret);
    }

    type.CreateType();
#if NET472
    assembly.Save(fileName);
#else
    assembly.Save(path);
#endif
  }

  private static ConstructorInfo DefineFixtureSelection(ModuleBuilder module, bool withDead) {
    TypeBuilder selection = module.DefineType("Fixture.Selection", TypeAttributes.Public);
    FieldBuilder installed = selection.DefineField("installed", typeof(bool), FieldAttributes.Private);
    FieldBuilder message = selection.DefineField("message", typeof(string), FieldAttributes.Private);
    FieldBuilder dead = withDead
        ? selection.DefineField("dead", typeof(bool), FieldAttributes.Private)
        : null;

    ConstructorBuilder ctor = selection.DefineConstructor(
        MethodAttributes.Public,
        CallingConventions.Standard,
        withDead ? [typeof(bool), typeof(string), typeof(bool)] : [typeof(bool), typeof(string)]);
    ILGenerator ctorIl = ctor.GetILGenerator();
    ctorIl.Emit(OpCodes.Ldarg_0);
    ctorIl.Emit(OpCodes.Ldarg_1);
    ctorIl.Emit(OpCodes.Stfld, installed);
    ctorIl.Emit(OpCodes.Ldarg_0);
    ctorIl.Emit(OpCodes.Ldarg_2);
    ctorIl.Emit(OpCodes.Stfld, message);

    if (withDead) {
      ctorIl.Emit(OpCodes.Ldarg_0);
      ctorIl.Emit(OpCodes.Ldarg_3);
      ctorIl.Emit(OpCodes.Stfld, dead);
    }

    ctorIl.Emit(OpCodes.Ret);

    DefineReadOnlyProperty(selection, "Installed", typeof(bool), installed);
    DefineReadOnlyProperty(selection, "Message", typeof(string), message);
    DefineReadOnlyProperty(selection, "Reason", typeof(string), message);

    if (withDead) {
      DefineReadOnlyProperty(selection, "Dead", typeof(bool), dead);
    }

    selection.CreateType();
    return ctor;
  }

  private static void DefineReadOnlyProperty(TypeBuilder type, string name, Type propertyType, FieldBuilder backingField) {
    PropertyBuilder property = type.DefineProperty(name, PropertyAttributes.None, propertyType, null);
    MethodBuilder getter = type.DefineMethod(
        "get_" + name,
        MethodAttributes.Public | MethodAttributes.SpecialName,
        propertyType,
        null);
    ILGenerator getterIl = getter.GetILGenerator();
    getterIl.Emit(OpCodes.Ldarg_0);
    getterIl.Emit(OpCodes.Ldfld, backingField);
    getterIl.Emit(OpCodes.Ret);
    property.SetGetMethod(getter);
  }
}
