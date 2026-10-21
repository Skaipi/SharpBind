using System.Xml.Linq;

namespace SharpBind.Tests;

[TestClass]
[TestCategory("BuildIntegration")]
public class NativeStagingTests
{
    [DataTestMethod]
    [DataRow("bridge", 0, "", "win-x64", ".dll")]
    [DataRow("bridge", 0, "", "linux-x64", ".so")]
    [DataRow("bridge", 0, "", "osx-arm64", ".dylib")]
    [DataRow("dependency", 0, "", "win-x64", ".dll")]
    [DataRow("missing", 1, "Native dependency is missing", "win-x64", ".dll")]
    [DataRow("duplicate", 1, "distinct filenames", "win-x64", ".dll")]
    public void StagingCopiesOnlyDeclaredFilesAndChecksDependencies(string scenario, int exitCode, string diagnostic, string rid, string extension)
    {
        string directory = Path.Combine(Path.GetTempPath(), "SharpBind.Staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "publish"));
        Directory.CreateDirectory(Path.Combine(directory, "dependencies"));
        try
        {
            string publish = Path.Combine(directory, "publish") + Path.DirectorySeparatorChar;
            File.WriteAllText(Path.Combine(publish, "Bridge" + extension), "bridge");
            File.WriteAllText(Path.Combine(publish, "Unrelated.dll"), "excluded");
            File.WriteAllText(Path.Combine(publish, "Bridge.pdb"), "excluded");
            string package = Path.Combine(directory, "package");
            var project = new XElement("Project",
                new XElement("PropertyGroup",
                    new XElement("AssemblyName", "Bridge"),
                    new XElement("RuntimeIdentifier", rid),
                    new XElement("NativeBinaryExt", extension),
                    new XElement("PublishDir", publish),
                    new XElement("SharpBindPythonPackageOutput", package)),
                new XElement("Import", new XAttribute("Project", Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                    "../../../../../build/SharpBind.Native.targets")))));
            if (scenario != "bridge")
            {
                string dependency = Path.Combine(directory, "dependencies", scenario == "duplicate" ? "Bridge" + extension : "Extra" + extension);
                if (scenario != "missing")
                    File.WriteAllText(dependency, "dependency");
                project.Add(new XElement("ItemGroup", new XElement("SharpBindNativeDependency", new XAttribute("Include", dependency))));
            }
            string file = Path.Combine(directory, "staging.proj");
            project.Save(file);
            var result = TestProcess.RunDotnet("msbuild", file, "-t:StageSharpBindNativeLibrary", "-v:quiet", "-nologo");

            Assert.AreEqual(exitCode, result.ExitCode, result.Output);
            if (exitCode != 0)
                StringAssert.Contains(result.Output, diagnostic);
            else
            {
                string native = Path.Combine(package, "_native", rid);
                Assert.AreEqual("bridge", File.ReadAllText(Path.Combine(native, "Bridge" + extension)));
                Assert.AreEqual(scenario == "dependency" ? 2 : 1, Directory.GetFiles(native).Length);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
