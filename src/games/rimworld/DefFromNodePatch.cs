using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using Concord;
using Verse;

namespace Concord.RimWorld;

[Patch(typeof(DirectXmlLoader))]
public static class DefFromNodePatch {
  [Inject(At.Around, nameof(DirectXmlLoader.DefFromNode))]
  public static Def WrapDefFromNode(XmlNode node, LoadableXmlAsset loadingAsset, Operation<XmlNode, LoadableXmlAsset, Def> original) {
    List<KeyValuePair<RimWorldAttachedPropertyRegistry.Entry, object>> lifted = Strip(node);
    Def result = original.Invoke(node, loadingAsset);
    Apply(result, lifted);
    return result;
  }

  [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1168", Justification = "null is the deliberate 'nothing to lift' sentinel on this hot XML-load path; Apply null-checks it, so returning an empty list would only add per-def allocation.")]
  private static List<KeyValuePair<RimWorldAttachedPropertyRegistry.Entry, object>> Strip(XmlNode node) {
    RimWorldAttachedPropertyRegistry registry = RimWorldRuntime.Registry;
    if (registry == null || registry.IsEmpty || node == null) {
      return null;
    }

    Type defType = ResolveDefType(node);
    if (defType == null) {
      return null;
    }

    IReadOnlyList<RimWorldAttachedPropertyRegistry.Entry> entries = registry.ForBaseType(defType);
    if (entries.Count == 0) {
      return null;
    }

    List<KeyValuePair<RimWorldAttachedPropertyRegistry.Entry, object>> lifted = null;
    foreach (RimWorldAttachedPropertyRegistry.Entry entry in entries) {
      XmlNode child = FindChild(node, entry.Key);
      if (child == null) {
        continue;
      }

      object value = ParseValue(child, entry.ValueType, entry.Key);

      node.RemoveChild(child);
      lifted ??= [];
      lifted.Add(new KeyValuePair<RimWorldAttachedPropertyRegistry.Entry, object>(entry, value));
    }

    return lifted;
  }

  private static Type ResolveDefType(XmlNode node) {
    string typeName = node.Name;
    return GenTypes.GetTypeInAnyAssembly(typeName, "Verse") ?? GenTypes.GetTypeInAnyAssembly(typeName, "RimWorld");
  }

  private static void Apply(Def result, List<KeyValuePair<RimWorldAttachedPropertyRegistry.Entry, object>> lifted) {
    if (result == null || lifted == null) {
      return;
    }

    foreach (KeyValuePair<RimWorldAttachedPropertyRegistry.Entry, object> pair in lifted) {
      pair.Key.Slot.Set(result, pair.Value);
    }
  }

  private static XmlNode FindChild(XmlNode node, string key) {
    foreach (XmlNode child in node.ChildNodes) {
      if (child.NodeType == XmlNodeType.Element && string.Equals(child.Name, key, StringComparison.OrdinalIgnoreCase)) {
        return child;
      }
    }

    return null;
  }

  private static object ParseValue(XmlNode node, Type valueType, string key) {
    if (valueType == typeof(int)) {
      string text = node.InnerText;
      if (text == null || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) {
        throw new ArgumentException("Concord attached-property '" + key + "' expected an int but got '" + (text ?? "<null>") + "'.");
      }

      return parsed;
    }

    throw new NotSupportedException("Concord attached-property type not yet supported for XML parse: " + valueType.FullName);
  }
}
