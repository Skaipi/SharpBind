using System.Reflection;
using System.Runtime.CompilerServices;

namespace SharpBind.Generator;

/// <summary>
/// Rejects declarations that cannot be represented by the synchronous public API.
/// </summary>
internal static class ContractValidator
{
    internal static void Class(Type type)
    {
        Aot(type);
        if (type.ContainsGenericParameters) throw new NotSupportedException($"{type}: exported classes must be closed generic types.");
        if (!type.IsClass || !type.IsVisible) throw new NotSupportedException($"{type}: only public classes can be exported.");
    }

    internal static void Lifecycle(Type type, bool annotated, ExportMethod[] methods)
    {
        if (!methods.Any(m => m.IsConstructor || !m.IsStatic)) return;
        if (!annotated) throw new NotSupportedException($"{type}: instance bindings require an exported class.");
        if (!methods.Any(m => m.IsConstructor)) throw new BindingValidationException($"{type}: instance methods require an exported constructor.");
    }

    internal static void Parameter(ParameterInfo parameter, bool followsOptional)
    {
        if (parameter.ParameterType.IsByRef || parameter.IsOut || parameter.GetCustomAttribute<ParamArrayAttribute>() is not null)
            throw new NotSupportedException($"{parameter.Member}({parameter.Name}): ref, out and params are unsupported.");
        if (followsOptional && !parameter.HasDefaultValue)
            throw new NotSupportedException($"{parameter.Member}: a required parameter follows an optional one.");
    }

    internal static void Method(MethodBase member, ExportAnnotation registration)
    {
        Aot(member);
        if (registration.ConcurrentCalls)
            throw new BindingValidationException($"{member}: ConcurrentCalls can only be specified on a class.");
        if (registration.TypeArguments.Length != 0)
            throw new BindingValidationException($"{member}: generic type arguments can only be specified on a class.");
        if (member.IsGenericMethod)
            throw new NotSupportedException($"{member}: generic exports are unsupported.");
        if (!member.IsPublic || member.ContainsGenericParameters ||
            member.GetCustomAttribute<AsyncStateMachineAttribute>() is not null ||
            member is MethodInfo { IsSpecialName: true } ||
            member is ConstructorInfo && member.DeclaringType!.IsAbstract)
            throw new NotSupportedException($"{member}: only public synchronous ordinary methods and concrete constructors are supported.");
        if (member is ConstructorInfo && registration.Name is not null)
            throw new BindingValidationException($"{member}: constructors cannot be named.");
    }

    internal static BindingType Map(Type type, bool result, NullabilityState nullable)
    {
        var mapped = TypeMapping.Map(type, result);
        bool allowsNull = !type.IsValueType && nullable != NullabilityState.NotNull;

        if (mapped.Kind == LogicalTypeKind.Tensor)
        {
            if (nullable == NullabilityState.Nullable) throw new NotSupportedException("Tensor owners must be non-null.");
            allowsNull = false;
        }

        return mapped with
        {
            Logical = mapped.Logical with
            {
                Nullable = allowsNull
            }
        };
    }

    internal static object? Default(ParameterInfo parameter)
    {
        if (!parameter.HasDefaultValue)
            return null;
        object? value = parameter.DefaultValue;
        if (value is DBNull or Missing || value is double d && !double.IsFinite(d) || value is float f && !float.IsFinite(f))
            throw new NotSupportedException($"Unsupported default value for {parameter.Member}.{parameter.Name}.");
        if (value is null && (parameter.ParameterType.IsValueType ||
            new NullabilityInfoContext().Create(parameter).ReadState == NullabilityState.NotNull))
            throw new NotSupportedException($"Null default contradicts the type of {parameter.Member}.{parameter.Name}.");
        return value;
    }

    private static void Aot(MemberInfo member)
    {
        if (member.GetCustomAttributesData().Any(a => a.AttributeType.FullName == "System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute"))
            throw new NotSupportedException($"{member}: RequiresDynamicCode is incompatible with Native AOT; export a compatible facade.");
    }
}
