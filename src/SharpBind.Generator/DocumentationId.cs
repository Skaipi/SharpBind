
using System.Reflection;
using System.Text.RegularExpressions;

namespace SharpBind.Generator;

/// <summary>
/// Generates XML documentation IDs for reflected declarations.
/// </summary>
internal static class DocumentationId
{
    internal static string For(MemberInfo member)
    {
        if (member is Type type) return $"T:{GetFullName(type)}";

        // All non-Type members are guaranteed to be MethodBase.
        return ForMethod((MethodBase)member);
    }

    private static string ForMethod(MethodBase method)
    {
        var declaringType = GetFullName(method.DeclaringType!);
        var name = method is ConstructorInfo
            ? "#ctor"
            : method.Name.Replace('.', '#');

        var parameters = method.GetParameters();
        var signature = parameters.Length == 0
            ? string.Empty
            : $"({string.Join(",", parameters.Select(p => FormatType(p.ParameterType)))})";

        return $"M:{declaringType}.{name}{signature}";
    }

    private static string FormatType(Type type)
    {
        if (type.IsGenericParameter)
        {
            var prefix = type.DeclaringMethod is null ? "`" : "``";
            return $"{prefix}{type.GenericParameterPosition}";
        }

        if (type.IsArray) return $"{FormatType(type.GetElementType()!)}[]";

        if (type.IsGenericType)
        {
            var name = Regex.Replace(GetFullName(type.GetGenericTypeDefinition()), @"`\d+", string.Empty);
            var arguments = string.Join(",", type.GetGenericArguments().Select(FormatType));

            return $"{name}{{{arguments}}}";
        }

        return GetFullName(type);
    }

    private static string GetFullName(Type type) => type.FullName!.Replace('+', '.');
}
