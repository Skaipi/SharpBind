using System.Text;

namespace SharpBind.Generator;

/// <summary>
/// Rules needed to generate code for given language.
/// </summary>
internal class CodeRules(string? scopeOpener, string? scopeCloser, int indentSize = 4)
{
    public static CodeRules CSharp { get; } = new("{", "}");
    public static CodeRules Python { get; } = new(null, null);

    public string? ScopeOpener { get; } = scopeOpener;
    public string? ScopeCloser { get; } = scopeCloser;
    public int IndentSize { get; } = indentSize;
}

/// <summary>
/// Builds a code file using explicit language rules.
/// </summary>
internal class CodeBuilder
{
    private readonly StringBuilder code = new();
    private readonly CodeRules style;
    private int indentLevel = 0;

    public CodeBuilder(CodeRules codeRules) => style = codeRules;

    /// <summary>
    /// Produces a single line of code.
    /// </summary>
    public void Line(string line)
    {
        if (line.Length == 0) return;
        if (line.Contains('\n') || line.Contains('\r'))
            throw new ArgumentException("Multiple lines provided to single line generator");

        code.Append(' ', indentLevel * style.IndentSize);
        code.Append(line);
        code.Append('\n');
    }

    public void BlankLine() => code.Append('\n');

    /// <summary>
    /// Produces multiple lines.
    /// </summary>
    public void Lines(string lines)
    {
        var validLines = lines.ReplaceLineEndings("\n");
        using var reader = new StringReader(validLines);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0) BlankLine();
            else Line(line);
        }
    }

    /// <summary>
    /// Add line of code that opens new scope. 
    /// </summary>
    public IDisposable ScopeStatement(string header)
    {
        Line(header);
        if (style.ScopeOpener is { } opener) Line(opener);

        indentLevel++;
        return new Scope(this);
    }

    public override string ToString() => code.ToString();

    private class Scope(CodeBuilder builder) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;

            disposed = true;
            builder.indentLevel--;
            if (builder.style.ScopeCloser is { } closer) builder.Line(closer);
        }
    }
}
