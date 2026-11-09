using System;
using Concord.AttachedData;
using Concord.Orchestration;

namespace Concord.Elin;

public sealed class ElinPropertyEntry {
    private readonly PropertyEntry inner;

    public ElinPropertyEntry(PropertyEntry inner, string label, int id, BagRoot root) {
        this.inner = inner;
        Label = label;
        Id = id;
        Root = root;
    }

    public Type BaseType => inner.BaseType;

    public string Key => inner.Key;

    public Type ValueType => inner.ValueType;

    public IAttachedSlot Slot => inner.Slot;

    public string Label { get; }

    public int Id { get; }

    public BagRoot Root { get; }
}
