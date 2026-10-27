namespace SharpBind.Generator;

/// <summary>
/// Formats managed types as fully qualified C# names.
/// </summary>
internal static class CSharpTypeNames
{
    private static readonly HashSet<string> CSharpKeywords =
    [
        "abstract", "as", "base", "bool", "break", "byte",
        "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do",
        "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach",
        "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new",
        "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref",
        "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint",
        "ulong", "unchecked", "unsafe", "ushort", "using",
        "virtual", "void", "volatile", "while"
    ];

    /// <summary>
    /// Returns a fully qualified C# type name with escaped identifiers.
    /// </summary>
    public static string Format(Type type)
    {
        if (type.IsArray)
        {
            var elementName = Format(type.GetElementType()!);
            var rank = new string(',', type.GetArrayRank() - 1);

            return $"{elementName}[{rank}]";
        }

        var hierarchy = new Stack<Type>();

        for (Type? current = type; current is not null; current = current.DeclaringType)
            hierarchy.Push(current);

        var genericArguments = type.GetGenericArguments();
        var names = new List<string>();
        var argumentOffset = 0;

        var namespaceName = hierarchy.Peek().Namespace;

        foreach (var current in hierarchy)
        {
            var name = current.Name.Split('`')[0];
            var inheritedArgumentCount = current.DeclaringType?.GetGenericArguments().Length ?? 0;
            var ownArgumentCount = current.GetGenericArguments().Length - inheritedArgumentCount;

            if (ownArgumentCount > 0)
            {
                var arguments = genericArguments
                    .Skip(argumentOffset)
                    .Take(ownArgumentCount)
                    .Select(Format);

                name += $"<{string.Join(", ", arguments)}>";
            }

            names.Add(EscapeName(name));
            argumentOffset += ownArgumentCount;
        }

        var prefix = namespaceName is null
            ? "global::"
            : $"global::{EscapeName(namespaceName)}.";

        return prefix + string.Join(".", names);
    }

    private static string EscapeName(string name)
    {
        var parts = name.Replace('+', '.').Split('.');
        var escaped = parts.Select(part => CSharpKeywords.Contains(part) ? $"@{part}" : part);

        return string.Join(".", escaped);
    }
}
