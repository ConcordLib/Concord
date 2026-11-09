using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace Concord.RimWorld.Tests;

public class ModLayoutTests {
  [Fact]
  public void A_Load_Folders_File_Lists_Only_The_Default_Scan() {
    AssertListsOnlyTheDefaultScan(ModRoot());
  }

  private static void AssertListsOnlyTheDefaultScan(string root) {
    string path = Directory.EnumerateFiles(root)
        .FirstOrDefault(file => string.Equals(Path.GetFileName(file), "LoadFolders.xml", StringComparison.OrdinalIgnoreCase));

    if (path == null) {
      return;
    }

    foreach (XElement version in XElement.Load(path).Elements()) {
      string versionNumber = version.Name.LocalName.Substring(1);

      Assert.Equal(new[] { "/", versionNumber }, version.Elements("li").Select(li => li.Value));
      Assert.True(Directory.Exists(Path.Combine(root, versionNumber)));
    }
  }

  [Fact]
  public void The_Version_This_Build_Targets_Is_A_Supported_Version() {
    string about = File.ReadAllText(Path.Combine(ModRoot(), "About", "About.xml"));

    Assert.Contains("<li>" + ModLayout.VersionDir + "</li>", about);
  }

  [Fact]
  public void Staged_Backend_Directory_Has_The_Body_Replacement_Backend() {
    string staged = StagedBackendPath();

    Assert.True(File.Exists(staged));
  }

  [Fact]
  public void Staged_Backend_Has_The_Install_Entry_Point_The_Loader_Looks_Up() {
    string staged = StagedBackendPath();

    Assert.NotNull(BackendLoader.FindInstall(Assembly.LoadFrom(staged)));
  }

  [Fact]
  public void Staged_Backend_Is_Stamped_With_The_Runtime_Build_It_Ships_Beside() {
    string staged = StagedBackendPath();
    string stamped = null;

    foreach (AssemblyMetadataAttribute stamp in
        Assembly.LoadFrom(staged).GetCustomAttributes<AssemblyMetadataAttribute>()) {
      if (stamp.Key == BackendLoader.RuntimeBuildKey) {
        stamped = stamp.Value;
      }
    }

    Assert.Equal(ModLayout.BuildSha(typeof(Patcher).Assembly), stamped);
  }

  private static string StagedBackendPath() {
    string backendDir = Path.Combine(ModRoot(), ModLayout.VersionDir, "Backend");
    return BackendLoader.ResolveBackendFile(backendDir);
  }

  private static string ModRoot() {
    string current = AppContext.BaseDirectory;

    while (current != null) {
      if (File.Exists(Path.Combine(current, "About", "About.xml"))) {
        return current;
      }

      current = Path.GetDirectoryName(current);
    }

    throw new InvalidOperationException("Could not resolve the mod root from the test assembly location.");
  }
}
