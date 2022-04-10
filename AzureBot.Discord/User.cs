using System.Text.Json.Serialization;

namespace AzureBot.Discord;

public record User
{
    public User(string username)
    {
        Username = username ?? throw new ArgumentNullException(nameof(username));
    }

    [JsonPropertyName("username")]
    public string Username { get; init; }
}
