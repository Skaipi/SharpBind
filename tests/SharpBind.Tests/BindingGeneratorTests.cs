using System.Reflection;
using System.Reflection.Emit;
using SharpBind;
using SharpBind.Generator;

namespace SharpBind.Tests;

[TestClass]
public class BindingGeneratorTests
{
    private string directory = null!;

    [TestInitialize]
    public void Initialize() => directory = Path.Combine(Path.GetTempPath(), "SharpBind.Tests", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private string PackageDirectory => Path.Combine(directory, "python", "test_api");

    private GeneratorOptions Options => new(
        AssemblyPath: "unused",
        NativeDirectory: directory,
        LibraryName: "NativeTest",
        PythonModuleName: "test_api",
        PythonPackageDirectory: PackageDirectory,
        StrictDocumentation: false);

    private void Generate(Assembly assembly)
    {
        BindingGenerator.Generate(assembly, Options);
    }

    [TestMethod]
    public void GenerationPreservesHandwrittenSourcesAndRegeneratesDeletedOutputs()
    {
        Directory.CreateDirectory(PackageDirectory);

        string entry = Path.Combine(PackageDirectory, "__init__.py");
        File.WriteAllText(entry, "# handwritten facade\n");

        var assembly = Define(type => Method(type));
        Generate(assembly);

        string native = Path.Combine(directory, "NativeExports.g.cs");
        string bindings = Path.Combine(PackageDirectory, "_bindings.py");
        string[] outputs = [
            native,
            Path.Combine(directory, "bindings.h"),
            Path.Combine(directory, "bindings.json"),
            bindings,
            Path.Combine(PackageDirectory, "_bindings.pyi"),
            Path.Combine(PackageDirectory, "py.typed")
        ];
        var originals = outputs.ToDictionary(path => path, File.ReadAllText);
        var timestamps = outputs.ToDictionary(path => path, File.GetLastWriteTimeUtc);
        Generate(assembly);

        foreach (string path in outputs)
        {
            Assert.AreEqual(originals[path], File.ReadAllText(path), path);
            Assert.AreEqual(timestamps[path], File.GetLastWriteTimeUtc(path), path);
            File.Delete(path);
        }
        StringAssert.Contains(originals[native], ".@Run(");
        StringAssert.Contains(originals[bindings], "test_api");
        StringAssert.Contains(originals[bindings], "def run(");
        Generate(assembly);
        foreach (string path in outputs)
            Assert.AreEqual(originals[path], File.ReadAllText(path), path);

        Generate(Define(_ => { }));
        Assert.IsFalse(File.ReadAllText(native).Contains(".@Run("));
        Assert.IsFalse(File.ReadAllText(bindings).Contains("def run("));
        Assert.AreEqual("# handwritten facade\n", File.ReadAllText(entry));
    }

    [TestMethod]
    public void ModuleNamesAreIndependentAndValidatedBeforeWriting()
    {
        var assembly = Define(type => Method(type));

        foreach (string name in new[] { "", "example-lib", "a.b", "class", "9module", "é", "../escape" })
        {
            Assert.ThrowsException<ArgumentException>(() =>
                BindingGenerator.Generate(assembly, Options with
                {
                    PythonModuleName = name
                }));

            Assert.IsFalse(Directory.Exists(directory));
        }

        Assert.ThrowsException<ArgumentException>(() =>
            BindingGenerator.Generate(assembly, Options with
            {
                PythonPackageDirectory = null
            }));

        Assert.ThrowsException<ArgumentException>(() =>
            BindingGenerator.Generate(assembly, Options with
            {
                PythonModuleName = null
            }));

        // Unlike exported methods, import names may be identifiers such as 'close' or '_api'.
        BindingGenerator.Generate(assembly, Options with
        {
            PythonModuleName = "close"
        });

        BindingGenerator.Generate(assembly, Options with
        {
            PythonModuleName = "_api"
        });
    }

    [TestMethod]
    public void ManagedOnlyGenerationDoesNotRequirePythonConfiguration()
    {
        BindingGenerator.Generate(
            Define(type => Method(type)),
            Options with
            {
                PythonModuleName = null,
                PythonPackageDirectory = null
            });
        Assert.IsTrue(File.Exists(Path.Combine(directory, "NativeExports.g.cs")));
        Assert.IsTrue(File.Exists(Path.Combine(directory, "bindings.json")));
        Assert.AreEqual(0, Directory.GetFiles(directory, "*.py", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public void ConcurrencyOptionCannotBeAppliedToAMethod()
    {
        var assembly = Define(type =>
        {
            var method = type.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(void), []);
            method.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(PythonExportAttribute).GetConstructor(Type.EmptyTypes)!, [],
                [typeof(PythonExportAttribute).GetProperty(nameof(PythonExportAttribute.ConcurrentCalls))!], [true]));
            method.GetILGenerator().Emit(OpCodes.Ret);
        });
        var error = Assert.ThrowsException<BindingValidationException>(() => Generate(assembly));

        StringAssert.Contains(error.Message, "ConcurrentCalls can only be specified on a class");
        Assert.IsFalse(Directory.Exists(directory), "Invalid concurrency settings must fail before writing outputs.");
    }

    private static CustomAttributeBuilder Export(string? name = null, params Type[] typeArguments)
    {
        var constructor = typeArguments.Length == 0
            ? typeof(PythonExportAttribute).GetConstructor(Type.EmptyTypes)!
            : typeof(PythonExportAttribute).GetConstructor([typeof(Type[])])!;
        object[] arguments = typeArguments.Length == 0 ? [] : [typeArguments];
        return name is null ? new CustomAttributeBuilder(constructor, arguments) :
            new CustomAttributeBuilder(constructor, arguments, [typeof(PythonExportAttribute).GetProperty("Name")!], [name]);
    }

    private static Assembly Define(Action<TypeBuilder> configure, bool annotateClass = false, string name = "Example")
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("BindingFixture" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
        var type = assembly.DefineDynamicModule("Main").DefineType(name, TypeAttributes.Public | TypeAttributes.Class);
        if (annotateClass)
            type.SetCustomAttribute(Export());
        configure(type);
        type.CreateType();
        return assembly;
    }

    private static MethodBuilder Method(TypeBuilder type, string name = "Run", Type? result = null,
        MethodAttributes attributes = MethodAttributes.Public | MethodAttributes.Static, string? exportName = null,
        Type[]? parameters = null)
    {
        var method = type.DefineMethod(name, attributes, result ?? typeof(void), parameters ?? []);
        method.SetCustomAttribute(Export(exportName));
        method.GetILGenerator().Emit(OpCodes.Ret); // Signatures are inspected; these fixture methods are never invoked.
        return method;
    }

    [TestMethod]
    public void UnsupportedSignaturesFailBeforeWritingOutputs()
    {
        var valid = Define(type => Method(type));
        Generate(valid);
        string path = Path.Combine(directory, "NativeExports.g.cs");
        string original = File.ReadAllText(path);
        foreach (Type result in new[]
        {
            typeof(DateTime),
            typeof(object),
            typeof(int[,]),
            typeof(bool[]),
            typeof(ReadOnlyTensorView<double>),
            typeof(TensorView<double>)
        })
        {
            Assert.ThrowsException<NotSupportedException>(() => Generate(Define(type => Method(type, result: result))));
            Assert.AreEqual(original, File.ReadAllText(path));
        }
    }

    [TestMethod]
    public void AmbiguousOverloadsRequireExplicitNames()
    {
        var assembly = Define(type =>
        {
            Method(type);
            Method(type, parameters: [typeof(int)]);
        });
        var error = Assert.ThrowsException<BindingValidationException>(() => Generate(assembly));

        StringAssert.Contains(error.Message, "duplicate exported member 'run'");
        Assert.IsFalse(Directory.Exists(directory), "Ambiguous overloads must fail before writing outputs.");

        Generate(Define(type =>
        {
            Method(type);
            Method(type, exportName: "run_int", parameters: [typeof(int)]);
        }));

        string python = File.ReadAllText(Path.Combine(PackageDirectory, "_bindings.py"));
        StringAssert.Contains(python, "def run(");
        StringAssert.Contains(python, "def run_int(");
    }

    [TestMethod]
    public void NonpublicMethodsAreRejected()
    {
        var assembly = Define(type =>
            Method(type, attributes: MethodAttributes.Private | MethodAttributes.Static));

        var error = Assert.ThrowsException<NotSupportedException>(() => Generate(assembly));

        StringAssert.Contains(error.Message, "only public synchronous ordinary methods");
        Assert.IsFalse(Directory.Exists(directory));
    }

    [TestMethod]
    public void RefParametersAreRejected()
    {
        var assembly = Define(type => Method(type, parameters: [typeof(int).MakeByRefType()]));

        var error = Assert.ThrowsException<NotSupportedException>(() => Generate(assembly));

        StringAssert.Contains(error.Message, "ref, out and params are unsupported");
        Assert.IsFalse(Directory.Exists(directory));
    }

    [TestMethod]
    public void InstanceMethodsRequireAnExportedClass()
    {
        var assembly = Define(type => Method(type, attributes: MethodAttributes.Public));

        var error = Assert.ThrowsException<NotSupportedException>(() => Generate(assembly));

        StringAssert.Contains(error.Message, "instance bindings require an exported class");
        Assert.IsFalse(Directory.Exists(directory));
    }

    [TestMethod]
    public void AsyncMethodsAreRejected()
    {
        var assembly = Define(type =>
        {
            var method = Method(type);
            method.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(System.Runtime.CompilerServices.AsyncStateMachineAttribute).GetConstructor([typeof(Type)])!, [typeof(object)]));
        });

