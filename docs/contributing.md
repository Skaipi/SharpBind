# Contributing to SharpBind

Use .NET 10, conventional 64-bit CPython 3.11 or higher, and Native AOT build tools for your target OS.
Consumer binding projects support .NET 9.0 or higher.

## Build and test

From the repository root:

```shell
python -m pip install hatchling twine setuptools numpy
dotnet test tests/SharpBind.Tests/SharpBind.Tests.csproj --filter "TestCategory!=BuildIntegration"
python tests/package_smoke.py
```

The default smoke test builds the runtime wheel and packages the source fixture through the MSBuild target, then runs the integration tests and examples against an isolated installation with .NET absent from PATH.

The managed filter skips tests that launch the generator or MSBuild in another process.
Run the unfiltered command when changing CLI diagnostics or build targets.

After a successful smoke test, inspect the generated wrappers in `artifacts/fixtures/sharpbind_fixture/_bindings.py` and `_bindings.pyi`.
These files contain the last successfully tested output.

The completed run writes `artifacts/latest-build.json` with the exact wheel and workspace paths.
Test the same build on additional interpreters with:

```shell
python tests/python_matrix.py --python /path/to/python3.11 --python /path/to/python3.12
```

Packaging unit tests use source build helpers and need no installed fixture or runtime. Run:

```shell
python -m unittest discover -s tests -p "test_*.py" -v
```

Installed-package tests live in `tests/integration`, grouped by API behavior.
Prefer tests with one behavioral purpose, descriptive input names, and `subTest` for tables of cases.
Assert observable behavior rather than internal aliases or generated formatting.
Keep memory ownership and interop checks when refactoring.

## Build local packages

Use this check when changing NuGet contents, templates, MSBuild integration, or Python packaging, or when you need packages to try in another project:

```shell
dotnet test tests/SharpBind.Tests/SharpBind.Tests.csproj
python tests/package_smoke.py --full
```

The unfiltered managed command includes tests that launch the generator and MSBuild.
The `--full` smoke test then:

1. Packs `src/SharpBind/SharpBind.csproj` into a NuGet package containing the library, generator, build helpers, and Python project template.
2. Installs that template in an isolated template directory, creates a consumer, and restores SharpBind from the local NuGet package.
3. Builds a fixture wheel, removes a temporary export, and rebuilds to check that deleted exports disappear and handwritten Python sources survive.
4. Builds a second independently named package, installs both fixture wheels offline in a fresh virtual environment, and checks native ownership and package isolation with .NET absent from PATH.

### Find the packages

After the command succeeds, inspect the manifest again:

```shell
python -m json.tool artifacts/latest-build.json
```

Check that `full` is `true`. In the paths below, replace `<workspace>` with its `workspace` value and `<wheels>` with its `wheels` value.

| Location | Contents or purpose |
| --- | --- |
| `<workspace>/packages/SharpBind.<version>.nupkg` | NuGet library, generator, build helpers, and template |
| `<wheels>/sharpbind_fixture-*.whl` | Generated bindings, private support, CPython adapter, and Native AOT fixture |
| `<wheels>/sharpbind_peer-*.whl` | Independently bundled package for isolation and array interchange tests |
| `<wheels>/sharpbind_scalar-*.whl` | Scalar-only package tested without any Python dependencies |
| `<workspace>/NuGet.Config` | Routes SharpBind restores to `packages` and uses a workspace-local NuGet cache |
| `<workspace>/template-hive` | Template installation created by the test |
| `<workspace>/Managed` and `<workspace>/python` | Managed library and generated Python project used by the test |
| `<workspace>/consumer-test` | Virtual environment and integration tests used to verify installation |

These outputs are retained after the test.
Ordinary `--full` runs put wheels in `<workspace>/wheels` and skip platform repair.

