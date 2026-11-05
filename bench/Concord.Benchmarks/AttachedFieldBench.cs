using BenchmarkDotNet.Attributes;
using Concord.AttachedData;

namespace Concord.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class AttachedFieldBench {
    private readonly object _target = new();
    private AttachedField<object, int> _field = null!;

    [GlobalSetup]
    public void Setup() {
        _field = new AttachedField<object, int>();
    }

    [Benchmark(Baseline = true)]
    public int Get() {
        _field.Set(_target, 42);
        return _field.Get(_target);
    }

    [Benchmark]
    public int GetSet() {
        for (int i = 0; i < 10; i++) {
            _field.Set(_target, i);
        }
        return _field.Get(_target);
    }
}
