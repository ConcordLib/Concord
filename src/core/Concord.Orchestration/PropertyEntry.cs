using Concord.AttachedData;

namespace Concord.Orchestration;

/// <summary>
///     One attached property a mod registered through <see cref="PropertyRegistry" />. Carries what
///     every host needs to deliver it to an <see cref="IAttachedPropertyRegistry" />.
/// </summary>
public sealed class PropertyEntry {
    /// <summary>
    ///     Initializes a new instance of the <see cref="PropertyEntry" /> class.
    /// </summary>
    /// <param name="declarationType">The type that declares the property. Its assembly is the owning mod.</param>
    /// <param name="baseType">The target type the property attaches to.</param>
    /// <param name="key">The field name, as the mod spelled it in source.</param>
    /// <param name="valueType">The field type as declared, not the runtime type of a stored value.</param>
    /// <param name="slot">The storage the patched code reads and writes.</param>
    public PropertyEntry(Type declarationType, Type baseType, string key, Type valueType, IAttachedSlot slot) {
        DeclarationType = declarationType;
        BaseType = baseType;
        Key = key;
        ValueType = valueType;
        Slot = slot;
    }

    /// <summary>The type that declares the property. Its assembly is the owning mod.</summary>
    public Type DeclarationType { get; }

    /// <summary>The target type the property attaches to.</summary>
    public Type BaseType { get; }

    /// <summary>The field name, as the mod spelled it in source.</summary>
    public string Key { get; }

    /// <summary>The field type as declared, not the runtime type of a stored value.</summary>
    public Type ValueType { get; }

    /// <summary>The storage the patched code reads and writes, for a host to read and write too.</summary>
    public IAttachedSlot Slot { get; }
}
