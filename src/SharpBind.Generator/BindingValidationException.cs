namespace SharpBind.Generator;

/// <summary>The exported API or binding configuration violates a binding rule.</summary>
public sealed class BindingValidationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
