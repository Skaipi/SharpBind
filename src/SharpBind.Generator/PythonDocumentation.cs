using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SharpBind.Generator;

/// <summary>
/// Exposes set of tools for mapping XML docstring into Python docstring.
/// </summary>
internal static class PythonDocumentation
{
    /// <summary>
    /// Map sections of XML docstrings into Python docstring.
    /// </summary>
    internal static string Format(XElement? xml, ExportArgument[]? arguments, Dictionary<string, string> substitutions)
    {
        var sections = new List<string>();
        var summary = GetText(xml?.Element("summary"), substitutions);

        if (summary.Length > 0) sections.Add(summary);
        if (arguments is { Length: > 0 }) sections.Add(FormatSection("Parameters", FormatParameters(xml, arguments, substitutions)));
        if (xml is null) return string.Join("\n\n", sections);

        AddSection(sections, "Returns", xml.Element("returns"), substitutions);
        AddSection(sections, "Notes", xml.Element("remarks"), substitutions);
        AddSection(sections, "Raises", xml.Elements("exception").Select(e => FormatException(e, substitutions)));
        AddSection(sections, "Examples", xml.Elements("example").Select(e => GetText(e, substitutions)));

        return string.Join("\n\n", sections);
    }

    private static string FormatParameters(XElement? xml, ExportArgument[] arguments, Dictionary<string, string> substitutions)
    {
        var parameters = arguments.Select(argument =>
        {
            var documentation = xml?.Elements("param").FirstOrDefault(e => (string?)e.Attribute("name") == argument.SourceName);
            var type = PythonTypes.TypeSignatures(argument.Type.Logical)
                .Replace("_npt.", "numpy.typing.").Replace("_np.", "numpy.");

            return $"{argument.Name} : {type}\n    {GetText(documentation, substitutions)}";
        });

        return string.Join("\n", parameters);
    }

    private static string FormatException(XElement exception, Dictionary<string, string> substitutions)
    {
        var reference = (string?)exception.Attribute("cref") ?? "";
        var name = RemoveTypePrefix(reference);

        return $"{name}\n    {GetText(exception, substitutions)}";
    }

    private static string GetText(XElement? element, Dictionary<string, string> substitutions)
    {
        if (element is null) return "";

        var copy = new XElement(element);

        foreach (var reference in copy.Descendants()
            .Where(e => e.Name.LocalName is "paramref" or "typeparamref" or "see")
            .ToArray())
        {
            reference.ReplaceWith(ResolveReference(reference, substitutions));
        }

        var text = copy.Value.Trim();

        // Preserve line breaks in examples; normalize prose whitespace.
        return element.Name.LocalName == "example" ? text : Regex.Replace(text, @"\s+", " ");
    }

    private static string ResolveReference(XElement element, Dictionary<string, string> substitutions)
    {
        var key = (string?)element.Attribute("name")
            ?? (string?)element.Attribute("cref")
            ?? (string?)element.Attribute("langword")
            ?? element.Value;

        return substitutions.GetValueOrDefault(key, RemoveTypePrefix(key));
    }

    /// <summary>
    /// Removes cref type identifiers (prefixes)
    /// </summary>
    private static string RemoveTypePrefix(string reference) =>
        reference.StartsWith("T:", StringComparison.Ordinal)
            ? reference[2..]
            : reference;

    private static void AddSection(List<string> sections, string title, XElement? element, Dictionary<string, string> substitutions)
    {
        if (element is not null)
            sections.Add(FormatSection(title, GetText(element, substitutions)));
    }

    private static void AddSection(List<string> sections, string title, IEnumerable<string> entries)
    {
        var lines = entries.ToArray();

        if (lines.Length > 0)
            sections.Add(FormatSection(title, string.Join("\n", lines)));
    }

    private static string FormatSection(string title, string content) =>
        $"{title}\n{new string('-', title.Length)}\n{content}";
}
