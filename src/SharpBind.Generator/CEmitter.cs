namespace SharpBind.Generator;

/// <summary>Renders one C header describing the native ABI and exported declarations.</summary>
internal class CEmitter(ExportClass[] classes)
{
    private readonly CodeBuilder code = new(CodeRules.CSharp);
    private bool emitted;

    internal string Emit()
    {
        if (emitted) throw new InvalidOperationException("An emitter can only render one file.");
        emitted = true;

        code.Lines(GenerationFiles.ReadTemplate("Abi.h"));
        code.Lines("""
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
                code.Line($"int32_t {cls.Prefix}_release(sb_handle handle, sb_error* error);");

            foreach (var method in cls.Methods)
                EmitMethod(method);
        }
        code.Lines("""
            #ifdef __cplusplus
            }
            #endif
            """);
        return code.ToString();
    }

    private void EmitMethod(ExportMethod method)
    {
        var parameters = new List<string>();
        if (!method.IsStatic && !method.IsConstructor) parameters.Add(CTypes.ThisSignature);
        foreach (var arg in method.Arguments)
            parameters.AddRange(CTypes.TypeSignature(arg.Type, arg.NativeName));
        if (method.IsConstructor) parameters.Add(CTypes.ConstructorResultSignature);
        parameters.AddRange(CTypes.ResultSignature(method.Result));
        parameters.Add(CTypes.ErrorSignature);

        code.Line($"int32_t {method.EntryPoint}({string.Join(", ", parameters)});");
    }
}
