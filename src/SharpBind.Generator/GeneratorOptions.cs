namespace SharpBind.Generator;

/// <summary>The inputs for a generation pass, independent of assembly loading and output writing.</summary>
internal record GeneratorOptions(string AssemblyPath, string NativeDirectory, string LibraryName,
    string? PythonModuleName, string? PythonPackageDirectory, bool StrictDocumentation)
{
    private const string Usage = "Usage: dotnet SharpBind.Generator.dll --assembly <dll> --native <directory> --library <native-library-name> [--module <import-name> --python-package <directory>] [--strict-docs true|false]";
    private static readonly string[] Allowed = ["--assembly", "--native", "--library", "--module", "--python-package", "--strict-docs"];

    /// <summary>Parses name/value pairs, rejecting missing, duplicate, and unknown options.</summary>
    public static GeneratorOptions Parse(string[] args)
    {
        if (args.Length % 2 != 0) throw new ArgumentException(Usage);

        var options = new Dictionary<string, string>();
        for (int index = 0; index < args.Length; index += 2)
        {
            if (!Allowed.Contains(args[index]) || string.IsNullOrWhiteSpace(args[index + 1]) || !options.TryAdd(args[index], args[index + 1]))
                throw new ArgumentException(Usage);
        }
        if (!options.ContainsKey("--assembly") || !options.ContainsKey("--native") || !options.ContainsKey("--library"))
            throw new ArgumentException(Usage);

        return new GeneratorOptions(Path.GetFullPath(options["--assembly"]), options["--native"], options["--library"],
            options.GetValueOrDefault("--module"), options.GetValueOrDefault("--python-package"),
            options.TryGetValue("--strict-docs", out var strict) && bool.Parse(strict));
    }
}
