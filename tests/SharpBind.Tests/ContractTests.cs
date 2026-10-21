using System.Text.Json;
using SharpBind.Generator;

namespace SharpBind.Tests;

[TestClass]
public class ContractTests
{
    private string directory = null!;
    [TestInitialize]
    public void Initialize() => directory = Path.Combine(Path.GetTempPath(), "SharpBind.Contract", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    private void Generate(string library = "FixtureNative", bool strict = false, string module = "sharpbind_fixture")
    {
        var options = new GeneratorOptions("unused", directory, library, module,
            Path.Combine(directory, "python"), strict);
        BindingGenerator.Generate(typeof(Fixture.NamedApi).Assembly, options);
    }
    private string Read(string file) => File.ReadAllText(Path.Combine(directory, file));

    [TestMethod]
    public void DocumentationAndTypingFilesAreEmitted()
    {
        Generate(strict: true);
        string python = Read("python/_bindings.py"), stub = Read("python/_bindings.pyi");
        foreach (string output in new[] { python, stub })
        {
            StringAssert.Contains(output, "Return value as an integer.");
        }
        StringAssert.Contains(Read("NativeExports.g.cs"), ".@EchoInt");
        Assert.IsTrue(File.Exists(Path.Combine(directory, "python/py.typed")));
    }

    [DataTestMethod]
    [DataRow("RenamedNativeFile", "sharpbind_fixture")]
    [DataRow("FixtureNative", "another_module")]
    public void LibraryAndModuleNamesDoNotChangeNativeExports(string library, string module)
    {
        Generate();
        string source = Read("NativeExports.g.cs");
        Generate(library: library, module: module);
        using var schema = JsonDocument.Parse(Read("bindings.json"));
        Assert.AreEqual(library, schema.RootElement.GetProperty("library").GetString());
        Assert.AreEqual(module, schema.RootElement.GetProperty("python_module").GetString());
        Assert.AreEqual(source, Read("NativeExports.g.cs"));
    }

    [TestMethod]
    [TestCategory("BuildIntegration")]
    public void InheritedDocumentationRequiresADirectFacadeDescription()
    {
        Directory.CreateDirectory(directory);
        string original = typeof(Fixture.NamedApi).Assembly.Location;
        string assembly = Path.Combine(directory, "Fixture.dll");
        File.Copy(original, assembly);
        var xml = System.Xml.Linq.XDocument.Load(Path.ChangeExtension(original, ".xml"));
        xml.Descendants("member").First(m => (string?)m.Attribute("name") ==
            "M:SharpBind.Fixture.NamedApi.EchoInt(System.Int32)").Add(new System.Xml.Linq.XElement("inheritdoc"));
        xml.Save(Path.ChangeExtension(assembly, ".xml"));
        File.Copy(typeof(SharpBind.PythonExportAttribute).Assembly.Location, Path.Combine(directory, "SharpBind.dll"));
        string generated = Path.Combine(directory, "generated");
        var result = TestProcess.RunDotnet(typeof(BindingGenerator).Assembly.Location, "--assembly", assembly,
            "--native", generated, "--library", "Native", "--strict-docs", "true");

        Assert.AreEqual(1, result.ExitCode, result.Output);
        StringAssert.Contains(result.Output, "document the exported facade directly");
        Assert.IsFalse(Directory.Exists(generated));
    }
}
