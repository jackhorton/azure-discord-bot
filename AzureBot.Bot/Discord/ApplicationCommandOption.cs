using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AzureBot.Bot.Discord;

public record ApplicationCommandOption
{
    [JsonPropertyName("string")]
    public string Name { get; init; }

    [JsonPropertyName("type")]
    public ApplicationCommandOptionType Type { get; init; }
    
    [JsonPropertyName("value")]
    public string Value { get; init; }

    [JsonPropertyName("options")]
    public IReadOnlyCollection<ApplicationCommandOption> Options { get; init; }
}
