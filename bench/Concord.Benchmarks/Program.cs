using BenchmarkDotNet.Running;
using Concord.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(BodySizeComposeBench).Assembly).Run(args);
