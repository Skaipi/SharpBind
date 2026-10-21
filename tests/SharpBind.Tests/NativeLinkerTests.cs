using System.Text.Json;
using System.Xml.Linq;

namespace SharpBind.Tests;

[TestClass]
[TestCategory("BuildIntegration")]
public class NativeLinkerTests
{
    [DataTestMethod]
    [DataRow("linux")]
    [DataRow("osx")]
    [DataRow("win")]
    public void NativeLinkerPreservesLibraryPathsWithSpacesAndArgumentOrder(string targetOS)
    {
        string directory = Path.Combine(Path.GetTempPath(), "SharpBind linker spaces", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string first = Path.Combine(directory, "NuGet cache", "libfirst.a");
            string second = Path.Combine(directory, "NuGet cache", "bootstrapper.o");
            Directory.CreateDirectory(Path.GetDirectoryName(first)!);
            File.WriteAllText(first, "first");
            File.WriteAllText(second, "second");
            File.WriteAllText(Path.Combine(directory, "relative.a"), "relative");
            string[] arguments = [
                "-Wl,--whole-archive", first, "-Wl,--no-whole-archive", second,
                $"\"{first}\"", $"-L\"{Path.GetDirectoryName(first)}\"", "relative.a", "-pthread"
            ];
            string project = Path.Combine(directory, "linker.proj");
            new XElement("Project",
                new XElement("PropertyGroup", new XElement("_targetOS", targetOS)),
                new XElement("Import", new XAttribute("Project", Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                    "../../../../../build/SharpBind.Native.targets")))),
                // Native AOT populates these items in a LinkNative dependency.
                new XElement("Target", new XAttribute("Name", "SetupOSSpecificProps"),
                    new XElement("ItemGroup",
                        new[] { first, second }.Select(value => new XElement("NativeLibrary", new XAttribute("Include", value))),
                        arguments.Select(value => new XElement("LinkerArg", new XAttribute("Include", value))))),
                new XElement("Target", new XAttribute("Name", "LinkNative"),
                    new XAttribute("DependsOnTargets", "SetupOSSpecificProps")))
                .Save(project);

            var result = TestProcess.RunDotnet("msbuild", project, "-t:LinkNative",
                "-getItem:LinkerArg,NativeLibrary", "-nologo");
            Assert.AreEqual(0, result.ExitCode, result.Output);
            using var output = JsonDocument.Parse(result.Output);
            var items = output.RootElement.GetProperty("Items");
            string[] actual = items.GetProperty("LinkerArg").EnumerateArray()
                .Select(item => item.GetProperty("Identity").GetString()!).ToArray();
            string[] expected = (string[])arguments.Clone();
            if (targetOS != "win")
            {
                expected[1] = $"\"{first}\"";
                expected[3] = $"\"{second}\"";
                expected[6] = "\"relative.a\"";
            }
            CollectionAssert.AreEqual(expected, actual);
            CollectionAssert.AreEqual(new[] { first, second }, items.GetProperty("NativeLibrary").EnumerateArray()
                .Select(item => item.GetProperty("Identity").GetString()!).ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
