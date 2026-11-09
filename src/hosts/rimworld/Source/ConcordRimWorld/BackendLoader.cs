using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Verse;

namespace Concord.RimWorld;

internal static class BackendLoader {
  internal const string BackendFileGlob = "Concord.*.dll";

  internal const string RuntimeBuildKey = "ConcordBuild";

  internal static bool TryInstall(
      string modRootDir,
      List<Assembly> hostAssemblies,
      Action<string> log,
      Action<string> error,
      Func<string[]> assemblyDirectories) {
    string backendDir = null;
    string backendPath = null;

    try {
      backendDir = Path.Combine(modRootDir, ModLayout.VersionDir, "Backend");
      backendPath = ResolveBackendFile(backendDir);

      if (backendPath == null) {
        error(
            "[Concord.RimWorld] Concord's patching backend is missing: no " + BackendFileGlob + " at " +
            backendDir + ". It is the only backend there is, so Concord will not patch anything this " +
            "session. Reinstall the mod.");
        return false;
      }

      Assembly backend = LoadedBackend(backendPath);

      if (backend == null) {
        backend = Assembly.LoadFrom(backendPath);

        if (backend.ReflectionOnly) {
          error(
              "[Concord.RimWorld] Concord's patching backend at " + backendPath + " was reloaded by the " +
              "host and no live copy of it is loaded, so Concord will not patch anything this session.");
          return false;
        }
      }

      Register(backend, hostAssemblies);

      if (!TryRun(backend, backendPath, log, error, assemblyDirectories, out object selection)) {
        return false;
      }

      if (Dead(selection)) {
        error(
            "[Concord.RimWorld] Concord's patching backend at " + backendPath + " reported itself dead, so " +
            "Concord will not patch anything this session: " + Read(selection, "Reason"));
        return false;
      }

      if (!Installed(selection)) {
        error(
            "[Concord.RimWorld] Concord's patching backend at " + backendPath + " installed nothing on this " +
            "runtime, so Concord will not patch anything this session: " + Read(selection, "Message"));
        return false;
      }

      return true;
    } catch (Exception e) {
      error(
          "[Concord.RimWorld] Concord's patching backend at " + (backendPath ?? backendDir ?? modRootDir) +
          " could not be installed, so Concord will not patch anything this session: " + e);
      return false;
    }
  }

  internal static string ResolveBackendFile(string backendDir) {
    if (!Directory.Exists(backendDir)) {
      return null;
    }

    foreach (string file in Directory.EnumerateFiles(backendDir, BackendFileGlob)) {
      return file;
    }

    return null;
  }

  private static Assembly LoadedBackend(string backendPath) {
    string name = Path.GetFileNameWithoutExtension(backendPath);

    foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
      if (assembly.ReflectionOnly || assembly.IsDynamic) {
        continue;
      }

      if (assembly.GetName().Name == name) {
        return assembly;
      }
    }

