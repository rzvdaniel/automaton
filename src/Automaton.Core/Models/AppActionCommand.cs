using System.Text.Json;

namespace Automaton.Core.Models;

/// <summary>App Action management payload fetched over authenticated HTTPS, never placed in RabbitMQ.</summary>
public sealed record AppActionCommand(string Kind, JsonElement Payload)
{
    public static AppActionCommand Create<T>(string kind, T payload)
        => new(kind, JsonSerializer.SerializeToElement(payload));
    public T Read<T>() => Payload.Deserialize<T>() ?? throw new InvalidDataException("App Action payload is required.");
}
