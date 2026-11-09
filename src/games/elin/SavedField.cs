using System;
using Concord.AttachedData;
using Concord.Orchestration;

namespace Concord.Elin;

public sealed class SavedField<TTarget, TValue> : IAttachedSlot
    where TTarget : class {
    private readonly AttachedField<TTarget, TValue> field = new AttachedField<TTarget, TValue>();

    private SavedField() {
    }

    public static SavedField<TTarget, TValue> Register(string key, Type owner) {
        SavedField<TTarget, TValue> saved = new SavedField<TTarget, TValue>();
        PropertyRegistry.Register(owner, typeof(TTarget), key, typeof(TValue), saved);
        return saved;
    }

    public TValue Get(TTarget target) {
        return field.Get(target);
    }

    public void Set(TTarget target, TValue value) {
        field.Set(target, value);
    }

    public bool TryGet(TTarget target, out TValue value) {
        return field.TryGet(target, out value);
    }

    object IAttachedSlot.Get(object target) {
        return field.Get((TTarget)target);
    }

    void IAttachedSlot.Set(object target, object value) {
        field.Set((TTarget)target, value is TValue typed ? typed : default);
    }
}
