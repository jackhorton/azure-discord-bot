using System.Text.Json.Serialization;

namespace AzureBot.Bot.Discord;

public record Interaction
{
    [JsonPropertyName("id")]
    public string Id { get; init; }

    [JsonPropertyName("type")]
    public InteractionType Type { get; init; }

    [JsonPropertyName("token")]
    public string Token { get; init; }

    [JsonPropertyName("guild_id")]
    public string GuildId { get; init; }

    [JsonPropertyName("channel_id")]
    public string ChannelId { get; init; }

    [JsonPropertyName("data")]
    public InteractionData Data { get; init; }

    [JsonPropertyName("member")]
    public GuildMember Member { get; init; }
}
