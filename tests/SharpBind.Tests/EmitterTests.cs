using SharpBind.Generator;

namespace SharpBind.Tests;

[TestClass]
public class EmitterTests
{
    [DataTestMethod]
    [DataRow("native")]
    [DataRow("c")]
    [DataRow("python")]
    [DataRow("stub")]
    public void EmittersRejectASecondRenderInsteadOfAccumulatingOutput(string language)
    {
        Func<string> emit = language switch
        {
            "native" => new NativeEmitter([]).Emit,
            "c" => new CEmitter([]).Emit,
            "python" => new PythonEmitter([], "{}", "example", PythonOutputKind.Code).Emit,
            "stub" => new PythonEmitter([], "{}", "example", PythonOutputKind.Stub).Emit,
            _ => throw new ArgumentOutOfRangeException(nameof(language))
        };

        Assert.IsFalse(string.IsNullOrEmpty(emit()));
        Assert.ThrowsException<InvalidOperationException>(() => emit());
    }
}
