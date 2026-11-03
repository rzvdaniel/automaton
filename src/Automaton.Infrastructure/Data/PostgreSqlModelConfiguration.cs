using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Automaton.Api.Data;

/// <summary>
/// PostgreSQL mapping rules that keep the behavior the platform had on MySQL:
/// case-insensitive text lookups and uniqueness, and UTC-only timestamps.
/// </summary>
internal static class PostgreSqlModelConfiguration
{
    /// <summary>Identifier-like text (names, slugs, emails, keys) was compared without case on MySQL.
    /// Longer columns (messages, logs, JSON) stay ordinary case-sensitive text.</summary>
    private const int MaxCaseInsensitiveLength = 512;

    private static readonly HashSet<string> UnboundedLookupNames =
        ["Name", "DisplayName", "PublicUsername", "GitHubUsername", "Category", "RegistrationProvider"];

    internal static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("citext");
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
        {
            if (property.ClrType == typeof(string) && property.GetColumnType() is null && IsCaseInsensitive(property))
                property.SetColumnType("citext");
        }
    }

    internal static void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<NullableUtcDateTimeOffsetConverter>();
    }

    // MySQL could index only bounded text (Pomelo mapped indexed text to varchar(255)), so indexed text
    // without a length was case-insensitive too. Unbounded names that people look up are listed explicitly.
    private static bool IsCaseInsensitive(IMutableProperty property)
        => property.GetMaxLength() is { } length
            ? length <= MaxCaseInsensitiveLength
            : property.GetContainingIndexes().Any() || property.IsKey() || UnboundedLookupNames.Contains(property.Name);

    // timestamptz accepts only UTC values; dates read back carry Kind=Utc like the app always wrote them.
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private sealed class UtcDateTimeConverter()
        : ValueConverter<DateTime, DateTime>(value => ToUtc(value), value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter()
        : ValueConverter<DateTime?, DateTime?>(
            value => value.HasValue ? ToUtc(value.Value) : value,
            value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value);

    private sealed class UtcDateTimeOffsetConverter()
        : ValueConverter<DateTimeOffset, DateTimeOffset>(value => value.ToUniversalTime(), value => value);

    private sealed class NullableUtcDateTimeOffsetConverter()
        : ValueConverter<DateTimeOffset?, DateTimeOffset?>(value => value.HasValue ? value.Value.ToUniversalTime() : value, value => value);
}