For release checks, install `abi3audit` and the [platform repair tools](configuration.md#wheel-builds),
then run `python tests/package_smoke.py --audit`. This includes the full package test,
repairs the wheels, and audits every consumer wheel with `abi3audit --strict` before testing installation.

### Try the local packages in a new project

Create your trial project inside the reported workspace so its `NuGet.Config` selects the freshly built SharpBind package:

```shell
cd "<workspace>"
dotnet new classlib -n Example
dotnet add Example/Example.csproj package SharpBind --version <version>
```

Add the documented `Counter` class from [Create the C# API](getting-started.md#create-the-c-api) to `Example/Counter.cs`.
In `Example/Example.csproj`, enable XML documentation inside its `PropertyGroup`:

```xml
<GenerateDocumentationFile>true</GenerateDocumentationFile>
```

Create the Python project using the template installation from this build, then build a local wheel.
Run these commands while still in `<workspace>`:

```shell
dotnet new sharpbind-python -n ExampleNative -o example-python --module example_lib --library ../../Example/Example.csproj --debug:custom-hive "<workspace>/template-hive"
dotnet msbuild example-python/native/ExampleNative.csproj -restore -t:SharpBindPackage -p:Configuration=Release -p:SharpBindLocalWheel=true
```

The `--library` path is relative to the generated `example-python/native` directory.
`--debug:custom-hive` selects the isolated template installed by `--full`.
`SharpBindLocalWheel=true` skips platform repair.
The resulting wheel is in `example-python/dist`.

Install your generated wheel. Its implementation is self-contained; numerical
APIs declare NumPy automatically:

```shell
python -m pip install --force-reinstall --find-links example-python/dist "example-lib==<version>"
python -c "from example_lib import Counter; counter = Counter(10); print(counter.add(5)); counter.close()"
```

The last command should print `15`.
After changing your example, rerun the MSBuild command and reinstall its wheel.
After changing SharpBind itself, rerun `--full` and use the new manifest paths so the trial project uses the new packages and cache.

To return to source development, change back to the repository root.
The routine smoke test uses `tests/NativeFixture/NativeFixture.csproj` with project references and an explicit generator path, so it tests source changes without packing NuGet or installing the template.

## Code organization

ABI declarations and primitive mappings are maintained directly in C, C# and Python.
See [ABI maintenance](abi-maintenance.md) for the files that must agree.

The binding generator produces consumer wrappers and native exports from annotated managed APIs.
Rebuild those outputs and native binaries together after changing ABI contracts.

Read the implementation in the order one API call follows:

1. `src/SharpBind.Generator/BindingGenerator.cs` coordinates discovery, validation and rendering.
`TypeMapping.cs` holds the type representations; `BindingSchema.cs` writes their call descriptions.
2. `PythonEmitter.cs` produces wrappers importing package-private `_sharpbind`
support from `python/common-runtime`. That directory owns the interoperability
implementation; `build/python/sharpbind_pack/adapter.py` handles bundling its
sources and compiling the private CPython adapter.
3. `_binding.py` prepares arguments and reads results.
`_provider.py` loads the library, binds ctypes signatures from `_abi.py`, and translates native errors.
4. `src/SharpBind.Generator/NativeEmitter.cs` emits native entry points that call the managed API through `src/SharpBind/Interop`.
5. For shared arrays, follow `_borrow.py` and `_result.py` into `python/common-runtime/native`.
`abi.h` defines the C layouts and is embedded directly by the generator when it emits a consumer header.

Edit generator emitters and `src/SharpBind.Generator/Templates/SupportExports.cs.txt` to change generated code.
Edit `src/SharpBind/Interop` or `python/common-runtime/native` to change compiled interop support.
Generated wrappers and native exports are build outputs; regenerate them after changing their source.

Add managed regression cases in `tests/SharpBind.Tests` and Python behavior cases in the appropriate `tests/integration/test_*.py` file.
Ownership and lifetime changes need a fresh smoke test so the checks exercise newly compiled native libraries through installed wheels.

Keep methods at one abstraction level.
Explain ownership transitions and protocol constraints in comments.
Avoid one-line blocks that hide cleanup and failure paths.

Build helpers live in `build/python` and ship inside NuGet.
They use the Python prerequisites installed at the start of this guide, plus platform repair tools for releases.
Set `SharpBindPythonExecutable` to an interpreter's full path when calling MSBuild directly to use an environment other than `python` on PATH.

For Python formatting, install and run Black from the repository root:

```shell
python -m pip install black
python -m black --exclude '/(artifacts|bin|obj)/' build/python python/common-runtime/src tests
```

Use the repository `.clang-format` for native C. C# follows `.editorconfig`.

## Documentation checks

After editing documentation, restore the pinned DocFX tool and build the site from the repository root:

```shell
dotnet tool restore
dotnet docfx docs/docfx.json --warningsAsErrors
```

The rendered site is written to `docs/_site`.
Fix warnings, including unresolved links, before submitting the change.
Use relative Markdown links, keep the handwritten attribute reference consistent with the public attributes, and check that quickstart commands still follow the current build workflow.
