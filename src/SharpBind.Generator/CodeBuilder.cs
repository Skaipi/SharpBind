using System.Text;

namespace SharpBind.Generator;

/// <summary>
/// Rules needed to generate code for given language.
/// </summary>
internal class CodeRules
{
    internal string ScopeOpener = "{";
    internal string ScopeCloser = "}";
    internal int IndentSize = 4;

    public CodeRules(string scopeOpener, string scopeCloser, int indent_size)
    {
        ScopeOpener = scopeOpener;
        ScopeCloser = scopeCloser;
        IndentSize = indent_size;
    }
}

/// <summary>
/// Builds content of a code file. Default parameters are set up for C# language.
/// </summary>
internal class CodeBuilder
{
    private StringBuilder code = new();
    private CodeRules style;
    private int indentLevel = 0;

    public CodeBuilder(CodeRules codeRules) => style = codeRules;

    /// <summary>
    /// Produces single line of code.
    /// </summary>
    public void Line(string line)
    {
        if (line.Contains('\n') || line.Contains('\r'))
            throw new ArgumentException("Multiple lines provided to single line generator");

        // Do not add indentation for empty lines
        if (line.Length > 0)
        {
            code.Append(' ', indentLevel * style.IndentSize);
            code.Append(line);
        }

        code.Append("\n");
    }

    /// <summary>
    /// Produces multiple lines of code.
    /// </summary>
    public void Lines(string lines)
    {
        lines.ReplaceLineEndings("\n");
        foreach (var line in lines.Split('\n'))
            Line(line);
    }

    /// <summary>
    /// Add line of code that opens new scope. 
    /// </summary>
    public IDisposable ScopeStatement(string header)
    {
        Line(header);
        Line(style.ScopeOpener);

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
            builder.indentLevel = Math.Max(0, builder.indentLevel - 1);
            builder.Line(builder.style.ScopeCloser);
        }
    }
}