        var error = Assert.ThrowsException<NotSupportedException>(() => Generate(assembly));

        StringAssert.Contains(error.Message, "only public synchronous ordinary methods");
        Assert.IsFalse(Directory.Exists(directory));
    }

    [TestMethod]
    public void NamesAreValidatedAndCollisionsFail()
    {
        foreach (string name in new[] { "bad-name", "class", "close" })
            Assert.ThrowsException<ArgumentException>(() => Generate(Define(type => Method(type, exportName: name))));
        var duplicate = Define(type =>
        {
            Method(type, "URLValue");
            Method(type, "UrlValue");
        });
        Assert.ThrowsException<BindingValidationException>(() => Generate(duplicate));
        Generate(Define(type => Method(type), name: "RuntimeError"));
    }

    [TestMethod]
    public void DynamicCodeRequirementsFailBeforeEmission()
    {
        var assembly = Define(type =>
        {
            var method = Method(type);
            method.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute).GetConstructor([typeof(string)])!, ["Needs JIT"]));
        });

        var error = Assert.ThrowsException<NotSupportedException>(() => Generate(assembly));

        StringAssert.Contains(error.Message, "RequiresDynamicCode");
        Assert.IsFalse(Directory.Exists(directory));
    }

    [TestMethod]
    public void MatricesAndJaggedInputsRequireTensorViews()
    {
        foreach (var type in new[] { typeof(double[,]), typeof(double[][]) })
            Assert.ThrowsException<NotSupportedException>(() => Generate(Define(t => Method(t, parameters: [type]))));
        Assert.IsFalse(Directory.Exists(directory));
    }

    [TestMethod]
    public void TensorLeasesRequireNumericNonNullInputs()
    {
        var mapped = ContractValidator.Map(typeof(Tensor<double>), false, NullabilityState.NotNull);
        Assert.AreEqual(LogicalTypeKind.Tensor, mapped.Kind);
        Assert.AreEqual(TensorLifetime.Retainable, mapped.Logical.Lifetime);
        Assert.IsFalse(mapped.Logical.Nullable);
        Assert.ThrowsException<NotSupportedException>(() =>
            ContractValidator.Map(typeof(Tensor<double>), false, NullabilityState.Nullable));
        Assert.AreEqual(LogicalTypeKind.Tensor, TypeMapping.Map(typeof(Tensor<double>), true).Kind);
        Assert.ThrowsException<NotSupportedException>(() =>
            TypeMapping.Map(typeof(Tensor<bool>), false));
    }

    [TestMethod]
    public void NamedAndDuplicateConstructorsAreRejected()
    {
        foreach (bool named in new[] { true, false })
        {
            var assembly = Define(type =>
            {
                var first = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, []);
                first.SetCustomAttribute(Export(named ? "create" : null));
                first.GetILGenerator().Emit(OpCodes.Ret);
                if (!named)
                {
                    var second = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [typeof(int)]);
                    second.SetCustomAttribute(Export());
                    second.GetILGenerator().Emit(OpCodes.Ret);
                }
            }, annotateClass: true);
            Assert.ThrowsException<BindingValidationException>(() => Generate(assembly));
            Assert.IsFalse(Directory.Exists(directory));
        }
    }

    [TestMethod]
    public void GenericClassesAreExportedOncePerNamedSpecialization()
    {
        var genericClass = Define(type =>
        {
            var parameter = type.DefineGenericParameters("T").Single();
            type.SetCustomAttribute(Export("IntegerExample", typeof(int)));
            type.SetCustomAttribute(Export("DoubleExample", typeof(double)));
            Method(type, "Echo", parameter, parameters: [parameter]);
        });

        Generate(genericClass);

        string native = File.ReadAllText(Path.Combine(directory, "NativeExports.g.cs"));
        StringAssert.Contains(native, "global::Example<global::System.Int32>");
        StringAssert.Contains(native, "global::Example<global::System.Double>");
        string python = File.ReadAllText(Path.Combine(PackageDirectory, "_bindings.py"));
        StringAssert.Contains(python, "class IntegerExample:");
        StringAssert.Contains(python, "class DoubleExample:");
    }

    [TestMethod]
    public void SpecializationsEmitExpectedManagedTypeNamesForEverySupportedValueType()
    {
        (Type Type, string Name)[] numeric =
        [
            (typeof(sbyte), "global::System.SByte"),
            (typeof(byte), "global::System.Byte"),
            (typeof(short), "global::System.Int16"),
            (typeof(ushort), "global::System.UInt16"),
            (typeof(int), "global::System.Int32"),
            (typeof(uint), "global::System.UInt32"),
            (typeof(long), "global::System.Int64"),
            (typeof(ulong), "global::System.UInt64"),
            (typeof(float), "global::System.Single"),
            (typeof(double), "global::System.Double")
        ];
        var supported = numeric
            .Concat(numeric.Select(t => (t.Type.MakeArrayType(), t.Name + "[]")))
            .Concat(numeric.Select(t => (typeof(Tensor<>).MakeGenericType(t.Type), "global::SharpBind.Tensor<" + t.Name + ">")))
            .Concat([(typeof(bool), "global::System.Boolean"), (typeof(string), "global::System.String")]);
        foreach (var (argument, expectedName) in supported)
        {
            var assembly = Define(type =>
            {
                var parameter = type.DefineGenericParameters("T").Single();
                type.SetCustomAttribute(Export("Specialized", argument));
                Method(type, "Echo", parameter, parameters: [parameter]);
            });
            Generate(assembly);
            StringAssert.Contains(File.ReadAllText(Path.Combine(directory, "NativeExports.g.cs")),
                "global::Example<" + expectedName + ">", argument.ToString());
        }
    }

    [TestMethod]
    public void NestedClosedTypesDistributeArgumentsToTheirDeclaringTypes()
    {
        Assert.AreEqual("global::System.Collections.Generic.Dictionary<global::System.Int32, global::System.String>.KeyCollection",
            CSharpTypeNames.Format(typeof(Dictionary<int, string>.KeyCollection)));
        Assert.AreEqual("global::SharpBind.Fixture.GenericContainer<global::System.Int32>.Inner<global::System.Double>",
            CSharpTypeNames.Format(typeof(Fixture.GenericContainer<int>.Inner<double>)));
    }

    [TestMethod]
    public void GenericClassesRequireSpecializationRegistrations()
    {
        var assembly = Define(type =>
        {
            type.DefineGenericParameters("T");
            Method(type);
        });

        var error = Assert.ThrowsException<BindingValidationException>(() => Generate(assembly));

        StringAssert.Contains(error.Message, "requires a PythonExport attribute");
        Assert.IsFalse(Directory.Exists(directory));
    }

    [TestMethod]
    public void GenericSpecializationsRequireExplicitNames()
    {
        var assembly = Define(type =>
        {
            type.DefineGenericParameters("T");
            type.SetCustomAttribute(Export(null, typeof(int)));
            Method(type);
        });

        var error = Assert.ThrowsException<BindingValidationException>(() => Generate(assembly));

        StringAssert.Contains(error.Message, "requires a unique Python name");
        Assert.IsFalse(Directory.Exists(directory));
    }

    [TestMethod]
    public void GenericSpecializationsRequireAllTypeArguments()
    {
        var assembly = Define(type =>
        {
            type.DefineGenericParameters("T");
            type.SetCustomAttribute(Export("IntegerExample"));
            Method(type);
        });

        var error = Assert.ThrowsException<BindingValidationException>(() => Generate(assembly));

        StringAssert.Contains(error.Message, "specialization 'IntegerExample' supplies 0 type arguments for 1 generic parameters");
        Assert.IsFalse(Directory.Exists(directory));
    }

    [TestMethod]
    public void GenericSpecializationsRequireClosedTypeArguments()
    {
        var assembly = Define(type =>
        {
            type.DefineGenericParameters("T");
            type.SetCustomAttribute(Export("OpenExample", typeof(List<>)));
            Method(type);
        });

        var error = Assert.ThrowsException<BindingValidationException>(() => Generate(assembly));

        StringAssert.Contains(error.Message, "specialization 'OpenExample' must use closed type arguments");
        Assert.IsFalse(Directory.Exists(directory));
    }

    [TestMethod]
    public void GenericSpecializationsMustSatisfyTypeConstraints()
    {
        var assembly = Define(type =>
        {
            var parameter = type.DefineGenericParameters("T").Single();
            parameter.SetGenericParameterAttributes(GenericParameterAttributes.ReferenceTypeConstraint);
            type.SetCustomAttribute(Export("InvalidExample", typeof(int)));
            Method(type);
        });

        var error = Assert.ThrowsException<BindingValidationException>(() => Generate(assembly));

        StringAssert.Contains(error.Message, "specialization 'InvalidExample' has invalid type arguments");
        Assert.IsInstanceOfType<ArgumentException>(error.InnerException);
        Assert.IsFalse(Directory.Exists(directory));
    }

    [TestMethod]
    public void GenericSpecializationsRequireUniquePythonNames()
    {
        var assembly = Define(type =>
        {
            type.DefineGenericParameters("T");
            type.SetCustomAttribute(Export("Example", typeof(int)));
            type.SetCustomAttribute(Export("Example", typeof(double)));
            Method(type);
        });

        var error = Assert.ThrowsException<BindingValidationException>(() => Generate(assembly));

        Assert.AreEqual("Duplicate Python class 'Example'.", error.Message);
        Assert.IsFalse(Directory.Exists(directory));
    }

    [TestMethod]
    public void NonGenericClassesRejectSpecializationArguments()
    {
        var assembly = Define(type =>
        {
            type.SetCustomAttribute(Export("Example", typeof(int)));
            Method(type);
        });

        var error = Assert.ThrowsException<BindingValidationException>(() => Generate(assembly));

        StringAssert.Contains(error.Message, "type arguments can only specialize a generic class");
        Assert.IsFalse(Directory.Exists(directory));
    }

    [TestMethod]
    public void MethodsRejectSpecializationArguments()
    {
        var assembly = Define(type =>
        {
            var method = type.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(void), []);
            method.SetCustomAttribute(Export(null, typeof(int)));
            method.GetILGenerator().Emit(OpCodes.Ret);
        });

        var error = Assert.ThrowsException<BindingValidationException>(() => Generate(assembly));

        StringAssert.Contains(error.Message, "generic type arguments can only be specified on a class");
        Assert.IsFalse(Directory.Exists(directory));
    }

    [TestMethod]
    public void GenericMethodsRemainUnsupported()
    {
        var genericMethod = Define(type =>
        {
            var method = type.DefineMethod("Echo", MethodAttributes.Public | MethodAttributes.Static, typeof(void), []);
            method.DefineGenericParameters("T");
            method.SetCustomAttribute(Export());
            method.GetILGenerator().Emit(OpCodes.Ret);
        });
        Assert.ThrowsException<NotSupportedException>(() => Generate(genericMethod));
        Assert.IsFalse(Directory.Exists(directory));
    }

    [TestMethod]
    public void InstanceMethodsRequireAnExportedConstructor()
    {
        var assembly = Define(type => Method(type, attributes: MethodAttributes.Public), annotateClass: true);
        var error = Assert.ThrowsException<BindingValidationException>(() => Generate(assembly));

        StringAssert.Contains(error.Message, "instance methods require an exported constructor");
        Assert.IsFalse(Directory.Exists(directory));
    }

    [TestMethod]
    public void StaticNamespacesDoNotInheritInstanceOwnership()
    {
        Generate(Define(type => Method(type)));
        foreach (var filename in new[] { "_bindings.py", "_bindings.pyi" })
        {
            string python = File.ReadAllText(Path.Combine(PackageDirectory, filename));
            StringAssert.Contains(python, "class Example:");
            Assert.IsFalse(python.Contains("class Example(_sb.NativeObject)"));
            Assert.IsFalse(python.Contains("_release_entry"));
        }
    }

    [TestMethod]
    public void PythonNumericalApiUsesNumpyAndPrivateRelativeSupport()
    {
        Generate(Define(type => Method(type, result: typeof(Tensor<double>), parameters: [typeof(TensorView<double>)])));
        string python = File.ReadAllText(Path.Combine(PackageDirectory, "_bindings.py"));
        string stub = File.ReadAllText(Path.Combine(PackageDirectory, "_bindings.pyi"));
        StringAssert.Contains(python, "from . import _sharpbind as _sb");
        StringAssert.Contains(python, "_npt.NDArray[_np.float64]");
        StringAssert.Contains(stub, "_npt.NDArray[_np.float64]");
        Assert.IsFalse(stub.Contains("_sb"));
        Assert.IsFalse(python.Contains("sharpbind_runtime"));
        Assert.IsFalse(python.Contains("TensorInput"));
    }

    [TestMethod]
    public void ScalarPythonApiDoesNotImportNumpy()
    {
        Generate(Define(type => Method(type, result: typeof(int), parameters: [typeof(int)])));
        foreach (string filename in new[] { "_bindings.py", "_bindings.pyi" })
            Assert.IsFalse(File.ReadAllText(Path.Combine(PackageDirectory, filename)).Contains("numpy"));
    }
}
