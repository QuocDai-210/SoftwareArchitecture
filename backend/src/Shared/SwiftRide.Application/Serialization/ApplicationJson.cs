using System.Text.Json;
using System.Text.Json.Serialization;

namespace SwiftRide.Application.Serialization;

public static class ApplicationJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
