using System.Text.Json.Serialization;

namespace AzureBot.Discord;

public record Interaction
{
    public Interaction(string id, InteractionType type, string token, string guildId, string channelId, InteractionData data, GuildMember member)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Type = type;
        Token = token ?? throw new ArgumentNullException(nameof(token));
        GuildId = guildId ?? throw new ArgumentNullException(nameof(guildId));
        ChannelId = channelId ?? throw new ArgumentNullException(nameof(channelId));
        Data = data ?? throw new ArgumentNullException(nameof(data));
        Member = member ?? throw new ArgumentNullException(nameof(member));
    }

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
