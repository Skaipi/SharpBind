using System.Reflection;

namespace SharpBind.Generator;

internal sealed record ExportAnnotation(string? Name, Type[] TypeArguments, bool ConcurrentCalls = false);

/// <summary>Reads opt-in metadata without constructing attributes from the input assembly.</summary>
internal static class AnnotationReader
{
    private const string AttributeName = "SharpBind.PythonExportAttribute";

    internal static ExportAnnotation? Read(MemberInfo member)
    {
        var annotations = ReadAll(member);

        return annotations.Length switch
        {
            0 => null,
            1 => annotations[0],
            _ => throw new BindingValidationException($"{member}: multiple PythonExport attributes are only supported on generic classes.")
        };
    }

    // Attribute types may come from different assembly load contexts.
    // Match by name to avoid constructing attributes or comparing Type instances.
    internal static ExportAnnotation[] ReadAll(MemberInfo member) =>
        member.GetCustomAttributesData()
            .Where(attribute => attribute.AttributeType.FullName == AttributeName)
            .Select(ParseAttribute)
            .ToArray();

    private static ExportAnnotation ParseAttribute(CustomAttributeData attribute)
    {
        string? name = null;
        bool concurrentCalls = false;

        foreach (var argument in attribute.NamedArguments)
        {
            switch (argument.MemberName)
            {
                case "Name":
                    name = argument.TypedValue.Value as string;
                    break;

                case "ConcurrentCalls":
                    concurrentCalls = argument.TypedValue.Value is true;
                    break;
            }
        }

        return new(name, ReadTypeArguments(attribute), concurrentCalls);
    }

    private static Type[] ReadTypeArguments(CustomAttributeData attribute)
    {
        if (attribute.ConstructorArguments.Count == 0)
            return [];

        if (attribute.ConstructorArguments.Single().Value is not IEnumerable<CustomAttributeTypedArgument> arguments)
            throw new BindingValidationException("PythonExport type arguments cannot be null.");

        return arguments
            .Select(argument => argument.Value as Type ?? throw new BindingValidationException("PythonExport type arguments cannot contain null."))
            .ToArray();
    }
}