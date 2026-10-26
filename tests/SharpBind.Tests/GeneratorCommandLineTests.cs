using System.Xml.Linq;
using SharpBind.Generator;

namespace SharpBind.Tests;

[TestClass]
public class GeneratorCommandLineTests
{
    [DataTestMethod]
    [TestCategory("BuildIntegration")]
    [DataRow("--python", "Usage:")]
    [DataRow("--unknown", "Usage:")]
    [DataRow("--module", "Usage:")]
    public void InvalidOptionsGiveActionableDiagnostics(string option, string expected)
    {
        var result = TestProcess.RunDotnet(typeof(BindingGenerator).Assembly.Location, option);

        Assert.AreEqual(1, result.ExitCode, result.Output);
        StringAssert.Contains(result.Output, "SharpBind : error :");
        StringAssert.Contains(result.Output, expected);
        Assert.IsFalse(result.Output.Contains("System.ArgumentException"), result.Output);
    }

    [TestMethod]
    public void ExpectedFailuresReportOnlyActionableMessages()
    {
        (Exception Error, string ExpectedDiagnostic)[] cases =
        [
            (new ArgumentException("Invalid module name."),
                "SharpBind : error : Invalid module name."),
            (new NotSupportedException("Unsupported binding type 'DateTime'."),
                "SharpBind : error : Unsupported binding type 'DateTime'."),
            (new InvalidDataException("Invalid configuration 'bindings.json'.", new FormatException("Bad input.")),
                "SharpBind : error : Invalid configuration 'bindings.json'."),
            (new FileNotFoundException("Input assembly 'missing.dll' was not found."),
                "SharpBind : error : Input assembly 'missing.dll' was not found.")
        ];

        foreach (var (error, expectedDiagnostic) in cases)
        {
            using var output = new StringWriter();
            int exitCode = Program.ReportFailure(error, output);

            Assert.AreEqual(1, exitCode, error.GetType().Name);
            Assert.AreEqual(expectedDiagnostic + Environment.NewLine, output.ToString(), error.GetType().Name);
        }
    }

    [DataTestMethod]
    [DataRow("\n", DisplayName = "Validation diagnostics with LF line endings")]
    [DataRow("\r\n", DisplayName = "Validation diagnostics with CRLF line endings")]
    [DataRow("\r", DisplayName = "Validation diagnostics with CR line endings")]
    public void ValidationFailuresPreserveDiagnosticOrderAndNormalizeLineEndings(string lineEnding)
    {
        const string message = """
            Invalid exported API:
            Duplicate Python class 'Counter'.
            """;
        const string expected = """
            SharpBind : error : Invalid exported API:
            SharpBind : error : Duplicate Python class 'Counter'.

            """;
        var error = new BindingValidationException(message.ReplaceLineEndings(lineEnding));
        using var output = new StringWriter();

        int exitCode = Program.ReportFailure(error, output);

        Assert.AreEqual(1, exitCode);
        Assert.AreEqual(expected.ReplaceLineEndings(Environment.NewLine), output.ToString());
    }

    [TestMethod]
    public void UnexpectedFailuresPreserveExceptionTypeStackTraceAndInnerException()
    {
        var failure = Assert.ThrowsException<InvalidOperationException>(FailUnexpectedly);
        using var output = new StringWriter();
        Assert.AreEqual(1, Program.ReportFailure(failure, output));
        string text = output.ToString();
        StringAssert.Contains(text, "SharpBind : error : Unexpected generation failure:");
        StringAssert.Contains(text, "System.InvalidOperationException: Generator bug.");
        StringAssert.Contains(text, "System.ArgumentException: Underlying cause.");
        StringAssert.Contains(text, nameof(FailUnexpectedly));
    }

    private static void FailUnexpectedly() =>
        throw new InvalidOperationException("Generator bug.", new ArgumentException("Underlying cause."));

    [DataTestMethod]
    [TestCategory("BuildIntegration")]
    [DataRow(false, false, 0, "warning", "Missing summary documentation",
        DisplayName = "Missing documentation produces MSBuild warnings")]
    [DataRow(false, true, 1, "error", "Missing summary documentation",
        DisplayName = "Warnings as errors rejects missing documentation")]
    [DataRow(true, false, 1, "error", "Usage:",
        DisplayName = "Invalid options produce an MSBuild error")]
    public void MsbuildRecognizesWarningsAndErrorsWithoutCodes(
        bool invalidOptions, bool warningsAsErrors, int exitCode, string severity, string expectedMessage)
    {
        string directory = Path.Combine(Path.GetTempPath(), "SharpBind.BuildMessages", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // Omitting the fixture's XML documentation produces real generator warnings.
            string assembly = Path.Combine(directory, "Fixture.dll");
            File.Copy(typeof(Fixture.NamedApi).Assembly.Location, assembly);
            File.Copy(typeof(PythonExportAttribute).Assembly.Location, Path.Combine(directory, "SharpBind.dll"));
            string native = Path.Combine(directory, "generated");
            string arguments = invalidOptions
                ? "--unknown"
                : $"""--assembly "{assembly}" --native "{native}" --library NativeTest""";
            string project = Path.Combine(directory, "diagnostics.proj");
            new XElement("Project",
                new XElement("Target", new XAttribute("Name", "Generate"),
                    new XElement("Exec",
                        new XAttribute("Command", $"""dotnet "{typeof(BindingGenerator).Assembly.Location}" {arguments}""")))).Save(project);
            var command = new List<string> { "msbuild", project, "-t:Generate", "-v:minimal", "-nologo" };
            if (warningsAsErrors)
                command.Add("-warnaserror");

            var result = TestProcess.RunDotnet(command.ToArray());
            string output = result.Output;

            Assert.AreEqual(exitCode, result.ExitCode, output);

            string diagnosticPrefix = $"SharpBind : {severity} : ";
            string[] outputLines = output.Split('\n', StringSplitOptions.TrimEntries);
            string[] diagnostics = outputLines
                .Where(line => line.StartsWith(diagnosticPrefix, StringComparison.Ordinal))
                .ToArray();
            Assert.IsTrue(diagnostics.Length > 0, $"Expected an MSBuild {severity} diagnostic.{Environment.NewLine}{output}");

            // MSBuild appends the project location to recognized diagnostics, not ordinary tool output.
            foreach (string diagnostic in diagnostics)
            {
                StringAssert.Contains(diagnostic, expectedMessage);
                StringAssert.EndsWith(diagnostic, $"[{project}]");
            }

            if (invalidOptions)
                Assert.IsFalse(Directory.Exists(native), "Invalid options must fail before writing generated files.");
            else
                Assert.IsTrue(File.Exists(Path.Combine(native, "NativeExports.g.cs")), output);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
