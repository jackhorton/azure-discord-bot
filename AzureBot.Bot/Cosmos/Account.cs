using Newtonsoft.Json;

namespace AzureBot.Bot.Cosmos;

public record Account
{
    [JsonProperty("id", Required = Required.Always)]
    public string Id { get; init; } = default!;
    [JsonProperty(Required = Required.Always)]
    public string Email { get; init; } = default!;
    public string? GuildId { get; init; }
}
