using System.Reflection;
using System.Runtime.Loader;

namespace SharpBind.Generator;

/// <summary>Loads the input library in a collectible context, preferring its own dependencies.</summary>
/// <remarks>
/// Resolves dependencies using the input library's dependency information, then checks beside its DLL.
/// </remarks>
internal sealed class InputAssemblyContext(string path) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver resolver = new(path);
    protected override Assembly? Load(AssemblyName name)
    {
        var resolved = resolver.ResolveAssemblyToPath(name);
        var sibling = Path.Combine(Path.GetDirectoryName(path)!, name.Name + ".dll");

        if (resolved is not null) return LoadFromAssemblyPath(resolved);

        return File.Exists(sibling) ? LoadFromAssemblyPath(sibling) : null;
    }
}
