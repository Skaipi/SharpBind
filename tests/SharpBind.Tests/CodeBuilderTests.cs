using SharpBind.Generator;

namespace SharpBind.Tests;

[TestClass]
public class CodeBuilderTests
{
    [TestMethod]
    public void CSharpScopesRestoreIndentationAfterNestedBlocks()
    {
        var code = new CodeBuilder(CodeRules.CSharp);
        using (code.ScopeStatement("class Example"))
        {
            using (code.ScopeStatement("void Run()"))
                code.Line("return;");
            code.Line("int value;");
        }
        code.Line("// end");

        const string expected = """
            class Example
            {
                void Run()
                {
                    return;
                }
                int value;
            }
            // end

            """;
        Assert.AreEqual(expected.ReplaceLineEndings("\n"), code.ToString());
    }

    [TestMethod]
    public void PythonScopesUseOnlyHeadersAndIndentation()
    {
        var code = new CodeBuilder(CodeRules.Python);
        using (code.ScopeStatement("class Example:"))
        {
            using (code.ScopeStatement("def run(self):"))
                code.Line("return 1");
            code.Line("value = 2");
        }
        code.Line("other = 3");

        const string expected = """
            class Example:
                def run(self):
                    return 1
                value = 2
            other = 3

            """;
        Assert.AreEqual(expected.ReplaceLineEndings("\n"), code.ToString());
    }

    [DataTestMethod]
    [DataRow("first\r\n\r\n    second\r\n")]
    [DataRow("first\n\n    second\n")]
    [DataRow("first\r\r    second")]
    public void MultilineTemplatesPreserveSpacingAndNormalizeLineEndings(string template)
    {
        var code = new CodeBuilder(CodeRules.CSharp);
        using (code.ScopeStatement("class Example"))
            code.Lines(template);

        const string expected = """
            class Example
            {
                first

                    second
            }

            """;
        Assert.AreEqual(expected.ReplaceLineEndings("\n"), code.ToString());
    }

    [TestMethod]
    public void BlankLinesAreExplicitAndHaveNoIndentation()
    {
        var code = new CodeBuilder(CodeRules.Python);
        code.Lines("");
        using (code.ScopeStatement("class Example:"))
        {
            code.Line("");
            code.BlankLine();
            code.Lines("\nvalue = 1\n\n");
        }

        const string expected = """
            class Example:


                value = 1


            """;
        Assert.AreEqual(expected.ReplaceLineEndings("\n"), code.ToString());
    }

    [DataTestMethod]
    [DataRow("first\nsecond")]
    [DataRow("first\rsecond")]
    public void SingleLinesRejectEmbeddedLineEndings(string line)
    {
        var code = new CodeBuilder(CodeRules.CSharp);

        Assert.ThrowsException<ArgumentException>(() => code.Line(line));
        Assert.AreEqual("", code.ToString());
    }

    [TestMethod]
    public void ScopeDisposalRestoresIndentationOnFailureAndIsIdempotent()
    {
        var code = new CodeBuilder(CodeRules.CSharp);
        var scope = code.ScopeStatement("class Example");
        Assert.ThrowsException<InvalidOperationException>(() =>
        {
            using (scope)
            {
                code.Line("int value;");
                throw new InvalidOperationException();
            }
        });
        scope.Dispose();
        code.Line("// end");

        const string expected = """
            class Example
            {
                int value;
            }
            // end

            """;
        Assert.AreEqual(expected.ReplaceLineEndings("\n"), code.ToString());
    }
}
