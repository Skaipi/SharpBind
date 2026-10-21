using System.Reflection;

namespace SharpBind.Generator;

/// <summary>Validates and renders a complete binding before changing any output file.</summary>
internal static class BindingGenerator
{
    public static void Generate(Assembly assembly, GeneratorOptions options)
    {
        ValidateOptions(options);
        var classes = ContractReader.Read(assembly, assembly.GetName().Name!, options.StrictDocumentation);
        var outputs = Render(classes, options);
        foreach (var (path, content) in outputs)
            GenerationFiles.WriteIfChanged(path, content);
    }

    private static void ValidateOptions(GeneratorOptions options)
    {
        if ((options.PythonModuleName is null) != (options.PythonPackageDirectory is null))
            throw new ArgumentException("Supply both --module and --python-package, or neither.");
        if (options.PythonModuleName is not null)
        {
            PythonNames.ValidateModule(options.PythonModuleName);
            ArgumentException.ThrowIfNullOrWhiteSpace(options.PythonPackageDirectory);
        }
        PythonNames.Identifier(options.LibraryName);
    }

    private static Dictionary<string, string> Render(ExportClass[] classes, GeneratorOptions options)
    {
        string schema = BindingSchema.Serialize(classes, options.LibraryName, options.PythonModuleName);
        var outputs = new Dictionary<string, string>
        {
            [Path.Combine(options.NativeDirectory, "NativeExports.g.cs")] = NativeEmitter.EmitSource(classes),
            [Path.Combine(options.NativeDirectory, "bindings.h")] = NativeEmitter.EmitHeader(classes),
            [Path.Combine(options.NativeDirectory, "bindings.json")] = schema + "\n"
        };
        if (options.PythonPackageDirectory is { } package)
        {
            outputs[Path.Combine(package, "_bindings.py")] = PythonEmitter.Emit(classes, schema, options.PythonModuleName!, false);
            outputs[Path.Combine(package, "_bindings.pyi")] = PythonEmitter.Emit(classes, schema, options.PythonModuleName!, true);
            outputs[Path.Combine(package, "py.typed")] = "";
            string entry = Path.Combine(package, "__init__.py");
            if (!File.Exists(entry))
                outputs[entry] = "from ._bindings import *\nfrom ._bindings import __all__\n";
        }
        return outputs;
    }
}
