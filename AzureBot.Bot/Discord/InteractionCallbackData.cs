using System.Text.Json.Serialization;

namespace AzureBot.Bot.Discord;

public record InteractionCallbackData
{
    [JsonPropertyName("content")]
    public string Content { get; init; }
}
