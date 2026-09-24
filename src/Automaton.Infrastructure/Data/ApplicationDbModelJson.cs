using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Automaton.Api.Data;

internal static class ApplicationDbModelJson
{
    public static void ConfigureJson<T>(PropertyBuilder<T> property)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        property.HasColumnType("json").HasConversion(
            value => JsonSerializer.Serialize(value, options),
            json => JsonSerializer.Deserialize<T>(json, options)!);
        property.Metadata.SetValueComparer(new ValueComparer<T>(
            (left, right) => JsonSerializer.Serialize(left, options) == JsonSerializer.Serialize(right, options),
            value => JsonSerializer.Serialize(value, options).GetHashCode(),
            value => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, options), options)!));
    }
}
