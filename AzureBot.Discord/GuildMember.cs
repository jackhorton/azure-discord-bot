using System.Text.Json.Serialization;

namespace AzureBot.Discord;

public record GuildMember
{
    public GuildMember(User user, IReadOnlyCollection<string> roles)
    {
        User = user ?? throw new ArgumentNullException(nameof(user));
        Roles = roles ?? throw new ArgumentNullException(nameof(roles));
    }

    [JsonPropertyName("user")]
    public User User { get; init; }

    [JsonPropertyName("roles")]
    public IReadOnlyCollection<string> Roles { get; init; }
}
