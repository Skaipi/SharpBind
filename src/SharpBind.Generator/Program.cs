namespace SharpBind.Generator;

/// <summary>
/// Entry point for code generation.
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var options = GeneratorOptions.Parse(args);
            var context = new InputAssemblyContext(options.AssemblyPath);
            try
            {
                var assembly = context.LoadFromAssemblyPath(options.AssemblyPath);
                BindingGenerator.Generate(assembly, options);
            }
            finally
            {
                context.Unload();
            }
            return 0;
        }
        catch (Exception error)
        {
            return ReportFailure(error, Console.Error);
        }
    }

    /// <summary>
    /// Reports failures in the diagnostic format understood by MSBuild.
    /// </summary>
    internal static int ReportFailure(Exception error, TextWriter output)
    {
        const string prefix = "SharpBind : error : ";

        if (IsExpectedFailure(error))
        {
            foreach (var line in error.Message.ReplaceLineEndings("\n").Split('\n'))
                output.WriteLine($"{prefix}{line}");
        }
        else
        {
            output.WriteLine($"{prefix}Unexpected generation failure: {error.Message}");
            output.WriteLine(error);
        }

        return 1;
    }

    private static bool IsExpectedFailure(Exception error) =>
        error is BindingValidationException
            or ArgumentException
            or NotSupportedException
            or FormatException
            or InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or BadImageFormatException;
}
