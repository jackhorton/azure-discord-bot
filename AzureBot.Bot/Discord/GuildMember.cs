using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AzureBot.Bot.Discord;

public record GuildMember
{
    [JsonPropertyName("user")]
    public User User { get; init; }

    [JsonPropertyName("roles")]
    public IReadOnlyCollection<string> Roles { get; init; }
}
