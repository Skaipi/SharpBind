using System.Text;

namespace SharpBind.Generator;

/// <summary>Handles template resources and generated files without triggering unnecessary rebuilds.</summary>
internal static class GenerationFiles
{
    public static string ReadTemplate(string name)
    {
        using var stream = typeof(GenerationFiles).Assembly.GetManifestResourceStream("SharpBind.Generator.Templates." + name)
            ?? throw new InvalidOperationException($"Embedded generation template '{name}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Atomically replaces a changed file</summary>
    public static void WriteIfChanged(string path, string content)
    {
        path = Path.GetFullPath(path);
        content = content.Replace("\r\n", "\n");
        if (File.Exists(path) && File.ReadAllText(path) == content) return;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, content, new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
