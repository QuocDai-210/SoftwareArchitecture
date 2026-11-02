using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwiftRide.Application.Serialization;

namespace SwiftRide.Infrastructure;

public static class JsonColumns
{
    public static readonly JsonSerializerOptions Json = ApplicationJson.Options;

    public static void JsonColumn<T>(this PropertyBuilder<T> property)
    {
        property.HasConversion(
            v => JsonSerializer.Serialize(v, Json),
            v => JsonSerializer.Deserialize<T>(v, Json)!
        );
        property.Metadata.SetValueComparer(
            new ValueComparer<T>(
                (a, b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json),
                v => JsonSerializer.Serialize(v, Json).GetHashCode(),
                v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Json), Json)!
            )
        );
    }
}
