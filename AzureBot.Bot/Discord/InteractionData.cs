using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AzureBot.Bot.Discord;

public record InteractionData
{
    [JsonPropertyName("id")]
    public string Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; }

    [JsonPropertyName("options")]
    public IReadOnlyCollection<ApplicationCommandOption> Options { get; init; }
}
