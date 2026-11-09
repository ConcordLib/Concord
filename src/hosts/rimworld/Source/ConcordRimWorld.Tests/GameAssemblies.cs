using Xunit;

namespace Concord.RimWorld.Tests;

// Mono's loader is not safe to drive from two threads at once: a collection loading Assembly-CSharp
// and its Unity dlls while another reads assembly names ends in FileNotFoundException, or takes the
// runner down with a loader assertion. Every class that touches a game or Harmony type shares this
// collection so those loads happen on one thread. Pure classes stay parallel.
[CollectionDefinition(Name)]
public sealed class GameAssemblies {
  public const string Name = "GameAssemblies";
}
