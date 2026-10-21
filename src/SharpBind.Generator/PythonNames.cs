using System.Text.RegularExpressions;

namespace SharpBind.Generator;

/// <summary>
/// Keeps Python names consistent.
/// </summary>
/// <remarks>
/// This class remains partial due to GeneratedRegex attribute
/// </remarks>
internal static partial class PythonNames
{
    private static readonly HashSet<string> PythonKeywords =
    [
        "False", "None", "True",
        "and", "as", "assert", "async", "await",
        "break",
        "class", "continue",
        "def", "del",
        "elif", "else", "except",
        "finally", "for", "from",
        "global",
        "if", "import", "in", "is",
        "lambda",
        "nonlocal", "not",
        "or",
        "pass",
        "raise", "return",
        "try",
        "while", "with",
        "yield",
    ];

    private static readonly HashSet<string> ReservedExportNames =
    [
        .. PythonKeywords,
        "close",
        "closed",
        "__init__",
        "__del__",
        "__enter__",
        "__exit__",
    ];

    /// <summary>Rejects names that cannot be exposed to Python.</summary>
    public static string Identifier(string value)
    {
        if (!ExportIdentifierRegex().IsMatch(value) || ReservedExportNames.Contains(value))
            throw new ArgumentException($"'{value}' is not valid Python/C identifier.", nameof(value));

        return value;
    }

    /// <summary>
    /// Validates a top-level import name. It may start with an underscore.
    /// </summary>
    public static void ValidateModule(string value)
    {
        if (!ModuleIdentifierRegex().IsMatch(value) || PythonKeywords.Contains(value))
            throw new ArgumentException($"'{value}' is not a top-level ASCII Python module name. ", nameof(value));
    }

    /// <summary>Converts PascalCase names and acronym boundaries to snake_case.</summary>
    public static string SnakeCase(string value)
    {
        value = AcronymBoundaryRegex().Replace(value, "$1_$2");
        value = WordBoundaryRegex().Replace(value, "$1_$2");
        return value.ToLowerInvariant();
    }

    /// <summary>Escapes Python parameter keywords without changing the managed parameter.</summary>
    public static string Parameter(string value)
    {
        var name = SnakeCase(value);

        if (ReservedExportNames.Contains(name) || name is "self" or "cls") name += '_';

        return Identifier(name);
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex ExportIdentifierRegex();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex ModuleIdentifierRegex();

    [GeneratedRegex(@"([A-Z]+)([A-Z][a-z])")]
    private static partial Regex AcronymBoundaryRegex();

    [GeneratedRegex(@"([a-z0-9])([A-Z])")]
    private static partial Regex WordBoundaryRegex();
}
