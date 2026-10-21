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
    public void ExpectedFailuresReportMessagesAndKeepMultilineDetails()
    {
        foreach (Exception error in new Exception[] {
            new ArgumentException("Invalid module name."),
            new NotSupportedException("Unsupported binding type 'DateTime'."),
            new InvalidDataException("Invalid configuration 'bindings.json'.", new FormatException("Bad input.")),
            new FileNotFoundException("Input assembly 'missing.dll' was not found."),
            new BindingValidationException("Invalid exported API:\nDuplicate Python class 'Counter'.") })
        {
            using var output = new StringWriter();
            Assert.AreEqual(1, Program.ReportFailure(error, output));
            foreach (string line in error.Message.Split('\n'))
                StringAssert.Contains(output.ToString(), "SharpBind : error : " + line);
            Assert.IsFalse(output.ToString().Contains(error.GetType().FullName!), output.ToString());
        }
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
    [DataRow(false, false, 0)]
    [DataRow(false, true, 1)]
    [DataRow(true, false, 1)]
    public void MsbuildRecognizesWarningsAndErrorsWithoutCodes(bool invalidOptions, bool warningsAsErrors, int exitCode)
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
            string arguments = invalidOptions ? "--unknown" : $"--assembly \"{assembly}\" --native \"{native}\" --library NativeTest";
            string project = Path.Combine(directory, "diagnostics.proj");
            new XElement("Project",
                new XElement("Target", new XAttribute("Name", "Generate"),
                    new XElement("Exec",
                        new XAttribute("Command", $"dotnet \"{typeof(BindingGenerator).Assembly.Location}\" {arguments}")))).Save(project);
            var command = new List<string> { "msbuild", project, "-t:Generate", "-v:minimal", "-nologo" };
            if (warningsAsErrors)
                command.Add("-warnaserror");

            var result = TestProcess.RunDotnet(command.ToArray());
            string output = result.Output;

            Assert.AreEqual(exitCode, result.ExitCode, output);
            StringAssert.Contains(output, invalidOptions ? "Usage:" : "Missing summary documentation");
            // MSBuild appends the project location to recognized diagnostics, not ordinary tool output.
            Assert.IsTrue(output.Split('\n').Any(line => line.Contains("SharpBind :") && line.Contains($"[{project}]")), output);
            if (!invalidOptions)
                Assert.IsTrue(File.Exists(Path.Combine(native, "NativeExports.g.cs")), output);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
