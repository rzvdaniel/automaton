using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;

namespace Automaton.Api.Extensions;

public static class AutomatonJsonOptions
{
    public static void Configure(JsonOptions options)
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    }
}