    return null;
  }

  internal static MethodInfo FindInstall(Assembly backend) {
    Type[] types;

    try {
      types = backend.GetTypes();
    } catch (ReflectionTypeLoadException e) {
      types = Array.FindAll(e.Types, type => type != null);
    }

    foreach (Type type in types) {
      if (!type.Name.EndsWith("EntryPoint", StringComparison.Ordinal)) {
        continue;
      }

      MethodInfo install = type.GetMethod(
          "Install",
          BindingFlags.Public | BindingFlags.Static,
          null,
          [typeof(Action<string>), typeof(string[])],
          null);

      if (install != null) {
        return install;
      }
    }

    return null;
  }

  private static bool TryRun(
      Assembly backend,
      string backendPath,
      Action<string> log,
      Action<string> error,
      Func<string[]> assemblyDirectories,
      out object selection) {
    selection = null;

    if (!PairedWithRuntime(backend, backendPath, log, error)) {
      return false;
    }

    MethodInfo install = FindInstall(backend);

    if (install == null) {
      error(
          "[Concord.RimWorld] Concord's patching backend at " + backendPath + " has no matching entry " +
          "point, so Concord will not patch anything this session. Reinstall the mod.");
      return false;
    }

    selection = install.Invoke(null, [log, assemblyDirectories()]);
    return true;
  }

  private static bool Installed(object selection) {
    return selection != null && (bool)Read(selection, "Installed");
  }

  private static bool Dead(object selection) {
    return selection != null && Read(selection, "Dead") is true;
  }

  private static void Register(Assembly backend, List<Assembly> hostAssemblies) {
    if (hostAssemblies != null && !hostAssemblies.Contains(backend)) {
      hostAssemblies.Add(backend);
    }
  }

  private static object Read(object selection, string property) {
    return selection?.GetType().GetProperty(property)?.GetValue(selection);
  }

  private static bool PairedWithRuntime(
      Assembly backend,
      string backendPath,
      Action<string> log,
      Action<string> error) {
    string runtimeBuild = ModLayout.BuildSha(typeof(Patcher).Assembly);
    string backendBuild = DeclaredRuntimeBuild(backend);

    if (runtimeBuild == null || backendBuild == null) {
      log(
          "[Concord.RimWorld] Concord's patching backend at " + backendPath + " carries no " +
          RuntimeBuildKey + " stamp naming the Concord runtime build it was compiled against, so it " +
          "cannot be checked against the runtime loaded here. Installing it unchecked.");
      return true;
    }

    if (runtimeBuild == backendBuild) {
      return true;
    }

    error(
        "[Concord.RimWorld] Concord's patching backend at " + backendPath + " was built against Concord " +
        backendBuild + ", this game runs " + runtimeBuild + ". They ship as a pair, so Concord will not " +
        "patch anything this session. Reinstall both from one release.");
    return false;
  }

  private static string DeclaredRuntimeBuild(Assembly backend) {
    foreach (AssemblyMetadataAttribute stamp in backend.GetCustomAttributes<AssemblyMetadataAttribute>()) {
      if (stamp.Key == RuntimeBuildKey) {
        return stamp.Value;
      }
    }

    return null;
  }

  internal static string[] ModAssemblyDirectories() {
    List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;
    List<Assembly> loaded = [];
    List<string> folders = [];

    if (mods != null) {
      for (int i = 0; i < mods.Count; i++) {
        List<Assembly> assemblies = mods[i]?.assemblies?.loadedAssemblies;

        if (assemblies != null) {
          for (int j = 0; j < assemblies.Count; j++) {
            if (assemblies[j] != null) {
              loaded.Add(assemblies[j]);
            }
          }
        }

        List<string> modFolders = mods[i]?.foldersToLoadDescendingOrder;
        if (modFolders != null) {
          folders.AddRange(modFolders);
        }
      }
    }

    HashSet<string> dirs = new HashSet<string>(StringComparer.Ordinal);
    dirs.UnionWith(AssemblyDirectories(loaded));
    dirs.UnionWith(AssemblyDirectoriesFromFolders(folders));
    return [.. dirs];
  }

  internal static string[] AssemblyDirectoriesFromFolders(IReadOnlyList<string> folders) {
    HashSet<string> dirs = new HashSet<string>(StringComparer.Ordinal);

    for (int i = 0; i < folders.Count; i++) {
      string assembliesDir = Path.Combine(folders[i], GenFilePaths.AssembliesFolder.TrimEnd('/'));

      if (Directory.Exists(assembliesDir)) {
        dirs.Add(assembliesDir);
      }
    }

    return [.. dirs];
  }

  internal static string[] AssemblyDirectories(IReadOnlyList<Assembly> loaded) {
    HashSet<string> dirs = new HashSet<string>(StringComparer.Ordinal);

    for (int i = 0; i < loaded.Count; i++) {
      string location = AssemblyLocation(loaded[i]);

      if (!string.IsNullOrEmpty(location)) {
        dirs.Add(Path.GetDirectoryName(location));
      }
    }

    return [.. dirs];
  }

  private static string AssemblyLocation(Assembly assembly) {
    if (assembly == null) {
      return null;
    }

    try {
      string location = assembly.Location;
      return string.IsNullOrEmpty(location) ? null : Path.GetFullPath(location);
    } catch (NotSupportedException) {
      return null;
    }
  }
}
