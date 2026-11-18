namespace Concord;

/// <summary>
///     Receives the Concord attributes that a mod's <c>Concord.Generated.PatchManifest</c> describes. Concord reads
///     them in place of the attributes the mod's assembly leaves out of its metadata.
/// </summary>
/// <remarks>
///     A member ID follows the C# documentation comment ID format. A file-local type and a compiler-made field
///     keep their metadata names, and only a conversion operator has a return type suffix.
/// </remarks>
public interface IPatchManifestSink {
    /// <summary>Records an attribute on the manifest's own assembly.</summary>
    /// <param name="attribute">The attribute, built with the arguments it has in source.</param>
    void OnAssembly(Attribute attribute);

    /// <summary>Records an attribute on a type, field, property, event or method of the manifest's assembly.</summary>
    /// <param name="memberId">The member's documentation comment ID, such as <c>M:My.Patch.Head(System.Int32@)</c>.</param>
    /// <param name="attribute">The attribute, built with the arguments it has in source.</param>
    void OnMember(string memberId, Attribute attribute);

    /// <summary>Records an attribute on a parameter of a method in the manifest's assembly.</summary>
    /// <param name="memberId">The documentation comment ID of the method that declares the parameter.</param>
    /// <param name="position">The zero-based position of the parameter, as <c>ParameterInfo.Position</c> reports it.</param>
    /// <param name="attribute">The attribute, built with the arguments it has in source.</param>
    void OnParameter(string memberId, int position, Attribute attribute);

    /// <summary>Records a type of the manifest's assembly whose attributes name a type that failed to load.</summary>
    /// <param name="memberId">The type's documentation comment ID, such as <c>T:My.Patch</c>.</param>
    /// <param name="error">The load failure.</param>
    void OnUnreadable(string memberId, Exception error);
}
