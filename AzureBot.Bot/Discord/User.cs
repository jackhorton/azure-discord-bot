using System.Text.Json.Serialization;

namespace AzureBot.Bot.Discord;

public record User
{
    [JsonPropertyName("username")]
    public string Username { get; init; }
}
