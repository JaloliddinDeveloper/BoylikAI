using System.Text.Json;
using System.Text.Json.Serialization;

namespace BoylikAI.Infrastructure.Common;

/// <summary>
/// Shared JsonSerializerOptions — prevents duplicate static instances scattered across the codebase.
/// </summary>
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions General = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static readonly JsonSerializerOptions WithEnums = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
