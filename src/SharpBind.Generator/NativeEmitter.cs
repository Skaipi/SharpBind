using System.Text;

namespace SharpBind.Generator;

internal static class NativeEmitter
{
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

    private static string Attribute(string entry) => $"    [UnmanagedCallersOnly(EntryPoint = \"{entry}\", CallConvs = [typeof(CallConvCdecl)])]";

    internal static string EmitSource(ExportClass[] classes)
    {
        var code = new StringBuilder("""
            #nullable enable
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            using SharpBind.Interop;
            namespace SharpBind.Generated;
            """);
        code.AppendLine("\ninternal static unsafe class NativeExports\n{");
        code.AppendLine("    private static readonly ObjectHandles ObjectHandles = new();");
        code.AppendLine("    private static readonly NativeErrors NativeErrors = new();");
        code.AppendLine("    private static readonly CopiedValues CopiedValues = new();");
        code.AppendLine(GenerationFiles.ReadTemplate("SupportExports.cs.txt"));
        int index = 0;
        foreach (var cls in classes)
        {
            string type = cls.ManagedTypeName;
            if (cls.HasInstances) AppendRelease(code, cls, index++);

            foreach (var method in cls.Methods)
                AppendMethod(code, method, type, index++, cls.ConcurrentCalls);
        }
        return code.AppendLine("}").ToString();
    }

    private static void AppendMethod(StringBuilder code, ExportMethod method, string type, int index, bool concurrentCalls)
    {
        var parameters = new List<string>();
        if (!method.IsStatic && !method.IsConstructor) parameters.Add(CsharpTypes.ThisSignature);
        foreach (var arg in method.Arguments)
            parameters.AddRange(CsharpTypes.TypeSignature(arg.Type, arg.NativeName));
        if (method.IsConstructor) parameters.Add(CsharpTypes.ConstructorResultSignature);
        parameters.AddRange(CsharpTypes.ResultSignature(method.Result));
        parameters.Add(CsharpTypes.ErrorSignature);

        code.AppendLine(Attribute(method.EntryPoint));
        code.AppendLine($"    public static int Export{index}({string.Join(", ", parameters)})");
        code.AppendLine("    {\n        try\n        {\n            NativeErrors.Begin(error);");

        AppendResultInitialization(code, method);

        if (!method.IsStatic && !method.IsConstructor)
            code.AppendLine($"            using var instance = ObjectHandles.Enter<{type}>(handle);");

        // Every acquired argument is released on all exits, including later argument failures.
        // Application code calls Retain() for any reference it needs beyond this invocation.
        foreach (var argument in method.Arguments.Where(a => a.Type.IsTensorOwner))
            code.AppendLine($"            using var {argument.NativeName}Tensor = TensorInterop.Acquire<{argument.Type.NativeTypeName}>({argument.NativeName});");

        AppendInvocation(code, method, Invocation(method, type), concurrentCalls);
        AppendSuccessAndCatch(code);
    }

    private static string Invocation(ExportMethod method, string type)
    {
        string target = method.IsStatic ? type : "instance.Value";
        string call = method.IsConstructor ? $"new {type}" : $"{target}.@{method.ManagedName}";
        call += "(" + string.Join(", ", method.Arguments.Select(ReadArgument)) + ")";

        return call;
    }

    private static void AppendInvocation(StringBuilder code, ExportMethod method, string call, bool concurrentCalls)
    {
        if (method.IsConstructor)
            code.AppendLine($"            *result = ObjectHandles.Add({call}, {(concurrentCalls ? "true" : "false")});");
        else if (method.Result is { Kind: not LogicalTypeKind.Void } result)
        {
            if (result.Kind == LogicalTypeKind.Tensor)
            {
                // Detach before call-owned arguments are disposed, allowing a method to
                // return its input handle directly. The output using cleans up if detach fails.
                code.AppendLine($"            using var output = {call};");
                code.AppendLine("            *result = TensorInterop.Detach(output);");
            }
            else if (result.IsBuffer)
                code.AppendLine($"            CopiedValues.{(result.Kind == LogicalTypeKind.String ? "WriteString" : "WriteArray")}({call}, result, resultLength, {(result.Logical.Nullable ? "true" : "false")});");
            else
                code.AppendLine($"            *result = {call}{(result.Logical.Element == LogicalElement.Bool ? " ? (byte)1 : (byte)0" : "")};");
        }
        else
            code.AppendLine($"            {call};");

    }

    private static void AppendResultInitialization(StringBuilder code, ExportMethod method)
    {
        if (method.IsConstructor || method.Result.Kind != LogicalTypeKind.Void)
        {
            // Failed conversion or invocation must leave no stale ownership token in
            // the caller's result slot; cleanup may inspect it after an error status.
            code.AppendLine("            if (result == null) throw new ArgumentNullException(nameof(result));\n            *result = default;");
            if (method.Result?.IsBuffer == true)
                code.AppendLine("            if (resultLength == null) throw new ArgumentNullException(nameof(resultLength));\n            *resultLength = 0;");
        }

    }

    private static void AppendRelease(StringBuilder code, ExportClass cls, int index)
    {
        code.AppendLine(Attribute(cls.Prefix + "_release"));
        code.AppendLine($"    public static int Release{index}(SbHandle handle, SbError* error)");
        code.AppendLine("    {\n        try\n        {\n            NativeErrors.Begin(error);");
        code.AppendLine($"            ObjectHandles.Release<{cls.ManagedTypeName}>(handle);");
        AppendSuccessAndCatch(code);
    }

    private static void AppendSuccessAndCatch(StringBuilder code)
    {
        code.AppendLine("""
                        return 0;
                    }
                    catch (Exception errorValue)
                    {
                        return NativeErrors.Fail(errorValue, error);
                    }
                }
            """);
    }

    internal static string EmitHeader(ExportClass[] classes)
    {
        var code = new StringBuilder(GenerationFiles.ReadTemplate("Abi.h"));
        code.AppendLine("""
            /* cdecl; success = 0, failure = -1 with caller-owned sb_error.
             * Initialize error.size, zero its other fields, and release populated errors once.
             * Strings and numeric vectors copy; free result buffers with sharpbind_free.
             * Owned tensor inputs consume their token on successful adoption.
             * Borrowed views require the caller to keep storage alive throughout the call.
             * Release callbacks must be thread-safe, remain loaded, and never throw.
             */
            #ifdef __cplusplus
            extern "C" {
            #endif
            void sharpbind_error_release(sb_error* error);
            int32_t sharpbind_free(void* buffer, sb_error* error);
            """);

        foreach (var cls in classes)
        {
            if (cls.HasInstances)
                code.AppendLine($"int32_t {cls.Prefix}_release(sb_handle handle, sb_error* error);");

            foreach (var method in cls.Methods)
            {
                var parameters = new List<string>();
                if (!method.IsStatic && !method.IsConstructor) parameters.Add(CTypes.ThisSignature);
                foreach (var arg in method.Arguments)
                    parameters.AddRange(CTypes.TypeSignature(arg.Type, arg.NativeName));
                if (method.IsConstructor) parameters.Add(CTypes.ConstructorResultSignature);
                parameters.AddRange(CTypes.ResultSignature(method.Result));
                parameters.Add(CTypes.ErrorSignature);

                code.AppendLine($"int32_t {method.EntryPoint}({string.Join(", ", parameters)});");
            }
        }
        return code.AppendLine("#ifdef __cplusplus\n}\n#endif").ToString();
    }
}
