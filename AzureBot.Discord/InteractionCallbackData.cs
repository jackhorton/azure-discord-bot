using System.Text.Json.Serialization;

namespace AzureBot.Discord;

public record InteractionCallbackData
{
    [JsonPropertyName("content")]
    public string Content { get; init; }

    public InteractionCallbackData(string content)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }
}
