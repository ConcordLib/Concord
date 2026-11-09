using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Concord.Backend;
using Concord.RimWorld;
using Xunit;

namespace Concord.RimWorld.Tests;

public class MethodIdentityTests {
  [Fact]
  public void Normalize_DistinctWrapperForSameMethod_CollapsesToCanonicalInstance() {
    MethodInfo canonical = typeof(NormalizationTarget).GetMethod(nameof(NormalizationTarget.M));
    MethodBase distinctWrapper = new ProxyMethodInfo(canonical);

    Assert.NotSame(canonical, distinctWrapper);

    MethodBase normalized = MethodIdentity.Normalize(distinctWrapper);

    Assert.Same(canonical, normalized);
  }

  [Fact]
  public void Normalize_DistinctWrapperForSameMethod_SharesOneDictionaryKeyWithCanonicalInstance() {
    MethodInfo canonical = typeof(NormalizationTarget).GetMethod(nameof(NormalizationTarget.M));
    MethodBase distinctWrapper = new ProxyMethodInfo(canonical);

    Assert.NotSame(canonical, distinctWrapper);

    Dictionary<MethodBase, int> unnormalized = new Dictionary<MethodBase, int>();
    unnormalized[canonical] = 1;
    unnormalized[distinctWrapper] = 2;

    Assert.Equal(2, unnormalized.Count);

    Dictionary<MethodBase, int> normalized = new Dictionary<MethodBase, int>();
    normalized[MethodIdentity.Normalize(canonical)] = 1;
    normalized[MethodIdentity.Normalize(distinctWrapper)] = 2;

    Assert.Single(normalized);
  }

  private class NormalizationTarget {
    public void M() {
    }
  }

  private sealed class ProxyMethodInfo : MethodInfo {
    private readonly MethodInfo inner;

    public ProxyMethodInfo(MethodInfo inner) {
      this.inner = inner;
    }

    public override ICustomAttributeProvider ReturnTypeCustomAttributes => inner.ReturnTypeCustomAttributes;

    public override RuntimeMethodHandle MethodHandle => inner.MethodHandle;

    public override MethodAttributes Attributes => inner.Attributes;

    public override Type DeclaringType => inner.DeclaringType;

    public override string Name => inner.Name;

    public override Type ReflectedType => inner.ReflectedType;

    public override Type ReturnType => inner.ReturnType;

    public override MethodImplAttributes GetMethodImplementationFlags() {
      return inner.GetMethodImplementationFlags();
    }

    public override ParameterInfo[] GetParameters() {
      return inner.GetParameters();
    }

    public override object Invoke(object obj, BindingFlags invokeAttr, Binder binder, object[] parameters, CultureInfo culture) {
      return inner.Invoke(obj, invokeAttr, binder, parameters, culture);
    }

    public override MethodInfo GetBaseDefinition() {
      return inner.GetBaseDefinition();
    }

    public override object[] GetCustomAttributes(bool inherit) {
      return inner.GetCustomAttributes(inherit);
    }

    public override object[] GetCustomAttributes(Type attributeType, bool inherit) {
      return inner.GetCustomAttributes(attributeType, inherit);
    }

    public override bool IsDefined(Type attributeType, bool inherit) {
      return inner.IsDefined(attributeType, inherit);
    }
  }
}
