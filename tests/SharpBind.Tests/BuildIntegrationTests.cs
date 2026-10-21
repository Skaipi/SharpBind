using System.Xml.Linq;
using SharpBind.Generator;

namespace SharpBind.Tests;

[TestClass]
[TestCategory("BuildIntegration")]
[DoNotParallelize]
public class BuildIntegrationTests
{
    [DataTestMethod]
    [DataRow("9.0.100", "net9.0")]
    [DataRow("10.0.100", "net10.0")]
    public void NativeTemplateSelectsSdkFramework(string sdkVersion, string expectedFramework)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var result = TestProcess.RunDotnet(
            "msbuild", Path.Combine(root, "templates/python/native/NativeBindings.csproj"),
            "-getProperty:TargetFramework", $"-p:NETCoreSdkVersion={sdkVersion}", "-nologo");
        Assert.AreEqual(0, result.ExitCode, result.Output);
        Assert.AreEqual(expectedFramework, result.Output.Trim());
    }

    [DataTestMethod]
    [DataRow("net9.0")]
    [DataRow("net10.0")]
    public void ManagedBuildGeneratesPythonAndBridgeUsesResolvedNet9Reference(string bridgeFramework)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        // macOS aliases /var to /private/var, which can break MSBuild's relative
        // transitive project references. Keep the test graph under the checkout.
        string directory = Path.Combine(root, "artifacts", "build-integration", "SharpBind build spaces", Guid.NewGuid().ToString("N"));
        string managed = Path.Combine(directory, "Managed");
        string bridge = Path.Combine(directory, "Bridge");
        Directory.CreateDirectory(managed);
        Directory.CreateDirectory(bridge);
        try
        {
            File.WriteAllText(Path.Combine(managed, "Example.cs"), """
                using SharpBind;
                public static class Example
                {
                    [PythonExport] public static double First(Tensor<double> input) => input.Memory.Span[0];
                }
                """);
            string managedProject = Path.Combine(managed, "ManagedExample.csproj");
            new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup",
                    new XElement("TargetFramework", "net9.0"),
                    new XElement("SharpBindPythonModuleName", "example_api"),
                    new XElement("SharpBindGeneratorPath", typeof(BindingGenerator).Assembly.Location)),
                new XElement("ItemGroup", new XElement("ProjectReference",
                    new XAttribute("Include", Path.Combine(root, "src/SharpBind/SharpBind.csproj")))),
                new XElement("Import", new XAttribute("Project", Path.Combine(root, "build/SharpBind.targets"))))
                .Save(managedProject);
            var build = TestProcess.RunDotnet("build", managedProject, "--ignore-failed-sources", "-v:quiet", "-nologo");
            Assert.AreEqual(0, build.ExitCode, build.Output);
            string package = Path.Combine(managed, "artifacts/sharpbind/Debug/net9.0/python/example_api");
            Assert.IsTrue(File.Exists(Path.Combine(package, "__init__.py")), build.Output);
            Assert.IsTrue(File.Exists(Path.Combine(package, "_bindings.pyi")), build.Output);
            StringAssert.Contains(File.ReadAllText(Path.Combine(package, "_bindings.py")), "ManagedExampleNative");

            string bridgeProject = Path.Combine(bridge, "ManagedExampleNative.csproj");
            new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup",
                    new XElement("TargetFramework", bridgeFramework),
                    new XElement("AllowUnsafeBlocks", "true"),
                    new XElement("ImplicitUsings", "enable"),
                    new XElement("SharpBindInputProject", managedProject),
                    new XElement("SharpBindGeneratorPath", typeof(BindingGenerator).Assembly.Location)),
                new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", managedProject))),
                new XElement("Import", new XAttribute("Project", Path.Combine(root, "build/SharpBind.targets"))))
                .Save(bridgeProject);
            var binding = TestProcess.RunDotnet("build", bridgeProject, "--ignore-failed-sources", "-v:quiet", "-nologo");
            Assert.AreEqual(0, binding.ExitCode, binding.Output);
            Assert.IsTrue(File.Exists(Path.Combine(bridge, $"obj/SharpBind/Debug/{bridgeFramework}/Generated/NativeExports.g.cs")), binding.Output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
