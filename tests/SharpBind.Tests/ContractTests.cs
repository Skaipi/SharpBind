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

    [DataTestMethod]
    [DataRow(typeof(void), "void", "void", "void", "void", "void")]
    [DataRow(typeof(string), "string", "byte", "uint8_t", "c_uint8", "uint8")]
    [DataRow(typeof(bool), "bool", "byte", "uint8_t", "c_uint8", "bool")]
    [DataRow(typeof(sbyte), "sbyte", "sbyte", "int8_t", "c_int8", "int8")]
    [DataRow(typeof(byte), "byte", "byte", "uint8_t", "c_uint8", "uint8")]
    [DataRow(typeof(short), "short", "short", "int16_t", "c_int16", "int16")]
    [DataRow(typeof(ushort), "ushort", "ushort", "uint16_t", "c_uint16", "uint16")]
    [DataRow(typeof(int), "int", "int", "int32_t", "c_int32", "int32")]
    [DataRow(typeof(uint), "uint", "uint", "uint32_t", "c_uint32", "uint32")]
    [DataRow(typeof(long), "long", "long", "int64_t", "c_int64", "int64")]
    [DataRow(typeof(ulong), "ulong", "ulong", "uint64_t", "c_uint64", "uint64")]
    [DataRow(typeof(float), "float", "float", "float", "c_float", "float32")]
    [DataRow(typeof(double), "double", "double", "double", "c_double", "float64")]
    public void ManagedTypesHaveExpectedBindingSpellings(
        Type type, string managed, string native, string c, string ctypes, string dtype)
    {
        var mapped = TypeMapping.Map(type, isResult: false);

        Assert.AreEqual(managed, mapped.ManagedTypeName, "Managed type spelling.");
        Assert.AreEqual(native, mapped.NativeTypeName, "Native C# type spelling.");
        Assert.AreEqual(c, mapped.CTypeName, "C type spelling.");
        Assert.AreEqual(ctypes, mapped.CtypesName, "ctypes type spelling.");
        Assert.AreEqual(dtype, mapped.NumpyDtype, "NumPy dtype spelling.");
    }

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

    [DataTestMethod]
    [DataRow("Functions", "fail",
        "SbError* error",
        "sb_error* error")]
    [DataRow("RetainedDataset", "increment",
        "SbHandle handle, SbError* error",
        "sb_handle handle, sb_error* error")]
    [DataRow("NamedApi", "__init__",
        "int a0, SbHandle* result, SbError* error",
        "int32_t a0, sb_handle* result, sb_error* error")]
    [DataRow("Functions", "negate",
        "byte a0, byte* result, SbError* error",
        "uint8_t a0, uint8_t* result, sb_error* error")]
    [DataRow("Functions", "echo_text",
        "byte* a0, long a0Length, byte** result, long* resultLength, SbError* error",
        "const uint8_t* a0, int64_t a0_length, uint8_t** result, int64_t* result_length, sb_error* error")]
    [DataRow("AccumulatorInt32", "echo",
        "int* a0, long a0Length, int** result, long* resultLength, SbError* error",
        "const int32_t* a0, int64_t a0_length, int32_t** result, int64_t* result_length, sb_error* error")]
    [DataRow("Tensors", "sum_view",
        "global::SharpBind.Interop.TensorDescriptor* a0, double* result, SbError* error",
        "const sb_tensor* a0, double* result, sb_error* error")]
    [DataRow("Tensors", "increment",
        "global::SharpBind.Interop.TensorDescriptor* a0, SbError* error",
        "const sb_tensor* a0, sb_error* error")]
    [DataRow("LeaseTests", "echo",
        "global::SharpBind.Interop.TensorDescriptor* a0, global::SharpBind.Interop.TensorDescriptor* result, SbError* error",
        "sb_tensor* a0, sb_tensor* result, sb_error* error")]
    public void NativeDeclarationsFollowTheSameCallContract(
        string className, string methodName, string csharpParameters, string cParameters)
    {
        Generate();
        using var schema = JsonDocument.Parse(Read("bindings.json"));
        var cls = schema.RootElement.GetProperty("classes").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == className);
        string entry = cls.GetProperty("methods").EnumerateArray()
            .Single(m => m.GetProperty("name").GetString() == methodName)
            .GetProperty("entry").GetString()!;

        string expectedCDeclaration = $"int32_t {entry}({cParameters});";
        string[] header = Read("bindings.h").Split('\n', StringSplitOptions.TrimEntries);
        string[] declarations = header
            .Where(line => line.StartsWith($"int32_t {entry}(", StringComparison.Ordinal))
            .ToArray();
        Assert.AreEqual(1, declarations.Length, $"Expected one C declaration for {className}.{methodName}.");
        Assert.AreEqual(expectedCDeclaration, declarations[0]);

        string[] source = Read("NativeExports.g.cs").Split('\n', StringSplitOptions.TrimEntries);
        string expectedAttribute =
            $"""[UnmanagedCallersOnly(EntryPoint = "{entry}", CallConvs = [typeof(CallConvCdecl)])]""";
        int attributeCount = source.Count(line => line == expectedAttribute);
        Assert.AreEqual(1, attributeCount, $"Expected one C# export for {className}.{methodName}.");

        int attributeIndex = Array.IndexOf(source, expectedAttribute);
        Assert.IsTrue(attributeIndex + 1 < source.Length, "The export attribute must be followed by a declaration.");
        string declaration = source[attributeIndex + 1];

        const string declarationPrefix = "public static int Export";
        StringAssert.StartsWith(declaration, declarationPrefix);
        int parametersStart = declaration.IndexOf('(');
        Assert.IsTrue(parametersStart > declarationPrefix.Length, $"Missing export index in: {declaration}");

        string exportIndex = declaration[declarationPrefix.Length..parametersStart];
        bool hasNumericExportIndex = exportIndex.All(char.IsAsciiDigit);
        Assert.IsTrue(hasNumericExportIndex, $"Expected a numeric export index in: {declaration}");

        string actualParameters = declaration[parametersStart..];
        Assert.AreEqual($"({csharpParameters})", actualParameters, $"C# parameters for {className}.{methodName}.");
    }

    [TestMethod]
    public void NativeDeclarationsRejectVoidArguments()
    {
        var type = TypeMapping.Map(typeof(void), isResult: false);
        Assert.ThrowsException<ArgumentException>(() => CsharpTypes.TypeSignature(type, "a0"));
        Assert.ThrowsException<ArgumentException>(() => CTypes.TypeSignature(type, "a0"));
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
