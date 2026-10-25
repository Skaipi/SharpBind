using System.Reflection;

namespace SharpBind.Generator;

/// <summary>Discovers opt-ins and assembles the validated model consumed by both emitters.</summary>
internal static class ContractReader
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    internal static ExportClass[] Read(Assembly assembly, string assemblyName, bool strictDocs)
    {
        var docs = new DocumentationReader(assembly, strictDocs);
        var classes = assembly.GetTypes()
            .Where(IsExported)
            .SelectMany(type => ReadClasses(type, assemblyName, docs))
            .OrderBy(cls => cls.Name, StringComparer.Ordinal)
            .ToArray();
        ValidateIdentities(classes);
        return classes;
    }

    private static bool IsExported(Type type) => AnnotationReader.ReadAll(type).Length != 0 ||
        type.GetMembers(Members).OfType<MethodBase>().Any(m => AnnotationReader.ReadAll(m).Length != 0);

    private static IEnumerable<ExportClass> ReadClasses(Type type, string assemblyName, DocumentationReader docs)
    {
        var annotations = AnnotationReader.ReadAll(type);
        if (!type.IsGenericTypeDefinition)
        {
            if (annotations.Length > 1)
                throw new BindingValidationException($"{type}: multiple PythonExport attributes are only supported on generic classes.");
            var annotation = annotations.SingleOrDefault();
            if (annotation?.TypeArguments.Length > 0)
                throw new BindingValidationException($"{type}: type arguments can only specialize a generic class.");
            yield return ReadClass(type, type, annotation, assemblyName, docs);
            yield break;
        }

        if (annotations.Length == 0)
            throw new BindingValidationException($"{type}: a generic class requires a PythonExport attribute for each specialization.");
        foreach (var annotation in annotations)
        {
            if (annotation.Name is null)
                throw new BindingValidationException($"{type}: each generic class specialization requires a unique Python name.");
            if (annotation.TypeArguments.Length != type.GetGenericArguments().Length)
                throw new BindingValidationException($"{type}: specialization '{annotation.Name}' supplies {annotation.TypeArguments.Length} type arguments for {type.GetGenericArguments().Length} generic parameters.");
            if (annotation.TypeArguments.Any(a => a.ContainsGenericParameters))
                throw new BindingValidationException($"{type}: specialization '{annotation.Name}' must use closed type arguments.");

            Type specialization;
            try
            {
                specialization = type.MakeGenericType(annotation.TypeArguments);
            }
            catch (ArgumentException error)
            {
                throw new BindingValidationException($"{type}: specialization '{annotation.Name}' has invalid type arguments.", error);
            }
            yield return ReadClass(specialization, type, annotation, assemblyName, docs);
        }
    }

    private static ExportClass ReadClass(Type type, Type declaration, ExportAnnotation? annotation,
        string assemblyName, DocumentationReader docs)
    {
        ContractValidator.Class(type);
        string name = PythonNames.Identifier(annotation?.Name ?? declaration.Name);
        string prefix = "sb_" + BindingJson.Hash(new[] { assemblyName, name })[..24];
        var methods = ReadMethods(type, prefix, docs);
        ContractValidator.Lifecycle(type, annotation is not null, methods);
        return new(name, prefix, TypeMapping.CSharpName(type), methods, docs.Read(declaration), annotation?.ConcurrentCalls ?? false);
    }

    private static ExportMethod[] ReadMethods(Type type, string prefix, DocumentationReader docs)
    {
        var methods = new List<ExportMethod>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in type.GetMethods(Members).Cast<MethodBase>().Concat(type.GetConstructors(Members)))
        {
            if (AnnotationReader.Read(member) is not { } annotation) continue;

            var method = ReadMethod(member, annotation, prefix, docs);
            if (!names.Add(method.Name))
                throw new BindingValidationException($"{type}: duplicate exported member '{method.Name}'; export one constructor and give overloaded methods distinct names.");
            methods.Add(method);
        }
        return methods.OrderBy(m => m.Name, StringComparer.Ordinal).ToArray();
    }

    private static ExportMethod ReadMethod(MethodBase member, ExportAnnotation annotation, string prefix, DocumentationReader docs)
    {
        ContractValidator.Method(member, annotation);
        var constructor = member is ConstructorInfo;
        var name = constructor ? "__init__" : PythonNames.Identifier(annotation.Name ?? PythonNames.SnakeCase(member.Name));
        var arguments = ReadArguments(member);
        var bindingType = constructor ? ReadResult((ConstructorInfo)member) : ReadResult((MethodInfo)member);
        return new(name, prefix + "_" + BindingJson.Hash(name)[..24], member.Name,
            constructor, member.IsStatic, bindingType, arguments, docs.Read(Declaration(member), arguments));
    }

    private static MethodBase Declaration(MethodBase member)
    {
        var declaringType = member.DeclaringType!;
        if (!declaringType.IsConstructedGenericType)
            return member;

        var definition = declaringType.GetGenericTypeDefinition();
        // XML documentation names the generic declaration, not the closed signature.
        // Metadata tokens identify the same member before and after substitution.
        return definition.GetMethods(Members).Cast<MethodBase>().Concat(definition.GetConstructors(Members))
            .Single(candidate => candidate.MetadataToken == member.MetadataToken);
    }

    private static BindingType ReadResult(MethodInfo method)
    {
        var mayReturnNull = new NullabilityInfoContext().Create(method.ReturnParameter).ReadState;
        return ContractValidator.Map(method.ReturnType, true, mayReturnNull);
    }

    private static BindingType ReadResult(ConstructorInfo constructor)
    {
        var mayReturnNull = NullabilityState.Nullable;
        return ContractValidator.Map(typeof(void), true, mayReturnNull);
    }

    private static ExportArgument[] ReadArguments(MethodBase member)
    {
        var arguments = new List<ExportArgument>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        bool optional = false;
        foreach (var parameter in member.GetParameters())
        {
            ContractValidator.Parameter(parameter, optional);
            var argument = ReadArgument(parameter, arguments.Count);
            if (!names.Add(argument.Name))
                throw new BindingValidationException($"{member}: duplicate Python parameter name.");
            optional |= argument.Optional;
            arguments.Add(argument);
        }
        return arguments.ToArray();
    }

    private static ExportArgument ReadArgument(ParameterInfo parameter, int index)
    {
        string source = parameter.Name ?? "arg" + index;
        var type = ContractValidator.Map(parameter.ParameterType, false,
            new NullabilityInfoContext().Create(parameter).ReadState);
        return new(PythonNames.Parameter(source), "a" + index, source, type,
            parameter.HasDefaultValue, ContractValidator.Default(parameter));
    }

    private static void ValidateIdentities(IEnumerable<ExportClass> classes)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var entries = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cls in classes)
        {
            if (!names.Add(cls.Name))
                throw new BindingValidationException($"Duplicate Python class '{cls.Name}'.");
            if (cls.HasInstances && !entries.Add(cls.Prefix + "_release"))
                throw new BindingValidationException("Native export identity collision.");
            foreach (var method in cls.Methods)
                if (!entries.Add(method.EntryPoint))
                    throw new BindingValidationException("Native export identity collision.");
        }
    }
}
