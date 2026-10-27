using System.Reflection;
using System.Xml;
using System.Xml.Linq;

namespace SharpBind.Generator;

/// <summary>Loads direct XML documentation and checks the descriptions of exported declarations.</summary>
internal sealed class DocumentationReader(Assembly assembly, bool strict)
{
    private readonly Dictionary<string, XElement> members = Load(assembly);

    private static Dictionary<string, XElement> Load(Assembly assembly)
    {
        if (assembly.IsDynamic) return [];

        string path = Path.ChangeExtension(assembly.Location, ".xml");
        if (!File.Exists(path)) return [];

        try
        {
            var members = new Dictionary<string, XElement>(StringComparer.Ordinal);
            foreach (var member in XDocument.Load(path).Descendants("member"))
            {
                string? name = (string?)member.Attribute("name");
                if (string.IsNullOrWhiteSpace(name) || !members.TryAdd(name, member))
                    throw new InvalidDataException($"XML documentation '{path}' contains an absent or duplicate member name.");
            }
            return members;
        }
        catch (XmlException error)
        {
            throw new InvalidDataException($"Invalid XML documentation '{path}': {error.Message}", error);
        }
    }

    internal string Read(MemberInfo member, ExportArgument[]? arguments = null)
    {
        string id = DocumentationId.For(member);
        members.TryGetValue(id, out var xml);
        Validate(xml, id, arguments ?? []);
        var substitutions = (arguments ?? []).ToDictionary(a => a.SourceName, a => a.Name);
        return PythonDocumentation.Format(xml, arguments, substitutions);
    }

    private void Validate(XElement? xml, string id, ExportArgument[] arguments)
    {
        if (xml?.Descendants("inheritdoc").Any() == true)
            throw new BindingValidationException($"{id}: inheritdoc is unsupported; document the exported facade directly.");

        if (string.IsNullOrWhiteSpace(xml?.Element("summary")?.Value))
        {
            string message = $"Missing summary documentation for {id}.";
            if (strict) throw new BindingValidationException(message);
            Console.Error.WriteLine($"SharpBind : warning : {message}");
        }

        if (!strict) return;

        foreach (var argument in arguments)
        {
            var description = xml?.Elements("param").FirstOrDefault(p => (string?)p.Attribute("name") == argument.SourceName);
            if (string.IsNullOrWhiteSpace(description?.Value))
                throw new BindingValidationException($"Missing parameter documentation for {id}:{argument.Name}.");
        }
    }
}
