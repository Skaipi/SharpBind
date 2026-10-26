namespace SharpBind.Generator;

/// <summary>Renders one C# native export file.</summary>
internal class NativeEmitter(ExportClass[] classes)
{
    private readonly CodeBuilder code = new(CodeRules.CSharp);
    private int exportIndex;
    private bool emitted;

    internal string Emit()
    {
        if (emitted) throw new InvalidOperationException("An emitter can only render one file.");
        emitted = true;

        code.Lines("""
            #nullable enable
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            using SharpBind.Interop;
            namespace SharpBind.Generated;
            """);
        using (code.ScopeStatement("internal static unsafe class NativeExports"))
        {
            code.Line("private static readonly ObjectHandles ObjectHandles = new();");
            code.Line("private static readonly NativeErrors NativeErrors = new();");
            code.Line("private static readonly CopiedValues CopiedValues = new();");
            code.Lines(GenerationFiles.ReadTemplate("SupportExports.cs.txt"));
            code.BlankLine();
            foreach (var cls in classes)
            {
                if (cls.HasInstances) EmitRelease(cls);

                foreach (var method in cls.Methods)
                    EmitMethod(cls, method);
            }
        }
        return code.ToString();
    }

    private void EmitMethod(ExportClass cls, ExportMethod method)
    {
        var parameters = new List<string>();
        if (!method.IsStatic && !method.IsConstructor) parameters.Add(CsharpTypes.ThisSignature);
        foreach (var arg in method.Arguments)
            parameters.AddRange(CsharpTypes.TypeSignature(arg.Type, arg.NativeName));
        if (method.IsConstructor) parameters.Add(CsharpTypes.ConstructorResultSignature);
        parameters.AddRange(CsharpTypes.ResultSignature(method.Result));
        parameters.Add(CsharpTypes.ErrorSignature);

        code.Line(Attribute(method.EntryPoint));
        using (code.ScopeStatement($"public static int Export{exportIndex++}({string.Join(", ", parameters)})"))
        {
            using (code.ScopeStatement("try"))
            {
                code.Line("NativeErrors.Begin(error);");
                EmitResultInitialization(method);

                if (!method.IsStatic && !method.IsConstructor)
                    code.Line($"using var instance = ObjectHandles.Enter<{cls.ManagedTypeName}>(handle);");

                // Every acquired argument is released on all exits, including later argument failures.
                // Application code calls Retain() for any reference it needs beyond this invocation.
                foreach (var argument in method.Arguments.Where(a => a.Type.IsTensorOwner))
                    code.Line($"using var {argument.NativeName}Tensor = TensorInterop.Acquire<{argument.Type.NativeTypeName}>({argument.NativeName});");

                EmitInvocation(cls, method);
                code.Line("return 0;");
            }
            EmitCatch();
        }
    }

    private void EmitInvocation(ExportClass cls, ExportMethod method)
    {
        string call = Invocation(method, cls.ManagedTypeName);
        if (method.IsConstructor)
            code.Line($"*result = ObjectHandles.Add({call}, {(cls.ConcurrentCalls ? "true" : "false")});");
        else if (method.Result is { Kind: not LogicalTypeKind.Void } result)
        {
            if (result.Kind == LogicalTypeKind.Tensor)
            {
                // Detach before call-owned arguments are disposed, allowing a method to
                // return its input handle directly. The output using cleans up if detach fails.
                code.Line($"using var output = {call};");
                code.Line("*result = TensorInterop.Detach(output);");
            }
            else if (result.IsBuffer)
                code.Line($"CopiedValues.{(result.Kind == LogicalTypeKind.String ? "WriteString" : "WriteArray")}({call}, result, resultLength, {(result.Logical.Nullable ? "true" : "false")});");
            else
                code.Line($"*result = {call}{(result.Logical.Element == LogicalElement.Bool ? " ? (byte)1 : (byte)0" : "")};");
        }
        else
            code.Line($"{call};");
    }

    private void EmitResultInitialization(ExportMethod method)
    {
        if (method.IsConstructor || method.Result.Kind != LogicalTypeKind.Void)
        {
            // Failed conversion or invocation must leave no stale ownership token in
            // the caller's result slot; cleanup may inspect it after an error status.
            code.Line("if (result == null) throw new ArgumentNullException(nameof(result));");
            code.Line("*result = default;");
            if (method.Result.IsBuffer)
            {
                code.Line("if (resultLength == null) throw new ArgumentNullException(nameof(resultLength));");
                code.Line("*resultLength = 0;");
            }
        }
    }

    private void EmitRelease(ExportClass cls)
    {
        code.Line(Attribute(cls.Prefix + "_release"));
        using (code.ScopeStatement($"public static int Release{exportIndex++}(SbHandle handle, SbError* error)"))
        {
            using (code.ScopeStatement("try"))
            {
                code.Line("NativeErrors.Begin(error);");
                code.Line($"ObjectHandles.Release<{cls.ManagedTypeName}>(handle);");
                code.Line("return 0;");
            }
            EmitCatch();
        }
    }

    private void EmitCatch()
    {
        using (code.ScopeStatement("catch (Exception errorValue)"))
            code.Line("return NativeErrors.Fail(errorValue, error);");
    }

    private static string Attribute(string entry) => $"[UnmanagedCallersOnly(EntryPoint = \"{entry}\", CallConvs = [typeof(CallConvCdecl)])]";

    private static string Invocation(ExportMethod method, string type)
    {
        string target = method.IsStatic ? type : "instance.Value";
        string call = method.IsConstructor ? $"new {type}" : $"{target}.@{method.ManagedName}";
        call += "(" + string.Join(", ", method.Arguments.Select(ReadArgument)) + ")";

        return call;
    }

    private static string ReadArgument(ExportArgument a)
    {
        string nullable = a.Type.Logical.Nullable ? "true" : "false";
        return a.Type.Kind switch
        {
            LogicalTypeKind.Scalar => a.Type.Logical.Element == LogicalElement.Bool ? $"CopiedValues.ReadBool({a.NativeName})" : a.NativeName,
            LogicalTypeKind.String => $"CopiedValues.ReadString({a.NativeName}, {a.NativeName}Length, {nullable})!",
            LogicalTypeKind.Array => $"CopiedValues.ReadArray({a.NativeName}, {a.NativeName}Length, {nullable})!",
            LogicalTypeKind.Tensor when a.Type.IsTensorOwner => $"{a.NativeName}Tensor",
            LogicalTypeKind.Tensor when a.Type.Logical.Access == TensorAccess.Writable => $"TensorInterop.WritableView<{a.Type.NativeTypeName}>({a.NativeName})",
            LogicalTypeKind.Tensor => $"TensorInterop.ReadOnlyView<{a.Type.NativeTypeName}>({a.NativeName})",
            _ => throw new InvalidOperationException()
        };
    }
}
