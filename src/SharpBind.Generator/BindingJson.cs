using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpBind.Generator;

internal static class BindingJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        NewLine = "\n",
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) }
    };

    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    internal static string EnumName<T>(T value) where T : struct, Enum =>
        JsonSerializer.SerializeToElement(value, Options).GetString()!;

    internal static string Hash<T>(T value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(value))));
}
